using SkiaSharp;
using Spobras.App.Windows;
using Spobras.Documentos.Pdf;

namespace Spobras.App.Telas.Tests;

/// <summary>OCR do Windows de verdade (so roda no Windows/CI). Se o Windows nao tiver OCR, registra e pula.</summary>
public class OcrWindowsTests : IDisposable
{
    private readonly string _pasta = Directory.CreateTempSubdirectory("spobras-ocr-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_pasta, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static void Registrar(string texto)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ArquivosSpobras.sln")))
        {
            dir = dir.Parent;
        }

        string pasta = Path.Combine(dir?.FullName ?? Path.GetTempPath(), "artifacts", "telas");
        Directory.CreateDirectory(pasta);
        File.AppendAllText(Path.Combine(pasta, "ocr.txt"), texto + Environment.NewLine);
    }

    [Fact]
    public void Le_imagem_e_pdf_escaneado_com_o_ocr_do_windows()
    {
        var ocr = new OcrWindows();
        if (!ocr.Disponivel)
        {
            Registrar("OCR do Windows: indisponível nesta máquina (teste pulado).");
            Assert.Skip("OCR do Windows indisponível nesta máquina.");
        }

        string imagem = Path.Combine(_pasta, "tabela.png");
        using (var bitmap = new SKBitmap(1800, 600))
        using (var tela = new SKCanvas(bitmap))
        using (var fonte = new SKFont(SKTypeface.FromFamilyName("Arial"), 64))
        using (var pincel = new SKPaint { Color = SKColors.Black, IsAntialias = true })
        {
            tela.Clear(SKColors.White);
            tela.DrawText("CONTRATO", 80, 150, fonte, pincel);
            tela.DrawText("VALOR", 1100, 150, fonte, pincel);
            tela.DrawText("123/EX/2024", 80, 350, fonte, pincel);
            tela.DrawText("1.234,56", 1100, 350, fonte, pincel);
            using var dados = SKImage.FromBitmap(bitmap).Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(imagem, dados.ToArray());
        }

        var palavras = ocr.LerImagem(imagem);
        string lido = string.Join(" ", palavras.Select(p => p.Texto));
        Registrar($"OCR do Windows ({ocr.Idioma}): imagem lida como \"{lido}\".");
        Assert.Contains("CONTRATO", lido, StringComparison.OrdinalIgnoreCase);

        string pdf = Path.Combine(_pasta, "escaneado.pdf");
        FerramentasPdf.FotosEmPdf([imagem], pdf);
        var extraido = TextoPdf.ParaExcel(pdf, Path.Combine(_pasta, "escaneado.xlsx"), ocr: ocr);
        Registrar($"OCR do Windows: PDF escaneado com {extraido.Linhas.Count} linhas: {string.Join(" | ", extraido.Linhas.Select(l => string.Join(" ; ", l.Colunas)))}");
        Assert.Equal([1], extraido.LidasPorOcr);
        Assert.Contains(extraido.Linhas, l => l.Colunas.Any(c => c.Contains("CONTRATO", StringComparison.OrdinalIgnoreCase)));
    }
}
