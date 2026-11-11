using System.Globalization;

namespace Spobras.Core;

public static class Tamanho
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>"950 bytes", "12,5 KB", "3,2 MB", "1,1 GB".</summary>
    public static string Formatar(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} bytes",
        < 1024 * 1024 => (bytes / 1024.0).ToString("0.#", PtBr) + " KB",
        < 1024L * 1024 * 1024 => (bytes / (1024.0 * 1024)).ToString("0.#", PtBr) + " MB",
        _ => (bytes / (1024.0 * 1024 * 1024)).ToString("0.##", PtBr) + " GB",
    };
}
