namespace Spobras.Core;

/// <summary>Arquivo de registro (ex.: erros) que nunca passa do tamanho maximo: guarda sempre o mais recente.</summary>
public static class RegistroLimitado
{
    public const long MaximoPadrao = 1024 * 1024;

    public static void Acrescentar(string caminho, string texto, long maximo = MaximoPadrao)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(caminho)!);
        File.AppendAllText(caminho, texto);
        var info = new FileInfo(caminho);
        if (info.Length <= maximo)
        {
            return;
        }

        // Passou do limite: fica a metade mais recente, comecando numa linha inteira.
        byte[] tudo = File.ReadAllBytes(caminho);
        int inicio = (int)(tudo.Length - maximo / 2);
        int quebra = Array.IndexOf(tudo, (byte)'\n', inicio);
        inicio = quebra < 0 ? inicio : quebra + 1;
        string temp = caminho + ".tmp";
        File.WriteAllBytes(temp, tudo[inicio..]);
        File.Move(temp, caminho, overwrite: true);
    }
}
