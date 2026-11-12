using Spobras.Core.Base;
using Spobras.Core.Modelos;
using Spobras.Documentos.Modelos;
using Spobras.Documentos.Planilhas;
using Spobras.Testes;

namespace Spobras.Documentos.Tests;

public class ModelosExcelTests : IDisposable
{
    private const string Aba = "Cronograma Dois";
    private readonly string _pasta = Directory.CreateTempSubdirectory("spobras-modelos-").FullName;
    private readonly string _arquivo;

    public ModelosExcelTests()
    {
        _arquivo = Path.Combine(_pasta, "modelo.xlsx");
        XlsxSintetico.GravarModeloCronograma(_arquivo);
    }

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    private static CampoModelo Campo(string nome, string celula, TipoCampo tipo = TipoCampo.Texto, bool obrigatorio = false, string aba = Aba) =>
        new() { Nome = nome, Aba = aba, Celula = celula, Tipo = tipo, Obrigatorio = obrigatorio };

    [Fact]
    public void Celula_aceita_minusculas_e_cifrao_e_recusa_o_que_nao_e_celula()
    {
        Assert.Equal("B12", ValidadorModelo.Celula(" $b$12 "));
        Assert.Equal("AE18", ValidadorModelo.Celula("ae18"));
        Assert.Null(ValidadorModelo.Celula("12B"));
        Assert.Null(ValidadorModelo.Celula("B0"));
        Assert.Null(ValidadorModelo.Celula("B3:C4"));
        Assert.Null(ValidadorModelo.Celula(""));
    }

    [Fact]
    public void Converte_conforme_o_tipo_e_explica_o_que_nao_entendeu()
    {
        Assert.Equal((1250.5m, null), ValidadorModelo.Converter(Campo("Valor", "G3", TipoCampo.Numero), "R$ 1.250,50"));
        Assert.Equal((new DateOnly(2026, 11, 10), null), ValidadorModelo.Converter(Campo("Início", "J3", TipoCampo.Data), "10/11/2026"));
        Assert.Equal(("EMEF Alfa", null), ValidadorModelo.Converter(Campo("Escola", "E3"), "  EMEF Alfa "));
        Assert.Equal((null, null), ValidadorModelo.Converter(Campo("Escopo", "F3"), ""));
        Assert.Equal("Falta “Escola”", ValidadorModelo.Converter(Campo("Escola", "E3", obrigatorio: true), " ").Erro);
        Assert.Contains("o número \"abc\" não foi entendido", ValidadorModelo.Converter(Campo("Valor", "G3", TipoCampo.Numero), "abc").Erro);
        Assert.Contains("a data \"31/02/2026\" não foi entendida", ValidadorModelo.Converter(Campo("Início", "J3", TipoCampo.Data), "31/02/2026").Erro);
    }

    [Fact]
    public void Cadastro_aponta_nome_repetido_celula_invalida_destino_repetido_e_marcador_sem_campo()
    {
        var modelo = new ModeloExcel
        {
            Nome = " ",
            PadraoNome = "Ficha - {Escola} - {Bairro}",
            Campos = [Campo("Escola", "E3"), Campo("escola", "E4"), Campo("", "X"), Campo("Outro", "e3")],
        };

        var problemas = ValidadorModelo.ConferirCadastro(modelo);

        Assert.Contains("Dê um nome ao modelo.", problemas);
        Assert.Contains("“escola”: já existe outro campo com esse nome.", problemas);
        Assert.Contains("Campo 3: falta o nome.", problemas);
        Assert.Contains(problemas, p => p.StartsWith("Campo 3: \"X\" não é uma célula."));
        Assert.Contains("“Outro”: a célula E3 já recebe o campo “Escola”.", problemas);
        Assert.Contains("O nome do arquivo usa {Bairro}, mas não há campo com esse nome.", problemas);
        Assert.Empty(ValidadorModelo.ConferirCadastro(new ModeloExcel { Nome = "Ficha", PadraoNome = "{escola}", Campos = [Campo("Escola", "E3")] }));
    }

