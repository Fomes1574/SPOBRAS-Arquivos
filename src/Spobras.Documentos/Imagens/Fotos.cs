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
    /// Copia em JPEG, na orientacao certa e com o maior lado limitado. A proporcao e sempre preservada.
    /// </summary>
    public static byte[] Preparar(string caminho, int ladoMaximo, int qualidade, int rotacaoManual = 0)
    {
        using var bitmap = Decodificar(caminho, ladoMaximo, rotacaoManual);
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

        // JPEG grande ja e decodificado menor (economiza memoria com fotos de celular).
        float fator = Math.Min(1f, ladoMaximo * 2f / Math.Max(codec.Info.Width, codec.Info.Height));
        var reduzido = codec.GetScaledDimensions(fator);
        var info = new SKImageInfo(reduzido.Width, reduzido.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var bruto = new SKBitmap(info);
        var resultado = codec.GetPixels(info, bruto.GetPixels());
        if (resultado is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
        {
            throw new FotoInvalidaException("O arquivo não é uma imagem válida ou está danificado.");
        }

        using var orientado = Orientar(bruto, exif.Orientacao, Normalizar(rotacaoManual));

        double escala = Math.Min(1.0, (double)ladoMaximo / Math.Max(orientado.Width, orientado.Height));
        int largura = Math.Max(1, (int)Math.Round(orientado.Width * escala));
        int altura = Math.Max(1, (int)Math.Round(orientado.Height * escala));

        // Fundo branco: PNG com transparencia nao vira preto no JPEG.
        var final = new SKBitmap(new SKImageInfo(largura, altura, SKColorType.Rgba8888, SKAlphaType.Opaque));
        using var tela = new SKCanvas(final);
        tela.Clear(SKColors.White);
        using var origem = SKImage.FromBitmap(orientado);
        using var tinta = new SKPaint { IsAntialias = true };
        tela.DrawImage(origem, new SKRect(0, 0, largura, altura), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), tinta);
        return final;
    }

    /// <summary>Aplica a orientacao EXIF (1 a 8) e depois a rotacao manual (0, 90, 180, 270 no sentido horario).</summary>
    private static SKBitmap Orientar(SKBitmap origem, int orientacao, int rotacaoManual)
    {
        int l = origem.Width, a = origem.Height;
        bool troca = TrocaLados(orientacao);
        var passo1 = new SKBitmap(troca ? a : l, troca ? l : a, origem.ColorType, origem.AlphaType);
        using (var tela = new SKCanvas(passo1))
        {
            var m = orientacao switch
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
            tela.SetMatrix(m);
            tela.DrawBitmap(origem, 0, 0);
        }

        if (rotacaoManual == 0)
        {
            return passo1;
        }

        using (passo1)
        {
            int l1 = passo1.Width, a1 = passo1.Height;
            bool deita = rotacaoManual % 180 != 0;
            var passo2 = new SKBitmap(deita ? a1 : l1, deita ? l1 : a1, passo1.ColorType, passo1.AlphaType);
            using var tela = new SKCanvas(passo2);
            tela.SetMatrix(rotacaoManual switch
            {
                90 => new SKMatrix(0, -1, a1, 1, 0, 0, 0, 0, 1),
                180 => new SKMatrix(-1, 0, l1, 0, -1, a1, 0, 0, 1),
                _ => new SKMatrix(0, 1, 0, -1, 0, l1, 0, 0, 1),
            });
            tela.DrawBitmap(passo1, 0, 0);
            return passo2;
        }
    }

    private static bool TrocaLados(int orientacao) => orientacao is >= 5 and <= 8;

    private static int Normalizar(int graus) => ((graus % 360) + 360) % 360 / 90 * 90;
}
