using PdfSharp.Fonts;
using SkiaSharp;

namespace Spobras.Documentos.Pdf;

/// <summary>
/// Fontes para o texto que o app escreve em PDF (numeracao de paginas). Usa as fontes instaladas no
/// computador, pelo SkiaSharp, para funcionar igual no Windows e nos testes.
/// </summary>
public sealed class FontesPdf : IFontResolver
{
    public const string Familia = "Arquivos SPOBRAS";
    private static readonly string[] Preferidas = ["Segoe UI", "Arial", "Inter", "DejaVu Sans", "Liberation Sans", "FreeSans"];
    private static readonly Lazy<byte[]> Regular = new(() => Carregar(SKFontStyle.Normal));
    private static readonly Lazy<byte[]> Negrito = new(() => Carregar(SKFontStyle.Bold));
    private static readonly object Trava = new();

    public static void Garantir()
    {
        lock (Trava)
        {
            GlobalFontSettings.FontResolver ??= new FontesPdf();
        }
    }

    public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic) =>
        new(bold ? "spobras-negrito" : "spobras-regular");

    public byte[]? GetFont(string faceName) => faceName == "spobras-negrito" ? Negrito.Value : Regular.Value;

    private static byte[] Carregar(SKFontStyle estilo)
    {
        foreach (string nome in Preferidas)
        {
            using var face = SKFontManager.Default.MatchFamily(nome, estilo);
            if (face is not null && face.FamilyName.Equals(nome, StringComparison.OrdinalIgnoreCase) && Ler(face) is { } dados)
            {
                return dados;
            }
        }

        using var padrao = SKTypeface.FromFamilyName(null, estilo) ?? SKTypeface.Default;
        return Ler(padrao) ?? throw new PdfInvalidoException("Nenhuma fonte encontrada neste computador para escrever no PDF.");
    }

    private static byte[]? Ler(SKTypeface face)
    {
        using var fluxo = face.OpenStream(out _);
        if (fluxo is null || !fluxo.HasLength || fluxo.Length == 0)
        {
            return null;
        }

        var dados = new byte[fluxo.Length];
        int lidos = fluxo.Read(dados, dados.Length);
        // Fontes .otf com contornos CFF ou colecoes .ttc nao servem para o PDFsharp: so TrueType simples.
        return lidos == dados.Length && dados.Length > 4 && (dados[0] == 0 && dados[1] == 1 || dados[0] == 't' && dados[1] == 'r') ? dados : null;
    }
}
