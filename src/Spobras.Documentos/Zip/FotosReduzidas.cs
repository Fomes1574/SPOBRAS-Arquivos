using System.Security.Cryptography;
using System.Text;
using Spobras.Documentos.Imagens;

namespace Spobras.Documentos.Zip;

/// <summary>Resultado da preparacao: original -> copia reduzida (so as que ficaram menores).</summary>
public sealed record PreparoFotos(IReadOnlyDictionary<string, (string Caminho, long Tamanho)> Copias, int Fotos, long Antes, long Depois, IReadOnlyList<string> Problemas);

/// <summary>
/// Copias reduzidas de fotos JPG para entrar no zip (especificacao 4.3, item 5). As copias ficam numa
/// pasta temporaria do app; os originais nunca sao alterados. Copia que nao fica menor e descartada.
/// </summary>
public static class FotosReduzidas
{
    public static bool Reduzivel(string caminho) =>
        Path.GetExtension(caminho).ToLowerInvariant() is ".jpg" or ".jpeg";

    public static PreparoFotos Preparar(
        IEnumerable<string> arquivos, string pastaCache, int ladoMaximo, int qualidade, IProgress<int>? feitas = null, CancellationToken cancelamento = default)
    {
        var copias = new Dictionary<string, (string, long)>(StringComparer.OrdinalIgnoreCase);
        var problemas = new List<string>();
        int fotos = 0, n = 0;
        long antes = 0, depois = 0;
        Directory.CreateDirectory(pastaCache);
        foreach (string foto in arquivos.Where(Reduzivel).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancelamento.ThrowIfCancellationRequested();
            feitas?.Report(++n);
            try
            {
                var info = new FileInfo(foto);
                fotos++;
                antes += info.Length;
                string chave = $"{foto}|{info.Length}|{info.LastWriteTimeUtc.Ticks}|{ladoMaximo}|{qualidade}";
                string copia = Path.Combine(pastaCache, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(chave)))[..32] + ".jpg");
                if (!File.Exists(copia))
                {
                    byte[] dados = Fotos.Preparar(foto, ladoMaximo, qualidade);
                    if (dados.Length >= info.Length)
                    {
                        depois += info.Length; // nao ficou menor: vai o original
                        continue;
                    }

                    string temporario = copia + ".tmp";
                    File.WriteAllBytes(temporario, dados);
                    File.Move(temporario, copia, overwrite: true);
                }

                long tamanho = new FileInfo(copia).Length;
                copias[foto] = (copia, tamanho);
                depois += tamanho;
            }
            catch (Exception ex) when (ex is FotoInvalidaException or IOException or UnauthorizedAccessException)
            {
                problemas.Add($"{Path.GetFileName(foto)}: {ex.Message} (vai sem reduzir)");
                if (File.Exists(foto))
                {
                    depois += new FileInfo(foto).Length;
                }
            }
        }

        return new PreparoFotos(copias, fotos, antes, depois, problemas);
    }
}
