using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Spobras.Documentos.Planilhas;

namespace Spobras.Documentos.Modelos;

/// <summary>Tentativa de escrever numa celula que tem formula: o modelo fica protegido.</summary>
public sealed class EscritaBloqueadaException(string mensagem) : Exception(mensagem);

/// <summary>
/// Edicao restrita de uma COPIA de planilha (Open XML, sem abrir o Excel): escreve valores em celulas de
/// entrada preservando formato, formulas, imagens e o resto do arquivo. Recusa escrever sobre formula.
/// </summary>
public sealed class EditorXlsx : IDisposable
{
    private readonly SpreadsheetDocument _doc;
    private readonly WorkbookPart _pasta;

    public EditorXlsx(string caminhoDaCopia, bool somenteLeitura = false)
    {
        try
        {
            _doc = SpreadsheetDocument.Open(caminhoDaCopia, isEditable: !somenteLeitura);
            _pasta = _doc.WorkbookPart ?? throw new PlanilhaInvalidaException("O arquivo não tem pasta de trabalho.");
        }
        catch (Exception ex) when (ex is OpenXmlPackageException or InvalidDataException or FileFormatException or IOException)
        {
            throw new PlanilhaInvalidaException($"\"{Path.GetFileName(caminhoDaCopia)}\" não pôde ser aberto como planilha do Excel: {ex.Message}");
        }
    }

    public IReadOnlyList<string> Abas => [.. _pasta.Workbook!.Sheets!.Elements<Sheet>().Select(s => s.Name!.Value!)];

    public void Dispose() => _doc.Dispose();

    /// <summary>Quantas celulas com formula a aba tem (usado para notar mudanca de estrutura).</summary>
    public int ContarFormulas(string aba) => Parte(aba).Worksheet!.Descendants<Cell>().Count(c => c.CellFormula is not null);

    /// <summary>
    /// Se a celula esta dentro de um grupo de celulas mescladas e nao e a primeira dele, devolve a primeira
    /// (so ela guarda o valor). Devolve null quando a celula pode receber o valor diretamente.
    /// </summary>
    public string? PrimeiraDaMesclagem(string aba, string celula)
    {
        var (coluna, linha) = Posicao(celula);
        foreach (var m in Parte(aba).Worksheet!.Descendants<MergeCell>())
        {
            string[] pontas = (m.Reference?.Value ?? "").Split(':');
            if (pontas.Length != 2)
            {
                continue;
            }

            var (c1, l1) = Posicao(pontas[0]);
            var (c2, l2) = Posicao(pontas[1]);
            if (coluna >= c1 && coluna <= c2 && linha >= l1 && linha <= l2)
            {
                return coluna == c1 && linha == l1 ? null : pontas[0].ToUpperInvariant();
            }
        }

        return null;
    }

    private static (int Coluna, int Linha) Posicao(string celula)
    {
        celula = celula.Replace("$", "").ToUpperInvariant();
        int corte = celula.AsSpan().IndexOfAnyInRange('0', '9');
        int coluna = 0;
        foreach (char letra in celula[..corte])
        {
            coluna = (coluna * 26) + (letra - 'A' + 1);
        }

        return (coluna, int.Parse(celula.AsSpan(corte), CultureInfo.InvariantCulture));
    }

    private WorksheetPart Parte(string aba)
    {
        var folha = _pasta.Workbook!.Sheets!.Elements<Sheet>().FirstOrDefault(s => string.Equals(s.Name?.Value, aba, StringComparison.OrdinalIgnoreCase))
                    ?? throw new PlanilhaInvalidaException($"A aba \"{aba}\" não existe no modelo.");
        return (WorksheetPart)_pasta.GetPartById(folha.Id!.Value!);
    }

    /// <summary>Texto da formula da celula, ou null se ela nao tiver formula.</summary>
    public string? Formula(string aba, string celula) => Achar(aba, celula, criar: false)?.CellFormula?.Text;

    public bool TemFormula(string aba, string celula) => Achar(aba, celula, criar: false)?.CellFormula is not null;

