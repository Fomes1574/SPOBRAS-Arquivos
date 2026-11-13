namespace Spobras.Core.Base;

/// <summary>Valores financeiros da obra, com o nome que cada um tem na planilha mestra (nada e reinterpretado).</summary>
public enum CampoValor
{
    ValorEmpreendimento,
    ValorAditado,
    ValorContratadoComAditamento,
    ValorMedido,
    ValorPago,
    SaldoAMedir,
    SaldoAPagar,
}

public static class CamposValor
{
    public static string Rotulo(CampoValor campo) => campo switch
    {
        CampoValor.ValorEmpreendimento => "Valor do empreendimento",
        CampoValor.ValorAditado => "Valor aditado",
        CampoValor.ValorContratadoComAditamento => "Valor contratado com aditamento",
        CampoValor.ValorMedido => "Valor medido",
        CampoValor.ValorPago => "Valor pago",
        CampoValor.SaldoAMedir => "Saldo a medir",
        _ => "Saldo a pagar",
    };
}

/// <summary>Faixa de valor com limites inclusivos. So minimo = "a partir de"; os dois iguais = valor exato.</summary>
public sealed record FaixaValor(decimal? Minimo, decimal? Maximo, bool SemInformacao = false)
{
    public bool Vazia => Minimo is null && Maximo is null && !SemInformacao;
}

/// <summary>
/// Filtros da base. Todos opcionais e combinados no mesmo vinculo (a mesma obra precisa atender a todos),
/// por isso a ordem em que o usuario escolhe nao muda o resultado.
/// </summary>
public sealed record FiltroObras
{
    public IReadOnlyList<string> Portaria { get; init; } = [];
    public IReadOnlyList<string> Contrato { get; init; } = [];
    public IReadOnlyList<string> Escola { get; init; } = [];
    public IReadOnlyList<string> Lote { get; init; } = [];
    public IReadOnlyList<string> Empresa { get; init; } = [];
    public IReadOnlyList<string> ContratoReceita { get; init; } = [];
    public IReadOnlyList<string> Dotacao { get; init; } = [];
    public IReadOnlyList<string> Status { get; init; } = [];
    public string? Texto { get; init; }
    public DateOnly? OsDe { get; init; }
    public DateOnly? OsAte { get; init; }
    public bool SemDataOs { get; init; }
    public IReadOnlyDictionary<CampoValor, FaixaValor> Valores { get; init; } = new Dictionary<CampoValor, FaixaValor>();

    public static string TituloLotes(IReadOnlyList<string> lotes)
    {
        if (lotes.Count == 0) return "";
        if (lotes.Count == 1) return lotes[0];
        var numeros = lotes.Select(lote => lote.StartsWith("LOTE ", StringComparison.OrdinalIgnoreCase) ? lote[5..] : lote).ToArray();
        return $"Lotes {string.Join(", ", numeros[..^1])} e {numeros[^1]}";
    }

    public bool Vazio =>
        Portaria.Count == 0 && Contrato.Count == 0 && Escola.Count == 0 && Lote.Count == 0 && Empresa.Count == 0 && ContratoReceita.Count == 0 && Dotacao.Count == 0 && Status.Count == 0
        && string.IsNullOrWhiteSpace(Texto) && OsDe is null && OsAte is null && !SemDataOs && Valores.Values.All(v => v.Vazia);
}

/// <summary>Uma obra: o ponto em que contrato, escola, portaria, dotacao, datas e valores se encontram.</summary>
public sealed record ObraLida
{
    public long Id { get; init; }
    public string Contrato { get; init; } = "";
    public string Empreendimento { get; init; } = "";
    public string? Contratada { get; init; }
    public string? Portaria { get; init; }
    public string? ContratoReceita { get; init; }
    public string? Dotacao { get; init; }
    public string? Atividade { get; init; }
    public string? Lote { get; init; }
    public string? Modalidade { get; init; }
    public string? Tipo { get; init; }
    public string? TipoDeObra { get; init; }
    public string? ProcessoSei { get; init; }
    public string? Status { get; init; }
    public string? StatusContrato { get; init; }
    public string? Situacao { get; init; }
    public string? Escopo { get; init; }
    public DateOnly? DataAssinatura { get; init; }

    /// <summary>Data da Ordem de Servico = coluna INÍCIO EXECUÇÃO da mestra (decisao do usuario).</summary>
    public DateOnly? DataOs { get; init; }
    public DateOnly? Termino { get; init; }
    public decimal? ValorEmpreendimento { get; init; }
    public decimal? ValorAditado { get; init; }
    public decimal? ValorContratadoComAditamento { get; init; }
    public decimal? ValorMedido { get; init; }
    public decimal? ValorPago { get; init; }
    public decimal? SaldoAMedir { get; init; }
    public decimal? SaldoAPagar { get; init; }

