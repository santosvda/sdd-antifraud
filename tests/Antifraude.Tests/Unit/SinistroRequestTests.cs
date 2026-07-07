using Antifraude.Api.Contratos;
using FluentAssertions;
using Xunit;

namespace Antifraude.Tests.Unit;

public sealed class SinistroRequestTests
{
    private static SinistroRequest Completo() => new(
        IdSinistro: "SIN-1",
        Apolice: "AP-1",
        Aparelho: new AparelhoRequest("356789101112131", "SN-42"),
        Fotos: ["img://1"],
        Metadados: new MetadadosRequest(DateTimeOffset.UtcNow, "app", "C1"));

    [Fact]
    public void Payload_completo_nao_e_parcial_e_tem_idSinistro()
    {
        var r = Completo();
        r.TemIdSinistro.Should().BeTrue();
        r.PayloadParcial.Should().BeFalse();
    }

    [Fact]
    public void So_idSinistro_e_payload_parcial()
    {
        var r = new SinistroRequest("SIN-1", null, null, null, null);
        r.TemIdSinistro.Should().BeTrue();
        r.PayloadParcial.Should().BeTrue("faltam apólice, aparelho, fotos e metadados");
    }

    [Fact]
    public void Sem_idSinistro_e_nao_processavel()
    {
        var r = new SinistroRequest(null, "AP-1", null, ["img://1"], null);
        r.TemIdSinistro.Should().BeFalse();
    }

    [Fact]
    public void ParaDominio_mapeia_campos_e_nao_traz_sinais()
    {
        var caseId = Guid.NewGuid();
        var s = Completo().ParaDominio(caseId);

        s.CaseId.Should().Be(caseId);
        s.IdSinistro.Should().Be("SIN-1");
        s.Apolice.Should().Be("AP-1");
        s.Aparelho!.Imei.Should().Be("356789101112131");
        s.Fotos.Should().ContainSingle().Which.Should().Be("img://1");
        s.PayloadParcial.Should().BeFalse();
        s.Sinais.Should().BeNull("os sinais não chegam na ingestão — são responsabilidade da 2.2");
    }
}
