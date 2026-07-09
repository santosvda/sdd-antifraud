using Antifraude.Core.Dominio;

namespace Antifraude.Core.Coleta;

/// <summary>
/// Um calculador de sinal da coleta (feature 2.2). Contrato de resiliência: a
/// implementação NUNCA lança por dado ausente ou fonte fora — esses ramos viram um
/// <see cref="Sinal"/> com estado <see cref="ValorSinal.Indisponivel"/> e o motivo
/// correspondente. Quando o dado de entrada falta, a fonte externa não é consultada.
/// </summary>
public interface ICalculadorDeSinal
{
    /// <summary>Nome canônico do sinal (chave usada na scoring_config).</summary>
    string Nome { get; }

    Task<Sinal> CalcularAsync(Sinistro sinistro, CancellationToken ct = default);
}