    [Fact]
    public void Nome_do_arquivo_troca_os_marcadores_e_nunca_fica_vazio_ou_invalido()
    {
        var modelo = new ModeloExcel { Nome = "Ficha da obra", PadraoNome = "Ficha - {Escola} - {Data}" };
        Assert.Equal("Ficha - EMEF Alfa - 10-11-2026", ValidadorModelo.NomeDoArquivo(modelo, new Dictionary<string, string> { ["Escola"] = "EMEF Alfa", ["Data"] = "10/11/2026" }));
        Assert.Equal("Ficha", ValidadorModelo.NomeDoArquivo(modelo, new Dictionary<string, string>()));
        Assert.Equal("Ficha da obra", ValidadorModelo.NomeDoArquivo(modelo with { PadraoNome = "" }, new Dictionary<string, string>()));
        Assert.Equal("Ficha da obra", ValidadorModelo.NomeDoArquivo(modelo with { PadraoNome = "{Escola}" }, new Dictionary<string, string> { ["Escola"] = "" }));
    }

    [Fact]
    public void Origem_traz_o_dado_da_obra_como_texto_para_conferir()
    {
        var obra = new ObraLida { Contrato = "001/EX/2024", Empreendimento = "EMEF Alfa", Lote = "LOTE 05", DataOs = new DateOnly(2026, 11, 10) };
        Assert.Equal("EMEF Alfa", OrigensCampo.Valor(obra, "escola"));
        Assert.Equal("10/11/2026", OrigensCampo.Valor(obra, "data-os"));
        Assert.Equal("", OrigensCampo.Valor(obra, "sei"));
        Assert.Equal("", OrigensCampo.Valor(obra, ""));
        Assert.Equal("Data da OS", OrigensCampo.Rotulo("data-os"));
    }

    [Fact]
    public void Estrutura_e_estavel_e_muda_quando_o_arquivo_muda_e_a_leitura_nao_altera_o_arquivo()
    {
        var antes = File.ReadAllBytes(_arquivo);
        var estrutura = PreenchedorModelo.Ler(_arquivo);
        Assert.Contains(Aba, estrutura.Abas);
        Assert.Equal(estrutura.Assinatura, PreenchedorModelo.Ler(_arquivo).Assinatura);
        Assert.Equal(antes, File.ReadAllBytes(_arquivo));

        string outro = Path.Combine(_pasta, "outro.xlsx");
        XlsxSintetico.Gravar(BaseSintetica.Dotacoes(), outro);
        Assert.NotEqual(estrutura.Assinatura, PreenchedorModelo.Ler(outro).Assinatura);
        Assert.Throws<PlanilhaInvalidaException>(() => PreenchedorModelo.Ler(Path.Combine(_pasta, "nao-existe.xlsx")));
    }

    [Fact]
    public void Conferencia_bloqueia_aba_inexistente_e_celula_com_formula()
    {
        var problemas = PreenchedorModelo.Conferir(_arquivo, [Campo("Escola", "E3"), Campo("Total", "G18"), Campo("Solto", "B2", aba: "Não existe")]);

        Assert.Equal(2, problemas.Count);
        Assert.Contains(problemas, p => p.StartsWith("“Total”: a célula G18 da aba \"Cronograma Dois\" tem fórmula."));
        Assert.Contains("“Solto”: a aba \"Não existe\" não existe neste arquivo.", problemas);
    }

