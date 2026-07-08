using Antifraude.Core.Decisao;
using Antifraude.Core.Dominio;
using FluentAssertions;
using Xunit;

namespace Antifraude.Tests.Unit;

public sealed class FiltroAtributosProibidosTests
{
    private static Sinal S(string nome) => new(nome, 1.0, "teste");

    [Fact]
    public void Atributo_proibido_e_removido_e_reportado()
    {
        var r = FiltroAtributosProibidos.Filtrar([S(SinaisConhecidos.ReusoImagem), S("raca")]);

        r.Permitidos.Should().ContainSingle(s => s.Nome == SinaisConhecidos.ReusoImagem);
        r.ProibidosDetectados.Should().ContainSingle().Which.Should().Be("raca");
    }

    [Fact]
    public void Nome_desconhecido_nao_proibido_e_descartado_sem_evento()
    {
        var r = FiltroAtributosProibidos.Filtrar([S(SinaisConhecidos.ImeiSerie), S("qualquer_coisa")]);

        r.Permitidos.Should().ContainSingle(s => s.Nome == SinaisConhecidos.ImeiSerie);
        r.ProibidosDetectados.Should().BeEmpty("nome desconhecido não é evento de conformidade");
    }

    [Fact]
    public void Deteccao_de_proibido_e_case_insensitive()
    {
        var r = FiltroAtributosProibidos.Filtrar([S("IDADE"), S("Genero")]);

        r.ProibidosDetectados.Should().HaveCount(2);
        r.Permitidos.Should().BeEmpty();
    }

    [Fact]
    public void Entrada_nula_nao_quebra()
    {
        var r = FiltroAtributosProibidos.Filtrar(null);

        r.Permitidos.Should().BeEmpty();
        r.ProibidosDetectados.Should().BeEmpty();
    }
}
