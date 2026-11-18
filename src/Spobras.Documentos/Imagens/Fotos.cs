using SkiaSharp;

namespace Spobras.Documentos.Imagens;

public sealed record InfoFoto(int Largura, int Altura, DateTime? CapturadaEm);

/// <summary>Foto que o app nao consegue abrir; a mensagem explica o motivo para o usuario.</summary>
public sealed class FotoInvalidaException(string mensagem) : Exception(mensagem);

/// <summary>
/// Leitura e preparo das fotos (SkiaSharp). Nunca altera o arquivo original: tudo o que sai daqui e copia.
/// Uma foto por vez na memoria.
/// </summary>
public static class Fotos
{
    /// <summary>Formatos testados. Outros (ex.: HEIC de celular) recebem mensagem por arquivo.</summary>
    public static readonly IReadOnlyList<string> Extensoes = [".jpg", ".jpeg", ".png", ".bmp", ".webp", ".gif"];

    public static bool Suportada(string caminho) =>
        Extensoes.Contains(Path.GetExtension(caminho), StringComparer.OrdinalIgnoreCase);

    public static string MotivoNaoSuportada(string caminho)
    {
        string extensao = Path.GetExtension(caminho).ToLowerInvariant();
        return extensao is ".heic" or ".heif"
            ? "Foto em formato HEIC (comum em iPhone) ainda não é aceita. Converta para JPG ou mude a câmera para \"Mais compatível\"."
            : $"Arquivo {extensao} não é uma foto aceita. Use JPG, PNG, BMP, WEBP ou GIF.";
    }

    /// <summary>Tamanho ja com a orientacao EXIF e a rotacao manual aplicadas, e data de captura se houver.</summary>
    public static InfoFoto Ler(string caminho, int rotacaoManual = 0)
    {
        if (!Suportada(caminho))
        {
            throw new FotoInvalidaException(MotivoNaoSuportada(caminho));
        }

        using var codec = Abrir(caminho);
        var exif = Exif.Ler(caminho);
        bool deitada = TrocaLados(exif.Orientacao) ^ (Normalizar(rotacaoManual) % 180 != 0);
        return deitada
            ? new InfoFoto(codec.Info.Height, codec.Info.Width, exif.CapturadaEm)
            : new InfoFoto(codec.Info.Width, codec.Info.Height, exif.CapturadaEm);
    }

    /// <summary>
    /// Copia em JPEG, na orientacao certa e com o maior lado limitado. A proporcao e sempre preservada. Com carimbo, ele
    /// e gravado nesta copia (a original nunca muda).
    /// </summary>
    public static byte[] Preparar(string caminho, int ladoMaximo, int qualidade, int rotacaoManual = 0, Spobras.Core.Relatorio.CarimboFoto? carimbo = null)
    {
        using var bitmap = Decodificar(caminho, ladoMaximo, rotacaoManual);
        Carimbador.Desenhar(bitmap, carimbo); // no mesmo passo, com a imagem ja na memoria
        using var imagem = SKImage.FromBitmap(bitmap);
        using var dados = imagem.Encode(SKEncodedImageFormat.Jpeg, Math.Clamp(qualidade, 1, 100));
        return dados.ToArray();
    }

    /// <summary>Miniatura em JPEG para navegar na tela sem carregar a foto inteira.</summary>
    public static byte[] Miniatura(string caminho, int lado = 320, int rotacaoManual = 0) => Preparar(caminho, lado, 80, rotacaoManual);

