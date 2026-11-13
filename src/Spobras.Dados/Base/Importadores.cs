using Spobras.Core.Base;
using Spobras.Core.Planilhas;

namespace Spobras.Dados.Base;

/// <summary>Importacao recusada: a base anterior continua valendo. A mensagem diz o que faltou.</summary>
public sealed class ImportacaoInvalidaException(string mensagem) : Exception(mensagem);

public sealed record MestraImportada(
    IReadOnlyList<ObraLida> Obras, IReadOnlyList<MedicaoLida> Medicoes, IReadOnlyList<EmpenhoLido> Empenhos,
    IReadOnlyDictionary<long, string?> DotacoesOriginais, IReadOnlyList<string> Avisos);

/// <summary>
/// Le a planilha mestra por nome de tabela e de coluna (docs/FONTES.md). Valida tudo antes de devolver:
/// tabela ou coluna que sumiu faz a importacao inteira ser recusada, em vez de entrar vazia.
/// </summary>
public static class ImportadorMestra
{
    public const string TabelaObras = "contratos_VW_ESCOLAS_CONTRATOS_MEDIDOS_PAGOS";
    public const string TabelaMedicoes = "preCadastro_VW_RELATORIO_MEDICOES";
    public const string TabelaEmpenhos = "contratos_VW_EMPENHOS_SOF";

    public static MestraImportada Importar(ILeitorPlanilha planilha)
    {
        var existentes = planilha.Tabelas().Select(t => t.Nome).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (string nome in new[] { TabelaObras, TabelaMedicoes, TabelaEmpenhos })
        {
            if (!existentes.Contains(nome))
            {
                throw new ImportacaoInvalidaException($"A tabela \"{nome}\" não foi encontrada na planilha. A base anterior foi mantida.");
            }
        }

        var avisos = new List<string>();
        var medicoes = Medicoes(planilha.LerTabela(TabelaMedicoes));
        var (obras, originais) = Obras(planilha.LerTabela(TabelaObras), medicoes, avisos);
        var empenhos = Empenhos(planilha.LerTabela(TabelaEmpenhos));
        if (obras.Count == 0)
        {
            throw new ImportacaoInvalidaException("A tabela de contratos veio sem nenhuma linha. A base anterior foi mantida.");
        }

        return new MestraImportada(obras, medicoes, empenhos, originais, avisos);
    }