    /// <summary>
    /// Escreve texto, numero (decimal), data (DateOnly/DateTime) ou vazio (null). O formato da celula e mantido.
    /// </summary>
    public void Escrever(string aba, string celula, object? valor)
    {
        var c = Achar(aba, celula, criar: valor is not null);
        if (c is null)
        {
            return; // limpar uma celula que nem existe
        }

        if (c.CellFormula is not null)
        {
            throw new EscritaBloqueadaException($"A célula {celula} da aba \"{aba}\" tem fórmula no modelo. O app não escreve sobre fórmulas.");
        }

        c.InlineString = null;
        switch (valor)
        {
            case null:
                c.CellValue = null;
                c.DataType = null;
                break;
            case string texto:
                c.CellValue = null;
                c.DataType = CellValues.InlineString;
                c.InlineString = new InlineString(new Text(texto) { Space = SpaceProcessingModeValues.Preserve });
                break;
            case decimal numero:
                c.DataType = null;
                c.CellValue = new CellValue(numero.ToString(CultureInfo.InvariantCulture));
                break;
            case DateOnly dia:
                c.DataType = null;
                c.CellValue = new CellValue(dia.ToDateTime(TimeOnly.MinValue).ToOADate().ToString(CultureInfo.InvariantCulture));
                break;
            case DateTime data:
                c.DataType = null;
                c.CellValue = new CellValue(data.ToOADate().ToString(CultureInfo.InvariantCulture));
                break;
            default:
                throw new ArgumentException($"Tipo de valor não aceito: {valor.GetType().Name}");
        }
    }

    /// <summary>
    /// Troca a formula de uma celula que JA tem formula (uso restrito: ajuste conhecido do modelo, como o
    /// percentual de remuneracao). Celula sem formula e recusada.
    /// </summary>
    public void TrocarFormula(string aba, string celula, string formula)
    {
        var c = Achar(aba, celula, criar: false);
        if (c?.CellFormula is null)
        {
            throw new EscritaBloqueadaException($"A célula {celula} da aba \"{aba}\" não tem fórmula para ajustar.");
        }

        c.CellFormula = new CellFormula(formula);
        c.CellValue = null;
    }

    /// <summary>
    /// Os resultados guardados das formulas deixam de valer depois da edicao: sao removidos e o arquivo e
    /// marcado para o Excel calcular tudo ao abrir.
    /// </summary>
    public void MarcarRecalculo()
    {
        foreach (var parte in _pasta.WorksheetParts)
        {
            foreach (var c in parte.Worksheet!.Descendants<Cell>().Where(c => c.CellFormula is not null))
            {
                c.CellValue = null;
                if (c.DataType?.Value == CellValues.String || c.DataType?.Value == CellValues.Error)
                {
                    c.DataType = null;
                }
            }
        }

        var calculo = _pasta.Workbook!.CalculationProperties ?? _pasta.Workbook.AppendChild(new CalculationProperties());
        calculo.FullCalculationOnLoad = true;

        // A cadeia de calculo antiga pode nao bater mais; o Excel a refaz sozinho.
        if (_pasta.CalculationChainPart is { } cadeia)
        {
            _pasta.DeletePart(cadeia);
        }
    }

    public void Salvar()
    {
        foreach (var parte in _pasta.WorksheetParts)
        {
            parte.Worksheet!.Save();
        }

        _pasta.Workbook!.Save();
    }

    private Cell? Achar(string aba, string celula, bool criar)
    {
        var dados = Parte(aba).Worksheet!.GetFirstChild<SheetData>()!;
        uint numeroLinha = uint.Parse(celula.AsSpan(celula.AsSpan().IndexOfAnyInRange('0', '9')), CultureInfo.InvariantCulture);
        string referencia = celula.ToUpperInvariant();

        var linha = dados.Elements<Row>().FirstOrDefault(r => r.RowIndex?.Value == numeroLinha);
        if (linha is null)
        {
            if (!criar)
            {
                return null;
            }

            linha = new Row { RowIndex = numeroLinha };
            var depois = dados.Elements<Row>().FirstOrDefault(r => r.RowIndex?.Value > numeroLinha);
            if (depois is null)
            {
                dados.AppendChild(linha);
            }
            else
            {
                dados.InsertBefore(linha, depois);
            }
        }

        var existente = linha.Elements<Cell>().FirstOrDefault(c => string.Equals(c.CellReference?.Value, referencia, StringComparison.OrdinalIgnoreCase));
        if (existente is not null || !criar)
        {
            return existente;
        }

        // As celulas ficam em ordem de coluna dentro da linha.
        var nova = new Cell { CellReference = referencia };
        int coluna = Coluna(referencia);
        var proxima = linha.Elements<Cell>().FirstOrDefault(c => Coluna(c.CellReference?.Value ?? "A") > coluna);
        if (proxima is null)
        {
            linha.AppendChild(nova);
        }
        else
        {
            linha.InsertBefore(nova, proxima);
        }

        return nova;
    }

    private static int Coluna(string referencia)
    {
        int coluna = 0;
        foreach (char c in referencia)
        {
            if (!char.IsAsciiLetter(c))
            {
                break;
            }

            coluna = coluna * 26 + (char.ToUpperInvariant(c) - 'A' + 1);
        }

        return coluna;
    }
}
