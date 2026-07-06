using Antifraude.Core.Dominio;

namespace Antifraude.Api.Contratos;

/// <summary>Um sinal no corpo do <c>POST /sinistros</c>.</summary>
/// <param name="Nome">Identificador do sinal (obrigatório).</param>
/// <param name="Valor">Intensidade normalizada em [0,1].</param>
/// <param name="Origem">Proveniência do sinal (ex.: <c>mock</c>). Default: <c>desconhecida</c>.</param>
public sealed record SinalRequest(string? Nome, double Valor, string? Origem);

/// <summary>
/// Corpo do <c>POST /sinistros</c>. Contrato mínimo da fundação — o contrato rico é da
/// fatia 1. A API não decide mérito: só valida formato e enfileira.
/// </summary>
public sealed record SinistroRequest(IReadOnlyList<SinalRequest>? Sinais)
{
    /// <summary>Valida a entrada na borda. Retorna as mensagens de erro (vazio = válido).</summary>
    public IReadOnlyList<string> Validar()
    {
        var erros = new List<string>();

        if (Sinais is null)
        {
            erros.Add("Campo 'sinais' é obrigatório (pode ser lista vazia, mas deve estar presente).");
            return erros;
        }

        for (var i = 0; i < Sinais.Count; i++)
        {
            var s = Sinais[i];
            if (string.IsNullOrWhiteSpace(s.Nome))
            {
                erros.Add($"sinais[{i}].nome é obrigatório.");
            }

            if (s.Valor is < 0 or > 1)
            {
                erros.Add($"sinais[{i}].valor deve estar em [0,1].");
            }
        }

        return erros;
    }

    /// <summary>Converte para o modelo de domínio com o <paramref name="caseId"/> de correlação.</summary>
    public Sinistro ParaDominio(Guid caseId)
    {
        var sinais = (Sinais ?? [])
            .Select(s => new Sinal(s.Nome!, s.Valor, string.IsNullOrWhiteSpace(s.Origem) ? "desconhecida" : s.Origem))
            .ToList();

        return new Sinistro(caseId, sinais);
    }
}
