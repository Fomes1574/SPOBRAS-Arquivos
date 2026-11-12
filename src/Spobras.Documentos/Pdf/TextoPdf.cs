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

public sealed record TextoExtraido(int Paginas, IReadOnlyList<LinhaPdf> Linhas, IReadOnlyList<int> PaginasSemTexto, IReadOnlyList<int>? PaginasPorOcr = null)
{
    public bool SemTextoNenhum => Linhas.Count == 0;
    public IReadOnlyList<int> LidasPorOcr => PaginasPorOcr ?? [];
}

/// <summary>Uma palavra e onde ela esta na pagina (Topo cresce para baixo; unidade qualquer, a mesma na pagina).</summary>
public sealed record PalavraPosicionada(string Texto, double Esquerda, double Direita, double Topo, double Altura)
{
    public double Base => Topo + Altura;
}

/// <summary>
/// Leitura de texto em imagem (OCR). No app, e o OCR do proprio Windows; nos testes, um falso.
/// </summary>
public interface IOcr
{
    bool Disponivel { get; }

    /// <summary>Idioma usado, para o usuario saber (ex.: "Português (Brasil)").</summary>
    string Idioma { get; }

    /// <summary>Palavras de uma pagina do PDF (1 = primeira), lida como imagem.</summary>
    IReadOnlyList<PalavraPosicionada> LerPaginaPdf(string pdf, int pagina);

    /// <summary>Palavras de uma foto ou imagem escaneada.</summary>
    IReadOnlyList<PalavraPosicionada> LerImagem(string imagem);
}

/// <summary>
/// Le o texto de PDFs que TEM texto (gerados por sistema). PDF digitalizado (imagem) nao tem texto: precisa
/// de OCR, que nao faz parte desta versao. O resultado e para conferir, nao uma copia fiel do documento.
/// </summary>
public static partial class TextoPdf
{
    /// <param name="ocr">Para paginas sem texto (digitalizadas): le a imagem. Null = nao le.</param>
    public static TextoExtraido Extrair(string caminho, CancellationToken cancelamento = default, IOcr? ocr = null)
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
            var porOcr = new List<int>();
            foreach (var pagina in doc.GetPages())
            {
                cancelamento.ThrowIfCancellationRequested();
                double altura = pagina.Height;
                var palavras = pagina.GetWords().Where(p => !string.IsNullOrWhiteSpace(p.Text))
                    .Select(w => new PalavraPosicionada(w.Text, w.BoundingBox.Left, w.BoundingBox.Right, altura - w.BoundingBox.Top, w.BoundingBox.Height))
                    .ToList();
                if (palavras.Count == 0 && ocr is { Disponivel: true })
                {
                    palavras = [.. ocr.LerPaginaPdf(caminho, pagina.Number).Where(p => !string.IsNullOrWhiteSpace(p.Texto))];
                    if (palavras.Count > 0)
                    {
                        porOcr.Add(pagina.Number);
                    }
                }

                if (palavras.Count == 0)
                {
                    semTexto.Add(pagina.Number);
                    continue;
                }

                int n = 0;
                foreach (var colunas in Montar(palavras))
                {
                    linhas.Add(new LinhaPdf(pagina.Number, ++n, colunas));
                }
            }

