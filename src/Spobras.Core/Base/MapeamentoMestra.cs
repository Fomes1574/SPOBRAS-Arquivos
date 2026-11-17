namespace Spobras.Core.Base;

public enum TabelaMestra
{
    Obras,
    Medicoes,
    Empenhos,
}

/// <summary>Uma coluna que o app le da planilha mestra: o nome padrao (docs/FONTES.md) e como aparece na tela.</summary>
public sealed record CampoMestra(TabelaMestra Tabela, string Padrao, string Rotulo, bool Obrigatorio = true)
{
    public string Chave => $"{Tabela}|{Padrao}";
}

/// <summary>Todas as colunas usadas pelo app, na ordem em que aparecem nas Configuracoes da base.</summary>
public static class CamposMestra
{
    public static string TabelaPadrao(TabelaMestra t) => t switch
    {
        TabelaMestra.Obras => "contratos_VW_ESCOLAS_CONTRATOS_MEDIDOS_PAGOS",
        TabelaMestra.Medicoes => "preCadastro_VW_RELATORIO_MEDICOES",
        _ => "contratos_VW_EMPENHOS_SOF",
    };

    public static string NomeDaTabela(TabelaMestra t) => t switch
    {
        TabelaMestra.Obras => "Obras",
        TabelaMestra.Medicoes => "Medições",
        _ => "Empenhos",
    };

    private static CampoMestra O(string padrao, string rotulo) => new(TabelaMestra.Obras, padrao, rotulo);

    private static CampoMestra M(string padrao, string rotulo, bool obrigatorio = true) => new(TabelaMestra.Medicoes, padrao, rotulo, obrigatorio);

    private static CampoMestra E(string padrao, string rotulo) => new(TabelaMestra.Empenhos, padrao, rotulo);

    public static readonly IReadOnlyList<CampoMestra> Todos =
    [
        O("CONTRATO", "Contrato"), O("EMPREENDIMENTO", "Escola"), O("CONTRATADA", "Contratada"), O("PORTARIA", "Portaria"),
        O("nmLote", "Lote"), O("INÍCIO EXECUÇÃO", "Data da OS"), O("TÉRMINO EXECUÇÃO", "Término"), O("DATA DE ASSINATURA", "Data de assinatura"),
        O("STATUS CONTRATO", "Status do contrato"), O("STATUS", "Status"), O("SITUAÇÃO", "Situação"),
        O("VALOR EMPREENDIMENTO", "Valor original"), O("VALOR ADITADO", "Aditado"), O("VALOR CONTRATADO COM ADITAMENTO", "Contratado com aditamento"),
        O("VALOR MEDIDO", "Medido"), O("VALOR PAGO", "Pago"), O("SALDO A MEDIR", "Saldo a medir"), O("SALDO A PAGAR", "Saldo a pagar"),
        O("DOTACAO", "Dotação"), O("ATIVIDADE", "Dotação resumida"), O("PROCESSO SEI", "Processo SEI"), O("ESCOPO", "Escopo"),
        O("MODALIDADE", "Modalidade"), O("TIPO", "Tipo"), O("TIPO DE OBRA", "Tipo de obra"),

        M("NR. DO CONTRATO", "Contrato"), M("NR. DA MEDIÇÃO", "Número da medição"), M("EMPREENDIMENTO", "Escola"),
        M("STATUS DA MEDIÇÃO", "Status da medição"), M("VALOR CONSIDERADO", "Valor considerado"), M("VALOR REJUSTE", "Reajuste", false),
        M("STATUS DO CONTRATO", "Status do contrato", false), M("DATA DE PROTOCOLO", "Data de protocolo", false), M("DATA DE INÍCIO", "Início"), M("DATA FIM", "Fim"),
        M("VALOR PROTOCOLADO", "Protocolado"), M("VALOR LIBERADO", "Liberado"), M("VALOR PAGO", "Pago"),
        M("CONTRATO RECEITA", "Contrato de receita"), M("DATA DE PAGAMENTO", "Data de pagamento"),

        E("Nr.Contrato", "Contrato"), E("Nr.Empenho", "Empenho"), E("Data do Empenho", "Data"), E("Ano do Empenho", "Ano"),
        E("Processo SEI", "Processo SEI"), E("Vl.Total Empenhado", "Total empenhado"), E("Vl.Empenhado Liquido", "Líquido"),
        E("Vl. Liquidado", "Liquidado"), E("Vl.Anulado", "Anulado"), E("Vl.Pago no Exercício", "Pago no exercício"),
        E("Vl.Pago Restos", "Pago de restos"), E("Credor", "Credor"), E("PORTARIA", "Portaria"), E("SALDO FINAL", "Saldo final"),
    ];

    public static CampoMestra Campo(TabelaMestra tabela, string padrao) =>
        Todos.First(c => c.Tabela == tabela && c.Padrao == padrao);
}

/// <summary>
/// Onde o app encontra cada tabela e coluna da planilha mestra. Vazio = os nomes padrao. So muda quando a planilha
/// muda de estrutura (Configuracoes da base); vale so no aparelho em que foi salvo.
/// </summary>
public sealed record MapeamentoMestra
{
    /// <summary>Nome da tabela por tabela do app ("Obras" → nome na planilha). So os que fogem do padrao.</summary>
    public IReadOnlyDictionary<string, string> Tabelas { get; init; } = new Dictionary<string, string>();

    /// <summary>Coluna por campo (chave "Obras|CONTRATO" → nome na planilha). So os que fogem do padrao.</summary>
    public IReadOnlyDictionary<string, string> Colunas { get; init; } = new Dictionary<string, string>();

    public static readonly MapeamentoMestra Padrao = new();

    public bool EPadrao => Tabelas.Count == 0 && Colunas.Count == 0;

    public string Tabela(TabelaMestra t) => Tabelas.TryGetValue(t.ToString(), out var nome) && !string.IsNullOrWhiteSpace(nome) ? nome : CamposMestra.TabelaPadrao(t);

    public string Coluna(CampoMestra c) => Colunas.TryGetValue(c.Chave, out var nome) && !string.IsNullOrWhiteSpace(nome) ? nome : c.Padrao;

    public MapeamentoMestra ComTabela(TabelaMestra t, string nome)
    {
        var d = new Dictionary<string, string>(Tabelas);
        if (string.Equals(nome, CamposMestra.TabelaPadrao(t), StringComparison.OrdinalIgnoreCase))
        {
            d.Remove(t.ToString());
        }
        else
        {
            d[t.ToString()] = nome;
        }

        return this with { Tabelas = d };
    }

    public MapeamentoMestra ComColuna(CampoMestra c, string nome)
    {
        var d = new Dictionary<string, string>(Colunas);
        if (string.Equals(nome, c.Padrao, StringComparison.OrdinalIgnoreCase))
        {
            d.Remove(c.Chave);
        }
        else
        {
            d[c.Chave] = nome;
        }

        return this with { Colunas = d };
    }
}
