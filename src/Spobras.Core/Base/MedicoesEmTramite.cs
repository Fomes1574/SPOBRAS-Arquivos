namespace Spobras.Core.Base;

public enum SituacaoContrato
{
    /// <summary>Contrato em andamento: as medicoes ainda nao pagas aparecem como em tramite.</summary>
    Andamento,

    /// <summary>Contrato concluido: so o pago e o saldo, sem medicoes.</summary>
    Concluido,

    /// <summary>Nao aparece nos relatorios do app (ex.: sob gestao da SIURB).</summary>
    Ignorado,

    /// <summary>Status que a regra ainda nao conhece: tratado como andamento, com aviso.</summary>
    Desconhecido,
}

/// <summary>Uma medicao ainda nao paga de uma obra em andamento.</summary>
public sealed record MedicaoEmTramite(string Numero, string Status, decimal ValorConsiderado, decimal Reajuste, DateOnly? Inicio)
{
    /// <summary>Valor considerado mais o reajuste, como estao na linha da planilha (sem remuneracao).</summary>
    public decimal Valor => ValorConsiderado + Reajuste;
}

/// <summary>
/// Regras combinadas com o usuario (13/11/2026) para a ficha da obra:
/// andamento = vigente, vencido, suspenso, em tramite - aditivo, nao iniciado e rescisao em tramite;
/// concluido = concluido, encerrado / TRD, TRP assinado, TRD em tramitacao, em encerramento e rescindido;
/// "sob gestao da SIURB" nao aparece. Em contrato em andamento, toda medicao que nao esta com
/// "pagamento efetuado" esta em tramite, com valor considerado + reajuste.
/// </summary>
public static class MedicoesEmTramite
{
    private static readonly HashSet<string> Andamento = Chaves(
        "VIGENTE", "VENCIDO", "SUSPENSO", "EM TRÂMITE - ADITIVO", "NÃO INICIADO", "RESCISÃO EM TRÂMITE");

    private static readonly HashSet<string> Concluido = Chaves(
        "CONCLUÍDO", "ENCERRADO / TRD", "TRP ASSINADO", "TRD EM TRAMITAÇÃO", "EM ENCERRAMENTO", "RESCINDIDO");

    private static readonly HashSet<string> Ignorado = Chaves("SOB GESTÃO DA SIURB");

    private const string PagamentoEfetuado = "pagamento efetuado";

    private static HashSet<string> Chaves(params string[] status) => [.. status.Select(Chave)];

    /// <summary>Sem acento, caixa ou espacos extras: "Em trâmite - Aditivo" e "EM TRAMITE - ADITIVO" sao o mesmo.</summary>
    private static string Chave(string? texto) =>
        string.Join(" ", Normalizacao.Busca(texto ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries));

    public static SituacaoContrato Classificar(string? statusDoContrato)
    {
        string chave = Chave(statusDoContrato);
        return chave.Length == 0 ? SituacaoContrato.Desconhecido
            : Andamento.Contains(chave) ? SituacaoContrato.Andamento
            : Concluido.Contains(chave) ? SituacaoContrato.Concluido
            : Ignorado.Contains(chave) ? SituacaoContrato.Ignorado
            : SituacaoContrato.Desconhecido;
    }

    /// <summary>
    /// Situacao da obra pelo status do contrato da tabela de contratos; sem ele, pelo status que vem nas medicoes
    /// da propria obra.
    /// </summary>
    public static SituacaoContrato Situacao(ObraLida obra, IEnumerable<MedicaoLida> medicoesDoContrato)
    {
        var situacao = Classificar(obra.StatusContrato);
        if (situacao != SituacaoContrato.Desconhecido || !string.IsNullOrWhiteSpace(obra.StatusContrato))
        {
            return situacao;
        }

        return medicoesDoContrato.Where(m => MesmaObra(m, obra)).Select(m => Classificar(m.StatusContrato))
            .FirstOrDefault(s => s != SituacaoContrato.Desconhecido, SituacaoContrato.Desconhecido);
    }

    /// <summary>
    /// Medicoes em tramite da obra (contrato + escola), da mais recente para a mais antiga. Contrato concluido ou
    /// ignorado: nenhuma. Medicao marcada "sob gestao da SIURB" fica de fora.
    /// </summary>
    public static IReadOnlyList<MedicaoEmTramite> DaObra(ObraLida obra, IEnumerable<MedicaoLida> medicoesDoContrato)
    {
        var lista = medicoesDoContrato.ToList();
        if (Situacao(obra, lista) is SituacaoContrato.Concluido or SituacaoContrato.Ignorado)
        {
            return [];
        }

        return [.. lista
            .Where(m => MesmaObra(m, obra)
                        && Chave(m.Status) != PagamentoEfetuado
                        && Classificar(m.StatusContrato) != SituacaoContrato.Ignorado)
            .Select(m => new MedicaoEmTramite(m.Numero ?? "", m.Status ?? "Sem status", m.ValorConsiderado ?? 0m, m.ValorReajuste ?? 0m, m.Inicio))
            .OrderByDescending(m => int.TryParse(m.Numero, out int n) ? n : -1)
            .ThenByDescending(m => m.Inicio)];
    }

    private static bool MesmaObra(MedicaoLida m, ObraLida obra) =>
        string.Equals(m.Contrato.Trim(), obra.Contrato.Trim(), StringComparison.OrdinalIgnoreCase)
        && Chave(m.Empreendimento) == Chave(obra.Empreendimento);

    /// <summary>Percentual do aditado sobre o valor original (para saber quanto o contrato ja cresceu). Null sem valor original.</summary>
    public static decimal? PercentualAditado(ObraLida obra) =>
        obra.ValorEmpreendimento is { } original && original != 0 && obra.ValorAditado is { } aditado
            ? Math.Round(aditado / original * 100m, 1, MidpointRounding.AwayFromZero)
            : null;
}
