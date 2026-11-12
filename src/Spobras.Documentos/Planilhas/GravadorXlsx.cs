using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace Spobras.Documentos.Planilhas;

/// <summary>Uma aba de uma planilha nova: titulos e linhas. Valores: string, decimal, int, DateOnly ou null.</summary>
public sealed record AbaNova(string Nome, IReadOnlyList<string> Titulos, IReadOnlyList<object?[]> Linhas, bool ComFiltro = true);

/// <summary>
/// Grava uma planilha nova simples (exportacoes, juntar planilhas). Numeros e datas vao como tipos do Excel,
/// titulos em negrito, filtro e primeira linha congelada. Nunca sobrescreve um arquivo existente.
/// </summary>
public static class GravadorXlsx
{
    private const uint EstiloTitulo = 1;
    private const uint EstiloData = 2;
    private const uint EstiloNumero = 3;
    private const uint EstiloInteiro = 4;

    public static void Gravar(string caminho, IReadOnlyList<AbaNova> abas)
    {
        if (File.Exists(caminho))
        {
            throw new IOException($"Já existe \"{Path.GetFileName(caminho)}\". Nada foi sobrescrito.");
        }

        string? pasta = Path.GetDirectoryName(caminho);
        if (!string.IsNullOrEmpty(pasta))
        {
            Directory.CreateDirectory(pasta);
        }

        string parcial = caminho + ".spobras-parcial";
        try
        {
            using (var doc = SpreadsheetDocument.Create(parcial, SpreadsheetDocumentType.Workbook))
            {
                var pastaTrabalho = doc.AddWorkbookPart();
                pastaTrabalho.Workbook = new Workbook();
                var estilos = pastaTrabalho.AddNewPart<WorkbookStylesPart>();
                estilos.Stylesheet = Estilos();
                var folhas = pastaTrabalho.Workbook.AppendChild(new Sheets());
                uint id = 1;
                var usados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var aba in abas)
                {
                    var parte = pastaTrabalho.AddNewPart<WorksheetPart>();
                    parte.Worksheet = Folha(aba);
                    folhas.Append(new Sheet { Id = pastaTrabalho.GetIdOfPart(parte), SheetId = id++, Name = NomeDeAba(aba.Nome, usados) });
                }
            }

            File.Move(parcial, caminho, overwrite: false);
        }
        finally
        {
            if (File.Exists(parcial))
            {
                File.Delete(parcial);
            }
        }
    }

    /// <summary>Nome valido de aba do Excel: ate 31 caracteres, sem []:*?/\ e sem repetir.</summary>
    public static string NomeDeAba(string nome, ISet<string> usados)
    {
        string limpo = new([.. nome.Select(c => "[]:*?/\\".Contains(c) ? '-' : c)]);
        limpo = limpo.Trim().Trim('\'');
        if (limpo.Length == 0)
        {
            limpo = "Planilha";
        }

        limpo = limpo.Length > 31 ? limpo[..31] : limpo;
        string tentativa = limpo;
        for (int n = 2; !usados.Add(tentativa); n++)
        {
            string sufixo = $" ({n})";
            tentativa = (limpo.Length + sufixo.Length > 31 ? limpo[..(31 - sufixo.Length)] : limpo) + sufixo;
        }

        return tentativa;
    }

    private static Worksheet Folha(AbaNova aba)
    {
        int colunas = Math.Max(aba.Titulos.Count, aba.Linhas.Count == 0 ? 0 : aba.Linhas.Max(l => l.Length));
        var larguras = new double[colunas];
        var dados = new SheetData();

        var titulo = new Row { RowIndex = 1 };
        for (int c = 0; c < aba.Titulos.Count; c++)
        {
            titulo.Append(Texto(Ref(c, 1), aba.Titulos[c], EstiloTitulo));
            larguras[c] = Math.Max(larguras[c], aba.Titulos[c].Length + 2);
        }

        dados.Append(titulo);
        uint r = 2;
        foreach (var linha in aba.Linhas)
        {
            var row = new Row { RowIndex = r };
            for (int c = 0; c < linha.Length; c++)
            {
                string referencia = Ref(c, r);
                Cell? cel = linha[c] switch
                {
                    null => null,
                    string s => Texto(referencia, s, 0),
                    decimal d => Numero(referencia, d.ToString(CultureInfo.InvariantCulture), EstiloNumero),
                    int i => Numero(referencia, i.ToString(CultureInfo.InvariantCulture), EstiloInteiro),
                    long l => Numero(referencia, l.ToString(CultureInfo.InvariantCulture), EstiloInteiro),
                    double x => Numero(referencia, x.ToString(CultureInfo.InvariantCulture), EstiloNumero),
                    DateOnly dia => Numero(referencia, dia.ToDateTime(TimeOnly.MinValue).ToOADate().ToString(CultureInfo.InvariantCulture), EstiloData),
                    DateTime data => Numero(referencia, data.ToOADate().ToString(CultureInfo.InvariantCulture), EstiloData),
                    var outro => Texto(referencia, Convert.ToString(outro, CultureInfo.CurrentCulture) ?? "", 0),
                };
                if (cel is not null)
                {
                    row.Append(cel);
                    double largura = linha[c] switch
                    {
                        string s => s.Length,
                        DateOnly or DateTime => 11,
                        decimal d => d.ToString("N2", CultureInfo.GetCultureInfo("pt-BR")).Length,
                        _ => 8,
                    };
                    larguras[c] = Math.Max(larguras[c], Math.Min(60, largura + 2));
                }
            }

            dados.Append(row);
            r++;
        }

        var folha = new Worksheet();
        if (aba.ComFiltro && aba.Titulos.Count > 0)
        {
            folha.Append(new SheetViews(new SheetView(new Pane
            {
                VerticalSplit = 1, TopLeftCell = "A2", ActivePane = PaneValues.BottomLeft, State = PaneStateValues.Frozen,
            }) { WorkbookViewId = 0 }));
        }

        if (colunas > 0)
        {
            folha.Append(new Columns(larguras.Select((l, i) => new Column
            {
                Min = (uint)(i + 1), Max = (uint)(i + 1), Width = Math.Max(8, l), CustomWidth = true,
            })));
        }

        folha.Append(dados);
        if (aba.ComFiltro && aba.Titulos.Count > 0)
        {
            folha.Append(new AutoFilter { Reference = $"A1:{Ref(aba.Titulos.Count - 1, Math.Max(1u, r - 1))}" });
        }

        return folha;
    }

    public static string Coluna(int indice)
    {
        string nome = "";
        for (int n = indice + 1; n > 0; n = (n - 1) / 26)
        {
            nome = (char)('A' + ((n - 1) % 26)) + nome;
        }

        return nome;
    }

    private static string Ref(int coluna, uint linha) => Coluna(coluna) + linha.ToString(CultureInfo.InvariantCulture);

    private static Cell Texto(string referencia, string texto, uint estilo) => new()
    {
        CellReference = referencia,
        DataType = CellValues.InlineString,
        StyleIndex = estilo == 0 ? null : estilo,
        InlineString = new InlineString(new Text(texto) { Space = SpaceProcessingModeValues.Preserve }),
    };

    private static Cell Numero(string referencia, string valor, uint estilo) => new()
    {
        CellReference = referencia,
        StyleIndex = estilo,
        CellValue = new CellValue(valor),
    };

    private static Stylesheet Estilos() => new(
        new NumberingFormats(new NumberingFormat { NumberFormatId = 164, FormatCode = "dd/mm/yyyy" }) { Count = 1 },
        new Fonts(new Font(new FontSize { Val = 11 }, new FontName { Val = "Calibri" }), new Font(new Bold(), new FontSize { Val = 11 }, new FontName { Val = "Calibri" })) { Count = 2 },
        new Fills(new Fill(new PatternFill { PatternType = PatternValues.None }), new Fill(new PatternFill { PatternType = PatternValues.Gray125 })) { Count = 2 },
        new Borders(new Border()) { Count = 1 },
        new CellFormats(
            new CellFormat(),
            new CellFormat { FontId = 1, ApplyFont = true },
            new CellFormat { NumberFormatId = 164, ApplyNumberFormat = true },
            new CellFormat { NumberFormatId = 4, ApplyNumberFormat = true },
            new CellFormat { NumberFormatId = 1, ApplyNumberFormat = true }) { Count = 5 });
}