            return new TextoExtraido(doc.NumberOfPages, linhas, semTexto, porOcr);
        }
    }

    /// <summary>Palavras na mesma altura formam uma linha (de cima para baixo); espaco largo separa colunas.</summary>
    public static IReadOnlyList<IReadOnlyList<string>> Montar(IReadOnlyList<PalavraPosicionada> palavras)
    {
        var linhas = new List<IReadOnlyList<string>>();
        var atual = new List<PalavraPosicionada>();
        double baseAtual = double.NaN;
        foreach (var p in palavras.OrderBy(p => p.Base))
        {
            double tolerancia = Math.Max(2, p.Altura * 0.5);
            if (atual.Count > 0 && Math.Abs(p.Base - baseAtual) > tolerancia)
            {
                linhas.Add(Colunas([.. atual.OrderBy(w => w.Esquerda)]));
                atual = [];
            }

            if (atual.Count == 0)
            {
                baseAtual = p.Base;
            }

            atual.Add(p);
        }

        if (atual.Count > 0)
        {
            linhas.Add(Colunas([.. atual.OrderBy(w => w.Esquerda)]));
        }

        return linhas;
    }

    /// <summary>Espaco maior que cerca de dois caracteres separa colunas; espaco normal junta palavras.</summary>
    private static List<string> Colunas(List<PalavraPosicionada> linha)
    {
        var colunas = new List<string>();
        var texto = new System.Text.StringBuilder();
        PalavraPosicionada? anterior = null;
        foreach (var w in linha)
        {
            if (anterior is not null)
            {
                double larguraLetra = (anterior.Direita - anterior.Esquerda) / Math.Max(1, anterior.Texto.Length);
                double espaco = w.Esquerda - anterior.Direita;
                if (espaco > Math.Max(larguraLetra * 2.2, anterior.Altura * 0.6))
                {
                    colunas.Add(texto.ToString());
                    texto.Clear();
                }
                else
                {
                    texto.Append(' ');
                }
            }

            texto.Append(w.Texto);
            anterior = w;
        }

        colunas.Add(texto.ToString());
        return colunas;
    }

    [GeneratedRegex(@"^-?(R\$\s?)?\d{1,3}(\.\d{3})*,\d{2}$|^-?(R\$\s?)?\d+,\d{2}$")]
    private static partial Regex ValorBr();

    [GeneratedRegex(@"^\d{2}/\d{2}/\d{4}$")]
    private static partial Regex DataBr();

    /// <summary>Foto ou imagem escaneada: tudo pelo OCR.</summary>
    private static TextoExtraido DeImagem(string imagem, IOcr? ocr)
    {
        if (ocr is not { Disponivel: true })
        {
            throw new PdfInvalidoException($"\"{Path.GetFileName(imagem)}\" é uma imagem e o OCR do Windows não está disponível neste computador.");
        }

        var linhas = Montar(ocr.LerImagem(imagem)).Select((c, i) => new LinhaPdf(1, i + 1, c)).ToList();
        return new TextoExtraido(1, linhas, linhas.Count == 0 ? [1] : [], linhas.Count == 0 ? [] : [1]);
    }

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
    public static TextoExtraido ParaExcel(string pdf, string saida, CancellationToken cancelamento = default, IOcr? ocr = null)
    {
        bool imagem = Path.GetExtension(pdf).ToLowerInvariant() is ".jpg" or ".jpeg" or ".png" or ".bmp" or ".tif" or ".tiff";
        var extraido = imagem ? DeImagem(pdf, ocr) : Extrair(pdf, cancelamento, ocr);
        if (extraido.SemTextoNenhum)
        {
            throw new PdfInvalidoException(ocr is { Disponivel: true }
                ? $"\"{Path.GetFileName(pdf)}\": o OCR não encontrou texto. Confira se a imagem está nítida e na posição certa."
                : $"\"{Path.GetFileName(pdf)}\" não tem texto (parece digitalizado) e o OCR do Windows não está disponível neste computador.");
        }

        int colunas = extraido.Linhas.Max(l => l.Colunas.Count);
        string[] titulos = ["Página", "Linha", .. Enumerable.Range(1, colunas).Select(c => $"Coluna {c}")];
        var linhas = extraido.Linhas.Select(l => new object?[] { l.Pagina, l.Numero }.Concat(l.Colunas.Select(c => (object?)Valor(c))).ToArray()).ToList();
        var sobre = new List<object?[]>
        {
            new object?[] { "Arquivo de origem", Path.GetFileName(pdf) },
            new object?[] { "Páginas", extraido.Paginas },
            new object?[] { "Linhas lidas", extraido.Linhas.Count },
            new object?[] { "Páginas sem texto", extraido.PaginasSemTexto.Count == 0 ? "nenhuma" : string.Join(", ", extraido.PaginasSemTexto) + " (ficaram de fora)" },
            new object?[] { "Lidas por OCR", extraido.LidasPorOcr.Count == 0 ? "nenhuma" : string.Join(", ", extraido.LidasPorOcr) + $" (imagem lida pelo OCR do Windows, {ocr?.Idioma}: confira com mais atenção)" },
            new object?[] { "Atenção", "Leitura automática. Confira valores, datas, linhas quebradas e subtotais antes de usar." },
        };
        GravadorXlsx.Gravar(saida, [new AbaNova("Texto do PDF", titulos, linhas), new AbaNova("Sobre", ["Item", "Valor"], sobre, ComFiltro: false)]);
        return extraido;
    }
}
