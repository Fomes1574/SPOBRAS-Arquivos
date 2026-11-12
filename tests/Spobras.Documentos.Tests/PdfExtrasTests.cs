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
        Assert.Contains("OCR do Windows não está disponível", erro.Message);
        Assert.False(File.Exists(Em("nada.xlsx")));
        Assert.Throws<PdfInvalidoException>(() => TextoPdf.Extrair(Em("nao-existe.pdf")));
    }

    private sealed class OcrFalso : IOcr
    {
        public bool Disponivel => true;
        public string Idioma => "Português (Brasil)";
        public List<int> PaginasLidas { get; } = [];

        // Uma "tabela" escaneada: duas colunas separadas por espaco largo, em pixels.
        private static IReadOnlyList<PalavraPosicionada> Tabela() =>
        [
            new("Unidade", 100, 260, 50, 30), new("Orçamento", 900, 1100, 52, 30),
            new("EMEF", 100, 190, 120, 30), new("Alfa", 200, 270, 121, 30), new("50.000,00", 900, 1080, 119, 30),
        ];

        public IReadOnlyList<PalavraPosicionada> LerPaginaPdf(string pdf, int pagina)
        {
            PaginasLidas.Add(pagina);
            return Tabela();
        }

        public IReadOnlyList<PalavraPosicionada> LerImagem(string imagem) => Tabela();
    }

    [Fact]
    public void Pdf_digitalizado_e_foto_sao_lidos_pelo_ocr_e_marcados_para_conferir()
    {
        var ocr = new OcrFalso();
        string pdf = PdfEmBranco("escaneado.pdf", 2);
        string saida = Em("escaneado.xlsx");

        var lido = TextoPdf.ParaExcel(pdf, saida, ocr: ocr);

        Assert.Equal([1, 2], ocr.PaginasLidas);
        Assert.Equal([1, 2], lido.LidasPorOcr);
        Assert.Equal(["EMEF Alfa", "50.000,00"], lido.Linhas[1].Colunas);
        using (var leitor = new LeitorXlsx(saida))
        {
            Assert.Equal(new object?[] { 1m, 2m, "EMEF Alfa", 50000m }, leitor.LerAba("Texto do PDF")[2].Take(4));
            Assert.Contains(leitor.LerAba("Sobre"), l => "Lidas por OCR".Equals(l[0]) && ((string)l[1]!).Contains("confira com mais atenção"));
        }

        // Pagina com texto nao passa pelo OCR
        var comTexto = PdfComTexto("texto.pdf", [(40, "Unidade")]);
        var ocr2 = new OcrFalso();
        TextoPdf.Extrair(comTexto, ocr: ocr2);
        Assert.Empty(ocr2.PaginasLidas);

        // Foto de uma tabela
        string foto = Em("foto.jpg");
        File.WriteAllBytes(foto, [0xFF, 0xD8, 0xFF]);
        var daFoto = TextoPdf.ParaExcel(foto, Em("foto.xlsx"), ocr: ocr);
        Assert.Equal(["Unidade", "Orçamento"], daFoto.Linhas[0].Colunas);
        Assert.Throws<PdfInvalidoException>(() => TextoPdf.ParaExcel(foto, Em("foto2.xlsx")));
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
