using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using SkiaSharp;
using Spobras.Core.Pdf;
using Spobras.Documentos.Pdf;

namespace Spobras.Documentos.Tests;

public class PdfTests : IDisposable
{
    private readonly string _pasta = Directory.CreateTempSubdirectory("spobras-pdf-").FullName;

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    private string Em(params string[] partes) => Path.Combine([_pasta, .. partes]);

    /// <summary>PDF de teste: cada pagina tem uma largura diferente, para saber qual e qual.</summary>
    private string CriarPdf(string nome, params int[] larguras)
    {
        using var doc = new PdfDocument();
        foreach (int largura in larguras)
        {
            var pagina = doc.AddPage();
            pagina.Width = XUnit.FromPoint(largura);
            pagina.Height = XUnit.FromPoint(500);
            using var g = XGraphics.FromPdfPage(pagina);
            g.DrawRectangle(XBrushes.SteelBlue, 20, 20, largura - 40, 100);
        }

        doc.Save(Em(nome));
        return Em(nome);
    }

    private string CriarFoto(string nome, int largura, int altura, bool ruido = false)
    {
        using var bitmap = new SKBitmap(largura, altura);
        using (var tela = new SKCanvas(bitmap))
        {
            tela.Clear(SKColors.SandyBrown);
            if (ruido)
            {
                var acaso = new Random(7);
                using var tinta = new SKPaint();
                for (int i = 0; i < 4000; i++)
                {
                    tinta.Color = new SKColor((byte)acaso.Next(256), (byte)acaso.Next(256), (byte)acaso.Next(256));
                    tela.DrawRect(acaso.Next(largura), acaso.Next(altura), 40, 40, tinta);
                }
            }
        }

        using var imagem = SKImage.FromBitmap(bitmap);
        File.WriteAllBytes(Em(nome), imagem.Encode(SKEncodedImageFormat.Jpeg, 95).ToArray());
        return Em(nome);
    }

    private static List<int> Larguras(string pdf)
    {
        using var doc = PdfReader.Open(pdf, PdfDocumentOpenMode.Import);
        return [.. doc.Pages.Cast<PdfPage>().Select(p => (int)Math.Round(p.Width.Point))];
    }

    [Theory]
    [InlineData("1-3, 5, 8-", 10, "1-3|5|8-10")]
    [InlineData("2", 2, "2")]
    [InlineData(" 4 - 6 ;1", 6, "4-6|1")]
    public void Intervalos_validos(string texto, int total, string esperado)
    {
        var lidos = IntervalosPdf.Interpretar(texto, total);

        Assert.True(lidos.Valido);
        Assert.Equal(esperado, string.Join("|", lidos.Intervalos));
    }

    [Theory]
    [InlineData("", "Digite as páginas")]
    [InlineData("a-3", "Não entendi \"a-3\"")]
    [InlineData("1-2-3", "Não entendi")]
    [InlineData("0-2", "começam no número 1")]
    [InlineData("5-2", "primeira página é maior")]
    [InlineData("3-12", "passa do fim: o PDF tem 10 página(s)")]
    public void Intervalos_invalidos_explicam_o_erro(string texto, string trecho)
    {
        var lidos = IntervalosPdf.Interpretar(texto, 10);

        Assert.False(lidos.Valido);
        Assert.Contains(trecho, lidos.Erro);
    }

    [Fact]
    public void Junta_na_ordem_escolhida_e_nao_altera_os_originais()
    {
        string a = CriarPdf("a.pdf", 300, 310), b = CriarPdf("b.pdf", 400), c = CriarPdf("c.pdf", 500, 510, 520);
        long tamanhoA = new FileInfo(a).Length;

        int paginas = FerramentasPdf.Juntar([c, a, b], Em("saida", "junto.pdf"));

        Assert.Equal(6, paginas);
        Assert.Equal([500, 510, 520, 300, 310, 400], Larguras(Em("saida", "junto.pdf")));
        Assert.Equal(tamanhoA, new FileInfo(a).Length);
        Assert.Equal(new InfoPdf(3, false), FerramentasPdf.Ler(c));

        Assert.Throws<IOException>(() => FerramentasPdf.Juntar([a], Em("saida", "junto.pdf"))); // nao sobrescreve
        Assert.Equal(6, Larguras(Em("saida", "junto.pdf")).Count);
    }