    private static SKCodec Abrir(string caminho)
    {
        try
        {
            return SKCodec.Create(caminho) ?? throw new FotoInvalidaException("O arquivo não é uma imagem válida ou está danificado.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new FotoInvalidaException("Não foi possível abrir a foto: " + ex.Message);
        }
    }

    private static SKBitmap Decodificar(string caminho, int ladoMaximo, int rotacaoManual)
    {
        if (!Suportada(caminho))
        {
            throw new FotoInvalidaException(MotivoNaoSuportada(caminho));
        }

        using var codec = Abrir(caminho);
        var exif = Exif.Ler(caminho);

        // JPEG grande e aberto ja reduzido (1/8, 2/8... do tamanho), na menor escala que ainda cobre o lado pedido:
        // bem mais rapido e com menos memoria que abrir os 12 MP inteiros.
        int maior = Math.Max(codec.Info.Width, codec.Info.Height);
        var reduzido = codec.Info.Size;
        for (int n = 1; n < 8; n++)
        {
            var d = codec.GetScaledDimensions(n / 8f);
            if (Math.Max(d.Width, d.Height) >= Math.Min(ladoMaximo, maior))
            {
                reduzido = d;
                break;
            }
        }

        var info = new SKImageInfo(reduzido.Width, reduzido.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var bruto = new SKBitmap(info);
        var resultado = codec.GetPixels(info, bruto.GetPixels());
        if (resultado is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
        {
            throw new FotoInvalidaException("O arquivo não é uma imagem válida ou está danificado.");
        }

        // Orientacao EXIF, giro manual e reducao num desenho so (sem copias intermediarias).
        int orientacao = exif.Orientacao, giro = Normalizar(rotacaoManual);
        int l = bruto.Width, a = bruto.Height;
        bool troca = TrocaLados(orientacao);
        int l1 = troca ? a : l, a1 = troca ? l : a;
        bool deita = giro % 180 != 0;
        int l2 = deita ? a1 : l1, a2 = deita ? l1 : a1;

        double escala = Math.Min(1.0, (double)ladoMaximo / Math.Max(l2, a2));
        int largura = Math.Max(1, (int)Math.Round(l2 * escala));
        int altura = Math.Max(1, (int)Math.Round(a2 * escala));

        var matriz = SKMatrix.Concat(
            SKMatrix.CreateScale((float)largura / l2, (float)altura / a2),
            SKMatrix.Concat(MatrizDoGiro(giro, l1, a1), MatrizDaOrientacao(orientacao, l, a)));

        // Fundo branco: PNG com transparencia nao vira preto no JPEG.
        var final = new SKBitmap(new SKImageInfo(largura, altura, SKColorType.Rgba8888, SKAlphaType.Opaque));
        using var tela = new SKCanvas(final);
        tela.Clear(SKColors.White);
        tela.SetMatrix(matriz);
        using var origem = SKImage.FromBitmap(bruto);
        // Reducao grande (miniatura) usa mipmap para nao serrilhar; reducao pequena nao precisa.
        var amostra = escala < 0.5 ? new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear) : new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None);
        using var tinta = new SKPaint { IsAntialias = true };
        tela.DrawImage(origem, 0, 0, amostra, tinta);
        return final;
    }

    private static SKMatrix MatrizDaOrientacao(int orientacao, int l, int a) => orientacao switch
    {
        2 => new SKMatrix(-1, 0, l, 0, 1, 0, 0, 0, 1),   // espelhada na horizontal
        3 => new SKMatrix(-1, 0, l, 0, -1, a, 0, 0, 1),  // 180 graus
        4 => new SKMatrix(1, 0, 0, 0, -1, a, 0, 0, 1),   // espelhada na vertical
        5 => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),    // transposta
        6 => new SKMatrix(0, -1, a, 1, 0, 0, 0, 0, 1),   // 90 graus horario
        7 => new SKMatrix(0, -1, a, -1, 0, l, 0, 0, 1),  // transversa
        8 => new SKMatrix(0, 1, 0, -1, 0, l, 0, 0, 1),   // 90 graus anti-horario
        _ => SKMatrix.Identity,
    };

    private static SKMatrix MatrizDoGiro(int giro, int l1, int a1) => giro switch
    {
        90 => new SKMatrix(0, -1, a1, 1, 0, 0, 0, 0, 1),
        180 => new SKMatrix(-1, 0, l1, 0, -1, a1, 0, 0, 1),
        270 => new SKMatrix(0, 1, 0, -1, 0, l1, 0, 0, 1),
        _ => SKMatrix.Identity,
    };

    private static bool TrocaLados(int orientacao) => orientacao is >= 5 and <= 8;

    private static int Normalizar(int graus) => ((graus % 360) + 360) % 360 / 90 * 90;
}
