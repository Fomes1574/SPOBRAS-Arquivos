using Spobras.Core;
using Spobras.Core.Cronograma;
using Spobras.Documentos.Modelos;
using Spobras.Documentos.Planilhas;
using Spobras.Testes;

namespace Spobras.Documentos.Tests;

public class CronogramaTests : IDisposable
{
    private readonly string _pasta = Directory.CreateTempSubdirectory("spobras-crono-").FullName;
    private readonly string _modelo;

    public CronogramaTests()
    {
        _modelo = Path.Combine(_pasta, "modelo.xlsx");
        XlsxSintetico.GravarModeloCronograma(_modelo);
    }

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    private static ObraCronograma Obra(int n, decimal? valor = 1000m, string dotacao = "3362") => new()
    {
        Portaria = "3", Lote = "12", Dotacao = dotacao, Nome = $"Escola {n}", Escopo = "Reforma", Valor = valor,
        Inicio = new DateOnly(2026, 11, 1), Fim = new DateOnly(2027, 2, 28),
    };

    [Fact]
    public void Analisa_o_modelo_acha_a_aba_do_resumo_e_as_dotacoes()
    {
        var m = ModeloCronograma.Analisar(_modelo);

        Assert.Equal(["Cronograma Um", "Cronograma Dois"], m.AbasDeCronograma);
        Assert.Equal("Cronograma Dois", m.AbaPrincipal); // a que a aba Parametros usa
        Assert.Equal(["2878", "3362"], m.Dotacoes);
        Assert.Equal(m.Assinatura, ModeloCronograma.Analisar(_modelo).Assinatura);
    }

    [Fact]
    public void T15_arquivo_que_nao_e_o_modelo_ou_modelo_alterado_e_recusado()
    {
        string outro = Path.Combine(_pasta, "outro.xlsx");
        XlsxSintetico.Gravar(BaseSintetica.Dotacoes(), outro);
        Assert.Contains("não tem a estrutura do modelo de cronograma", Assert.Throws<PlanilhaInvalidaException>(() => ModeloCronograma.Analisar(outro)).Message);

        // Modelo em que alguem trocou a formula da remuneracao por um numero fixo.
        string alterado = Path.Combine(_pasta, "alterado.xlsx");
        File.Copy(_modelo, alterado);
        using (var doc = DocumentFormat.OpenXml.Packaging.SpreadsheetDocument.Open(alterado, true))
        {
            foreach (var parte in doc.WorkbookPart!.WorksheetParts)
            {
                foreach (var c in parte.Worksheet!.Descendants<DocumentFormat.OpenXml.Spreadsheet.Cell>().Where(c => c.CellReference?.Value == "H3"))
                {
                    c.CellFormula = null;
                    c.CellValue = new DocumentFormat.OpenXml.Spreadsheet.CellValue("100");
                }

                parte.Worksheet.Save();
            }
        }

        var erro = Assert.Throws<PlanilhaInvalidaException>(() => ModeloCronograma.Analisar(alterado));
        Assert.Contains("H3 não tem fórmula", erro.Message);
        Assert.Contains("nada foi gerado", erro.Message);
    }

