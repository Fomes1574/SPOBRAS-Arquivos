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
    public void Para_excel_sem_grade_usa_as_mesmas_colunas_na_pagina_inteira_e_pdf_sem_texto_e_explicado()
    {
        string pdf = PdfComTexto("anexo.pdf",
            [(40, "Unidade"), (240, "Orçamento")],
            [(40, "EMEF Alfa"), (240, "50.000,00")],
            [(40, "CEI Beta do Jardim"), (240, "R$ 1.234,56")]);
        string saida = Em("anexo.xlsx");

        var lido = TextoPdf.ParaExcel(pdf, saida);

        Assert.Equal(3, lido.Linhas.Count);
        Assert.Equal([1], lido.Tabela!.PaginasPorAlinhamento);
        using (var leitor = new LeitorXlsx(saida))
        {
            Assert.Equal([GravadorTabela.NomeDaAba], leitor.NomesDasAbas()); // so a tabela, sem aba "Sobre"
            var grade = leitor.LerAba(GravadorTabela.NomeDaAba);
            Assert.Equal(new object?[] { "Unidade", "Orçamento" }, grade[0].Take(2));
            Assert.Equal(new object?[] { "EMEF Alfa", 50000m }, grade[1].Take(2));
            Assert.Equal(new object?[] { "CEI Beta do Jardim", 1234.56m }, grade[2].Take(2)); // mesma coluna, nome inteiro
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

    /// <summary>
    /// Tabela como o Excel imprime em PDF: grade de tracos finos, titulo mesclado, cabecalho em negrito repetido
    /// em cada pagina, numero que o PDF separou ("3 5.000,00"), codigo com zero a esquerda e SEI de 16 digitos.
    /// </summary>
    private string PdfComGrade(string nome)
    {
        FontesPdf.Garantir();
        using var doc = new PdfDocument();
        var normal = new XFont(FontesPdf.Familia, 9);
        var negrito = new XFont(FontesPdf.Familia, 9, XFontStyleEx.Bold);
        double[] xs = [40, 110, 170, 300, 400];
        string[] titulos = ["DATA", "CÓDIGO", "SEI", "VALOR"];
        int numero = 0;
        for (int pg = 0; pg < 2; pg++)
        {
            var p = doc.AddPage();
            using var g = XGraphics.FromPdfPage(p);
            double y = 50;
            void Traco(double x1, double y1, double x2, double y2) => g.DrawRectangle(XBrushes.Black, x1, y1, Math.Max(0.5, x2 - x1), Math.Max(0.5, y2 - y1));
            void Linha(string?[] textos, XFont fonte, bool mesclada = false)
            {
                Traco(xs[0], y, xs[^1], y);
                Traco(xs[0], y, xs[0], y + 14);
                Traco(xs[^1], y, xs[^1], y + 14);
                if (mesclada)
                {
                    g.DrawString(textos[0]!, fonte, XBrushes.Black, new XRect(xs[0], y, xs[^1] - xs[0], 14), XStringFormats.Center);
                }
                else
                {
                    for (int j = 0; j < 4; j++)
                    {
                        Traco(xs[j], y, xs[j], y + 14);
                        if (textos[j] is { } t)
                        {
                            g.DrawString(t, fonte, XBrushes.Black, new XRect(xs[j] + 2, y, xs[j + 1] - xs[j] - 4, 14), j == 3 ? XStringFormats.CenterRight : XStringFormats.CenterLeft);
                        }
                    }
                }

                y += 14;
                Traco(xs[0], y, xs[^1], y);
            }

            Linha(["RELATÓRIO DE EXEMPLO"], negrito, mesclada: true);
            Linha(titulos, negrito);
            for (int i = 0; i < 3; i++)
            {
                numero++;
                Linha([$"{numero:00}/02/2025", "000123", "6022202200067805", null], normal);
                // "3 5.000,00": o numero sai em dois pedacos com um vao no meio, como em PDFs do Excel.
                g.DrawString("3", normal, XBrushes.Black, xs[3] + 40, y - 4);
                g.DrawString("5.000,00", normal, XBrushes.Black, xs[3] + 50, y - 4);
            }

            g.DrawString($"Página {pg + 1} de 2", normal, XBrushes.Black, 280, 800);
        }

        string caminho = Em(nome);
        doc.Save(caminho);
        return caminho;
    }

    [Fact]
    public void Para_excel_com_grade_copia_a_tabela_fiel_sem_pagina_linha_nem_logo()
    {
        string pdf = PdfComGrade("restos.pdf");
        string saida = Em("restos.xlsx");

        var lido = TextoPdf.ParaExcel(pdf, saida);
        var tabela = lido.Tabela!;

        Assert.Equal([1, 2], tabela.PaginasPorGrade);
        Assert.Equal(4, tabela.Larguras.Count);
        Assert.Equal(8, tabela.Linhas.Count); // titulo + cabecalho (uma vez so) + 6 linhas; "Página 1 de 2" fica de fora
        Assert.Equal(4, tabela.Linhas[0].Celulas[0]!.Colunas); // titulo mesclado na largura toda
        Assert.True(tabela.Linhas[1].Celulas[0]!.Negrito);
        Assert.Equal(AlinhamentoCelula.Direita, tabela.Linhas[2].Celulas[3]!.Alinhamento);

        using (var leitor = new LeitorXlsx(saida))
        {
            Assert.Equal([GravadorTabela.NomeDaAba], leitor.NomesDasAbas());
            var grade = leitor.LerAba(GravadorTabela.NomeDaAba);
            Assert.Equal("RELATÓRIO DE EXEMPLO", grade[0][0]);
            Assert.Equal(new object?[] { "DATA", "CÓDIGO", "SEI", "VALOR" }, grade[1].Take(4));
            Assert.Equal(new DateTime(2025, 2, 1), grade[2][0]);
            Assert.Equal("000123", grade[2][1]); // codigo continua texto, com o zero
            Assert.Equal("6022202200067805", grade[2][2]); // SEI nao vira numero (perderia digitos)
            Assert.Equal(35000m, grade[2][3]); // "3 5.000,00" voltou inteiro
            Assert.Equal(new DateTime(2025, 2, 6), grade[7][0]);
            Assert.DoesNotContain(grade.SelectMany(l => l), v => v is string t && (t.StartsWith("Página") || t.StartsWith("Coluna")));
        }

        using var zip = System.IO.Compression.ZipFile.OpenRead(saida);
        Assert.DoesNotContain(zip.Entries, e => e.FullName.StartsWith("xl/media/")); // sem logo
        using (var doc = DocumentFormat.OpenXml.Packaging.SpreadsheetDocument.Open(saida, false))
        {
            Assert.Empty(new DocumentFormat.OpenXml.Validation.OpenXmlValidator().Validate(doc).Select(e => e.Description));
        }
    }

    [Fact]
    public void Numero_separado_pelo_pdf_volta_inteiro_e_texto_comum_nao_muda()
    {
        Assert.Equal("35.000,00", TabelaPdf.Compactar("3 5.000,00"));
        Assert.Equal("2.318,59", TabelaPdf.Compactar("2 .318,59"));
        Assert.Equal("R$ 35.000,00", TabelaPdf.Compactar("R$ 3 5.000,00"));
        Assert.Equal("22 DE MARCO", TabelaPdf.Compactar("22 DE MARCO"));
        Assert.Equal("123 456", TabelaPdf.Compactar("123 456")); // dois codigos: nao vira um numero so
        Assert.Equal("DATA NE", TabelaPdf.Compactar("DATA NE"));
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

        public IReadOnlyList<PalavraPosicionada> LerImagem(string imagem)
        {
            PaginasLidas.Add(PaginasLidas.Count + 1);
            Assert.True(File.Exists(imagem));
            return Tabela();
        }
    }

    [Fact]
    public void Pdf_digitalizado_e_foto_sao_lidos_pelo_ocr_e_marcados_para_conferir()
    {
        var ocr = new OcrFalso();
        // "Escaneado": duas paginas que sao so foto (como sai do scanner)
        var fotos = Enumerable.Range(1, 2).Select(n =>
        {
            string f = Em($"scan{n}.jpg");
            using var b = new SkiaSharp.SKBitmap(800, 1100);
            b.Erase(SkiaSharp.SKColors.White);
            using var img = SkiaSharp.SKImage.FromBitmap(b);
            File.WriteAllBytes(f, img.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 80).ToArray());
            return f;
        }).ToList();
        string pdf = Em("escaneado.pdf");
        FerramentasPdf.FotosEmPdf(fotos, pdf);
        string saida = Em("escaneado.xlsx");

        var lido = TextoPdf.ParaExcel(pdf, saida, ocr: ocr);

        Assert.Equal([1, 2], ocr.PaginasLidas);
        Assert.Equal([1, 2], lido.LidasPorOcr);
        Assert.Equal(["EMEF Alfa", "50.000,00"], lido.Linhas[1].Colunas);
        using (var leitor = new LeitorXlsx(saida))
        {
            Assert.Equal(new object?[] { "EMEF Alfa", 50000m }, leitor.LerAba(GravadorTabela.NomeDaAba)[1].Take(2));
        }

        // Pagina com texto nao passa pelo OCR
        var comTexto = PdfComTexto("texto.pdf", [(40, "Unidade")]);
        var ocr2 = new OcrFalso();
        TextoPdf.Extrair(comTexto, ocr: ocr2);
        Assert.Empty(ocr2.PaginasLidas);

        // PDF em branco (sem texto e sem imagem) nao chama o OCR
        var ocr3 = new OcrFalso();
        Assert.True(TextoPdf.Extrair(PdfEmBranco("branco.pdf", 1), ocr: ocr3).SemTextoNenhum);
        Assert.Empty(ocr3.PaginasLidas);

        // Foto de uma tabela
        string foto = fotos[0];
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
