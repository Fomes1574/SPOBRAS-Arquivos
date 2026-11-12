using System.Globalization;
using System.Text.RegularExpressions;
using Spobras.Documentos.Planilhas;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Exceptions;

namespace Spobras.Documentos.Pdf;

/// <summary>Uma linha de texto da pagina, ja dividida em colunas pelos espacos largos.</summary>
public sealed record LinhaPdf(int Pagina, int Numero, IReadOnlyList<string> Colunas);

public sealed record TextoExtraido(int Paginas, IReadOnlyList<LinhaPdf> Linhas, IReadOnlyList<int> PaginasSemTexto)
{
    public bool SemTextoNenhum => Linhas.Count == 0;
}

/// <summary>
/// Le o texto de PDFs que TEM texto (gerados por sistema). PDF digitalizado (imagem) nao tem texto: precisa
/// de OCR, que nao faz parte desta versao. O resultado e para conferir, nao uma copia fiel do documento.
/// </summary>
public static partial class TextoPdf
{
    public static TextoExtraido Extrair(string caminho, CancellationToken cancelamento = default)
    {
        PdfDocument doc;
        try
        {
            doc = PdfDocument.Open(caminho);
        }
        catch (PdfDocumentEncryptedException)
        {
            throw new PdfInvalidoException($"\"{Path.GetFileName(caminho)}\" é protegido por senha. O app não lê PDF protegido.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new PdfInvalidoException($"Não foi possível abrir \"{Path.GetFileName(caminho)}\": {ex.Message}");
        }
        catch (Exception ex) when (ex is PdfDocumentFormatException or InvalidOperationException or ArgumentException or FormatException)
        {
            throw new PdfInvalidoException($"\"{Path.GetFileName(caminho)}\" não pôde ser lido como PDF: {ex.Message}");
        }

        using (doc)
        {
            var linhas = new List<LinhaPdf>();
            var semTexto = new List<int>();
            foreach (var pagina in doc.GetPages())
            {
                cancelamento.ThrowIfCancellationRequested();
                var palavras = pagina.GetWords().Where(p => !string.IsNullOrWhiteSpace(p.Text)).ToList();
                if (palavras.Count == 0)
                {
                    semTexto.Add(pagina.Number);
                    continue;
                }

                int n = 0;
                foreach (var linha in Agrupar(palavras))
                {
                    linhas.Add(new LinhaPdf(pagina.Number, ++n, Colunas(linha)));
                }
            }

            return new TextoExtraido(doc.NumberOfPages, linhas, semTexto);
        }
    }

    /// <summary>Palavras na mesma altura (com tolerancia) formam uma linha, de cima para baixo.</summary>
    private static IEnumerable<List<Word>> Agrupar(List<Word> palavras)
    {
        var ordenadas = palavras.OrderByDescending(p => p.BoundingBox.Bottom).ToList();
        var atual = new List<Word>();
        double baseAtual = double.NaN;
        foreach (var p in ordenadas)
        {
            double tolerancia = Math.Max(2, p.BoundingBox.Height * 0.5);
            if (atual.Count > 0 && Math.Abs(p.BoundingBox.Bottom - baseAtual) > tolerancia)
            {
                yield return [.. atual.OrderBy(w => w.BoundingBox.Left)];
                atual = [];
            }

            if (atual.Count == 0)
            {
                baseAtual = p.BoundingBox.Bottom;
            }

            atual.Add(p);
        }

        if (atual.Count > 0)
        {
            yield return [.. atual.OrderBy(w => w.BoundingBox.Left)];
        }
    }

    /// <summary>Espaco maior que cerca de dois caracteres separa colunas; espaco normal junta palavras.</summary>
    private static List<string> Colunas(List<Word> linha)
    {
        var colunas = new List<string>();
        var texto = new System.Text.StringBuilder();
        Word? anterior = null;
        foreach (var w in linha)
        {
            if (anterior is not null)
            {
                double larguraLetra = anterior.BoundingBox.Width / Math.Max(1, anterior.Text.Length);
                double espaco = w.BoundingBox.Left - anterior.BoundingBox.Right;
                if (espaco > Math.Max(larguraLetra * 2.2, 6))
                {
                    colunas.Add(texto.ToString());
                    texto.Clear();
                }
                else
                {
                    texto.Append(' ');
                }
            }

            texto.Append(w.Text);
            anterior = w;
        }

        colunas.Add(texto.ToString());
        return colunas;
    }

    [GeneratedRegex(@"^-?(R\$\s?)?\d{1,3}(\.\d{3})*,\d{2}$|^-?(R\$\s?)?\d+,\d{2}$")]
    private static partial Regex ValorBr();

    [GeneratedRegex(@"^\d{2}/\d{2}/\d{4}$")]
    private static partial Regex DataBr();

    /// <summary>"1.234,56" vira numero e "10/11/2026" vira data; o resto (inclusive NE, SEI, dotacao) fica texto.</summary>
    public static object Valor(string texto)
    {
        string t = texto.Trim();
        if (ValorBr().IsMatch(t) && decimal.TryParse(t.Replace("R$", "").Trim(), NumberStyles.Number, CultureInfo.GetCultureInfo("pt-BR"), out decimal numero))
        {
            return numero;
        }

        if (DataBr().IsMatch(t) && DateOnly.TryParseExact(t, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var data))
        {
            return data;
        }

        return t;
    }

    /// <summary>
    /// Grava uma planilha nova com uma linha por linha do PDF: pagina e linha de origem, depois as colunas.
    /// Valores em reais e datas viram numero e data do Excel; a aba "Sobre" diz o que conferir.
    /// </summary>
    public static TextoExtraido ParaExcel(string pdf, string saida, CancellationToken cancelamento = default)
    {
        var extraido = Extrair(pdf, cancelamento);
        if (extraido.SemTextoNenhum)
        {
            throw new PdfInvalidoException($"\"{Path.GetFileName(pdf)}\" não tem texto (parece digitalizado). Ler PDF digitalizado precisa de OCR, que ainda não faz parte do app.");
        }

        int colunas = extraido.Linhas.Max(l => l.Colunas.Count);
        string[] titulos = ["Página", "Linha", .. Enumerable.Range(1, colunas).Select(c => $"Coluna {c}")];
        var linhas = extraido.Linhas.Select(l => new object?[] { l.Pagina, l.Numero }.Concat(l.Colunas.Select(c => (object?)Valor(c))).ToArray()).ToList();
        var sobre = new List<object?[]>
        {
            new object?[] { "Arquivo de origem", Path.GetFileName(pdf) },
            new object?[] { "Páginas", extraido.Paginas },
            new object?[] { "Linhas lidas", extraido.Linhas.Count },
            new object?[] { "Páginas sem texto", extraido.PaginasSemTexto.Count == 0 ? "nenhuma" : string.Join(", ", extraido.PaginasSemTexto) + " (digitalizadas: precisam de OCR)" },
            new object?[] { "Atenção", "Leitura automática do texto do PDF. Confira valores, datas, linhas quebradas e subtotais antes de usar." },
        };
        GravadorXlsx.Gravar(saida, [new AbaNova("Texto do PDF", titulos, linhas), new AbaNova("Sobre", ["Item", "Valor"], sobre, ComFiltro: false)]);
        return extraido;
    }
}
