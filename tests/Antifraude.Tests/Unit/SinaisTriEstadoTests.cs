using Antifraude.Core.Dominio;
using Antifraude.Core.Portas;
using Antifraude.Infra.Score;
using FluentAssertions;
using Xunit;

namespace Antifraude.Tests.Unit;

/// <summary>Tri-estado do sinal: SinaisIncompletos e o mapeamento no mock de score.</summary>
public sealed class SinaisTriEstadoTests
{
    private static Sinal Ativo(string nome = "reuso_imagem") => new(nome, ValorSinal.Ativo, "teste");

    private static Sinal Inativo(string nome) => new(nome, ValorSinal.Inativo, "teste");

    private static Sinal Indisponivel(string nome) =>
        new(nome, ValorSinal.Indisponivel, "teste", Motivo: MotivoIndisponibilidade.FonteIndisponivel);

    private static Sinistro Com(params Sinal[] sinais) => new(Guid.NewGuid(), "SIN-1", Sinais: sinais);

    [Fact]
    public void Lista_vazia_e_incompleta() =>
        Com().SinaisIncompletos.Should().BeTrue();

    [Fact]
    public void Todos_indisponiveis_e_incompleto_equivale_a_nao_avaliado() =>
        Com(Indisponivel("a"), Indisponivel("b"), Indisponivel("c"))
            .SinaisIncompletos.Should().BeTrue();

    [Fact]
    public void Um_sinal_calculado_ja_nao_e_incompleto()
    {
        var sinistro = Com(Inativo("a"), Indisponivel("b"), Indisponivel("c"));

        sinistro.SinaisIncompletos.Should().BeFalse("indisponibilidade parcial segue para score");
        sinistro.AlgumSinalIndisponivel.Should().BeTrue("mas o caso fica marcado como dados incompletos");
    }

    [Fact]
    public void Sinal_serializa_para_json_com_estado_legivel_e_evidencia_estruturada()
    {
        // Mesma serialização usada pelo adapter de persistência (sinais_json da auditoria):
        // enums como texto e evidência como objeto — consultável pelo Compliance/painel.
        var sinal = new Sinal(
            "velocity", ValorSinal.Indisponivel, "historico-sinistros-v1",
            Evidencia: new Dictionary<string, object?> { ["campoAusente"] = "idCliente/imei" },
            Motivo: MotivoIndisponibilidade.DadoAusente);

        var json = System.Text.Json.JsonSerializer.Serialize(new[] { sinal });

        json.Should().Contain("\"Estado\":\"Indisponivel\"", "enum legível, não número");
        json.Should().Contain("\"Motivo\":\"DadoAusente\"");
        json.Should().Contain("\"campoAusente\":\"idCliente/imei\"", "evidência é objeto, não string escapada");

        var deVolta = System.Text.Json.JsonSerializer.Deserialize<List<Sinal>>(json)!;
        deVolta.Single().Estado.Should().Be(ValorSinal.Indisponivel);
    }

    [Fact]
    public async Task Mock_de_score_ignora_indisponivel_e_nunca_o_trata_como_falso()
    {
        var provider = new MockScoreProvider(new MockScoreProviderOptions());
        var config = new ScoringConfig
        {
            Versao = 1,
            Ativa = true,
            Pesos = new Dictionary<string, double>
            {
                ["reuso_imagem"] = 30,
                ["imei_serie_divergente"] = 25,
                ["velocity"] = 20,
            },
            LimiarMedio = 30,
            LimiarAlto = 60,
            CriadaEm = DateTimeOffset.UnixEpoch,
        };

        // Ativo soma o peso; inativo soma zero; indisponível é ignorado (não vira 0 "falso").
        var score = await provider.CalcularScoreAsync(
            Com(Ativo("reuso_imagem"), Inativo("imei_serie_divergente"), Indisponivel("velocity")),
            config);

        score.Should().Be(30);
    }
}
