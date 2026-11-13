using Spobras.Core.Base;

namespace Spobras.Celular.Componentes;

public static class SelosObra
{
    /// <summary>Classe do selo e texto: andamento (azul), concluido (verde), sem OS ou desconhecido (laranja).</summary>
    public static (string Classe, string Texto) Selo(ObraLida obra)
    {
        string texto = string.IsNullOrWhiteSpace(obra.StatusContrato) ? (obra.Status ?? "SEM STATUS") : obra.StatusContrato.Trim().ToUpperInvariant();
        return MedicoesEmTramite.Classificar(obra.StatusContrato) switch
        {
            SituacaoContrato.Concluido => ("concluido", texto),
            SituacaoContrato.Andamento when obra.DataOs is null => ("atencao", texto),
            SituacaoContrato.Andamento => ("andamento", texto),
            _ => ("atencao", texto),
        };
    }

    /// <summary>"Lote 216" a partir de "LOTE 216" ou "216".</summary>
    public static string Lote(string? lote)
    {
        if (string.IsNullOrWhiteSpace(lote))
        {
            return "";
        }

        string chave = Spobras.Dados.Base.ProcuraObras.ChaveDoLote(lote);
        return chave.All(char.IsDigit) && chave.Length > 0 ? "Lote " + chave : lote.Trim();
    }

    /// <summary>Status da medicao mais curto para caber no selo.</summary>
    public static string StatusCurto(string status) => status.Trim().ToUpperInvariant()
        .Replace("LIBERADO PARA ", "LIB. P/ ", StringComparison.Ordinal)
        .Replace("CADASTRADO PARA ", "CAD. P/ ", StringComparison.Ordinal);
}
