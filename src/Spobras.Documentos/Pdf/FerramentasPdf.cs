using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;
using SkiaSharp;
using Spobras.Core.Nomes;
using Spobras.Core.Pdf;
using Spobras.Documentos.Imagens;

namespace Spobras.Documentos.Pdf;

/// <summary>PDF que o app nao pode abrir ou alterar; a mensagem explica o motivo.</summary>
public sealed class PdfInvalidoException(string mensagem) : Exception(mensagem);

public sealed record InfoPdf(int Paginas, bool Assinado);

public sealed record PdfReduzido(string? Caminho, long TamanhoOriginal, long TamanhoNovo, int ImagensReduzidas)
{
    /// <summary>False quando o resultado nao ficou menor: nenhum arquivo novo e criado e o original segue valendo.</summary>
    public bool Reduziu => Caminho is not null;
}

/// <summary>
/// Operacoes basicas de PDF (especificacao 4.8, itens 1 a 4). Sempre gera arquivo novo: o original
/// nunca e alterado, e nada e sobrescrito.
/// </summary>
public static class FerramentasPdf
{
    private const string SufixoParcial = ".spobras-parcial";

    public static InfoPdf Ler(string caminho)
    {
        using var doc = Abrir(caminho, PdfDocumentOpenMode.Import);
        return new InfoPdf(doc.PageCount, Assinado(doc));
    }

    /// <summary>Junta os PDFs na ordem recebida.</summary>
    public static int Juntar(IReadOnlyList<string> pdfs, string saida, CancellationToken cancelamento = default)
    {
        if (pdfs.Count == 0)
        {
            throw new PdfInvalidoException("Escolha pelo menos um PDF.");
        }

        return Publicar(saida, parcial =>
        {
            using var junto = new PdfDocument();
            foreach (string pdf in pdfs)
            {
                cancelamento.ThrowIfCancellationRequested();
                using var origem = Abrir(pdf, PdfDocumentOpenMode.Import);
                foreach (var pagina in origem.Pages)
                {
                    junto.AddPage(pagina);
                }
            }

            int paginas = junto.PageCount; // depois de salvar o documento nao pode mais ser consultado
            junto.Save(parcial);
            return paginas;
        });
    }

    /// <summary>
    /// Um arquivo por intervalo, na pasta de destino: "nome - p 1-3.pdf". Devolve os caminhos criados.
    /// </summary>
    public static IReadOnlyList<string> Separar(
        string pdf, IReadOnlyList<IntervaloPdf> intervalos, string pastaDestino, CancellationToken cancelamento = default)
    {
        using var origem = Abrir(pdf, PdfDocumentOpenMode.Import);
        if (intervalos.Any(i => i.Inicio < 1 || i.Fim > origem.PageCount || i.Inicio > i.Fim))
        {
            throw new PdfInvalidoException($"Intervalo fora do PDF, que tem {origem.PageCount} página(s).");
        }

        Directory.CreateDirectory(pastaDestino);
        string nomeBase = Path.GetFileNameWithoutExtension(pdf);
        var criados = new List<string>();
        foreach (var intervalo in intervalos)
        {
            cancelamento.ThrowIfCancellationRequested();
            string nome = NomeArquivo.Livre($"{nomeBase} - p {intervalo}.pdf", n => File.Exists(Path.Combine(pastaDestino, n)));
            string saida = Path.Combine(pastaDestino, nome);
            Publicar(saida, parcial =>
            {
                using var parte = new PdfDocument();
                for (int p = intervalo.Inicio; p <= intervalo.Fim; p++)
                {
                    parte.AddPage(origem.Pages[p - 1]);
                }

                int paginas = parte.PageCount; // depois de salvar o documento nao pode mais ser consultado
                parte.Save(parcial);
                return paginas;
            });
            criados.Add(saida);
        }

        return criados;
    }

    /// <summary>
    /// Uma foto por pagina A4, na orientacao da foto (em pe ou deitada), inteira e centralizada.
    /// </summary>
    public static int FotosEmPdf(
        IReadOnlyList<string> fotos, string saida, int ladoMaximo = 2000, int qualidade = 85,
        IProgress<int>? feitas = null, CancellationToken cancelamento = default)
    {
        if (fotos.Count == 0)
        {
            throw new PdfInvalidoException("Escolha pelo menos uma foto.");
        }

        return Publicar(saida, parcial =>
        {
            using var doc = new PdfDocument();
            const double margem = 28; // cerca de 1 cm, em pontos
            int n = 0;
            foreach (string foto in fotos)
            {
                cancelamento.ThrowIfCancellationRequested();
                byte[] jpeg = Fotos.Preparar(foto, ladoMaximo, qualidade);
                using var imagem = XImage.FromStream(new MemoryStream(jpeg));
                var pagina = doc.AddPage();
                pagina.Size = PdfSharp.PageSize.A4;
                pagina.Orientation = imagem.PixelWidth > imagem.PixelHeight ? PdfSharp.PageOrientation.Landscape : PdfSharp.PageOrientation.Portrait;

                double largura = pagina.Width.Point - 2 * margem, altura = pagina.Height.Point - 2 * margem;
                double escala = Math.Min(largura / imagem.PixelWidth, altura / imagem.PixelHeight);
                double l = imagem.PixelWidth * escala, a = imagem.PixelHeight * escala;
                using var desenho = XGraphics.FromPdfPage(pagina);
                desenho.DrawImage(imagem, margem + (largura - l) / 2, margem + (altura - a) / 2, l, a);
                feitas?.Report(++n);
            }

            int paginas = doc.PageCount; // depois de salvar o documento nao pode mais ser consultado
            doc.Save(parcial);
            return paginas;
        });
    }