    private static (List<ObraLida>, Dictionary<long, string?>) Obras(TabelaLida t, List<MedicaoLida> medicoes, List<string> avisos)
    {
        var c = new Colunas(t);
        int contrato = c["CONTRATO"], empreendimento = c["EMPREENDIMENTO"], contratada = c["CONTRATADA"], portaria = c["PORTARIA"],
            dotacao = c["DOTACAO"], atividade = c["ATIVIDADE"], lote = c["nmLote"], modalidade = c["MODALIDADE"], tipo = c["TIPO"],
            tipoObra = c["TIPO DE OBRA"], sei = c["PROCESSO SEI"], status = c["STATUS"], statusContrato = c["STATUS CONTRATO"],
            situacao = c["SITUAÇÃO"], escopo = c["ESCOPO"], assinatura = c["DATA DE ASSINATURA"], inicio = c["INÍCIO EXECUÇÃO"],
            termino = c["TÉRMINO EXECUÇÃO"], vEmp = c["VALOR EMPREENDIMENTO"], vAdit = c["VALOR ADITADO"],
            vTotal = c["VALOR CONTRATADO COM ADITAMENTO"], vMedido = c["VALOR MEDIDO"], vPago = c["VALOR PAGO"],
            sMedir = c["SALDO A MEDIR"], sPagar = c["SALDO A PAGAR"];
        c.Conferir(t.Nome);

        // Contrato de receita: a planilha informa por contrato, nas medicoes.
        var receitas = medicoes
            .Where(m => !string.IsNullOrWhiteSpace(m.ContratoReceita))
            .GroupBy(m => m.Contrato, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(m => m.ContratoReceita!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList(), StringComparer.OrdinalIgnoreCase);

        var obras = new List<ObraLida>(t.Linhas.Count);
        var originais = new Dictionary<long, string?>();
        var vistos = new HashSet<(string, string)>();
        int semChave = 0, repetidas = 0, dotacoesCorrigidas = 0;
        long id = 0;
        foreach (var l in t.Linhas)
        {
            string? ct = Texto(l[contrato]), emp = Texto(l[empreendimento]);
            if (ct is null || emp is null)
            {
                semChave++;
                continue;
            }

            if (!vistos.Add((ct.ToUpperInvariant(), emp.ToUpperInvariant())))
            {
                repetidas++;
                continue;
            }

            id++;
            string? dotacaoBruta = Texto(l[dotacao]);
            DotacaoNormalizada? norm = dotacaoBruta is null ? null : Normalizacao.Dotacao(dotacaoBruta);
            if (norm is { Corrigida: true })
            {
                dotacoesCorrigidas++;
                originais[id] = norm.Original;
            }

            string? receita = receitas.TryGetValue(ct, out var lista) ? string.Join(" | ", lista) : null;
            obras.Add(new ObraLida
            {
                Id = id,
                Contrato = ct,
                Empreendimento = emp,
                Contratada = Texto(l[contratada]),
                Portaria = Texto(l[portaria]),
                ContratoReceita = receita,
                Dotacao = norm?.Completa,
                Atividade = Texto(l[atividade]),
                Lote = Texto(l[lote]),
                Modalidade = Texto(l[modalidade]),
                Tipo = Texto(l[tipo]),
                TipoDeObra = Texto(l[tipoObra]),
                ProcessoSei = Texto(l[sei]),
                Status = Texto(l[status]),
                StatusContrato = Texto(l[statusContrato]),
                Situacao = Texto(l[situacao]),
                Escopo = Texto(l[escopo]),
                DataAssinatura = Data(l[assinatura]),
                DataOs = Data(l[inicio]),
                Termino = Data(l[termino]),
                ValorEmpreendimento = Reais(l[vEmp]),
                ValorAditado = Reais(l[vAdit]),
                ValorContratadoComAditamento = Reais(l[vTotal]),
                ValorMedido = Reais(l[vMedido]),
                ValorPago = Reais(l[vPago]),
                SaldoAMedir = Reais(l[sMedir]),
                SaldoAPagar = Reais(l[sPagar]),
            });
        }

        if (semChave > 0)
        {
            avisos.Add($"{semChave} linha(s) sem contrato ou sem empreendimento ficaram de fora.");
        }

        if (repetidas > 0)
        {
            avisos.Add($"{repetidas} linha(s) repetiam o mesmo contrato e empreendimento; ficou a primeira de cada.");
        }

        if (dotacoesCorrigidas > 0)
        {
            avisos.Add($"{dotacoesCorrigidas} dotação(ões) estavam com espaço no lugar do ponto e foram padronizadas (o texto original foi guardado).");
        }

        int variasReceitas = receitas.Count(r => r.Value.Count > 1);
        if (variasReceitas > 0)
        {
            avisos.Add($"{variasReceitas} contrato(s) aparecem com mais de um contrato de receita nas medições; os dois são mostrados.");
        }

        return (obras, originais);
    }

    private static List<MedicaoLida> Medicoes(TabelaLida t)
    {
        var c = new Colunas(t);
        int contrato = c["NR. DO CONTRATO"], numero = c["NR. DA MEDIÇÃO"], emp = c["EMPREENDIMENTO"], inicio = c["DATA DE INÍCIO"],
            fim = c["DATA FIM"], protocolado = c["VALOR PROTOCOLADO"], liberado = c["VALOR LIBERADO"], considerado = c["VALOR CONSIDERADO"],
            pago = c["VALOR PAGO"], status = c["STATUS DA MEDIÇÃO"], receita = c["CONTRATO RECEITA"], pagamento = c["DATA DE PAGAMENTO"];
        c.Conferir(t.Nome);

        // Opcionais: sem elas a importacao continua, so sem reajuste e sem o status do contrato na medicao.
        // "VALOR REJUSTE" e o nome como esta na planilha (sem o "a").
        int reajuste = t.Indice("VALOR REJUSTE"), statusContrato = t.Indice("STATUS DO CONTRATO");

        var lista = new List<MedicaoLida>(t.Linhas.Count);
        foreach (var l in t.Linhas)
        {
            if (Texto(l[contrato]) is { } ct)
            {
                lista.Add(new MedicaoLida(ct, Texto(l[numero]), Texto(l[emp]), Data(l[inicio]), Data(l[fim]), Reais(l[protocolado]),
                    Reais(l[liberado]), Reais(l[considerado]), Reais(l[pago]), Texto(l[status]), Texto(l[receita]), Data(l[pagamento]),
                    reajuste >= 0 ? Reais(l[reajuste]) : null, statusContrato >= 0 ? Texto(l[statusContrato]) : null));
            }
        }

        return lista;
    }

    private static List<EmpenhoLido> Empenhos(TabelaLida t)
    {
        var c = new Colunas(t);
        int contrato = c["Nr.Contrato"], numero = c["Nr.Empenho"], data = c["Data do Empenho"], ano = c["Ano do Empenho"],
            sei = c["Processo SEI"], total = c["Vl.Total Empenhado"], liquido = c["Vl.Empenhado Liquido"], liquidado = c["Vl. Liquidado"],
            anulado = c["Vl.Anulado"], pagoEx = c["Vl.Pago no Exercício"], pagoRestos = c["Vl.Pago Restos"], credor = c["Credor"],
            portaria = c["PORTARIA"], saldo = c["SALDO FINAL"];
        c.Conferir(t.Nome);

        var lista = new List<EmpenhoLido>(t.Linhas.Count);
        foreach (var l in t.Linhas)
        {
            lista.Add(new EmpenhoLido(Texto(l[contrato]), Texto(l[numero]), Data(l[data]), l[ano] is decimal a ? (int)a : null, Texto(l[sei]),
                Reais(l[total]), Reais(l[liquido]), Reais(l[liquidado]), Reais(l[anulado]), Reais(l[pagoEx]), Reais(l[pagoRestos]),
                Texto(l[credor]), Texto(l[portaria]), Reais(l[saldo])));
        }

        return lista;
    }

    /// <summary>Localiza colunas pelo nome e junta as que faltarem numa mensagem so.</summary>
    private sealed class Colunas(TabelaLida tabela)
    {
        private readonly List<string> _faltando = [];

        public int this[string nome]
        {
            get
            {
                int i = tabela.Indice(nome);
                if (i < 0)
                {
                    _faltando.Add(nome);
                }

                return Math.Max(0, i);
            }
        }

        public void Conferir(string nomeTabela)
        {
            if (_faltando.Count > 0)
            {
                throw new ImportacaoInvalidaException(
                    $"Na tabela \"{nomeTabela}\" não foi encontrada a coluna {string.Join(", ", _faltando.Select(f => $"\"{f}\""))}. " +
                    "A planilha pode ter mudado de estrutura. A base anterior foi mantida.");
            }
        }
    }

    // Codigos sao texto: numero vindo do Excel vira texto sem casas ("0", "2878").
    internal static string? Texto(object? valor) => valor switch
    {
        null => null,
        string s => string.IsNullOrWhiteSpace(s) ? null : s.Trim(),
        decimal d => d.ToString(System.Globalization.CultureInfo.InvariantCulture),
        DateTime dt => dt.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture),
        _ => valor.ToString(),
    };

    internal static DateOnly? Data(object? valor) => valor is DateTime d ? DateOnly.FromDateTime(d) : null;

    // Dinheiro em decimal com duas casas (o Excel guarda 465897.1200000001).
    internal static decimal? Reais(object? valor) => valor is decimal d ? Math.Round(d, 2, MidpointRounding.AwayFromZero) : null;
}

