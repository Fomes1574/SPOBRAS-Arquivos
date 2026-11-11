using System.IO.Compression;
using Spobras.Core;
using Spobras.Core.Compactar;

namespace Spobras.Documentos.Zip;

public sealed record ZipCriado(string Caminho, long Tamanho, int Arquivos);

/// <summary>
/// Grava um zip planejado (especificacao 4.3). Escreve num arquivo temporario, confere a integridade
/// e o limite, e so entao publica o nome final. Nunca sobrescreve e nunca altera os originais.
/// </summary>
public static class Compactador
{
    public const string SufixoParcial = ".spobras-parcial";

    /// <param name="limiteBytes">Tamanho maximo do zip final, ou null se nao houver limite.</param>
    public static ZipCriado Criar(
        ZipPlanejado plano,
        long? limiteBytes = null,
        CancellationToken cancelamento = default,
        IProgress<int>? arquivosFeitos = null)
    {
        if (File.Exists(plano.CaminhoZip))
        {
            throw new IOException($"Já existe \"{plano.NomeZip}\" na pasta de destino. Nada foi sobrescrito.");
        }

        string? pasta = Path.GetDirectoryName(plano.CaminhoZip);
        if (!string.IsNullOrEmpty(pasta))
        {
            Directory.CreateDirectory(pasta);
        }

        string parcial = plano.CaminhoZip + SufixoParcial;
        try
        {
            using (var saida = new FileStream(parcial, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var zip = new ZipArchive(saida, ZipArchiveMode.Create))
            {
                int feitos = 0;
                foreach (var entrada in plano.Entradas)
                {
                    cancelamento.ThrowIfCancellationRequested();
                    var item = zip.CreateEntry(entrada.NomeNoZip, CompressionLevel.Optimal);
                    item.LastWriteTime = File.GetLastWriteTime(entrada.CaminhoOrigem);
                    using var origem = new FileStream(entrada.CaminhoOrigem, FileMode.Open, FileAccess.Read, FileShare.Read);
                    using var destino = item.Open();
                    origem.CopyTo(destino);
                    arquivosFeitos?.Report(++feitos);
                }
            }

            long tamanho = new FileInfo(parcial).Length;
            if (limiteBytes is { } limite && tamanho > limite)
            {
                throw new InvalidOperationException(
                    $"\"{plano.NomeZip}\" ficou com {Tamanho.Formatar(tamanho)}, acima do limite de {Tamanho.Formatar(limite)}. O zip não foi criado.");
            }

            Conferir(parcial, plano);
            File.Move(parcial, plano.CaminhoZip, overwrite: false);
            return new ZipCriado(plano.CaminhoZip, tamanho, plano.Entradas.Count);
        }
        catch
        {
            // O temporario e nosso e nunca teve o nome final.
            File.Delete(parcial);
            throw;
        }
    }

    /// <summary>Reabre o zip e le cada arquivo ate o fim, conferindo quantidade e tamanhos.</summary>
    private static void Conferir(string caminhoZip, ZipPlanejado plano)
    {
        using var zip = ZipFile.OpenRead(caminhoZip);
        if (zip.Entries.Count != plano.Entradas.Count)
        {
            throw new InvalidDataException("O zip gerado não tem todos os arquivos. O zip não foi criado.");
        }

        var buffer = new byte[81920];
        for (int i = 0; i < zip.Entries.Count; i++)
        {
            using var fluxo = zip.Entries[i].Open();
            long lidos = 0;
            int n;
            while ((n = fluxo.Read(buffer, 0, buffer.Length)) > 0)
            {
                lidos += n;
            }

            if (lidos != plano.Entradas[i].Tamanho)
            {
                throw new InvalidDataException(
                    $"\"{plano.Entradas[i].NomeNoZip}\" mudou de tamanho durante a compactação. O zip não foi criado.");
            }
        }
    }
}