    [Fact]
    public void T12_T14_preenche_so_as_entradas_preserva_as_formulas_e_nao_altera_o_modelo()
    {
        var antes = File.ReadAllBytes(_modelo);
        string saida = Path.Combine(_pasta, "saida", "cronograma.xlsx");
        var obras = new[] { Obra(1, 100000m), Obra(2, 50000.55m, "2878") };

        var gerado = ModeloCronograma.Gerar(_modelo, "Cronograma Dois", obras, saida);

        Assert.Equal(2, gerado.Obras);
        Assert.Equal(antes, File.ReadAllBytes(_modelo)); // o modelo fica intacto
        using var leitor = new LeitorXlsx(saida);
        var grade = leitor.LerAba("Cronograma Dois");
        Assert.Equal("3", grade[2][1]);                 // portaria como texto
        Assert.Equal("3362", grade[2][3]);              // dotacao como texto (zeros e pontos preservados)
        Assert.Equal("Escola 1", grade[2][4]);
        Assert.Equal(100000m, grade[2][6]);
        Assert.Equal(50000.55m, grade[3][6]);           // decimal exato
        Assert.Equal((decimal)new DateTime(2026, 11, 1).ToOADate(), grade[2][9]);
        Assert.Null(grade[4][4]);                       // linhas que sobram ficam vazias

        using var editor = new EditorXlsx(Copia(saida));
        Assert.Equal("IF($G3=\"\",\"\",$G3*0.1)", editor.Formula("Cronograma Dois", "H3")); // formula intacta
        Assert.Equal("SUM(I3:I17)", editor.Formula("Cronograma Dois", "I18"));
        Assert.Equal("SUM(I3:I17)", editor.Formula("Cronograma Um", "I18"));                // outra aba intacta
        Assert.Null(editor.Formula("Cronograma Dois", "G3"));
        // O resultado antigo das formulas foi tirado e o arquivo pede recalculo ao abrir.
        Assert.Null(grade[17][8]);
        using var doc = DocumentFormat.OpenXml.Packaging.SpreadsheetDocument.Open(saida, false);
        Assert.True(doc.WorkbookPart!.Workbook!.CalculationProperties!.FullCalculationOnLoad!.Value);
    }

    [Fact]
    public void T15_escrita_sobre_formula_e_bloqueada_e_nada_e_publicado()
    {
        string copia = Copia(_modelo);
        using (var editor = new EditorXlsx(copia))
        {
            var erro = Assert.Throws<EscritaBloqueadaException>(() => editor.Escrever("Cronograma Um", "H3", 5m));
            Assert.Contains("tem fórmula no modelo", erro.Message);
            Assert.Throws<PlanilhaInvalidaException>(() => editor.Escrever("Aba que não existe", "A1", "x"));
        }

        string saida = Path.Combine(_pasta, "existe.xlsx");
        File.WriteAllText(saida, "ja existia");
        Assert.Throws<IOException>(() => ModeloCronograma.Gerar(_modelo, "Cronograma Um", [Obra(1)], saida));
        Assert.Equal("ja existia", File.ReadAllText(saida));

        // Aba errada: falha sem deixar arquivo pela metade.
        string outra = Path.Combine(_pasta, "outra.xlsx");
        Assert.Throws<PlanilhaInvalidaException>(() => ModeloCronograma.Gerar(_modelo, "Sumiu", [Obra(1)], outra));
        Assert.Empty(Directory.GetFiles(_pasta, "outra*"));
        Assert.Throws<ArgumentException>(() => ModeloCronograma.Gerar(_modelo, "Cronograma Um", [.. Enumerable.Range(1, 16).Select(n => Obra(n))], outra));
    }

    [Fact]
    public void T12_dezesseis_obras_viram_dois_cronogramas_sem_descartar_nenhuma()
    {
        var obras = Enumerable.Range(1, 16).Select(n => Obra(n)).Append(new ObraCronograma()).ToList();

        var validado = ValidadorCronograma.Validar(obras, ["3362"]);
        var grupos = ValidadorCronograma.Dividir(obras);

        Assert.True(validado.PodeGerar);
        Assert.Contains(validado.Gerais, a => a.Gravidade == Gravidade.PrecisaConferir && a.Mensagem.Contains("Serão gerados 2 cronogramas"));
        Assert.Equal([15, 1], grupos.Select(g => g.Count));
        Assert.Equal("Escola 16", grupos[1][0].Nome);
        Assert.Equal(16000m, ValidadorCronograma.TotalParaObras(obras));
        Assert.Single(ValidadorCronograma.Dividir([.. obras.Take(15)]));
        Assert.Equal("Cronograma - Portaria 3 - Lote 12 - parte 2 de 2", ModeloCronograma.NomeSugerido(grupos[1], 1, 2));
        Assert.Equal("Cronograma - Escola 1", ModeloCronograma.NomeSugerido([Obra(1)]));
    }

