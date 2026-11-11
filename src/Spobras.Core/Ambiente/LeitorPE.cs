namespace Spobras.Core.Ambiente;

/// <summary>Le do cabecalho de um executavel Windows se ele e de 32 ou 64 bits.</summary>
public static class LeitorPE
{
    /// <returns>"32 bits", "64 bits", "ARM64" ou null se o arquivo nao for um executavel valido.</returns>
    public static string? LerArquitetura(Stream executavel)
    {
        using var leitor = new BinaryReader(executavel, System.Text.Encoding.ASCII, leaveOpen: true);
        try
        {
            if (leitor.ReadUInt16() != 0x5A4D) // "MZ"
            {
                return null;
            }

            executavel.Seek(0x3C, SeekOrigin.Begin);
            uint posicaoPE = leitor.ReadUInt32();
            executavel.Seek(posicaoPE, SeekOrigin.Begin);
            if (leitor.ReadUInt32() != 0x00004550) // "PE\0\0"
            {
                return null;
            }

            return leitor.ReadUInt16() switch
            {
                0x014C => "32 bits",
                0x8664 => "64 bits",
                0xAA64 => "ARM64",
                _ => null,
            };
        }
        catch (EndOfStreamException)
        {
            return null;
        }
    }

    public static string? LerArquitetura(string caminho)
    {
        using var arquivo = new FileStream(caminho, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return LerArquitetura(arquivo);
    }
}
