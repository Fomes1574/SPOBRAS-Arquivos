using System.Diagnostics;

namespace Spobras.Core.Ambiente;

/// <summary>Item 9: vigia uma pasta e detecta um arquivo novo criado pelo proprio teste.</summary>
public static class VerificacaoVigia
{
    public const int Numero = 9;
    public const string Titulo = "Vigiar Downloads: detectar um arquivo novo";

    public static async Task<ResultadoVerificacao> ExecutarAsync(string pasta, TimeSpan limite)
    {
        if (!Directory.Exists(pasta))
        {
            return ResultadoVerificacao.Falhou(Numero, Titulo, $"Pasta não encontrada: {pasta}");
        }

        string nome = "ArquivosSPOBRAS-vigia-" + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        string caminho = Path.Combine(pasta, nome);
        var detectado = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            using var vigia = new FileSystemWatcher(pasta)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                IncludeSubdirectories = false,
            };
            vigia.Created += (_, e) =>
            {
                if (string.Equals(e.Name, nome, StringComparison.OrdinalIgnoreCase))
                {
                    detectado.TrySetResult("criação");
                }
            };
            vigia.Error += (_, e) => detectado.TrySetException(e.GetException());
            vigia.EnableRaisingEvents = true;

            var relogio = Stopwatch.StartNew();
            await File.WriteAllTextAsync(caminho, "teste").ConfigureAwait(false);

            var primeiro = await Task.WhenAny(detectado.Task, Task.Delay(limite)).ConfigureAwait(false);
            if (primeiro != detectado.Task)
            {
                return ResultadoVerificacao.Falhou(Numero, Titulo,
                    $"Pasta: {pasta}",
                    $"O arquivo novo não foi detectado em {limite.TotalSeconds:0} s.");
            }

            await detectado.Task.ConfigureAwait(false);
            return ResultadoVerificacao.Passou(Numero, Titulo,
                $"Pasta: {pasta}",
                $"Arquivo novo detectado em {relogio.ElapsedMilliseconds} ms.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
                                       or System.ComponentModel.Win32Exception)
        {
            return ResultadoVerificacao.Falhou(Numero, Titulo, $"Pasta: {pasta}", $"Falha ao vigiar: {ex.Message}");
        }
        finally
        {
            try
            {
                File.Delete(caminho);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Arquivo temporario nosso.
            }
        }
    }
}
