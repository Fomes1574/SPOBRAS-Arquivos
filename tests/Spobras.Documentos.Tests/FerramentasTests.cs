using System.IO.Compression;
using Spobras.Core.Ferramentas;
using Spobras.Documentos.Ferramentas;
using Spobras.Documentos.Pdf;
using Spobras.Documentos.Planilhas;
using Spobras.Testes;

namespace Spobras.Documentos.Tests;

public class FerramentasTests : IDisposable
{
    private readonly string _pasta = Directory.CreateTempSubdirectory("spobras-ferr-").FullName;

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    private string Em(params string[] partes)
    {
        string caminho = Path.Combine([_pasta, .. partes]);
        Directory.CreateDirectory(Path.GetDirectoryName(caminho)!);
        return caminho;
    }

    private sealed class IndiceMemoria : IIndiceTexto
    {
        public Dictionary<string, (long, long, TextoIndexado)> Dados { get; } = [];
        public int Leituras { get; private set; }

        public TextoIndexado? Ler(string caminho, long tamanho, long modificado) =>
            Dados.TryGetValue(caminho, out var d) && d.Item1 == tamanho && d.Item2 == modificado ? d.Item3 : null;

        public void Gravar(string caminho, long tamanho, long modificado, TextoIndexado texto)
        {
            Leituras++;
            Dados[caminho] = (tamanho, modificado, texto);
        }
    }

    [Fact]
    public void Trecho_ignora_acento_e_caixa()
    {
        Assert.Equal("…obra da EMEF José de Alencar, lote 5", TrechoBusca.Trecho("Relatório da obra da EMEF José de Alencar, lote 5", "jose de alencar", 30));
        Assert.Null(TrechoBusca.Trecho("nada aqui", "contrato"));
        Assert.Null(TrechoBusca.Trecho("qualquer", "  "));
    }

    [Fact]
    public void Busca_no_nome_e_no_conteudo_de_planilha_pdf_word_e_texto_e_usa_o_indice()
    {
        XlsxSintetico.Gravar(BaseSintetica.Mestra(), Em("Base", "mestra.xlsx"));
        File.WriteAllText(Em("Notas", "anotacoes.txt"), "Ligar para a fiscal sobre o contrato 004/EX/2025 amanhã");
        File.WriteAllText(Em("Notas", "Contrato 004-EX-2025 - medição.txt"), "x");
        using (var zip = ZipFile.Open(Em("Docs", "oficio.docx"), ZipArchiveMode.Create))
        {
            using var w = new StreamWriter(zip.CreateEntry("word/document.xml").Open());
            w.Write("<w:document><w:body><w:p><w:r><w:t>Ofício sobre a CEU Delta</w:t></w:r></w:p></w:body></w:document>");
        }

        CarimboPdf.Carimbar(PdfEmBranco(Em("Docs", "base.pdf")), Em("Docs", "numerado.pdf"), new OpcoesCarimbo { Texto = "CEU Delta {n}" });
        File.WriteAllText(Em("Docs", "foto.jpg"), "CEU Delta"); // tipo sem leitura de conteudo
        var indice = new IndiceMemoria();

        var r = BuscaArquivos.Buscar([_pasta], "ceu delta", indice);

        Assert.Equal(["mestra.xlsx", "numerado.pdf", "oficio.docx"], r.Achados.Select(a => Path.GetFileName(a.Caminho)).Order());
        Assert.Contains("Ofício sobre a CEU Delta", r.Achados.Single(a => a.Caminho.EndsWith("oficio.docx")).Trecho);
        Assert.Equal(1, r.SemLeitura); // base.pdf sem texto
        Assert.Equal(6, r.LidosAgora);

        var contrato = BuscaArquivos.Buscar([_pasta], "004/EX/2025", indice);
        Assert.Equal(["anotacoes.txt", "mestra.xlsx"], contrato.Achados.Select(a => Path.GetFileName(a.Caminho)).Order());
        Assert.Equal(0, contrato.LidosAgora); // tudo veio do indice

        File.WriteAllText(Em("Notas", "anotacoes.txt"), "mudou: agora fala da CEU Delta");
        File.SetLastWriteTimeUtc(Em("Notas", "anotacoes.txt"), DateTime.UtcNow.AddMinutes(1));
        var depois = BuscaArquivos.Buscar([_pasta], "ceu delta", indice);
        Assert.Equal(1, depois.LidosAgora);
        Assert.Contains(depois.Achados, a => a.Caminho.EndsWith("anotacoes.txt"));
        Assert.Contains(depois.Achados, a => a.NoNome == false);
        Assert.Contains(BuscaArquivos.Buscar([_pasta], "medição", indice).Achados, a => a.NoNome);
    }

