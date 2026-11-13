using Spobras.Core.Planilhas;

namespace Spobras.Testes;

/// <summary>Planilha em memoria com a mesma estrutura das planilhas reais (dados inventados).</summary>
public sealed class PlanilhaFalsa : ILeitorPlanilha
{
    public Dictionary<string, TabelaLida> TabelasPorNome { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, List<object?[]>> AbasPorNome { get; } = new(StringComparer.OrdinalIgnoreCase);

    public void Dispose()
    {
    }

    public IReadOnlyList<DescricaoTabela> Tabelas() =>
        [.. TabelasPorNome.Values.Select(t => new DescricaoTabela(t.Nome, t.Aba, t.Linhas.Count, t.Colunas))];

    public IReadOnlyList<string> NomesDasAbas() => [.. AbasPorNome.Keys];

    public TabelaLida LerTabela(string nome) => TabelasPorNome[nome];

    public IReadOnlyList<object?[]> LerAba(string nomeDaAba) => AbasPorNome[nomeDaAba];

    public PlanilhaFalsa Tabela(string nome, string[] colunas, params object?[][] linhas)
    {
        TabelasPorNome[nome] = new TabelaLida(nome, nome, colunas, linhas);
        return this;
    }
}

public static class BaseSintetica
{
    public const string P3De2021 = "PORTARIA EXEMPLO SME/SIURB Nº 03 (2021)";
    public const string P3De2025 = "PORTARIA EXEMPLO SME/SIURB Nº 03 (2025)";
    public const string P6 = "PORTARIA EXEMPLO SME/SIURB Nº 06 (2022)";

    public static readonly string[] ColunasObras =
    [
        "CONTRATADA", "CONTRATO", "DATA DE ASSINATURA", "INÍCIO EXECUÇÃO", "TÉRMINO EXECUÇÃO", "EMPREENDIMENTO", "VALOR EMPREENDIMENTO",
        "VALOR ADITADO", "VALOR MEDIDO", "VALOR PAGO", "STATUS", "ATIVIDADE", "DOTACAO", "nmLote", "MODALIDADE", "TIPO", "PROCESSO SEI",
        "STATUS CONTRATO", "SITUAÇÃO", "ESCOPO", "TIPO DE OBRA", "VALOR CONTRATADO COM ADITAMENTO", "SALDO A MEDIR", "SALDO A PAGAR", "PORTARIA",
    ];

    public static readonly string[] ColunasMedicoes =
    [
        "NR. DO CONTRATO", "NR. DA MEDIÇÃO", "EMPREENDIMENTO", "DATA DE INÍCIO", "DATA FIM", "VALOR PROTOCOLADO", "VALOR LIBERADO",
        "VALOR CONSIDERADO", "VALOR PAGO", "STATUS DA MEDIÇÃO", "CONTRATO RECEITA", "DATA DE PAGAMENTO", "VALOR REJUSTE", "STATUS DO CONTRATO",
    ];

    public static readonly string[] ColunasEmpenhos =
    [
        "Nr.Contrato", "Nr.Empenho", "Data do Empenho", "Ano do Empenho", "Processo SEI", "Vl.Total Empenhado", "Vl.Empenhado Liquido",
        "Vl. Liquidado", "Vl.Anulado", "Vl.Pago no Exercício", "Vl.Pago Restos", "Credor", "PORTARIA", "SALDO FINAL",
    ];

    public static object?[] Obra(
        string contrato, string escola, string portaria, DateTime? os, decimal? saldoPagar, decimal? pago = 100m, string? dotacao = "16.10.12.365.3010.2878.3390.3900 00",
        string? atividade = "2878", string status = "ANDAMENTO", string lote = "LOTE 01") =>
    [
        "Construtora Exemplo Ltda", contrato, new DateTime(2024, 1, 5), os, new DateTime(2027, 1, 5), escola, 10000.129999m,
        0m, 5000m, pago, status, atividade, dotacao, lote, "Manutenção", "EMEF", "7910.2024/0000001-0",
        "VIGENTE", "no prazo", "Reforma geral", "CONSERVAÇÃO E MANUTENÇÃO", 10000.13m, 5000m, saldoPagar, portaria,
    ];

    public static object?[] Medicao(string contrato, string numero, decimal pago, string? receita, string escola = "EMEF Alfa") =>
        [contrato, numero, escola, new DateTime(2026, 1, 1), new DateTime(2026, 1, 31), pago, pago, pago, pago, "PAGAMENTO EFETUADO", receita, new DateTime(2026, 2, 20), null, "VIGENTE"];

    /// <summary>Medicao ainda nao paga (em tramite): valor considerado, reajuste e o status do contrato que ela traz.</summary>
    public static object?[] MedicaoPendente(string contrato, string numero, string status, decimal considerado, decimal? reajuste, string escola, string statusContrato = "VIGENTE") =>
        [contrato, numero, escola, new DateTime(2026, 3, 1), new DateTime(2026, 3, 31), considerado, considerado, considerado, 0m, status, "015/EX/2025", null, reajuste, statusContrato];

    public static object?[] Empenho(string contrato, string numero, decimal total) =>
        [contrato, numero, new DateTime(2026, 1, 15), 2026m, "7910.2026/0000002-0", total, total, 0m, 0m, 0m, 0m, "Credor Exemplo", "GERENCIAMENTO PORTARIA 03 (2021)", total];

