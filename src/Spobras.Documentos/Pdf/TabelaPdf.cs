using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Exceptions;
using UglyToad.PdfPig.Graphics.Colors;

namespace Spobras.Documentos.Pdf;

public enum AlinhamentoCelula
{
    Esquerda,
    Centro,
    Direita,
}

/// <summary>Uma celula lida do PDF, com a aparencia dela (para a planilha ficar parecida com o PDF).</summary>
public sealed record CelulaTabela(string Texto)
{
    public bool Negrito { get; init; }

    /// <summary>Tamanho da letra em pontos, como no PDF.</summary>
    public double Fonte { get; init; } = 11;

    /// <summary>Cor da letra "RRGGBB" (null = preta).</summary>
    public string? Cor { get; init; }

    /// <summary>Cor de fundo "RRGGBB" (null = sem fundo).</summary>
    public string? Fundo { get; init; }

    public AlinhamentoCelula Alinhamento { get; init; }

    /// <summary>A celula tem a grade em volta (tabela com linhas no PDF).</summary>
    public bool Borda { get; init; }

    /// <summary>Quantas colunas a celula ocupa (mesclada no PDF).</summary>
    public int Colunas { get; init; } = 1;
}

/// <summary>Uma linha da tabela. <see cref="Celulas"/> tem uma posicao por coluna; null = coberta por uma mescla.</summary>
public sealed record LinhaTabela(int Pagina, double Altura, IReadOnlyList<CelulaTabela?> Celulas)
{
    public IEnumerable<string> Textos => Celulas.Where(c => c is not null).Select(c => c!.Texto);
    public bool Vazia => Celulas.All(c => c is null || c.Texto.Length == 0);
}

public sealed record TabelaLida(
    IReadOnlyList<double> Larguras,
    IReadOnlyList<LinhaTabela> Linhas,
    int Paginas,
    IReadOnlyList<int> PaginasPorGrade,
    IReadOnlyList<int> PaginasPorAlinhamento,
    IReadOnlyList<int> PaginasPorOcr,
    IReadOnlyList<int> PaginasSemTexto,
    int? LinhaDoCabecalho,
    bool Paisagem);

/// <summary>
/// Le tabelas de PDF para virar planilha fiel. Pagina com as linhas da grade (PDF feito pelo Excel ou por
/// sistema): as celulas saem das proprias linhas, com mesclas, negrito, cores e alinhamento. Pagina sem grade:
/// as colunas saem do alinhamento da pagina inteira. Pagina escaneada: OCR, com o mesmo alinhamento.
/// O cabecalho que se repete em cada pagina entra uma vez so; "Pagina X de Y" fica de fora.
/// </summary>
public static partial class TabelaPdf
{
    private const double Fina = 2.5;     // espessura maxima (pt) de um traco da grade
    private const double Tolerancia = 1.6; // pt para juntar tracos na mesma posicao

    private sealed record Traco(double Pos, double De, double Ate);

    private sealed record Fundo(PdfRectangle Area, string Cor);

    private sealed record PaginaLida(int Numero, List<double> Larguras, List<LinhaTabela> Linhas, bool Grade);

    public static TabelaLida Ler(string caminho, CancellationToken cancelamento = default, IOcr? ocr = null)
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
            var paginas = new List<PaginaLida>();
            var semTexto = new List<int>();
            var porOcr = new List<int>();
            bool paisagem = false;
            foreach (var pagina in doc.GetPages())
            {
                cancelamento.ThrowIfCancellationRequested();
                paisagem |= pagina.Width > pagina.Height;
                var letras = pagina.Letters.Where(l => !string.IsNullOrWhiteSpace(l.Value)).ToList();
                if (letras.Count > 0)
                {
                    paginas.Add(PorGrade(pagina, letras) ?? PorAlinhamento(pagina.Number, Palavras(pagina)));
                    continue;
                }

                var lidas = LerPorOcr(pagina, ocr);
                if (lidas.Count == 0)
                {
                    semTexto.Add(pagina.Number);
                    continue;
                }

                porOcr.Add(pagina.Number);
                paginas.Add(PorAlinhamento(pagina.Number, lidas));
            }

