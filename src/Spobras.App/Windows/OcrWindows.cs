using Spobras.Documentos.Pdf;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;
using Windows.Storage.Streams;
using IdiomaWindows = Windows.Globalization.Language;
using PdfWindows = Windows.Data.Pdf.PdfDocument;

namespace Spobras.App.Windows;

/// <summary>
/// OCR do proprio Windows (Windows.Media.Ocr), local e sem instalar nada. A pagina do PDF vira imagem
/// pela leitura de PDF do Windows (Windows.Data.Pdf). Usa o portugues se o pacote de idioma estiver
/// instalado; senao, o idioma do usuario.
/// </summary>
public sealed class OcrWindows : IOcr
{
    // Nada do Windows fica guardado entre uma leitura e outra: so o nome do idioma e se ha OCR.
    private readonly Lazy<string?> _idioma = new(() => Rodar(() =>
    {
        string? nome = Criar()?.RecognizerLanguage.DisplayName;
        return Task.FromResult(nome);
    }));

    public bool Disponivel => _idioma.Value is not null;

    public string Idioma => _idioma.Value ?? "indisponível";

    /// <summary>Solta os objetos do Windows que ainda estejam pendentes (chamado ao fechar o app).</summary>
    public static void Liberar()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static OcrEngine? Criar()
    {
        try
        {
            var portugues = new IdiomaWindows("pt-BR");
            return OcrEngine.IsLanguageSupported(portugues) ? OcrEngine.TryCreateFromLanguage(portugues) : OcrEngine.TryCreateFromUserProfileLanguages();
        }
        catch (Exception)
        {
            return null; // Windows sem OCR (por exemplo, versao reduzida): o app explica e segue sem
        }
    }

    public IReadOnlyList<PalavraPosicionada> LerPaginaPdf(string pdf, int pagina) => Rodar(async () =>
    {
        var arquivo = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(pdf));
        var documento = await PdfWindows.LoadFromFileAsync(arquivo);
        using var pagina1 = documento.GetPage((uint)(pagina - 1));
        using var fluxo = new InMemoryRandomAccessStream();
        // Cerca de 200 dpi numa A4: nitido para o OCR sem passar do tamanho maximo de imagem dele.
        double escala = Math.Min(3.0, 2400 / Math.Max(pagina1.Size.Width, pagina1.Size.Height));
        await pagina1.RenderToStreamAsync(fluxo, new global::Windows.Data.Pdf.PdfPageRenderOptions
        {
            DestinationWidth = (uint)(pagina1.Size.Width * escala),
            DestinationHeight = (uint)(pagina1.Size.Height * escala),
        });
        return await Ler(fluxo);
    });

    public IReadOnlyList<PalavraPosicionada> LerImagem(string imagem) => Rodar(async () =>
    {
        var arquivo = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(imagem));
        using var fluxo = await arquivo.OpenAsync(FileAccessMode.Read);
        return await Ler(fluxo);
    });

    private async Task<IReadOnlyList<PalavraPosicionada>> Ler(IRandomAccessStream fluxo)
    {
        var motor = Criar() ?? throw new PdfInvalidoException("O OCR do Windows não está disponível neste computador.");
        var decodificador = await BitmapDecoder.CreateAsync(fluxo);
        uint maior = Math.Max(decodificador.PixelWidth, decodificador.PixelHeight);
        var transformacao = new BitmapTransform();
        if (maior > OcrEngine.MaxImageDimension)
        {
            double fator = (double)OcrEngine.MaxImageDimension / maior;
            transformacao.ScaledWidth = (uint)(decodificador.PixelWidth * fator);
            transformacao.ScaledHeight = (uint)(decodificador.PixelHeight * fator);
        }

        using var imagem = await decodificador.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transformacao, ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage);
        var resultado = await motor.RecognizeAsync(imagem);
        return [.. resultado.Lines.SelectMany(l => l.Words).Select(w =>
            new PalavraPosicionada(w.Text, w.BoundingRect.X, w.BoundingRect.X + w.BoundingRect.Width, w.BoundingRect.Y, w.BoundingRect.Height))];
    }

    private static T Rodar<T>(Func<Task<T>> trabalho)
    {
        try
        {
            return Task.Run(trabalho).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is not PdfInvalidoException)
        {
            throw new PdfInvalidoException("O OCR do Windows não conseguiu ler este arquivo: " + ex.Message);
        }
        finally
        {
            Liberar(); // libera ja os objetos do Windows desta leitura, em vez de deixar para o fim do processo
        }
    }
}
