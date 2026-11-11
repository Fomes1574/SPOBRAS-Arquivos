using Spobras.Core.Abstracoes;

namespace Spobras.Core.Operacoes;

public sealed record LinhaDesfazer(ItemLote Item, bool Pode, string? Motivo);

public sealed record PlanoDesfazer(Lote Original, IReadOnlyList<LinhaDesfazer> Linhas)
{
    public int Reversiveis => Linhas.Count(l => l.Pode);
    public int Conflitos => Linhas.Count(l => !l.Pode);
    public bool PodeDesfazer => Reversiveis > 0;
}

/// <summary>
/// Desfazer de renomear/mover (especificacao 3.4): so volta o que ainda e seguro voltar.
/// Arquivo alterado depois, ou caminho anterior ocupado, vira conflito explicado e fica como esta.
/// </summary>
public sealed class Desfazer(IArquivos arquivos, IRelogio relogio)
{
    public PlanoDesfazer Planejar(Lote original)
    {
        var concluidos = original.Itens.Where(i => i.Estado == EstadoItem.Concluido).ToList();
        var motivos = new Dictionary<Guid, string?>();

        if (original.Tipo == TipoLote.Criar)
        {
            // Gerar um documento nao da ao app o direito de apaga-lo (especificacao 3.4).
            return new PlanoDesfazer(original, [.. concluidos.Select(i => new LinhaDesfazer(i, false, "Arquivo gerado pelo app: não é apagado. Use Abrir pasta."))]);
        }

        if (original.DesfeitoPorLoteId is not null)
        {
            return new PlanoDesfazer(original, [.. concluidos.Select(i => new LinhaDesfazer(i, false, "Esta ação já foi desfeita."))]);
        }

        foreach (var item in concluidos)
        {
            var info = arquivos.Info(item.Destino);
            motivos[item.Id] = info is null
                ? "O arquivo não está mais onde o app deixou."
                : info.Tamanho != item.Tamanho || info.ModificadoEm != item.ModificadoEm
                    ? "O arquivo foi alterado depois da ação. O conteúdo atual foi preservado."
                    : null;
        }

        // Caminho anterior ocupado: so e aceitavel se quem ocupa tambem vai voltar neste mesmo Desfazer (troca).
        bool mudou = true;
        while (mudou)
        {
            mudou = false;
            foreach (var item in concluidos.Where(i => motivos[i.Id] is null))
            {
                if (string.Equals(item.Origem, item.Destino, StringComparison.OrdinalIgnoreCase)
                    || !arquivos.ArquivoExiste(item.Origem))
                {
                    continue;
                }

                bool ocupanteVolta = concluidos.Any(o =>
                    o.Id != item.Id && motivos[o.Id] is null
                    && string.Equals(o.Destino, item.Origem, StringComparison.OrdinalIgnoreCase));
                if (!ocupanteVolta)
                {
                    motivos[item.Id] = "O caminho anterior está ocupado por outro arquivo. Nada foi sobrescrito.";
                    mudou = true;
                }
            }
        }

        return new PlanoDesfazer(original, [.. concluidos.Select(i => new LinhaDesfazer(i, motivos[i.Id] is null, motivos[i.Id]))]);
    }

    /// <summary>Lote inverso com os itens reversiveis, pronto para o <see cref="ExecutorLote"/>.</summary>
    public Lote CriarLote(PlanoDesfazer plano) => new()
    {
        Tipo = plano.Original.Tipo,
        Descricao = "Desfazer: " + plano.Original.Descricao,
        CriadoEm = relogio.Agora,
        DesfazLoteId = plano.Original.Id,
        Itens = [.. plano.Linhas
            .Where(l => l.Pode)
            .Reverse()
            .Select((l, ordem) => new ItemLote { Ordem = ordem, Origem = l.Item.Destino, Destino = l.Item.Origem })],
    };
}