    [Fact]
    public void T13_valores_e_datas_invalidos_e_dotacao_ausente_tem_impedimento_claro()
    {
        var obras = new[]
        {
            Obra(1) with { Nome = "" },
            Obra(2, valor: null),
            Obra(3) with { Fim = new DateOnly(2026, 10, 1) },
            Obra(4) with { Inicio = null },
            Obra(5, dotacao: "9999"),
            Obra(6, valor: 0m, dotacao: ""),
            Obra(7) with { Fim = new DateOnly(2028, 12, 1) },
            Obra(8) with { Inicio = new DateOnly(2026, 11, 20), DataOsDaBase = new DateOnly(2026, 11, 10) },
            Obra(9) with { OrigemDoInicio = OrigemInicio.Previsto },
        };

        var v = ValidadorCronograma.Validar(obras, ["2878", "3362"]);
        string Da(int linha) => string.Join(" | ", v.Linhas.Where(l => l.Linha == linha).Select(l => $"{l.Aviso.Gravidade}: {l.Aviso.Mensagem}"));

        Assert.False(v.PodeGerar);
        Assert.Contains("ImpedeGerar: Falta o nome da obra", Da(1));
        Assert.Contains("ImpedeGerar: Falta o valor para obras", Da(2));
        Assert.Contains("ImpedeGerar: O fim é anterior ao início", Da(3));
        Assert.Contains("ImpedeGerar: Falta o início", Da(4));
        Assert.Contains("ImpedeGerar: A dotação 9999 não existe na aba Dotacoes do modelo", Da(5));
        Assert.Contains("PrecisaConferir: Valor zero ou negativo", Da(6));
        Assert.Contains("PrecisaConferir: Sem dotação", Da(6));
        Assert.Contains("ImpedeGerar: O fim passa dos 20 meses", Da(7));
        Assert.Contains("PrecisaConferir: O início (20/11/2026) é diferente da Data da OS da base (10/11/2026)", Da(8));
        Assert.Contains("Informacao: Início previsto", Da(9));

        Assert.True(ValidadorCronograma.Validar([Obra(1)], ["3362"]).PodeGerar);
        Assert.False(ValidadorCronograma.Validar([new ObraCronograma()], []).PodeGerar);
    }

    [Fact]
    public void Colagem_do_excel_converte_valores_e_datas_e_avisa_o_que_ignorou()
    {
        string texto = "Portaria \tLote \tDotação \tNome da Obra\tBreve escopo\tValores para Obras\tInício\tFim\r\n"
                       + "3\t12\t3362\tEMEF Um\tReforma\tR$ 1.234,56\t10/11/2026\tfev/27\r\n"
                       + "3\t12\t2878\tCEI Dois\t\tmil reais\t11/2026\t31/02/2027\r\n"
                       + "\r\n"
                       + "só duas\tcolunas\r\n";

        var lida = ColagemCronograma.Ler(texto);

        Assert.Equal(2, lida.Obras.Count);
        Assert.Equal(("3", "12", "3362", "EMEF Um", "Reforma", 1234.56m), (lida.Obras[0].Portaria, lida.Obras[0].Lote, lida.Obras[0].Dotacao, lida.Obras[0].Nome, lida.Obras[0].Escopo, lida.Obras[0].Valor));
        Assert.Equal((new DateOnly(2026, 11, 10), new DateOnly(2027, 2, 1)), (lida.Obras[0].Inicio, lida.Obras[0].Fim));
        Assert.Equal(new DateOnly(2026, 11, 1), lida.Obras[1].Inicio);
        Assert.Null(lida.Obras[1].Valor);
        Assert.Null(lida.Obras[1].Fim);
        Assert.Contains(lida.Avisos, a => a == "Linha 1: é o cabeçalho e foi ignorada.");
        Assert.Contains(lida.Avisos, a => a.Contains("o valor \"mil reais\" não foi entendido"));
        Assert.Contains(lida.Avisos, a => a.Contains("o fim \"31/02/2027\" não foi entendido"));
        Assert.Contains(lida.Avisos, a => a.StartsWith("Linha 5: tem menos de 4 colunas"));
    }

    private string Copia(string origem)
    {
        string copia = Path.Combine(_pasta, "copia-" + Guid.NewGuid().ToString("N")[..6] + ".xlsx");
        File.Copy(origem, copia);
        return copia;
    }
}