    /// <summary>
    /// Reduz o tamanho recomprimindo as fotos do PDF (com perda, na qualidade escolhida). Se nao ficar
    /// menor, nada e criado. Texto e desenhos nao sao tocados.
    /// </summary>
    /// <param name="ladoMaximo">Maior lado, em pixels, das imagens dentro do PDF.</param>
    public static PdfReduzido Reduzir(string pdf, string saida, int ladoMaximo, int qualidade, CancellationToken cancelamento = default)
    {
        long original = new FileInfo(pdf).Length;
        int reduzidas = 0;
        string parcial = saida + SufixoParcial;
        if (File.Exists(saida))
        {
            throw new IOException($"Já existe \"{Path.GetFileName(saida)}\" na pasta de destino. Nada foi sobrescrito.");
        }

        try
        {
            using (var doc = Abrir(pdf, PdfDocumentOpenMode.Modify))
            {
                foreach (var objeto in doc.Internals.GetAllObjects())
                {
                    cancelamento.ThrowIfCancellationRequested();
                    if (objeto is PdfDictionary dic && Recomprimir(dic, ladoMaximo, qualidade))
                    {
                        reduzidas++;
                    }
                }

                doc.Options.CompressContentStreams = true;
                doc.Options.NoCompression = false;
                doc.Options.FlateEncodeMode = PdfFlateEncodeMode.BestCompression;
                doc.Save(parcial);
            }

            long novo = new FileInfo(parcial).Length;
            if (novo >= original)
            {
                File.Delete(parcial);
                return new PdfReduzido(null, original, novo, reduzidas);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(saida)!);
            File.Move(parcial, saida, overwrite: false);
            return new PdfReduzido(saida, original, novo, reduzidas);
        }
        catch
        {
            File.Delete(parcial);
            throw;
        }
    }

    /// <summary>Recomprime uma imagem JPEG RGB/cinza do PDF, se o resultado ficar menor.</summary>
    private static bool Recomprimir(PdfDictionary dic, int ladoMaximo, int qualidade)
    {
        if (dic.Stream is null
            || dic.Elements.GetName("/Subtype") != "/Image"
            || dic.Elements.GetName("/Filter") != "/DCTDecode"
            || dic.Elements.GetName("/ColorSpace") is not ("/DeviceRGB" or "/DeviceGray")
            || dic.Elements.ContainsKey("/Decode"))
        {
            return false; // so fotos comuns; mascaras, CMYK e outros formatos ficam como estao
        }

        byte[] atual = dic.Stream.Value;
        using var bitmap = SKBitmap.Decode(atual);
        if (bitmap is null)
        {
            return false;
        }

        double escala = Math.Min(1.0, (double)ladoMaximo / Math.Max(bitmap.Width, bitmap.Height));
        int largura = Math.Max(1, (int)Math.Round(bitmap.Width * escala));
        int altura = Math.Max(1, (int)Math.Round(bitmap.Height * escala));
        using var menor = escala < 1
            ? bitmap.Resize(new SKImageInfo(largura, altura), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear))
            : bitmap.Copy();
        if (menor is null)
        {
            return false;
        }

        using var imagem = SKImage.FromBitmap(menor);
        byte[] novo = imagem.Encode(SKEncodedImageFormat.Jpeg, Math.Clamp(qualidade, 1, 100)).ToArray();
        if (novo.Length >= atual.Length)
        {
            return false;
        }

        dic.Stream.Value = novo;
        dic.Elements.SetInteger("/Width", largura);
        dic.Elements.SetInteger("/Height", altura);
        dic.Elements.SetInteger("/Length", novo.Length);
        dic.Elements.SetName("/ColorSpace", "/DeviceRGB");
        dic.Elements.SetInteger("/BitsPerComponent", 8);
        return true;
    }

    private static PdfDocument Abrir(string caminho, PdfDocumentOpenMode modo)
    {
        try
        {
            return PdfReader.Open(caminho, modo);
        }
        catch (PdfReaderException ex) when (ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase))
        {
            throw new PdfInvalidoException($"\"{Path.GetFileName(caminho)}\" é protegido por senha. O app não abre nem altera PDF protegido.");
        }
        catch (Exception ex) when (ex is PdfReaderException or InvalidOperationException or FormatException or ArgumentException or NotSupportedException)
        {
            throw new PdfInvalidoException($"\"{Path.GetFileName(caminho)}\" não pôde ser aberto como PDF: {ex.Message}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new PdfInvalidoException($"Não foi possível abrir \"{Path.GetFileName(caminho)}\": {ex.Message}");
        }
    }

    /// <summary>PDF com campo de assinatura digital: alterar pode invalidar a assinatura.</summary>
    private static bool Assinado(PdfDocument doc)
    {
        try
        {
            var formulario = doc.Internals.Catalog.Elements.GetDictionary("/AcroForm");
            return formulario is not null && formulario.Elements.GetInteger("/SigFlags") != 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidCastException)
        {
            return false;
        }
    }

    /// <summary>Grava num temporario e so publica o nome final se der certo. Nunca sobrescreve.</summary>
    private static int Publicar(string saida, Func<string, int> gravar)
    {
        if (File.Exists(saida))
        {
            throw new IOException($"Já existe \"{Path.GetFileName(saida)}\" na pasta de destino. Nada foi sobrescrito.");
        }

        string? pasta = Path.GetDirectoryName(saida);
        if (!string.IsNullOrEmpty(pasta))
        {
            Directory.CreateDirectory(pasta);
        }

        string parcial = saida + SufixoParcial;
        try
        {
            int paginas = gravar(parcial);
            File.Move(parcial, saida, overwrite: false);
            return paginas;
        }
        catch
        {
            File.Delete(parcial);
            throw;
        }
    }
}
