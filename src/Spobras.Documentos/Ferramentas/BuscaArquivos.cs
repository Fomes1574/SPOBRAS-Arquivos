using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Spobras.Core.Ferramentas;
using Spobras.Documentos.Pdf;
using Spobras.Documentos.Planilhas;

namespace Spobras.Documentos.Ferramentas;

public sealed record ResultadoBusca(IReadOnlyList<AchadoBusca> Achados, int Arquivos, int LidosAgora, int SemLeitura, bool Interrompida);

/// <summary>
/// Busca de um texto (contrato, escola...) no nome e no conteudo de planilhas, PDFs com texto, Word e
/// texto simples, dentro das pastas escolhidas. O que ja foi lido fica no indice e so e relido se o
/// arquivo mudar. Nada e aberto no Excel e nenhum arquivo e alterado.
/// </summary>
public static partial class BuscaArquivos
{
    public const int LimiteArquivos = 20000;
    public const long LimiteTamanho = 40L * 1024 * 1024;
    private static readonly HashSet<string> ComConteudo = new(StringComparer.OrdinalIgnoreCase) { ".xlsx", ".xlsm", ".pdf", ".docx", ".txt", ".csv" };

    public static bool LeConteudo(string caminho) => ComConteudo.Contains(Path.GetExtension(caminho));

    public static ResultadoBusca Buscar(
        IReadOnlyList<string> pastas, string termo, IIndiceTexto indice, IProgress<(int Feitos, string Atual)>? progresso = null,
        CancellationToken cancelamento = default)
    {
        var achados = new List<AchadoBusca>();
        int arquivos = 0, lidosAgora = 0, semLeitura = 0;
        bool interrompida = false;
        foreach (string caminho in Arquivos(pastas))
        {
            if (cancelamento.IsCancellationRequested || arquivos >= LimiteArquivos)
            {
                interrompida = true;
                break;
            }

            arquivos++;
            if (arquivos % 25 == 0)
            {
                progresso?.Report((arquivos, Path.GetFileName(caminho)));
            }

            string nome = Path.GetFileName(caminho);
            if (TrechoBusca.Trecho(nome, termo) is not null)
            {
                achados.Add(new AchadoBusca(caminho, true, "No nome do arquivo"));
                continue;
            }

            if (!LeConteudo(caminho))
            {
                continue;
            }

            FileInfo info;
            try
            {
                info = new FileInfo(caminho);
                _ = info.Length;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            long ticks = info.LastWriteTimeUtc.Ticks;
            var texto = indice.Ler(caminho, info.Length, ticks);
            if (texto is null)
            {
                texto = Ler(caminho, info.Length, cancelamento);
                indice.Gravar(caminho, info.Length, ticks, texto);
                lidosAgora++;
            }

            if (texto.Texto is null)
            {
                semLeitura++;
                continue;
            }

            if (TrechoBusca.Trecho(texto.Texto, termo) is { } trecho)
            {
                achados.Add(new AchadoBusca(caminho, false, trecho));
            }
        }

        return new ResultadoBusca(achados, arquivos, lidosAgora, semLeitura, interrompida);
    }

    private static IEnumerable<string> Arquivos(IReadOnlyList<string> pastas)
    {
        var opcoes = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.System | FileAttributes.Hidden };
        var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string pasta in pastas.Where(Directory.Exists))
        {
            foreach (string arquivo in Directory.EnumerateFiles(pasta, "*", opcoes))
            {
                string nome = Path.GetFileName(arquivo);
                if (nome.StartsWith("~$", StringComparison.Ordinal) || nome.EndsWith(".spobras-parcial", StringComparison.OrdinalIgnoreCase) || !vistos.Add(arquivo))
                {
                    continue;
                }

                yield return arquivo;
            }
        }
    }

    /// <summary>Texto do arquivo para a busca, ou o motivo de nao ter lido.</summary>
    public static TextoIndexado Ler(string caminho, long tamanho, CancellationToken cancelamento = default)
    {
        if (tamanho > LimiteTamanho)
        {
            return new TextoIndexado(null, "arquivo muito grande para ler o conteúdo");
        }

        try
        {
            string? texto = Path.GetExtension(caminho).ToLowerInvariant() switch
            {
                ".txt" or ".csv" => File.ReadAllText(caminho),
                ".xlsx" or ".xlsm" => Planilha(caminho),
                ".docx" => Word(caminho),
                ".pdf" => Pdf(caminho, cancelamento),
                _ => null,
            };
            return texto is null || texto.Trim().Length == 0
                ? new TextoIndexado(null, Path.GetExtension(caminho).Equals(".pdf", StringComparison.OrdinalIgnoreCase) ? "PDF sem texto (digitalizado)" : "sem texto")
                : new TextoIndexado(texto, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or PlanilhaInvalidaException or PdfInvalidoException
                                   or System.Xml.XmlException or InvalidOperationException)
        {
            return new TextoIndexado(null, ex is PdfInvalidoException or PlanilhaInvalidaException ? ex.Message : "não foi possível ler: " + ex.Message);
        }
    }

    private static string Planilha(string caminho)
    {
        using var leitor = new LeitorXlsx(caminho);
        var texto = new StringBuilder();
        foreach (string aba in leitor.NomesDasAbas())
        {
            texto.Append(aba).Append('\n');
            foreach (var linha in leitor.LerAba(aba))
            {
                foreach (var celula in linha)
                {
                    if (celula is string s)
                    {
                        texto.Append(s).Append(' ');
                    }
                }

                texto.Append('\n');
            }
        }

        return texto.ToString();
    }

    private static string Word(string caminho)
    {
        using var zip = ZipFile.OpenRead(caminho);
        var parte = zip.GetEntry("word/document.xml") ?? throw new InvalidDataException("não é um documento do Word");
        using var leitor = new StreamReader(parte.Open());
        string xml = leitor.ReadToEnd();
        xml = Paragrafo().Replace(xml, "\n");
        return System.Net.WebUtility.HtmlDecode(Marcas().Replace(xml, ""));
    }

    private static string Pdf(string caminho, CancellationToken cancelamento)
    {
        var lido = TextoPdf.Extrair(caminho, cancelamento);
        return string.Join("\n", lido.Linhas.Select(l => string.Join("  ", l.Colunas)));
    }

    [GeneratedRegex("</w:p>")]
    private static partial Regex Paragrafo();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Marcas();
}