public sealed record VinculoImportado(
    string Portaria, int? Numero, int? Ano, string ContratoReceita, string? Finalidade, string Dotacao, string? DotacaoOriginal,
    string? Resumida, string? Descricao);

public sealed record DotacoesImportadas(IReadOnlyList<VinculoImportado> Vinculos, IReadOnlyList<string> Avisos);

/// <summary>
/// Le "Dotações Completas.xlsx": sem tabelas nomeadas, entao acha os cabecalhos pelo texto (nao pela celula).
/// A aba Planilha2 e a fonte; a Planilha1 serve so para conferir.
/// </summary>
public static class ImportadorDotacoes
{
    public const string AbaPrincipal = "Planilha2";
    public const string AbaConferencia = "Planilha1";

    public static DotacoesImportadas Importar(ILeitorPlanilha planilha)
    {
        if (!planilha.NomesDasAbas().Contains(AbaPrincipal, StringComparer.OrdinalIgnoreCase))
        {
            throw new ImportacaoInvalidaException($"A aba \"{AbaPrincipal}\" não foi encontrada. A base anterior foi mantida.");
        }

        var grade = planilha.LerAba(AbaPrincipal);
        var (linha, colunas) = Cabecalhos(grade,
            ["portarias"], ["contratos de receita"], ["dotações completas", "dotacoes completas"],
            ["dotação resumida + descrição", "dotacao resumida + descricao"], ["dotação resumida", "dotacao resumida"]);
        if (linha < 0)
        {
            throw new ImportacaoInvalidaException(
                $"Na aba \"{AbaPrincipal}\" não foram encontrados os cabeçalhos Portarias, Contratos de receita e Dotações Completas. A base anterior foi mantida.");
        }

        int cPortaria = colunas[0], cReceita = colunas[1], cCompleta = colunas[2], cDescricao = colunas[3], cResumida = colunas[4];
        var avisos = new List<string>();
        var vinculos = new List<VinculoImportado>();
        var chaves = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = linha + 1; i < grade.Count; i++)
        {
            var l = grade[i];
            string? rotulo = ImportadorMestra.Texto(l[cPortaria]), receita = ImportadorMestra.Texto(l[cReceita]), completa = ImportadorMestra.Texto(l[cCompleta]);
            if (rotulo is null && receita is null && completa is null)
            {
                continue;
            }

            if (rotulo is null || receita is null || completa is null)
            {
                avisos.Add($"Linha {i + 1}: falta portaria, contrato de receita ou dotação; a linha ficou de fora.");
                continue;
            }

            var lida = Normalizacao.Portaria(rotulo);
            // "Portaria 3 (Construção Nova)" e a Portaria 3; o complemento e a finalidade do contrato de receita.
            string portaria = lida.Complemento is null ? rotulo : rotulo[..rotulo.IndexOf('(')].Trim();
            var dotacao = Normalizacao.Dotacao(completa);
            if (dotacao.Corrigida)
            {
                avisos.Add($"Linha {i + 1}: dotação \"{dotacao.Original}\" corrigida para \"{dotacao.Completa}\" (espaço no lugar do ponto).");
            }

            // A chave do vinculo e portaria + contrato de receita + dotacao.
            if (!chaves.Add($"{portaria}|{receita}|{dotacao.Completa}"))
            {
                avisos.Add($"Linha {i + 1}: vínculo repetido ({portaria}, {receita}, {dotacao.Completa}); ficou uma vez só.");
                continue;
            }

            vinculos.Add(new VinculoImportado(portaria, lida.Numero, lida.Ano, receita, lida.Complemento, dotacao.Completa,
                dotacao.Corrigida ? dotacao.Original : null,
                cResumida >= 0 ? ImportadorMestra.Texto(l[cResumida]) : null,
                cDescricao >= 0 ? ImportadorMestra.Texto(l[cDescricao]) : null));
        }

