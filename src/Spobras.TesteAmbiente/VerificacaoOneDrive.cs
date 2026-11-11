using System.Diagnostics;
using Microsoft.Win32;
using Spobras.Core.Ambiente;

namespace Spobras.TesteAmbiente;

/// <summary>Item 6: lista as pastas sincronizadas e le um arquivo que esteja so na nuvem.</summary>
internal static class VerificacaoOneDrive
{
    public const string Titulo = "Ler a pasta sincronizada do OneDrive (listar e ler um arquivo só na nuvem)";

    private const int AtributoOffline = 0x1000;
    private const int AtributoBaixarAoAbrir = 0x400000; // FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS
    private const int MaximoDeEntradas = 20_000;
    private const long TamanhoMaximo = 20 * 1024 * 1024;
    private static readonly TimeSpan LimiteLeitura = TimeSpan.FromSeconds(90);

    public static async Task<ResultadoVerificacao> ExecutarAsync()
    {
        var detalhes = new List<string>();
        var raizes = PastasSincronizadas();
        if (raizes.Count == 0)
        {
            return ResultadoVerificacao.Falhou(6, Titulo,
                "Nenhuma pasta sincronizada do OneDrive/SharePoint foi encontrada neste usuário.");
        }

        FileInfo? naNuvem = null;
        FileInfo? local = null;
        int totalArquivos = 0;
        bool erroAoListar = false;

        foreach (string raiz in raizes)
        {
            int arquivos = 0;
            int soNaNuvem = 0;
            try
            {
                var opcoes = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
                foreach (var arquivo in new DirectoryInfo(raiz).EnumerateFiles("*", opcoes))
                {
                    arquivos++;
                    int atributos = (int)arquivo.Attributes;
                    if ((atributos & (AtributoOffline | AtributoBaixarAoAbrir)) != 0)
                    {
                        soNaNuvem++;
                        if (arquivo.Length is > 0 and <= TamanhoMaximo && (naNuvem is null || arquivo.Length < naNuvem.Length))
                        {
                            naNuvem = arquivo;
                        }
                    }
                    else if (local is null && arquivo.Length is > 0 and <= TamanhoMaximo)
                    {
                        local = arquivo;
                    }

                    if (arquivos >= MaximoDeEntradas)
                    {
                        break;
                    }
                }

                string limite = arquivos >= MaximoDeEntradas ? " (parou no limite do teste)" : "";
                detalhes.Add($"{raiz}: {arquivos} arquivo(s) listado(s){limite}, {soNaNuvem} só na nuvem.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                erroAoListar = true;
                detalhes.Add($"{raiz}: falha ao listar: {ex.Message}");
            }

            totalArquivos += arquivos;
        }

        if (erroAoListar && totalArquivos == 0)
        {
            return ResultadoVerificacao.Falhou(6, Titulo, [.. detalhes]);
        }

        if (naNuvem is not null)
        {
            var (ok, texto) = await LerAsync(naNuvem, "só na nuvem");
            detalhes.Add(texto);
            if (ok)
            {
                detalhes.Add("Observação: esse arquivo foi baixado pelo OneDrive para ser lido e agora ocupa espaço no PC.");
            }

            return ok
                ? ResultadoVerificacao.Passou(6, Titulo, [.. detalhes])
                : ResultadoVerificacao.Falhou(6, Titulo, [.. detalhes]);
        }

        if (local is not null)
        {
            var (ok, texto) = await LerAsync(local, "já baixado");
            detalhes.Add(texto);
            if (!ok)
            {
                return ResultadoVerificacao.Falhou(6, Titulo, [.. detalhes]);
            }
        }

        detalhes.Add("Listar: ok. Não há arquivo só na nuvem (até 20 MB) para testar a leitura sob demanda.");
        detalhes.Add("Para testar: no Explorador, clique com o botão direito em um arquivo pequeno do OneDrive, escolha \"Liberar espaço\" e rode o teste de novo.");
        return ResultadoVerificacao.NaoVerificado(6, Titulo, [.. detalhes]);
    }

    private static async Task<(bool Ok, string Texto)> LerAsync(FileInfo arquivo, string tipo)
    {
        string descricao = $"arquivo {tipo} ({arquivo.Extension}, {arquivo.Length / 1024.0:0.0} KB)";
        var relogio = Stopwatch.StartNew();
        var leitura = Task.Run(() =>
        {
            using var fluxo = new FileStream(arquivo.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var buffer = new byte[81920];
            long total = 0;
            int lidos;
            while ((lidos = fluxo.Read(buffer, 0, buffer.Length)) > 0)
            {
                total += lidos;
            }

            return total;
        });

        try
        {
            if (await Task.WhenAny(leitura, Task.Delay(LimiteLeitura)) != leitura)
            {
                return (false, $"Ler {descricao}: não terminou em {LimiteLeitura.TotalSeconds:0} s (sem internet ou OneDrive pausado?).");
            }

            long bytes = await leitura;
            return bytes == arquivo.Length
                ? (true, $"Ler {descricao}: ok em {relogio.ElapsedMilliseconds} ms.")
                : (false, $"Ler {descricao}: leu {bytes} de {arquivo.Length} bytes.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (false, $"Ler {descricao}: falha: {ex.Message}");
        }
    }

    /// <summary>OneDrive pessoal/corporativo e bibliotecas do SharePoint sincronizadas, sem repetir pastas.</summary>
    private static List<string> PastasSincronizadas()
    {
        var pastas = new List<string>();

        void Adicionar(string? caminho)
        {
            if (string.IsNullOrWhiteSpace(caminho))
            {
                return;
            }

            caminho = caminho.TrimEnd('\\');
            if (Directory.Exists(caminho) && !pastas.Contains(caminho, StringComparer.OrdinalIgnoreCase))
            {
                pastas.Add(caminho);
            }
        }

        foreach (string variavel in new[] { "OneDriveCommercial", "OneDrive", "OneDriveConsumer" })
        {
            Adicionar(Environment.GetEnvironmentVariable(variavel));
        }

        try
        {
            using var contas = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\OneDrive\Accounts");
            foreach (string nome in contas?.GetSubKeyNames() ?? [])
            {
                using var conta = contas!.OpenSubKey(nome);
                Adicionar(conta?.GetValue("UserFolder") as string);
            }

            // Bibliotecas do SharePoint/Teams sincronizadas aparecem aqui como pontos de montagem.
            using var provedores = Registry.CurrentUser.OpenSubKey(@"Software\SyncEngines\Providers\OneDrive");
            foreach (string nome in provedores?.GetSubKeyNames() ?? [])
            {
                using var provedor = provedores!.OpenSubKey(nome);
                Adicionar(provedor?.GetValue("MountPoint") as string);
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            // Sem acesso ao registro: seguimos com as variaveis de ambiente.
        }

        return pastas;
    }
}
