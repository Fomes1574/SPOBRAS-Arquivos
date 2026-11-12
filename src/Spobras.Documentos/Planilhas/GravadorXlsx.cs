using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Drawing = DocumentFormat.OpenXml.Spreadsheet.Drawing;

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

    /// <param name="logo">Imagem do logo (cadastrada no app): vai no topo da primeira aba, com o titulo ao lado.</param>
    /// <param name="titulo">Titulo ao lado do logo; sem ele, o nome do arquivo.</param>
    public static void Gravar(string caminho, IReadOnlyList<AbaNova> abas, string? logo = null, string? titulo = null)
    {
        bool comLogo = !string.IsNullOrWhiteSpace(logo) && File.Exists(logo);
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
                bool primeira = true;
                foreach (var aba in abas)
                {
                    var parte = pastaTrabalho.AddNewPart<WorksheetPart>();
                    bool topo = primeira && comLogo;
                    parte.Worksheet = Folha(aba, topo ? LinhasDoTopo : 0);
                    if (topo)
                    {
                        Topo(parte, logo!, titulo ?? Path.GetFileNameWithoutExtension(caminho));
                    }

                    primeira = false;
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

    /// <summary>Linhas reservadas no topo para o logo e o titulo.</summary>
    private const int LinhasDoTopo = 3;

    private static Worksheet Folha(AbaNova aba, int topo = 0)
    {
        int colunas = Math.Max(aba.Titulos.Count, aba.Linhas.Count == 0 ? 0 : aba.Linhas.Max(l => l.Length));
        var larguras = new double[colunas];
        var dados = new SheetData();
        for (uint t = 1; t <= topo; t++)
        {
            dados.Append(new Row { RowIndex = t, Height = 22, CustomHeight = true });
        }

        uint linhaTitulos = (uint)topo + 1;
        var titulo = new Row { RowIndex = linhaTitulos };
        for (int c = 0; c < aba.Titulos.Count; c++)
        {
            titulo.Append(Texto(Ref(c, linhaTitulos), aba.Titulos[c], EstiloTitulo));
            larguras[c] = Math.Max(larguras[c], aba.Titulos[c].Length + 2);
        }

        dados.Append(titulo);
        uint r = linhaTitulos + 1;
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
                VerticalSplit = linhaTitulos, TopLeftCell = $"A{linhaTitulos + 1}", ActivePane = PaneValues.BottomLeft, State = PaneStateValues.Frozen,
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
            folha.Append(new AutoFilter { Reference = $"A{linhaTitulos}:{Ref(aba.Titulos.Count - 1, Math.Max(linhaTitulos, r - 1))}" });
        }

        return folha;
    }

    /// <summary>Logo no canto (ate 3 linhas de altura) e o titulo em negrito ao lado.</summary>
    private static void Topo(WorksheetPart parte, string logo, string titulo)
    {
        const int Emu = 9525;
        var info = Spobras.Documentos.Imagens.Fotos.Ler(logo);
        double altura = 3 * 22 * 96 / 72.0 - 8; // pixels das 3 linhas do topo, com folga
        double largura = Math.Min(240, altura * info.Largura / Math.Max(1, info.Altura));
        altura = largura * info.Altura / Math.Max(1, info.Largura);

        var desenhos = parte.AddNewPart<DrawingsPart>();
        bool png = Path.GetExtension(logo).Equals(".png", StringComparison.OrdinalIgnoreCase);
        var imagem = desenhos.AddImagePart(png ? ImagePartType.Png : ImagePartType.Jpeg);
        using (var dados = png ? File.OpenRead(logo) : new MemoryStream(Spobras.Documentos.Imagens.Fotos.Preparar(logo, 800, 92)) as Stream)
        {
            imagem.FeedData(dados);
        }

        string xml = string.Create(CultureInfo.InvariantCulture, $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?><xdr:wsDr xmlns:xdr="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><xdr:oneCellAnchor><xdr:from><xdr:col>0</xdr:col><xdr:colOff>{4 * Emu}</xdr:colOff><xdr:row>0</xdr:row><xdr:rowOff>{4 * Emu}</xdr:rowOff></xdr:from><xdr:ext cx="{(long)(largura * Emu)}" cy="{(long)(altura * Emu)}"/><xdr:pic><xdr:nvPicPr><xdr:cNvPr id="1" name="Logo"/><xdr:cNvPicPr><a:picLocks noChangeAspect="1"/></xdr:cNvPicPr></xdr:nvPicPr><xdr:blipFill><a:blip r:embed="{desenhos.GetIdOfPart(imagem)}"/><a:stretch><a:fillRect/></a:stretch></xdr:blipFill><xdr:spPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="{(long)(largura * Emu)}" cy="{(long)(altura * Emu)}"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom></xdr:spPr></xdr:pic><xdr:clientData/></xdr:oneCellAnchor></xdr:wsDr>
            """).Trim();
        using (var fluxo = desenhos.GetStream(FileMode.Create))
        using (var escritor = new StreamWriter(fluxo, new System.Text.UTF8Encoding(false)))
        {
            escritor.Write(xml);
        }

        // Titulo na primeira coluna depois do logo (colunas com cerca de 7 px por caractere + 5 de margem).
        var colunas = parte.Worksheet!.GetFirstChild<Columns>()?.Elements<Column>().ToList() ?? [];
        double x = 0;
        int coluna = 0;
        while (x < largura + 8)
        {
            double w = colunas.FirstOrDefault(c => c.Min!.Value == coluna + 1)?.Width?.Value ?? 8.43;
            x += Math.Truncate(w * 7 + 5);
            coluna++;
        }

        var segunda = parte.Worksheet.GetFirstChild<SheetData>()!.Elements<Row>().First(r => r.RowIndex!.Value == 2);
        segunda.Append(Texto(Ref(coluna, 2), titulo, EstiloTitulo));
        parte.Worksheet.Append(new Drawing { Id = parte.GetIdOfPart(desenhos) });
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