            return Juntar(paginas, doc.NumberOfPages, porOcr, semTexto, paisagem);
        }
    }

    /// <summary>Foto de documento: tudo pelo OCR, colunas pelo alinhamento.</summary>
    public static TabelaLida LerImagem(string imagem, IOcr ocr)
    {
        var palavras = ocr.LerImagem(imagem).Where(p => !string.IsNullOrWhiteSpace(p.Texto)).ToList();
        var paginas = palavras.Count == 0 ? [] : new List<PaginaLida> { PorAlinhamento(1, palavras) };
        return Juntar(paginas, 1, palavras.Count == 0 ? [] : [1], palavras.Count == 0 ? [1] : [], false);
    }

    // ---------------- Pagina com grade ----------------

    private static PaginaLida? PorGrade(Page pagina, List<Letter> letras)
    {
        var horizontais = new List<Traco>();
        var verticais = new List<Traco>();
        var fundos = new List<Fundo>();
        foreach (var caminho in pagina.Paths)
        {
            if (!caminho.IsFilled && !caminho.IsStroked)
            {
                continue; // recorte (clip): nao aparece na pagina
            }

            foreach (var parte in caminho)
            {
                if (parte.GetBoundingRectangle() is not { } r)
                {
                    continue;
                }

                double espessura = caminho.IsStroked ? Math.Max(caminho.LineWidth, 0.1) : 0;
                double largura = r.Width + espessura, altura = r.Height + espessura;
                if (largura <= Fina && altura > Fina)
                {
                    verticais.Add(new Traco((r.Left + r.Right) / 2, r.Bottom, r.Top));
                }
                else if (altura <= Fina && largura > Fina)
                {
                    horizontais.Add(new Traco((r.Bottom + r.Top) / 2, r.Left, r.Right));
                }
                else if (caminho.IsFilled && largura > Fina && altura > Fina && Hex(caminho.FillColor) is { } cor && cor != "FFFFFF")
                {
                    fundos.Add(new Fundo(r, cor));

                    // Excel desenha a borda de celula com fundo como retangulo: as bordas dele contam como grade.
                }
            }
        }

        var xs = Agrupar(verticais.Select(v => v.Pos));
        var ys = Agrupar(horizontais.Select(h => h.Pos)).OrderByDescending(y => y).ToList();
        if (xs.Count < 2 || ys.Count < 2)
        {
            return null;
        }

        // Linhas muito baixas (traco duplo) nao sao linhas da tabela.
        ys = Limpar(ys);
        xs = Limpar(xs);
        if (xs.Count < 2 || ys.Count < 2)
        {
            return null;
        }

        int colunas = xs.Count - 1, linhas = ys.Count - 1;
        var dentro = letras.Where(l => Centro(l) is var (x, y) && x >= xs[0] - 1 && x <= xs[^1] + 1 && y <= ys[0] + 1 && y >= ys[^1] - 1).ToList();
        var fora = letras.Except(dentro).ToList();
        if (dentro.Count < letras.Count / 3)
        {
            return null; // a grade e decoracao (moldura), nao a tabela
        }

        bool TemVertical(double x, double y1, double y2) =>
            verticais.Any(v => Math.Abs(v.Pos - x) <= Tolerancia && v.De <= Math.Min(y1, y2) + Tolerancia && v.Ate >= Math.Max(y1, y2) - Tolerancia);

        // Celulas por letra (centro da letra), ja resolvendo as mesclas: sem traco entre duas colunas, e a mesma celula.
        var porCelula = new Dictionary<(int Linha, int Coluna), List<Letter>>();
        var inicioDaMescla = new int[linhas, colunas];
        var larguraDaMescla = new int[linhas, colunas];
        for (int i = 0; i < linhas; i++)
        {
            int inicio = 0;
            for (int j = 0; j < colunas; j++)
            {
                bool separa = j == 0 || TemVertical(xs[j], ys[i], ys[i + 1]);
                if (separa)
                {
                    inicio = j;
                }

                inicioDaMescla[i, j] = inicio;
                larguraDaMescla[i, inicio]++;
            }
        }

        foreach (var letra in dentro)
        {
            var (x, y) = Centro(letra);
            int j = Math.Clamp(Indice(xs, x), 0, colunas - 1);
            int i = Math.Clamp(ys.FindIndex(v => v < y) - 1, 0, linhas - 1);
            if (ys.FindIndex(v => v < y) < 0)
            {
                i = linhas - 1;
            }

            var chave = (i, inicioDaMescla[i, j]);
            if (!porCelula.TryGetValue(chave, out var lista))
            {
                porCelula[chave] = lista = [];
            }

            lista.Add(letra);
        }

        var resultado = new List<LinhaTabela>();
        resultado.AddRange(Soltas(fora.Where(l => Centro(l).Y > ys[0]).ToList(), pagina.Number, colunas));
        for (int i = 0; i < linhas; i++)
        {
            var celulas = new CelulaTabela?[colunas];
            for (int j = 0; j < colunas; j++)
            {
                if (inicioDaMescla[i, j] != j)
                {
                    continue;
                }

                int largura = larguraDaMescla[i, j];
                var area = new PdfRectangle(xs[j], ys[i + 1], xs[j + largura], ys[i]);
                porCelula.TryGetValue((i, j), out var suas);
                celulas[j] = Celula(suas ?? [], area, fundos) with { Borda = true, Colunas = largura };
            }

            resultado.Add(new LinhaTabela(pagina.Number, ys[i] - ys[i + 1], celulas));
        }

        resultado.AddRange(Soltas(fora.Where(l => Centro(l).Y < ys[^1]).ToList(), pagina.Number, colunas));
        return new PaginaLida(pagina.Number, [.. xs.Zip(xs.Skip(1), (a, b) => b - a)], resultado, true);
    }

    /// <summary>Texto fora da grade (titulo acima, observacao abaixo): uma linha mesclada na largura toda.</summary>
    private static IEnumerable<LinhaTabela> Soltas(List<Letter> letras, int pagina, int colunas)
    {
        foreach (var linha in EmLinhas(letras))
        {
            string texto = Texto([linha]);
            if (texto.Length == 0 || NumeroDePagina().IsMatch(texto))
            {
                continue;
            }

            var celulas = new CelulaTabela?[colunas];
            celulas[0] = new CelulaTabela(texto)
            {
                Negrito = Negrito(linha), Fonte = Tamanho(linha), Cor = CorDoTexto(linha), Colunas = colunas, Alinhamento = AlinhamentoCelula.Esquerda,
            };
            yield return new LinhaTabela(pagina, Tamanho(linha) * 1.6, celulas);
        }
    }

    private static CelulaTabela Celula(List<Letter> letras, PdfRectangle area, List<Fundo> fundos)
    {
        string? fundo = fundos.Where(f => f.Area.Left <= area.Centroid.X && f.Area.Right >= area.Centroid.X && f.Area.Bottom <= area.Centroid.Y && f.Area.Top >= area.Centroid.Y)
            .OrderBy(f => f.Area.Width * f.Area.Height).Select(f => f.Cor).FirstOrDefault();
        if (letras.Count == 0)
        {
            return new CelulaTabela("") { Fundo = fundo };
        }

        var linhas = EmLinhas(letras);
        string texto = Compactar(Texto(linhas));
        double esquerda = letras.Min(l => l.BoundingBox.Left), direita = letras.Max(l => l.BoundingBox.Right);
        double folgaEsq = esquerda - area.Left, folgaDir = area.Right - direita;
        // Centralizado quando as duas folgas sao parecidas em relacao ao espaco livre (texto que quase enche a
        // celula, com folgas pequenas e diferentes, e alinhado a esquerda ou a direita).
        var alinhamento = Math.Abs(folgaEsq - folgaDir) <= Math.Max(1.2, (folgaEsq + folgaDir) * 0.25)
            ? AlinhamentoCelula.Centro
            : folgaEsq < folgaDir ? AlinhamentoCelula.Esquerda : AlinhamentoCelula.Direita;
        return new CelulaTabela(texto)
        {
            Negrito = Negrito(letras), Fonte = Tamanho(letras), Cor = CorDoTexto(letras), Fundo = fundo, Alinhamento = alinhamento,
        };
    }

    // ---------------- Pagina sem grade (ou OCR) ----------------

    private static List<PalavraPosicionada> Palavras(Page pagina) =>
        [.. pagina.GetWords().Where(p => !string.IsNullOrWhiteSpace(p.Text))
            .Select(w => new PalavraPosicionada(w.Text, w.BoundingBox.Left, w.BoundingBox.Right, pagina.Height - w.BoundingBox.Top, w.BoundingBox.Height))];

    /// <summary>
    /// Colunas pelo alinhamento da pagina inteira: faixas verticais que quase nenhuma linha atravessa separam
    /// as colunas. Assim todas as linhas usam as mesmas colunas (e nao cada linha as suas).
    /// </summary>
    private static PaginaLida PorAlinhamento(int numero, List<PalavraPosicionada> palavras)
    {
        var linhas = Agrupar(palavras);
        double inicio = palavras.Min(p => p.Esquerda), fim = palavras.Max(p => p.Direita);
        int largura = (int)Math.Ceiling(fim - inicio) + 1;
        var cobertura = new int[largura];
        var linhasDeTabela = linhas.Where(l => l.Count > 1).ToList();
        foreach (var linha in linhasDeTabela)
        {
            foreach (var p in linha)
            {
                for (int x = (int)(p.Esquerda - inicio); x <= (int)(p.Direita - inicio) && x < largura; x++)
                {
                    cobertura[Math.Max(0, x)]++;
                }
            }
        }

        // Cortes: meio de cada faixa vazia (ate 10% das linhas cruzando) com pelo menos 4 pontos de largura.
        double limite = Math.Max(0, linhasDeTabela.Count * 0.1);
        var cortes = new List<double>();
        double altura = palavras.Average(p => p.Altura);
        for (int x = 0; x < largura;)
        {
            if (cobertura[x] <= limite && linhasDeTabela.Count > 0)
            {
                int y = x;
                while (y < largura && cobertura[y] <= limite)
                {
                    y++;
                }

                if (x > 0 && y < largura && y - x >= Math.Max(4, altura * 0.6))
                {
                    cortes.Add(inicio + (x + y) / 2.0);
                }

                x = y;
            }
            else
            {
                x++;
            }
        }

        var bordas = new List<double> { inicio };
        bordas.AddRange(cortes);
        bordas.Add(fim);
        int colunas = bordas.Count - 1;
        var resultado = new List<LinhaTabela>();
        foreach (var linha in linhas)
        {
            var textos = new StringBuilder[colunas];
            foreach (var p in linha.OrderBy(p => p.Esquerda))
            {
                int j = Math.Clamp(Indice(bordas, (p.Esquerda + p.Direita) / 2), 0, colunas - 1);
                textos[j] ??= new StringBuilder();
                if (textos[j].Length > 0)
                {
                    textos[j].Append(' ');
                }

                textos[j].Append(p.Texto);
            }

            string unico = string.Join(" ", linha.OrderBy(p => p.Esquerda).Select(p => p.Texto));
            if (NumeroDePagina().IsMatch(unico))
            {
                continue;
            }

            var celulas = new CelulaTabela?[colunas];
            for (int j = 0; j < colunas; j++)
            {
                celulas[j] = new CelulaTabela(Compactar(textos[j]?.ToString() ?? ""));
            }

            resultado.Add(new LinhaTabela(numero, Math.Max(12, linha.Max(p => p.Altura) * 1.5), celulas));
        }

        return new PaginaLida(numero, [.. bordas.Zip(bordas.Skip(1), (a, b) => b - a)], resultado, false);
    }

    private static List<List<PalavraPosicionada>> Agrupar(List<PalavraPosicionada> palavras)
    {
        var linhas = new List<List<PalavraPosicionada>>();
        foreach (var p in palavras.OrderBy(p => p.Base))
        {
            var ultima = linhas.LastOrDefault();
            if (ultima is not null && Math.Abs(ultima[0].Base - p.Base) <= Math.Max(2, p.Altura * 0.5))
            {
                ultima.Add(p);
            }
            else
            {
                linhas.Add([p]);
            }
        }

        return linhas;
    }

    private static List<PalavraPosicionada> LerPorOcr(Page pagina, IOcr? ocr)
    {
        if (ocr is not { Disponivel: true } || ImagemDaPagina(pagina) is not { } imagem)
        {
            return [];
        }

        string temporario = Path.Combine(Path.GetTempPath(), $"spobras-ocr-{Guid.NewGuid():N}{imagem.Extensao}");
        try
        {
            File.WriteAllBytes(temporario, imagem.Dados);
            return [.. ocr.LerImagem(temporario).Where(p => !string.IsNullOrWhiteSpace(p.Texto))];
        }
        finally
        {
            File.Delete(temporario);
        }
    }

    private static (byte[] Dados, string Extensao)? ImagemDaPagina(Page pagina)
    {
        foreach (var imagem in pagina.GetImages().Where(i => !i.IsImageMask).OrderByDescending(i => (long)i.WidthInSamples * i.HeightInSamples))
        {
            byte[] bruto = imagem.RawMemory.ToArray();
            if (bruto.Length > 3 && bruto[0] == 0xFF && bruto[1] == 0xD8)
            {
                return (bruto, ".jpg");
            }

            if (imagem.TryGetPng(out byte[]? png) && png is not null)
            {
                return (png, ".png");
            }
        }

        return null;
    }

    // ---------------- Juntar as paginas ----------------

    private static TabelaLida Juntar(List<PaginaLida> paginas, int total, List<int> porOcr, List<int> semTexto, bool paisagem)
    {
        if (paginas.Count == 0)
        {
            return new TabelaLida([], [], total, [], [], porOcr, semTexto, null, paisagem);
        }

        // As colunas vem da primeira pagina com grade (ou da primeira pagina); as outras se encaixam nelas.
        var molde = paginas.FirstOrDefault(p => p.Grade) ?? paginas[0];
        int colunas = paginas.Max(p => p.Larguras.Count);
        var larguras = molde.Larguras.Count == colunas ? molde.Larguras : paginas.First(p => p.Larguras.Count == colunas).Larguras;

        var linhas = new List<LinhaTabela>();
        int? cabecalho = null;
        List<string>? textosDoTopo = null;
        foreach (var pagina in paginas)
        {
            var dela = pagina.Linhas.Select(l => Alargar(l, colunas)).Where(l => !l.Vazia).ToList();
            if (textosDoTopo is null)
            {
                int c = Cabecalho(dela);
                if (c >= 0)
                {
                    cabecalho = linhas.Count + c;
                    textosDoTopo = [.. dela.Take(c + 1).Select(Chave)];
                }

                linhas.AddRange(dela);
                continue;
            }

            // Cabecalho e titulo repetidos no alto da pagina seguinte entram uma vez so.
            int pular = 0;
            while (pular < dela.Count && textosDoTopo.Contains(Chave(dela[pular])))
            {
                pular++;
            }

            linhas.AddRange(dela.Skip(pular));
        }

        return new TabelaLida(larguras, linhas, total,
            [.. paginas.Where(p => p.Grade).Select(p => p.Numero)],
            [.. paginas.Where(p => !p.Grade && !porOcr.Contains(p.Numero)).Select(p => p.Numero)],
            porOcr, semTexto, cabecalho, paisagem);
    }

    private static string Chave(LinhaTabela l) => string.Join("|", l.Textos);

    private static LinhaTabela Alargar(LinhaTabela linha, int colunas)
    {
        if (linha.Celulas.Count >= colunas)
        {
            return linha;
        }

        var celulas = linha.Celulas.ToList();
        var ultima = celulas.FindLastIndex(c => c is not null);
        while (celulas.Count < colunas)
        {
            celulas.Add(null);
        }

        // A ultima celula cresce ate o fim (assim nada fica sem lugar).
        if (ultima >= 0)
        {
            celulas[ultima] = celulas[ultima]! with { Colunas = colunas - ultima };
        }

        return linha with { Celulas = celulas };
    }

    /// <summary>A linha de titulos: a primeira com quase todas as colunas preenchidas so com texto, seguida de dados.</summary>
    private static int Cabecalho(List<LinhaTabela> linhas)
    {
        for (int i = 0; i < Math.Min(linhas.Count - 1, 15); i++)
        {
            var celulas = linhas[i].Celulas.Where(c => c is not null).ToList();
            int preenchidas = celulas.Count(c => c!.Texto.Length > 0);
            bool soTexto = celulas.All(c => c!.Texto.Length == 0 || TextoPdf.Valor(c.Texto) is string);
            if (celulas.Count >= 2 && preenchidas >= Math.Max(2, linhas[i].Celulas.Count * 0.6) && soTexto
                && linhas[i + 1].Celulas.Any(c => c is not null && TextoPdf.Valor(c.Texto) is not string))
            {
                return i;
            }
        }

        return -1;
    }

    // ---------------- Texto e aparencia ----------------

    private static (double X, double Y) Centro(Letter l) => ((l.BoundingBox.Left + l.BoundingBox.Right) / 2, (l.BoundingBox.Bottom + l.BoundingBox.Top) / 2);

    private static List<List<Letter>> EmLinhas(List<Letter> letras)
    {
        var linhas = new List<List<Letter>>();
        foreach (var l in letras.OrderByDescending(l => l.StartBaseLine.Y))
        {
            var ultima = linhas.LastOrDefault();
            if (ultima is not null && Math.Abs(ultima[0].StartBaseLine.Y - l.StartBaseLine.Y) <= Math.Max(1, l.PointSize * 0.4))
            {
                ultima.Add(l);
            }
            else
            {
                linhas.Add([l]);
            }
        }

        return linhas;
    }

    /// <summary>Letras em texto: espaco onde o vao entre letras e maior que um quarto do tamanho da fonte.</summary>
    private static string Texto(List<List<Letter>> linhas)
    {
        var partes = new List<string>();
        foreach (var linha in linhas)
        {
            var texto = new StringBuilder();
            Letter? anterior = null;
            foreach (var l in linha.OrderBy(l => l.BoundingBox.Left))
            {
                if (anterior is not null && l.BoundingBox.Left - anterior.BoundingBox.Right > Math.Max(0.8, l.PointSize * 0.22))
                {
                    texto.Append(' ');
                }

                texto.Append(l.Value);
                anterior = l;
            }

            partes.Add(texto.ToString().Trim());
        }

        return string.Join("\n", partes.Where(p => p.Length > 0));
    }

    /// <summary>
    /// Numero que o PDF separou com um espaco no meio ("3 5.000,00", "2 .318,59", "R$ 1 .850,00") volta inteiro.
    /// So mexe quando, sem os espacos, o texto e um valor ou uma data; o resto fica como esta.
    /// </summary>
    public static string Compactar(string texto)
    {
        string t = texto.Trim();
        if (!t.Any(char.IsDigit) || !SoNumero().IsMatch(t))
        {
            return t;
        }

        bool reais = t.StartsWith("R$", StringComparison.Ordinal);
        string junto = (reais ? t[2..] : t).Replace(" ", "");
        if (TextoPdf.Valor(junto) is string)
        {
            return t;
        }

        return reais ? "R$ " + junto : junto;
    }

    private static bool Negrito(IEnumerable<Letter> letras)
    {
        var lista = letras.ToList();
        return lista.Count(l => l.FontName?.Contains("Bold", StringComparison.OrdinalIgnoreCase) == true) * 2 > lista.Count;
    }

    private static bool Negrito(List<List<Letter>> linhas) => Negrito(linhas.SelectMany(l => l));

    private static double Tamanho(IEnumerable<Letter> letras) =>
        Math.Round(letras.GroupBy(l => Math.Round(l.PointSize * 2) / 2).OrderByDescending(g => g.Count()).First().Key, 1);

    private static double Tamanho(List<List<Letter>> linhas) => Tamanho(linhas.SelectMany(l => l));

    private static string? CorDoTexto(IEnumerable<Letter> letras)
    {
        string? cor = letras.GroupBy(l => Hex(l.Color)).OrderByDescending(g => g.Count()).First().Key;
        return cor is null or "000000" ? null : cor;
    }

    private static string? CorDoTexto(List<List<Letter>> linhas) => CorDoTexto(linhas.SelectMany(l => l));

    private static string? Hex(IColor? cor)
    {
        if (cor is null)
        {
            return null;
        }

        var (r, g, b) = cor.ToRGBValues();
        return string.Create(CultureInfo.InvariantCulture, $"{(int)Math.Round(r * 255):X2}{(int)Math.Round(g * 255):X2}{(int)Math.Round(b * 255):X2}");
    }

    private static List<double> Agrupar(IEnumerable<double> posicoes)
    {
        var grupos = new List<List<double>>();
        foreach (double p in posicoes.Order())
        {
            if (grupos.Count > 0 && p - grupos[^1][^1] <= Tolerancia)
            {
                grupos[^1].Add(p);
            }
            else
            {
                grupos.Add([p]);
            }
        }

        return [.. grupos.Select(g => g.Average())];
    }

    /// <summary>Tira posicoes coladas demais (traco duplo vira um so).</summary>
    private static List<double> Limpar(List<double> posicoes)
    {
        var limpas = new List<double>();
        foreach (double p in posicoes)
        {
            if (limpas.Count == 0 || Math.Abs(p - limpas[^1]) > 3)
            {
                limpas.Add(p);
            }
        }

        return limpas;
    }

    /// <summary>Em qual faixa [bordas[j], bordas[j+1]) cai o valor.</summary>
    private static int Indice(IReadOnlyList<double> bordas, double valor)
    {
        for (int j = 0; j < bordas.Count - 1; j++)
        {
            if (valor < bordas[j + 1])
            {
                return j;
            }
        }

        return bordas.Count - 2;
    }

    [GeneratedRegex(@"^(R\$)?\s?-?[\d\s.,/]+$")]
    private static partial Regex SoNumero();

    [GeneratedRegex(@"^p[áa]gina\s+\d+(\s+de\s+\d+)?$", RegexOptions.IgnoreCase)]
    private static partial Regex NumeroDePagina();
}
