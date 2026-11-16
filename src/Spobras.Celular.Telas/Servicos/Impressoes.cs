using System.Security.Cryptography;

namespace Spobras.Celular.Servicos;

/// <summary>
/// Marca rapida do conteudo de uma foto: tamanho mais o comeco e o fim do arquivo. Basta para reconhecer a mesma
/// foto escolhida duas vezes sem ler arquivos de varios MB inteiros.
/// </summary>
public static class Impressoes
{
    private const int Trecho = 64 * 1024;

    public static string De(byte[] dados) => Calcular(dados.LongLength, dados.AsSpan(0, (int)Math.Min(Trecho, dados.Length)), dados.AsSpan((int)Math.Max(0, dados.Length - Trecho)));

    public static string De(string arquivo)
    {
        using var f = new FileStream(arquivo, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        long tamanho = f.Length;
        var comeco = new byte[(int)Math.Min(Trecho, tamanho)];
        f.ReadExactly(comeco);
        var fim = new byte[(int)Math.Min(Trecho, tamanho)];
        f.Seek(tamanho - fim.Length, SeekOrigin.Begin);
        f.ReadExactly(fim);
        return Calcular(tamanho, comeco, fim);
    }

    private static string Calcular(long tamanho, ReadOnlySpan<byte> comeco, ReadOnlySpan<byte> fim)
    {
        using var h = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        h.AppendData(BitConverter.GetBytes(tamanho));
        h.AppendData(comeco);
        h.AppendData(fim);
        return Convert.ToHexString(h.GetHashAndReset())[..24];
    }
}