    [Fact]
    public void Preenche_so_as_celulas_mapeadas_numa_copia_e_mantem_as_formulas()
    {
        var antes = File.ReadAllBytes(_arquivo);
        string saida = Path.Combine(_pasta, "saida", "Ficha.xlsx");

        PreenchedorModelo.Preencher(_arquivo,
        [
            (Campo("Escola", "e3"), "EMEF Alfa"),
            (Campo("Valor", "G3", TipoCampo.Numero), 1250.5m),
            (Campo("Início", "J3", TipoCampo.Data), new DateOnly(2026, 11, 10)),
            (Campo("Escopo", "F3"), null),
        ], saida);

        Assert.Equal(antes, File.ReadAllBytes(_arquivo));
        Assert.Equal(["Ficha.xlsx"], Directory.GetFiles(Path.GetDirectoryName(saida)!).Select(Path.GetFileName));
        using (var leitor = new LeitorXlsx(saida))
        {
            var grade = leitor.LerAba(Aba);
            Assert.Equal("EMEF Alfa", grade[2][4]);
            Assert.Equal(1250.5m, grade[2][6]);
        }

        using var modelo = new EditorXlsx(_arquivo, somenteLeitura: true);
        using var gerado = new EditorXlsx(saida, somenteLeitura: true);
        Assert.NotNull(modelo.Formula(Aba, "G18"));
        Assert.Equal(modelo.Formula(Aba, "G18"), gerado.Formula(Aba, "G18"));
        Assert.Equal(modelo.ContarFormulas(Aba), gerado.ContarFormulas(Aba));
    }

    [Fact]
    public void Nao_sobrescreve_arquivo_existente_e_nao_deixa_resto_quando_a_escrita_e_bloqueada()
    {
        string saida = Path.Combine(_pasta, "Ficha.xlsx");
        File.WriteAllText(saida, "já existia");
        Assert.Throws<IOException>(() => PreenchedorModelo.Preencher(_arquivo, [(Campo("Escola", "E3"), "x")], saida));
        Assert.Equal("já existia", File.ReadAllText(saida));

        string outra = Path.Combine(_pasta, "Outra.xlsx");
        Assert.Throws<EscritaBloqueadaException>(() => PreenchedorModelo.Preencher(_arquivo, [(Campo("Total", "G18", TipoCampo.Numero), 1m)], outra));
        Assert.False(File.Exists(outra));
        Assert.Empty(Directory.GetFiles(_pasta, "*.spobras-parcial"));
    }
}

public class MesclagemTests : IDisposable
{
    private readonly string _pasta = Directory.CreateTempSubdirectory("spobras-mescla-").FullName;

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    [Fact]
    public void Celula_no_meio_de_mesclagem_e_recusada_e_a_primeira_do_grupo_e_indicada()
    {
        string arquivo = Path.Combine(_pasta, "modelo.xlsx");
        XlsxSintetico.GravarModeloCronograma(arquivo);
        using (var doc = DocumentFormat.OpenXml.Packaging.SpreadsheetDocument.Open(arquivo, true))
        {
            var folha = doc.WorkbookPart!.WorksheetParts.First().Worksheet!;
            var dados = folha.GetFirstChild<DocumentFormat.OpenXml.Spreadsheet.SheetData>()!;
            folha.InsertAfter(
                new DocumentFormat.OpenXml.Spreadsheet.MergeCells(new DocumentFormat.OpenXml.Spreadsheet.MergeCell { Reference = "B40:D41" }), dados);
            folha.Save();
        }

        string aba;
        using (var editor = new EditorXlsx(arquivo, somenteLeitura: true))
        {
            aba = editor.Abas[0];
            Assert.Equal("B40", editor.PrimeiraDaMesclagem(aba, "C41"));
            Assert.Null(editor.PrimeiraDaMesclagem(aba, "B40"));
            Assert.Null(editor.PrimeiraDaMesclagem(aba, "E40"));
        }

        var campo = new CampoModelo { Nome = "Obs", Aba = aba, Celula = "D40" };
        Assert.Contains("Use B40", Assert.Single(PreenchedorModelo.Conferir(arquivo, [campo])));
        Assert.Throws<EscritaBloqueadaException>(() => PreenchedorModelo.Preencher(arquivo, [(campo, "x")], Path.Combine(_pasta, "s.xlsx")));
        PreenchedorModelo.Preencher(arquivo, [(campo with { Celula = "B40" }, "x")], Path.Combine(_pasta, "ok.xlsx"));
        Assert.True(File.Exists(Path.Combine(_pasta, "ok.xlsx")));
    }
}
