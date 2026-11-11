namespace Spobras.Core.Operacoes;

/// <summary>
/// Historico por arquivo. Cada mudanca de estado e gravada na hora, para que um fechamento
/// inesperado possa ser reconciliado com o disco.
/// </summary>
public interface IHistorico
{
    void CriarLote(Lote lote);
    void AtualizarItem(Guid loteId, ItemLote item);
    void MarcarDesfeito(Guid loteId, Guid desfeitoPorLoteId);
    Lote? Obter(Guid loteId);

    /// <summary>Lotes do mais novo para o mais antigo.</summary>
    IReadOnlyList<Lote> Listar(int maximo = 100);
}

/// <summary>Historico so em memoria (testes e uso antes de o banco existir).</summary>
public sealed class HistoricoMemoria : IHistorico
{
    private readonly List<Lote> _lotes = [];

    public void CriarLote(Lote lote) => _lotes.Add(lote);

    public void AtualizarItem(Guid loteId, ItemLote item)
    {
        // Os itens sao os mesmos objetos do lote guardado; nada a copiar.
    }

    public void MarcarDesfeito(Guid loteId, Guid desfeitoPorLoteId)
    {
        if (Obter(loteId) is { } lote)
        {
            lote.DesfeitoPorLoteId = desfeitoPorLoteId;
        }
    }

    public Lote? Obter(Guid loteId) => _lotes.FirstOrDefault(l => l.Id == loteId);

    public IReadOnlyList<Lote> Listar(int maximo = 100) =>
        [.. _lotes.OrderByDescending(l => l.CriadoEm).Take(maximo)];
}
