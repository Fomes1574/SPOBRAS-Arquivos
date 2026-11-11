using System.Security.Cryptography;

namespace Spobras.Core.Abstracoes;

public sealed record InfoArquivo(long Tamanho, DateTimeOffset ModificadoEm, DateTimeOffset CriadoEm);

/// <summary>
/// Acesso ao disco usado pelas operacoes em arquivos. Existe para testar as regras
/// (falhas, interrupcoes, outro volume) sem depender do disco real.
/// Nenhum metodo sobrescreve: mover ou copiar para um caminho ocupado da erro.
/// </summary>
public interface IArquivos
{
    bool ArquivoExiste(string caminho);
    bool PastaExiste(string caminho);
    InfoArquivo? Info(string caminho);

    /// <summary>Nomes (sem a pasta) dos arquivos diretamente dentro da pasta.</summary>
    IReadOnlyList<string> ListarArquivos(string pasta);

    /// <summary>Caminhos de todos os arquivos da pasta e das subpastas.</summary>
    IReadOnlyList<string> ListarArquivosRecursivo(string pasta);

    void CriarPasta(string caminho);
    void Mover(string origem, string destino);
    void Copiar(string origem, string destino);

    /// <summary>
    /// Remove um arquivo. Usado so para temporarios da propria operacao e para a origem
    /// de um "mover" entre volumes depois de a copia ter sido verificada.
    /// </summary>
    void Remover(string caminho);

    bool MesmoVolume(string caminhoA, string caminhoB);
    Stream AbrirLeitura(string caminho);
    string Hash(string caminho);
}

public sealed class ArquivosDisco : IArquivos
{
    public bool ArquivoExiste(string caminho) => File.Exists(caminho);

    public bool PastaExiste(string caminho) => Directory.Exists(caminho);

    public InfoArquivo? Info(string caminho)
    {
        var info = new FileInfo(caminho);
        return info.Exists
            ? new InfoArquivo(info.Length, info.LastWriteTimeUtc, info.CreationTimeUtc)
            : null;
    }

    public IReadOnlyList<string> ListarArquivos(string pasta) =>
        Directory.Exists(pasta)
            ? [.. Directory.EnumerateFiles(pasta).Select(c => Path.GetFileName(c))]
            : [];

    public IReadOnlyList<string> ListarArquivosRecursivo(string pasta) =>
        Directory.Exists(pasta)
            ? [.. Directory.EnumerateFiles(pasta, "*", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                // Nao segue atalhos/pontos de redirecionamento (evita laco e sair da pasta).
                AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System,
            })]
            : [];

    public void CriarPasta(string caminho) => Directory.CreateDirectory(caminho);

    public void Mover(string origem, string destino) => File.Move(origem, destino, overwrite: false);

    public void Copiar(string origem, string destino) => File.Copy(origem, destino, overwrite: false);

    public void Remover(string caminho) => File.Delete(caminho);

    public bool MesmoVolume(string caminhoA, string caminhoB) => string.Equals(
        Path.GetPathRoot(Path.GetFullPath(caminhoA)),
        Path.GetPathRoot(Path.GetFullPath(caminhoB)),
        StringComparison.OrdinalIgnoreCase);

    public Stream AbrirLeitura(string caminho) =>
        new FileStream(caminho, FileMode.Open, FileAccess.Read, FileShare.Read);

    public string Hash(string caminho)
    {
        using var fluxo = AbrirLeitura(caminho);
        return Convert.ToHexString(SHA256.HashData(fluxo));
    }
}