        if (vinculos.Count == 0)
        {
            throw new ImportacaoInvalidaException($"A aba \"{AbaPrincipal}\" não tem nenhum vínculo de portaria e dotação. A base anterior foi mantida.");
        }

        Conferir(planilha, vinculos, avisos);
        return new DotacoesImportadas(vinculos, avisos);
    }

    /// <summary>Compara com a Planilha1: dotacao ou vinculo portaria/contrato que so existe num dos lados vira aviso.</summary>
    private static void Conferir(ILeitorPlanilha planilha, List<VinculoImportado> vinculos, List<string> avisos)
    {
        if (!planilha.NomesDasAbas().Contains(AbaConferencia, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        var grade = planilha.LerAba(AbaConferencia);
        var (linhaDot, colDot) = Cabecalhos(grade, ["dotações completas", "dotacoes completas"]);
        if (linhaDot >= 0)
        {
            var naConferencia = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = linhaDot + 1; i < grade.Count && ImportadorMestra.Texto(grade[i][colDot[0]]) is { } texto; i++)
            {
                naConferencia.Add(Normalizacao.Dotacao(texto).Completa);
            }

            foreach (string so in vinculos.Select(v => v.Dotacao).Distinct().Where(d => !naConferencia.Contains(d)))
            {
                avisos.Add($"Conferência: a dotação {so} está na {AbaPrincipal} mas não na lista da {AbaConferencia}.");
            }
        }

        var (linhaPort, colPort) = Cabecalhos(grade, ["portarias"], ["contratos de receita"]);
        if (linhaPort >= 0)
        {
            var pares = vinculos.Select(v => $"{v.Portaria}{(v.Finalidade is null ? "" : $" ({v.Finalidade})")}|{v.ContratoReceita}").ToHashSet(StringComparer.OrdinalIgnoreCase);
            for (int i = linhaPort + 1; i < grade.Count; i++)
            {
                string? p = ImportadorMestra.Texto(grade[i][colPort[0]]), r = ImportadorMestra.Texto(grade[i][colPort[1]]);
                if (p is not null && r is not null && !pares.Contains($"{p}|{r}"))
                {
                    avisos.Add($"Conferência: {p} com o contrato {r} está na {AbaConferencia} mas não na {AbaPrincipal}.");
                }
            }
        }
    }

    /// <summary>Acha a linha em que todos os cabecalhos obrigatorios aparecem; devolve a coluna de cada um (-1 se opcional ausente).</summary>
    private static (int Linha, int[] Colunas) Cabecalhos(IReadOnlyList<object?[]> grade, params string[][] cabecalhos)
    {
        for (int linha = 0; linha < grade.Count; linha++)
        {
            var textos = grade[linha].Select(v => Normalizacao.Busca(v as string).Trim()).ToArray();
            var colunas = new int[cabecalhos.Length];
            var usadas = new HashSet<int>();
            for (int h = 0; h < cabecalhos.Length; h++)
            {
                colunas[h] = -1;
                // O cabecalho mais especifico vem antes na lista de pedidos, e cada coluna so serve a um.
                for (int col = 0; col < textos.Length; col++)
                {
                    if (!usadas.Contains(col) && cabecalhos[h].Any(c => textos[col].StartsWith(Normalizacao.Busca(c), StringComparison.Ordinal)))
                    {
                        colunas[h] = col;
                        usadas.Add(col);
                        break;
                    }
                }
            }

            // Os tres primeiros (ou todos, se forem menos) sao obrigatorios.
            if (colunas.Take(Math.Min(3, colunas.Length)).All(col => col >= 0))
            {
                return (linha, colunas);
            }
        }

        return (-1, []);
    }
}
