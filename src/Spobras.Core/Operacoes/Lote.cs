namespace Spobras.Core.Operacoes;

public enum TipoLote
{
    Renomear,
    Mover,

    /// <summary>Arquivos gerados pelo app (zip, relatorio...). Ficam no historico, mas nao sao desfeitos.</summary>
    Criar,
}

/// <summary>Estados da especificacao 3.4.</summary>
public enum EstadoItem
{
    Pendente,
    EmExecucao,
    Concluido,
    Ignorado,
    Falhou,
    Cancelado,
}

/// <summary>Um arquivo dentro de um lote: de onde sai, para onde vai e o que aconteceu.</summary>
public sealed class ItemLote
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public int Ordem { get; init; }
    public required string Origem { get; init; }
    public required string Destino { get; init; }
    public EstadoItem Estado { get; set; } = EstadoItem.Pendente;
    public string? Mensagem { get; set; }

    /// <summary>Nome temporario usado numa troca A &lt;-&gt; B; gravado antes de o arquivo ir para ele.</summary>
    public string? Temporario { get; set; }

    /// <summary>Tamanho e data do arquivo ao fim da acao: servem para conferir antes de desfazer.</summary>
    public long? Tamanho { get; set; }
    public DateTimeOffset? ModificadoEm { get; set; }
    public DateTimeOffset? ConcluidoEm { get; set; }
}

public sealed class Lote
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required TipoLote Tipo { get; init; }
    public required string Descricao { get; init; }
    public required DateTimeOffset CriadoEm { get; init; }
    public List<ItemLote> Itens { get; init; } = [];

    /// <summary>Se este lote e o Desfazer de outro, o lote original.</summary>
    public Guid? DesfazLoteId { get; init; }

    /// <summary>Preenchido no lote original quando ele ja foi desfeito.</summary>
    public Guid? DesfeitoPorLoteId { get; set; }

    public int Contar(EstadoItem estado) => Itens.Count(i => i.Estado == estado);

    public bool TemPendentes => Itens.Any(i => i.Estado is EstadoItem.Pendente or EstadoItem.Cancelado or EstadoItem.EmExecucao);
}

public sealed record ProgressoLote(int Feitos, int Total, string? Atual);

public sealed record ResultadoLote(int Concluidos, int Falhas, int Cancelados, int Ignorados)
{
    public static ResultadoLote De(Lote lote) => new(
        lote.Contar(EstadoItem.Concluido),
        lote.Contar(EstadoItem.Falhou),
        lote.Contar(EstadoItem.Cancelado) + lote.Contar(EstadoItem.Pendente),
        lote.Contar(EstadoItem.Ignorado));
}