    [Fact]
    public void Separa_por_intervalos_e_por_pagina_sem_sobrescrever()
    {
        string pdf = CriarPdf("Restos a Pagar.pdf", 300, 310, 320, 330, 340);

        var partes = FerramentasPdf.Separar(pdf, IntervalosPdf.Interpretar("1-2, 4-", 5).Intervalos, Em("saida"));
        var cadaUma = FerramentasPdf.Separar(pdf, IntervalosPdf.CadaPagina(5), Em("paginas"));
        var deNovo = FerramentasPdf.Separar(pdf, [new IntervaloPdf(1, 2)], Em("saida"));

        Assert.Equal(["Restos a Pagar - p 1-2.pdf", "Restos a Pagar - p 4-5.pdf"], partes.Select(Path.GetFileName));
        Assert.Equal([300, 310], Larguras(partes[0]));
        Assert.Equal([330, 340], Larguras(partes[1]));
        Assert.Equal(5, cadaUma.Count);
        Assert.Equal([320], Larguras(cadaUma[2]));
        Assert.Equal("Restos a Pagar - p 1-2 (2).pdf", Path.GetFileName(deNovo[0]));
        Assert.Throws<PdfInvalidoException>(() => FerramentasPdf.Separar(pdf, [new IntervaloPdf(4, 9)], Em("saida")));
    }

    [Fact]
    public void Fotos_viram_pdf_uma_por_pagina_na_orientacao_da_foto()
    {
        string deitada = CriarFoto("deitada.jpg", 800, 600), emPe = CriarFoto("em-pe.jpg", 600, 800);
        var feitas = new List<int>();

        int paginas = FerramentasPdf.FotosEmPdf([deitada, emPe], Em("fotos.pdf"), feitas: new Coletor(feitas));

        Assert.Equal(2, paginas);
        Assert.Equal([842, 595], Larguras(Em("fotos.pdf"))); // A4 deitado e A4 em pe
        Assert.Equal([1, 2], feitas);
        Assert.Throws<PdfInvalidoException>(() => FerramentasPdf.FotosEmPdf([], Em("vazio.pdf")));
        Assert.False(File.Exists(Em("vazio.pdf")));
    }

    [Fact]
    public void T20_reduz_pdf_com_fotos_grandes_e_preserva_o_original()
    {
        string foto = CriarFoto("grande.jpg", 3000, 2250, ruido: true);
        FerramentasPdf.FotosEmPdf([foto, foto], Em("pesado.pdf"), ladoMaximo: 3000, qualidade: 95);
        long original = new FileInfo(Em("pesado.pdf")).Length;

        var reduzido = FerramentasPdf.Reduzir(Em("pesado.pdf"), Em("leve.pdf"), ladoMaximo: 1200, qualidade: 60);

        Assert.True(reduzido.Reduziu);
        Assert.True(reduzido.ImagensReduzidas >= 1);
        Assert.True(reduzido.TamanhoNovo < original / 2, $"{reduzido.TamanhoNovo} de {original}");
        Assert.Equal(original, new FileInfo(Em("pesado.pdf")).Length);
        Assert.Equal(2, FerramentasPdf.Ler(Em("leve.pdf")).Paginas);
    }

    [Fact]
    public void T20_pdf_ja_otimizado_nao_gera_arquivo_e_informa()
    {
        string pdf = CriarPdf("so-desenho.pdf", 300, 310);

        var resultado = FerramentasPdf.Reduzir(pdf, Em("menor.pdf"), 1200, 60);

        Assert.False(resultado.Reduziu);
        Assert.Equal(0, resultado.ImagensReduzidas);
        Assert.False(File.Exists(Em("menor.pdf")));
        Assert.Empty(Directory.GetFiles(_pasta, "*.spobras-parcial"));
    }

    [Fact]
    public void T20_pdf_protegido_ou_invalido_tem_mensagem_propria()
    {
        using (var doc = new PdfDocument())
        {
            doc.AddPage();
            doc.SecuritySettings.UserPassword = "segredo";
            doc.SecuritySettings.OwnerPassword = "dono";
            doc.Save(Em("protegido.pdf"));
        }

        File.WriteAllText(Em("falso.pdf"), "isto não é um PDF");

        Assert.Contains("protegido por senha", Assert.Throws<PdfInvalidoException>(() => FerramentasPdf.Ler(Em("protegido.pdf"))).Message);
        Assert.Contains("não pôde ser aberto como PDF", Assert.Throws<PdfInvalidoException>(() => FerramentasPdf.Juntar([Em("falso.pdf")], Em("x.pdf"))).Message);
        Assert.False(File.Exists(Em("x.pdf")));
        Assert.Contains("Não foi possível abrir", Assert.Throws<PdfInvalidoException>(() => FerramentasPdf.Ler(Em("nao-existe.pdf"))).Message);
    }

    private sealed class Coletor(List<int> vistos) : IProgress<int>
    {
        public void Report(int value) => vistos.Add(value);
    }
}
