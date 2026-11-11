using System.IO.Compression;
using Spobras.Core;
using Spobras.Core.Nomes;

namespace Spobras.Documentos.Zip;

public sealed record LimitesExtracao
{
    /// <summary>Total maximo depois de descompactado.</summary>
    public long TamanhoTotal { get; init; } = 4L * 1024 * 1024 * 1024;
    public int Arquivos { get; init; } = 50_000;
}

public sealed record ZipExtraido(string Pasta, int Arquivos, long Tamanho);

/// <summary>
/// Extrai um zip numa pasta propria (especificacao 4.3): nada sai da pasta de destino,
/// nada e sobrescrito e ha limite de tamanho e de quantidade.
/// </summary>
public static class Descompactador
{
    public static ZipExtraido Extrair(
        string caminhoZip,
        string pastaBase,
        LimitesExtracao? limites = null,
        CancellationToken cancelamento = default)
    {
        limites ??= new LimitesExtracao();
        using var zip = ZipFile.OpenRead(caminhoZip);

        var arquivos = zip.Entries.Where(e => !e.FullName.EndsWith('/') && !e.FullName.EndsWith('\\')).ToList();
        if (arquivos.Count > limites.Arquivos)
        {
            throw new InvalidDataException($"O zip tem {arquivos.Count} arquivos; o limite é {limites.Arquivos}. Nada foi extraído.");
        }

        long declarado = arquivos.Sum(e => e.Length);
        if (declarado > limites.TamanhoTotal)
        {
            throw new InvalidDataException(
                $"O zip ocuparia {Tamanho.Formatar(declarado)} depois de aberto; o limite é {Tamanho.Formatar(limites.TamanhoTotal)}. Nada foi extraído.");
        }

        // Pasta propria com o nome do zip; se ja existir, (2), (3).
        string nomePasta = NomeArquivo.Livre(
            Path.GetFileNameWithoutExtension(caminhoZip),
            n => Directory.Exists(Path.Combine(pastaBase, n)) || File.Exists(Path.Combine(pastaBase, n)));
        string pasta = Path.GetFullPath(Path.Combine(pastaBase, nomePasta));
        string raiz = pasta + Path.DirectorySeparatorChar;

        // Confere todos os caminhos antes de gravar qualquer coisa.
        var destinos = new List<(ZipArchiveEntry Entrada, string Destino)>(arquivos.Count);
        var usados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entrada in arquivos)
        {
            string relativo = entrada.FullName.Replace('\\', '/');
            string destino = Path.GetFullPath(Path.Combine(pasta, relativo));
            bool sai = Path.IsPathRooted(relativo)
                       || relativo.Split('/').Any(p => p == "..")
                       || relativo.Contains(':')
                       || !destino.StartsWith(raiz, StringComparison.Ordinal);
            if (sai)
            {
                throw new InvalidDataException(
                    $"O zip tem um arquivo que sairia da pasta de destino (\"{entrada.FullName}\"). Nada foi extraído.");
            }

            // Dois itens com o mesmo nome dentro do zip nao se sobrepoem.
            string dir = Path.GetDirectoryName(destino)!;
            string nome = NomeArquivo.Livre(Path.GetFileName(destino), n => usados.Contains(Path.Combine(dir, n)));
            destino = Path.Combine(dir, nome);
            usados.Add(destino);
            destinos.Add((entrada, destino));
        }

        Directory.CreateDirectory(pasta);
        long total = 0;
        int feitos = 0;
        var buffer = new byte[81920];
        foreach (var (entrada, destino) in destinos)
        {
            cancelamento.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
            try
            {
                using var origem = entrada.Open();
                using var saida = new FileStream(destino, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                int n;
                while ((n = origem.Read(buffer, 0, buffer.Length)) > 0)
                {
                    // O tamanho declarado pode mentir: conta o que realmente sai.
                    total += n;
                    if (total > limites.TamanhoTotal)
                    {
                        throw new InvalidDataException(
                            $"O zip passou de {Tamanho.Formatar(limites.TamanhoTotal)} ao ser aberto. A extração foi interrompida; {feitos} arquivo(s) já extraído(s) ficaram em \"{nomePasta}\".");
                    }

                    saida.Write(buffer, 0, n);
                }
            }
            catch
            {
                File.Delete(destino); // arquivo incompleto, criado por esta operacao
                throw;
            }

            File.SetLastWriteTime(destino, entrada.LastWriteTime.DateTime);
            feitos++;
        }

        return new ZipExtraido(pasta, feitos, total);
    }
}
