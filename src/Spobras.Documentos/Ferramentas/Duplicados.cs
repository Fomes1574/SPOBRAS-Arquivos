using System.Security.Cryptography;

namespace Spobras.Documentos.Ferramentas;

/// <summary>Arquivos com conteudo identico (mesmo tamanho e mesmo SHA-256). O primeiro e o mais antigo.</summary>
public sealed record GrupoDuplicados(long Tamanho, IReadOnlyList<string> Arquivos)
{
    public long Sobra => Tamanho * (Arquivos.Count - 1);
}

/// <summary>
/// Acha arquivos repetidos pelo conteudo, nao pelo nome. So lista: o app nunca apaga arquivos. A tela
/// pode oferecer separar as copias numa pasta, com previa e Desfazer.
/// </summary>
public static class Duplicados
{
    public static IReadOnlyList<GrupoDuplicados> Procurar(IReadOnlyList<string> pastas, IProgress<int>? progresso = null, CancellationToken cancelamento = default)
    {
        var opcoes = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.System | FileAttributes.Hidden };
        var porTamanho = new Dictionary<long, List<FileInfo>>();
        var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string pasta in pastas.Where(Directory.Exists))
        {
            foreach (string caminho in Directory.EnumerateFiles(pasta, "*", opcoes))
            {
                cancelamento.ThrowIfCancellationRequested();
                if (!vistos.Add(caminho) || Path.GetFileName(caminho).StartsWith("~$", StringComparison.Ordinal))
                {
                    continue;
                }

                var info = new FileInfo(caminho);
                if (info.Length == 0)
                {
                    continue; // arquivos vazios sao todos "iguais" e nao interessam
                }

                (porTamanho.TryGetValue(info.Length, out var lista) ? lista : porTamanho[info.Length] = []).Add(info);
            }
        }

        var grupos = new List<GrupoDuplicados>();
        int feitos = 0;
        foreach (var (tamanho, candidatos) in porTamanho.Where(p => p.Value.Count > 1))
        {
            // Primeiro so o comeco de cada arquivo (rapido, inclusive em pasta de rede): le o arquivo inteiro so de
            // quem ainda pode ser igual.
            var talvez = tamanho <= Trecho
                ? candidatos
                : candidatos.GroupBy(i => Hash(i.FullName, Trecho)).Where(g => g.Key is not null && g.Count() > 1).SelectMany(g => g).ToList();
            feitos += candidatos.Count - talvez.Count;
            var porHash = new Dictionary<string, List<FileInfo>>();
            foreach (var info in talvez)
            {
                cancelamento.ThrowIfCancellationRequested();
                string? hash = Hash(info.FullName);
                if (hash is not null)
                {
                    (porHash.TryGetValue(hash, out var lista) ? lista : porHash[hash] = []).Add(info);
                }

                progresso?.Report(++feitos);
            }

            grupos.AddRange(porHash.Values.Where(l => l.Count > 1)
                .Select(l => new GrupoDuplicados(tamanho, [.. l.OrderBy(f => f.LastWriteTimeUtc).ThenBy(f => f.FullName.Length).Select(f => f.FullName)])));
        }

        return [.. grupos.OrderByDescending(g => g.Sobra)];
    }

    private const int Trecho = 64 * 1024;

    private static string? Hash(string caminho, int? soOComeco = null)
    {
        try
        {
            using var fluxo = File.OpenRead(caminho);
            if (soOComeco is int n)
            {
                var buffer = new byte[n];
                int lidos = fluxo.ReadAtLeast(buffer, n, throwOnEndOfStream: false);
                return Convert.ToHexString(SHA256.HashData(buffer.AsSpan(0, lidos)));
            }

            return Convert.ToHexString(SHA256.HashData(fluxo));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
