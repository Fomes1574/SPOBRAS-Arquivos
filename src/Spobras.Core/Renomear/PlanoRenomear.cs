namespace Spobras.Core.Renomear;

public enum SituacaoRenomear
{
    Ok,

    /// <summary>O app mudou o nome pedido (ex.: sufixo (2) por colisao) e a previa mostra o motivo.</summary>
    Ajustado,
    SemMudanca,

    /// <summary>Nao pode ser renomeado; fica fora do lote se o usuario mandar executar os validos.</summary>
    Impedido,
}

public sealed record LinhaRenomear(
    string Caminho,
    string NomeAtual,
    string NomeNovo,
    SituacaoRenomear Situacao,
    string? Motivo)
{
    public string CaminhoNovo => Path.Combine(Path.GetDirectoryName(Caminho) ?? "", NomeNovo);
    public bool Executavel => Situacao is SituacaoRenomear.Ok or SituacaoRenomear.Ajustado;
}

public sealed record PlanoRenomear(
    IReadOnlyList<LinhaRenomear> Linhas,
    IReadOnlyList<Aviso> Avisos)
{
    public int Executaveis => Linhas.Count(l => l.Executavel);
    public int Impedidos => Linhas.Count(l => l.Situacao == SituacaoRenomear.Impedido);
    public bool PodeExecutar => Executaveis > 0 && !Avisos.Any(a => a.Gravidade == Gravidade.ImpedeGerar);
}

public static class LoteRenomear
{
    /// <summary>
    /// Lote com as linhas executaveis da previa. As impedidas e as sem mudanca entram como
    /// "ignorado", com o motivo, para o historico mostrar o lote inteiro.
    /// </summary>
    public static Operacoes.Lote Criar(PlanoRenomear plano, Abstracoes.IRelogio relogio)
    {
        var itens = plano.Linhas.Select((l, ordem) => new Operacoes.ItemLote
        {
            Ordem = ordem,
            Origem = l.Caminho,
            Destino = l.CaminhoNovo,
            Estado = l.Executavel ? Operacoes.EstadoItem.Pendente : Operacoes.EstadoItem.Ignorado,
            Mensagem = l.Executavel ? null : l.Motivo,
        }).ToList();

        var validas = plano.Linhas.Where(l => l.Executavel).ToList();
        string descricao = validas.Count switch
        {
            0 => "Renomear: nenhum arquivo válido",
            1 => $"Renomeou 1 arquivo para “{Path.GetFileNameWithoutExtension(validas[0].NomeNovo)}”",
            _ => $"Renomeou {validas.Count} arquivos, de “{Path.GetFileNameWithoutExtension(validas[0].NomeNovo)}” " +
                 $"a “{Path.GetFileNameWithoutExtension(validas[^1].NomeNovo)}”",
        };

        return new Operacoes.Lote
        {
            Tipo = Operacoes.TipoLote.Renomear,
            Descricao = descricao,
            CriadoEm = relogio.Agora,
            Itens = itens,
        };
    }
}