    /// <summary>
    /// Mestra sintetica: um contrato com duas escolas e datas de OS diferentes, a mesma escola em dois contratos,
    /// duas "Portaria 3" de anos diferentes, saldo zero, saldo ausente e varias medicoes por contrato.
    /// </summary>
    public static PlanilhaFalsa Mestra() => new PlanilhaFalsa()
        .Tabela("contratos_VW_ESCOLAS_CONTRATOS_MEDIDOS_PAGOS", ColunasObras,
            Obra("001/EX/2024", "EMEF Alfa", P3De2021, new DateTime(2026, 1, 10), 1500m),
            Obra("001/EX/2024", "CEI Beta", P3De2021, new DateTime(2026, 3, 1), 0m, dotacao: "16.10.12.365.3010.3360.4490.5100.00", atividade: "3360"),
            Obra("002/EX/2024", "EMEI Gama", P3De2021, null, null, dotacao: null, atividade: null, status: "CONCLUIDO"),
            Obra("003/EX/2025", "EMEF Alfa", P3De2025, new DateTime(2026, 12, 31), 999.99m, lote: "LOTE 07"),
            Obra("004/EX/2025", "CEU Delta", P6, new DateTime(2025, 6, 15), 1000m, atividade: "3364", dotacao: "16.10.12.368.3010.3364.4490.5100.00"))
        .Tabela("preCadastro_VW_RELATORIO_MEDICOES", ColunasMedicoes,
            Medicao("001/EX/2024", "00001", 100m, "015/EX/2025"),
            Medicao("001/EX/2024", "00002", 200m, "015/EX/2025"),
            Medicao("001/EX/2024", "00003", 300m, "015/EX/2025", "CEI Beta"),
            Medicao("003/EX/2025", "00001", 50m, "016/EX/2026"),
            Medicao("003/EX/2025", "00002", 60m, "016/EX/2026"),
            Medicao("004/EX/2025", "00001", 70m, "015/EX/2025, 128/EX/2021", "CEU Delta"),
            MedicaoPendente("004/EX/2025", "00002", "ANÁLISE GEC", 80m, 5.5m, "CEU Delta"),
            MedicaoPendente("004/EX/2025", "00003", "DEVOLUÇÃO FISCAL", 20m, null, "CEU Delta"),
            MedicaoPendente("004/EX/2025", "00004", "LIBERADO PARA PAGAMENTO", 1000m, null, "CEU Delta", "SOB GESTÃO DA SIURB"),
            Medicao("999/OUTRO/2020", "00001", 5m, null, "Obra de outro programa"))
        .Tabela("contratos_VW_EMPENHOS_SOF", ColunasEmpenhos,
            Empenho("015/EX/2025", "100001", 5000m),
            Empenho("015/EX/2025", "100002", 2500.5m));

    /// <summary>Dotações Completas sintetica, com cabecalhos fora da primeira linha e um espaco no lugar do ponto.</summary>
    public static PlanilhaFalsa Dotacoes()
    {
        var planilha = new PlanilhaFalsa();
        planilha.AbasPorNome["Planilha2"] =
        [
            new object?[7],
            [null, "Portarias", "Contratos de receita (Por onde vem o dinheiro para obra)", "Dotações Completas", "Dotação Resumida", "Dotação Resumida + Descrição", null],
            [null, "Portaria 2 (2024)", "016/EX/2026", "16.10.12.368.3010.3363.4490.3900.00", "3363", "Construção de CEU", null],
            [null, "Portaria 3", "015/EX/2025", "16.10.12.365.3010.2878.3390.3900 00", "2878", "Conservação e Manutenção", null],
            [null, "Portaria 3", "015/EX/2025", "16.10.12.365.3025.3362.4490.5100.00", "3362", "Ampliação e Reforma", null],
            [null, "Portaria 3 (Construção Nova)", "164/EX/2025", "16.10.12.365.3010.3359.4490.5100.00", "3359", "Construção de CEI", null],
            [null, "Portaria 4", "301/EX/2022", "16.10.12.365.3025.3362.4490.5100.00", "3362", "Ampliação e Reforma", null],
            [null, "Portaria 4", "301/EX/2022", "16.10.12.365.3025.3362.4490.5100.00", "3362", "Ampliação e Reforma", null],
            [null, "Portaria 6", null, "16.10.12.361.3010.3366.4490.5100.00", "3366", "Linha incompleta", null],
        ];
        planilha.AbasPorNome["Planilha1"] =
        [
            [null, "Dotações Completas", "Dotação Resumida + Descrição"],
            [null, "16.10.12.368.3010.3363.4490.3900.00", "3363 - Construção de CEU"],
            [null, "16.10.12.365.3010.2878.3390.3900 00", "2878 - Conservação e Manutenção"],
            [null, "16.10.12.365.3025.3362.4490.5100.00", "3362 - Ampliação e Reforma"],
            new object?[3],
            [null, "Portarias", "Contratos de receita (Por onde vem o dinheiro para obra)"],
            [null, "Portaria 2 (2024)", "016/EX/2026"],
            [null, "Portaria 3", "015/EX/2025"],
            [null, "Portaria 3 (Construção Nova)", "164/EX/2025"],
            [null, "Portaria 4", "301/EX/2022"],
            [null, "Portaria 6", "303/EX/2022"],
        ];
        return planilha;
    }
}