    /// <summary>Obra que nao esta na planilha: incluida pelo usuario no app.</summary>
    public bool IncluidaNoApp { get; init; }

    /// <summary>Quantos campos desta obra foram mudados no app (o valor da planilha fica guardado).</summary>
    public int CamposEditados { get; init; }

    public decimal? Valor(CampoValor campo) => campo switch
    {
        CampoValor.ValorEmpreendimento => ValorEmpreendimento,
        CampoValor.ValorAditado => ValorAditado,
        CampoValor.ValorContratadoComAditamento => ValorContratadoComAditamento,
        CampoValor.ValorMedido => ValorMedido,
        CampoValor.ValorPago => ValorPago,
        CampoValor.SaldoAMedir => SaldoAMedir,
        _ => SaldoAPagar,
    };
}

public enum OrdemObras
{
    Contrato,
    Escola,
    DataOs,
    SaldoAPagar,
}

public sealed record ResultadoObras(IReadOnlyList<ObraLida> Obras, int Total);

/// <summary>Opcoes de cada filtro que ainda combinam com os outros filtros ativos (sem repeticao).</summary>
public sealed record OpcoesFiltro(
    IReadOnlyList<string> Portarias,
    IReadOnlyList<string> Contratos,
    IReadOnlyList<string> Escolas,
    IReadOnlyList<string> ContratosReceita,
    IReadOnlyList<string> Dotacoes,
    IReadOnlyList<string> Status)
{
    /// <summary>Lotes que combinam com os outros filtros ("LOTE 216"), em ordem de numero.</summary>
    public IReadOnlyList<string> Lotes { get; init; } = [];
    public IReadOnlyList<string> Empresas { get; init; } = [];
}

public sealed record MedicaoLida(
    string Contrato, string? Numero, string? Empreendimento, DateOnly? Inicio, DateOnly? Fim, decimal? ValorProtocolado,
    decimal? ValorLiberado, decimal? ValorConsiderado, decimal? ValorPago, string? Status, string? ContratoReceita, DateOnly? Pagamento,
    decimal? ValorReajuste = null, string? StatusContrato = null);

public sealed record EmpenhoLido(
    string? Contrato, string? Numero, DateOnly? Data, int? Ano, string? ProcessoSei, decimal? TotalEmpenhado, decimal? Liquido,
    decimal? Liquidado, decimal? Anulado, decimal? PagoNoExercicio, decimal? PagoRestos, string? Credor, string? Portaria, decimal? SaldoFinal);

// ---- Base de portarias e dotacoes (Dotações Completas.xlsx) ----

public sealed record PortariaBase(long Id, string Rotulo, int? Numero, int? Ano);

public sealed record VinculoBase(
    long Id, string Portaria, string ContratoReceita, string? Finalidade, string DotacaoCompleta, string? DotacaoOriginal,
    string? DotacaoResumida, string? Descricao, string Origem = "planilha")
{
    /// <summary>Identifica o vinculo entre uma leitura e outra da planilha de dotacoes.</summary>
    public string Chave => $"{Portaria}|{ContratoReceita}|{DotacaoCompleta}";
}

public enum EstadoFonte
{
    NuncaLida,
    Atualizada,

    /// <summary>A ultima tentativa falhou; os dados mostrados sao da ultima leitura valida.</summary>
    Desatualizada,
    ArquivoNaoEncontrado,
}

public sealed record FonteInfo
{
    public string Id { get; init; } = "";
    public string Nome { get; init; } = "";
    public string Caminho { get; init; } = "";
    public EstadoFonte Estado { get; init; }
    public string Mensagem { get; init; } = "";

    /// <summary>Ultima leitura valida feita pelo app.</summary>
    public DateTimeOffset? UltimaLeitura { get; init; }

    /// <summary>Data de modificacao do arquivo na ultima leitura valida.</summary>
    public DateTimeOffset? ModificadoArquivo { get; init; }
    public long TamanhoArquivo { get; init; }
    public int Linhas { get; init; }
    public int Versao { get; init; }
    public IReadOnlyList<string> Avisos { get; init; } = [];
}

public static class Fontes
{
    public const string Mestra = "mestra";
    public const string Dotacoes = "dotacoes";
    public const string NomeArquivoMestra = "Escolas_Contratos_Com_Relatorio.xlsx";
    public const string NomeArquivoDotacoes = "Dotações Completas.xlsx";
}