    private static string PdfEmBranco(string caminho)
    {
        using var doc = new PdfSharp.Pdf.PdfDocument();
        doc.AddPage();
        doc.Save(caminho);
        return caminho;
    }

    [Fact]
    public void Juntar_casa_colunas_pelo_titulo_marca_origem_e_nao_soma_nada()
    {
        string a = Em("a.xlsx"), b = Em("b.xlsx"), ruim = Em("ruim.xlsx");
        XlsxSintetico.Gravar(new PlanilhaFalsa { AbasPorNome = { ["Jan"] = [["Escola", "Valor", "Data"], ["EMEF Alfa", 10.5m, new DateTime(2026, 1, 5)], ["CEI Beta", 2m, null]] } }, a);
        XlsxSintetico.Gravar(new PlanilhaFalsa { AbasPorNome = { ["Fev"] = [["  ESCOLA ", "Observação", "valor"], ["EMEI Gama", "ok", 7m]] } }, b);
        File.WriteAllText(ruim, "não é planilha");

        var previa = JuntarPlanilhas.Analisar([a, b, ruim]);

        Assert.Equal(["Escola", "Valor", "Data", "Observação"], previa.Colunas);
        Assert.Equal(3, previa.TotalLinhas);
        Assert.Equal(["Observação"], previa.Faltando(previa.Origens[0]));
        Assert.Equal(["Data"], previa.Faltando(previa.Origens[1]));
        Assert.False(previa.Origens[2].Ok);

        string saida = Em("saida", "juntas.xlsx");
        JuntarPlanilhas.Gravar(previa, saida);
        using var leitor = new LeitorXlsx(saida);
        var grade = leitor.LerAba("Dados juntos");
        Assert.Equal(["Arquivo de origem", "Tabela ou aba", "Escola", "Valor", "Data", "Observação"], grade[0].Take(6));
        Assert.Equal(new object?[] { "a.xlsx", "aba Jan", "EMEF Alfa", 10.5m, new DateTime(2026, 1, 5), null }, grade[1].Take(6));
        Assert.Equal(new object?[] { "b.xlsx", "aba Fev", "EMEI Gama", 7m, null, "ok" }, grade[3].Take(6));
        Assert.Equal(4, grade.Count); // sem linha de total
        Assert.Contains(leitor.LerAba("Origem"), l => "ruim.xlsx".Equals(l[0]) && "não entrou".Equals(l[1]));
    }

    [Fact]
    public void Duplicados_pelo_conteudo_ignora_nome_e_vazios()
    {
        File.WriteAllText(Em("A", "relatorio.pdf"), "conteudo igual");
        File.SetLastWriteTimeUtc(Em("A", "relatorio.pdf"), new DateTime(2026, 1, 1));
        File.WriteAllText(Em("B", "relatorio (1).pdf"), "conteudo igual");
        File.WriteAllText(Em("B", "outro nome.pdf"), "conteudo igual");
        File.WriteAllText(Em("B", "mesmo tamanho.pdf"), "conteudo IGUAL");
        File.WriteAllText(Em("A", "vazio1.txt"), "");
        File.WriteAllText(Em("B", "vazio2.txt"), "");

        var grupos = Duplicados.Procurar([Path.Combine(_pasta, "A"), Path.Combine(_pasta, "B")]);

        var grupo = Assert.Single(grupos);
        Assert.Equal(3, grupo.Arquivos.Count);
        Assert.Equal("relatorio.pdf", Path.GetFileName(grupo.Arquivos[0])); // o mais antigo primeiro
        Assert.Equal(2 * 14, grupo.Sobra);
    }
}
