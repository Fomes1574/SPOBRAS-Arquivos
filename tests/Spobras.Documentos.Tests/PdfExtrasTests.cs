using PdfSharp.Drawing;
using PdfSharp.Pdf;
using Spobras.Documentos.Pdf;
using Spobras.Documentos.Planilhas;

namespace Spobras.Documentos.Tests;

public class PdfExtrasTests : IDisposable
{
    private readonly string _pasta = Directory.CreateTempSubdirectory("spobras-pdfx-").FullName;

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    private string Em(string nome) => Path.Combine(_pasta, nome);

    /// <summary>PDF com texto de verdade: cada linha e uma lista de (x em pontos, texto).</summary>
    private string PdfComTexto(string nome, params (double X, string Texto)[][] linhasPorPagina)
    {
        FontesPdf.Garantir();
        using var doc = new PdfDocument();
        var fonte = new XFont(FontesPdf.Familia, 10);
        foreach (var pagina in linhasPorPagina.Chunk(3))
        {
            var p = doc.AddPage();
            using var g = XGraphics.FromPdfPage(p);
            double y = 60;
            foreach (var linha in pagina)
            {
                foreach (var (x, texto) in linha)
                {
                    g.DrawString(texto, fonte, XBrushes.Black, x, y);
                }

                y += 20;
            }
        }

        string caminho = Em(nome);
        doc.Save(caminho);
        return caminho;
    }

    private string PdfEmBranco(string nome, int paginas)
    {
        using var doc = new PdfDocument();
        for (int i = 0; i < paginas; i++)
        {
            doc.AddPage();
        }

        string caminho = Em(nome);
        doc.Save(caminho);
        return caminho;
    }

    [Fact]
    public void Extrai_linhas_e_colunas_e_converte_reais_e_datas()
    {
        string pdf = PdfComTexto("restos.pdf",
            [(40, "Data"), (140, "NE"), (240, "Valor a liquidar")],
            [(40, "10/11/2026"), (140, "2026NE001234"), (240, "1.234,56")],
            [(40, "Total geral do exercício"), (240, "R$ 10,00")],
            [(40, "Segunda página")]);

        var texto = TextoPdf.Extrair(pdf);

        Assert.Equal(2, texto.Paginas);
        Assert.Empty(texto.PaginasSemTexto);
        Assert.Equal(4, texto.Linhas.Count);
        Assert.Equal(["Data", "NE", "Valor a liquidar"], texto.Linhas[0].Colunas);
        Assert.Equal(["10/11/2026", "2026NE001234", "1.234,56"], texto.Linhas[1].Colunas);
        Assert.Equal(["Total geral do exercício", "R$ 10,00"], texto.Linhas[2].Colunas); // espaco normal junta palavras
        Assert.Equal((2, 1), (texto.Linhas[3].Pagina, texto.Linhas[3].Numero));

        Assert.Equal(1234.56m, TextoPdf.Valor("1.234,56"));
        Assert.Equal(10m, TextoPdf.Valor("R$ 10,00"));
        Assert.Equal(new DateOnly(2026, 11, 10), TextoPdf.Valor("10/11/2026"));
        Assert.Equal("16.10.12.365.3010.2878", TextoPdf.Valor("16.10.12.365.3010.2878")); // dotacao fica texto
        Assert.Equal("2026NE001234", TextoPdf.Valor("2026NE001234"));
    }

    [Fact]
    public void Para_excel_guarda_origem_e_tipos_e_pdf_sem_texto_e_explicado()
    {
        string pdf = PdfComTexto("anexo.pdf", [(40, "Unidade"), (240, "Orçamento")], [(40, "EMEF Alfa"), (240, "50.000,00")]);
        string saida = Em("anexo.xlsx");

        var lido = TextoPdf.ParaExcel(pdf, saida);

        Assert.Equal(2, lido.Linhas.Count);
        using (var leitor = new LeitorXlsx(saida))
        {
            Assert.Equal(["Texto do PDF", "Sobre"], leitor.NomesDasAbas());
            var grade = leitor.LerAba("Texto do PDF");
            Assert.Equal(["Página", "Linha", "Coluna 1", "Coluna 2"], grade[0].Take(4));
            Assert.Equal(new object?[] { 1m, 2m, "EMEF Alfa", 50000m }, grade[2].Take(4));
        }

        string branco = PdfEmBranco("digitalizado.pdf", 2);
        var semTexto = TextoPdf.Extrair(branco);
        Assert.True(semTexto.SemTextoNenhum);
        Assert.Equal([1, 2], semTexto.PaginasSemTexto);
        var erro = Assert.Throws<PdfInvalidoException>(() => TextoPdf.ParaExcel(branco, Em("nada.xlsx")));
        Assert.Contains("precisa de OCR", erro.Message);
        Assert.False(File.Exists(Em("nada.xlsx")));
        Assert.Throws<PdfInvalidoException>(() => TextoPdf.Extrair(Em("nao-existe.pdf")));
    }

    [Fact]
    public void Numera_e_poe_logo_numa_copia_a_partir_da_pagina_escolhida()
    {
        string pdf = PdfEmBranco("relatorio.pdf", 3);
        var antes = File.ReadAllBytes(pdf);
        string logo = Path.Combine(_pasta, "logo.png");
        using (var bmp = new SkiaSharp.SKBitmap(200, 80))
        using (var canvas = new SkiaSharp.SKCanvas(bmp))
        {
            canvas.Clear(SkiaSharp.SKColors.Red);
            using var img = SkiaSharp.SKImage.FromBitmap(bmp);
            using var dados = img.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(logo, dados.ToArray());
        }

        var opcoes = new OpcoesCarimbo { Logo = logo, APartirDa = 2 };
        Assert.Equal((2, 3, 2), CarimboPdf.Atingidas(3, opcoes));
        int feitas = CarimboPdf.Carimbar(pdf, Em("relatorio numerado.pdf"), opcoes);

        Assert.Equal(2, feitas);
        Assert.Equal(antes, File.ReadAllBytes(pdf)); // original intacto
        var texto = TextoPdf.Extrair(Em("relatorio numerado.pdf"));
        Assert.Equal([1], texto.PaginasSemTexto); // a capa ficou sem numero
        Assert.Equal(["Página 2 de 3", "Página 3 de 3"], texto.Linhas.Select(l => string.Join(" ", l.Colunas)));

        Assert.Throws<IOException>(() => CarimboPdf.Carimbar(pdf, Em("relatorio numerado.pdf"), opcoes)); // nao sobrescreve
        Assert.Throws<PdfInvalidoException>(() => CarimboPdf.Carimbar(pdf, Em("x.pdf"), opcoes with { APartirDa = 9 }));
        Assert.Throws<PdfInvalidoException>(() => CarimboPdf.Carimbar(pdf, Em("y.pdf"), new OpcoesCarimbo { Numerar = false }));
        Assert.False(File.Exists(Em("x.pdf")));
    }
}
