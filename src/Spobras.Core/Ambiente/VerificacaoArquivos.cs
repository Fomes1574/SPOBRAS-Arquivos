using System.Text;

namespace Spobras.Core.Ambiente;

/// <summary>
/// Item 5: cria, renomeia, move e remove um arquivo de teste dentro de uma pasta.
/// So mexe em arquivos que ele mesmo criou, dentro de uma subpasta propria.
/// </summary>
public static class VerificacaoArquivos
{
    public const int Numero = 5;
    public const string Titulo = "Criar, renomear, mover e remover um arquivo de teste em Downloads";

    public static ResultadoVerificacao Executar(string pasta)
    {
        var detalhes = new List<string>();
        string etapa = "localizar a pasta";
        string? pastaTeste = null;
        try
        {
            if (!Directory.Exists(pasta))
            {
                return ResultadoVerificacao.Falhou(Numero, Titulo, $"Pasta não encontrada: {pasta}");
            }

            detalhes.Add($"Pasta: {pasta}");

            etapa = "criar a subpasta de teste";
            pastaTeste = Path.Combine(pasta, "ArquivosSPOBRAS-teste-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(pastaTeste);

            etapa = "criar o arquivo";
            const string conteudo = "Teste de Ambiente - Arquivos SPOBRAS - ação, medição, nº 1";
            string original = Path.Combine(pastaTeste, "teste original.txt");
            File.WriteAllText(original, conteudo, Encoding.UTF8);
            detalhes.Add("Criar: ok");

            etapa = "renomear o arquivo";
            string renomeado = Path.Combine(pastaTeste, "Medição 01 - teste renomeado.txt");
            File.Move(original, renomeado);
            if (File.Exists(original) || !File.Exists(renomeado))
            {
                throw new IOException("O arquivo renomeado não apareceu com o novo nome.");
            }

            detalhes.Add("Renomear (com acento e espaço): ok");

            etapa = "mover o arquivo";
            string subpasta = Path.Combine(pastaTeste, "Destino");
            Directory.CreateDirectory(subpasta);
            string movido = Path.Combine(subpasta, Path.GetFileName(renomeado));
            File.Move(renomeado, movido);
            if (File.ReadAllText(movido, Encoding.UTF8) != conteudo)
            {
                throw new IOException("O conteúdo mudou depois de mover.");
            }

            detalhes.Add("Mover para subpasta: ok");

            etapa = "remover o arquivo";
            File.Delete(movido);
            if (File.Exists(movido))
            {
                throw new IOException("O arquivo de teste continuou existindo depois de removido.");
            }

            detalhes.Add("Remover: ok");
            return ResultadoVerificacao.Passou(Numero, Titulo, [.. detalhes]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            detalhes.Add($"Falha ao {etapa}: {ex.Message}");
            return ResultadoVerificacao.Falhou(Numero, Titulo, [.. detalhes]);
        }
        finally
        {
            LimparPastaDeTeste(pastaTeste);
        }
    }

    /// <summary>Remove a subpasta criada pelo proprio teste. Nunca falha o teste por causa da limpeza.</summary>
    public static void LimparPastaDeTeste(string? pastaTeste)
    {
        if (pastaTeste is null)
        {
            return;
        }

        try
        {
            if (Directory.Exists(pastaTeste))
            {
                Directory.Delete(pastaTeste, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A pasta e nossa; se ficar para tras nao atrapalha o usuario.
        }
    }
}
