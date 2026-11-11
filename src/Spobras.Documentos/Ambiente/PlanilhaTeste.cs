using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace Spobras.Documentos.Ambiente;

/// <summary>
/// Planilha de teste com formulas, criada pelo proprio programa (sem abrir o Excel),
/// usada no item 4 do Teste de Ambiente.
/// </summary>
public static class PlanilhaTeste
{
    public const string NomeAba = "Teste";
    public const string CelulaEntrada = "B2";
    public const string CelulaTotal = "B5";
    public const string CelulaDobro = "B6";

    public const decimal ValorInicialEntrada = 100m;
    private const decimal Parcela2 = 250.5m;
    private const decimal Parcela3 = 49.5m;

    /// <summary>Total que a formula de <see cref="CelulaTotal"/> deve dar para um valor de entrada.</summary>
    public static decimal TotalEsperado(decimal entrada) => entrada + Parcela2 + Parcela3;

    public static void Criar(string caminho)
    {
        using var doc = SpreadsheetDocument.Create(caminho, SpreadsheetDocumentType.Workbook);
        var pasta = doc.AddWorkbookPart();
        var planilha = pasta.AddNewPart<WorksheetPart>();

        var dados = new SheetData(
            Linha(1, Texto("A1", "Item"), Texto("B1", "Valor")),
            Linha(2, Texto("A2", "Entrada"), Numero("B2", ValorInicialEntrada)),
            Linha(3, Texto("A3", "Parcela 2"), Numero("B3", Parcela2)),
            Linha(4, Texto("A4", "Parcela 3"), Numero("B4", Parcela3)),
            Linha(5, Texto("A5", "Total"), Formula("B5", "SUM(B2:B4)")),
            Linha(6, Texto("A6", "Dobro do total"), Formula("B6", "B5*2")));
        planilha.Worksheet = new Worksheet(dados);

        pasta.Workbook = new Workbook(
            new Sheets(new Sheet
            {
                Id = pasta.GetIdOfPart(planilha),
                SheetId = 1U,
                Name = NomeAba,
            }),
            // As formulas vao sem valor em cache; o Excel calcula ao abrir.
            new CalculationProperties { FullCalculationOnLoad = true });
    }

    /// <summary>Le o valor numerico guardado em uma celula (para formulas, o ultimo valor calculado).</summary>
    public static decimal? LerNumero(string caminho, string celula)
    {
        using var arquivo = new FileStream(caminho, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var doc = SpreadsheetDocument.Open(arquivo, isEditable: false);
        var pasta = doc.WorkbookPart ?? throw new InvalidDataException("Arquivo sem pasta de trabalho.");
        var aba = pasta.Workbook?.Sheets?.Elements<Sheet>().FirstOrDefault(s => s.Name == NomeAba)
                  ?? throw new InvalidDataException($"Aba \"{NomeAba}\" não encontrada.");
        var planilha = (WorksheetPart)pasta.GetPartById(aba.Id!.Value!);

        var cel = planilha.Worksheet?.Descendants<Cell>().FirstOrDefault(c => c.CellReference?.Value == celula);
        string? texto = cel?.CellValue?.Text;
        return decimal.TryParse(texto, NumberStyles.Float, CultureInfo.InvariantCulture, out var valor) ? valor : null;
    }

    /// <summary>Texto da formula de uma celula, ou null se nao houver.</summary>
    public static string? LerFormula(string caminho, string celula)
    {
        using var arquivo = new FileStream(caminho, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var doc = SpreadsheetDocument.Open(arquivo, isEditable: false);
        return doc.WorkbookPart?.WorksheetParts
            .SelectMany(p => p.Worksheet?.Descendants<Cell>() ?? [])
            .FirstOrDefault(c => c.CellReference?.Value == celula)?.CellFormula?.Text;
    }

    private static Row Linha(uint indice, params Cell[] celulas) => new(celulas) { RowIndex = indice };

    private static Cell Texto(string referencia, string texto) => new()
    {
        CellReference = referencia,
        DataType = CellValues.InlineString,
        InlineString = new InlineString(new Text(texto)),
    };

    private static Cell Numero(string referencia, decimal valor) => new()
    {
        CellReference = referencia,
        CellValue = new CellValue(valor.ToString(CultureInfo.InvariantCulture)),
    };

    private static Cell Formula(string referencia, string formula) => new()
    {
        CellReference = referencia,
        CellFormula = new CellFormula(formula),
    };
}
