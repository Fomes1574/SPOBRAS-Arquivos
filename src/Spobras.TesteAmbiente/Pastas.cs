using System.Runtime.InteropServices;

namespace Spobras.TesteAmbiente;

internal static class Pastas
{
    private static readonly Guid IdDownloads = new("374DE290-123F-4565-9164-39C4925E467B");

    /// <summary>Pasta Downloads do usuario (mesmo se ele a mudou de lugar).</summary>
    public static string Downloads()
    {
        if (SHGetKnownFolderPath(IdDownloads, 0, 0, out nint ponteiro) == 0)
        {
            try
            {
                string? caminho = Marshal.PtrToStringUni(ponteiro);
                if (!string.IsNullOrEmpty(caminho))
                {
                    return caminho;
                }
            }
            finally
            {
                Marshal.FreeCoTaskMem(ponteiro);
            }
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }

    public static string DadosDoApp() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ArquivosSPOBRAS");

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath(
        [MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint dwFlags, nint hToken, out nint ppszPath);
}
