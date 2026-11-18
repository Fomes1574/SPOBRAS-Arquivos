using SkiaSharp;
using Spobras.Core.Relatorio;
using Spobras.Documentos.Pdf;

namespace Spobras.Documentos.Imagens;

/// <summary>
/// Grava o carimbo (escola, endereco, data e hora) numa copia da foto: faixa preta translucida com cantos arredondados
/// e texto branco na fonte do relatorio. A posicao vem de <see cref="GeometriaCarimbo"/>, a mesma conta das previas.
/// </summary>
public static class Carimbador
{
    /// <summary>Preto a 55%: o texto branco fica legivel sobre ceu claro e sobre parede escura.</summary>
    public static readonly SKColor Fundo = new(0, 0, 0, 140);

    private static readonly Lazy<SKTypeface> Regular = new(() => Carregar(false));
    private static readonly Lazy<SKTypeface> Negrito = new(() => Carregar(true));

    private static SKTypeface Carregar(bool negrito) =>
        SKTypeface.FromData(SKData.CreateCopy(FontesPdf.FonteDoRelatorio(negrito))) ?? SKTypeface.Default;

    public static void Desenhar(SKBitmap foto, CarimboFoto? carimbo)
    {
        if (GeometriaCarimbo.Calcular(foto.Width, foto.Height, carimbo) is not { } g)
        {
            return;
        }

        using var tela = new SKCanvas(foto);
        using var fundo = new SKPaint { Color = Fundo, IsAntialias = true, Style = SKPaintStyle.Fill };
        tela.DrawRoundRect(new SKRect((float)g.X, (float)g.Y, (float)(g.X + g.Largura), (float)(g.Y + g.Altura)), (float)g.Raio, (float)g.Raio, fundo);
        using var tinta = new SKPaint { Color = SKColors.White, IsAntialias = true };
        using var regular = new SKFont(Regular.Value, (float)g.Letra) { Subpixel = true, Edging = SKFontEdging.Antialias };
        using var negrito = new SKFont(Negrito.Value, (float)g.Letra) { Subpixel = true, Edging = SKFontEdging.Antialias };
        foreach (var linha in g.Linhas)
        {
            tela.DrawText(linha.Texto, (float)linha.X, (float)linha.Base, SKTextAlign.Left, linha.Negrito ? negrito : regular, tinta);
        }

        tela.Flush();
    }
}
