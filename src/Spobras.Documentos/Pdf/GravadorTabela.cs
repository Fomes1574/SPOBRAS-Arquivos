using System.Globalization;
using System.Security;
using System.Text;
using DocumentFormat.OpenXml.Packaging;

namespace Spobras.Documentos.Pdf;

/// <summary>
/// Grava a tabela lida do PDF numa planilha parecida com o PDF: mesmas colunas e larguras, alturas de linha,
/// mesclas, negrito, tamanho e cor da letra, fundos, grade e alinhamento. Valores em reais viram numero em
/// formato contabil, datas viram data; codigos, SEI e dotacoes ficam como texto (nao perdem zeros nem digitos).
/// So a tabela: sem colunas de pagina e linha, sem logo e sem aba extra.
/// </summary>
public static class GravadorTabela
{
    public const string NomeDaAba = "Tabela";

    private const int FmtContabil = 164, FmtNumero = 165, FmtData = 166, FmtTexto = 49;

    private sealed record Estilo(bool Negrito, double Fonte, string? Cor, string? Fundo, AlinhamentoCelula? Alinhamento, bool Borda, int Formato, bool Quebra);

    public static void Gravar(TabelaLida tabela, string caminho)
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
            Escrever(tabela, parcial);
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

    private static void Escrever(TabelaLida tabela, string caminho)
    {
        var estilos = new List<Estilo>();
        int IdDoEstilo(Estilo e)
        {
            int i = estilos.IndexOf(e);
            if (i < 0)
            {
                estilos.Add(e);
                i = estilos.Count - 1;
            }

            return i + 1; // 0 e o estilo padrao
        }

        int colunas = tabela.Larguras.Count;
        var xml = new StringBuilder();
        var mesclas = new List<string>();
        xml.Append("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">""");
        xml.Append("""<sheetPr><pageSetUpPr fitToPage="1"/></sheetPr>""");
        int? cab = tabela.LinhaDoCabecalho;
        xml.Append(cab is { } c0
            ? string.Create(CultureInfo.InvariantCulture, $"""<sheetViews><sheetView showGridLines="0" workbookViewId="0"><pane ySplit="{c0 + 1}" topLeftCell="A{c0 + 2}" activePane="bottomLeft" state="frozen"/><selection pane="bottomLeft" activeCell="A{c0 + 2}" sqref="A{c0 + 2}"/></sheetView></sheetViews>""")
            : """<sheetViews><sheetView showGridLines="0" workbookViewId="0"/></sheetViews>""");
        xml.Append("""<sheetFormatPr defaultRowHeight="15"/><cols>""");
        for (int j = 0; j < colunas; j++)
        {
            // Largura do PDF (pontos) em pixels, gravada pela conta do Excel; minimo para caber "R$ 0,00".
            int pixels = (int)Math.Round(Math.Max(tabela.Larguras[j], 12) * 96 / 72) + 6;
            xml.Append(CultureInfo.InvariantCulture, $"""<col min="{j + 1}" max="{j + 1}" width="{Math.Truncate(pixels * 256.0 / 7) / 256:0.########}" customWidth="1"/>""");
        }

        xml.Append("</cols><sheetData>");
        for (int i = 0; i < tabela.Linhas.Count; i++)
        {
            var linha = tabela.Linhas[i];
            int r = i + 1;
            bool varias = linha.Celulas.Any(c => c?.Texto.Contains('\n') == true);
            xml.Append(CultureInfo.InvariantCulture, $"""<row r="{r}" ht="{Math.Max(10, linha.Altura):0.##}" customHeight="1">""");
            for (int j = 0; j < colunas; j++)
            {
                var celula = linha.Celulas[j];
                if (celula is null)
                {
                    continue;
                }

                var (valor, formato) = Converter(celula.Texto);
                var estilo = new Estilo(celula.Negrito, celula.Fonte, celula.Cor, celula.Fundo,
                    formato is FmtContabil or FmtNumero ? (formato == FmtContabil ? null : AlinhamentoCelula.Direita) : celula.Alinhamento,
                    celula.Borda, formato, varias);
                int s = IdDoEstilo(estilo);
                string referencia = Ref(r, j + 1);
                switch (valor)
                {
                    case decimal d:
                        xml.Append(CultureInfo.InvariantCulture, $"""<c r="{referencia}" s="{s}"><v>{d}</v></c>""");
                        break;
                    case DateOnly data:
                        xml.Append(CultureInfo.InvariantCulture, $"""<c r="{referencia}" s="{s}"><v>{data.ToDateTime(TimeOnly.MinValue).ToOADate()}</v></c>""");
                        break;
                    case string { Length: > 0 } t:
                        xml.Append(CultureInfo.InvariantCulture, $"""<c r="{referencia}" s="{s}" t="inlineStr"><is><t xml:space="preserve">{Xml(t)}</t></is></c>""");
                        break;
                    default:
                        xml.Append(CultureInfo.InvariantCulture, $"""<c r="{referencia}" s="{s}"/>""");
                        break;
                }

                // Celulas cobertas pela mescla levam a mesma borda e fundo.
                for (int k = 1; k < celula.Colunas && j + k < colunas; k++)
                {
                    xml.Append(CultureInfo.InvariantCulture, $"""<c r="{Ref(r, j + k + 1)}" s="{IdDoEstilo(estilo with { Formato = FmtTexto })}"/>""");
                }

                if (celula.Colunas > 1)
                {
                    mesclas.Add($"{referencia}:{Ref(r, Math.Min(colunas, j + celula.Colunas))}");
                }
            }

            xml.Append("</row>");
        }

        xml.Append("</sheetData>");
        if (cab is { } c && tabela.Linhas.Count > c + 1)
        {
            xml.Append(CultureInfo.InvariantCulture, $"""<autoFilter ref="A{c + 1}:{Letras(colunas)}{tabela.Linhas.Count}"/>""");
        }

        if (mesclas.Count > 0)
        {
            xml.Append(CultureInfo.InvariantCulture, $"""<mergeCells count="{mesclas.Count}">""");
            foreach (string m in mesclas)
            {
                xml.Append(CultureInfo.InvariantCulture, $"""<mergeCell ref="{m}"/>""");
            }

            xml.Append("</mergeCells>");
        }

        xml.Append("""<printOptions horizontalCentered="1"/><pageMargins left="0.4" right="0.4" top="0.5" bottom="0.5" header="0.3" footer="0.3"/>""");
        xml.Append(CultureInfo.InvariantCulture, $"""<pageSetup paperSize="9" orientation="{(tabela.Paisagem ? "landscape" : "portrait")}" fitToWidth="1" fitToHeight="0"/>""");
        // Codigos (NE, SEI, codigo da escola) sao texto de proposito: sem o aviso verde de "numero como texto".
        xml.Append(CultureInfo.InvariantCulture, $"""<ignoredErrors><ignoredError sqref="A1:{Letras(Math.Max(1, colunas))}{Math.Max(1, tabela.Linhas.Count)}" numberStoredAsText="1"/></ignoredErrors>""");
        xml.Append("</worksheet>");

        using var doc = SpreadsheetDocument.Create(caminho, DocumentFormat.OpenXml.SpreadsheetDocumentType.Workbook);
        var pastaDeTrabalho = doc.AddWorkbookPart();
        var planilha = pastaDeTrabalho.AddNewPart<WorksheetPart>();
        var parteDosEstilos = pastaDeTrabalho.AddNewPart<WorkbookStylesPart>();
        Gravar(planilha, xml.ToString());
        Gravar(parteDosEstilos, Estilos(estilos));
        string filtro = cab is { } cf && tabela.Linhas.Count > cf + 1
            ? string.Create(CultureInfo.InvariantCulture, $"""<definedNames><definedName name="_xlnm._FilterDatabase" localSheetId="0" hidden="1">'{NomeDaAba}'!$A${cf + 1}:${Letras(colunas)}${tabela.Linhas.Count}</definedName></definedNames>""")
            : "";
        Gravar(pastaDeTrabalho, $"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="{NomeDaAba}" sheetId="1" r:id="{pastaDeTrabalho.GetIdOfPart(planilha)}"/></sheets>{filtro}</workbook>""");
    }

    /// <summary>Valor e formato: "R$ 1.234,56" contabil; "1.234,56" numero; "10/11/2026" data; o resto texto.</summary>
    private static (object Valor, int Formato) Converter(string texto) => TextoPdf.Valor(texto) switch
    {
        decimal d => (d, texto.TrimStart('-').StartsWith("R$", StringComparison.Ordinal) ? FmtContabil : FmtNumero),
        DateOnly data => (data, FmtData),
        var t => (t, FmtTexto),
    };

    private static string Estilos(List<Estilo> estilos)
    {
        var fontes = new List<string> { """<font><sz val="11"/><color rgb="FF000000"/><name val="Calibri"/><family val="2"/></font>""" };
        var fundos = new List<string> { """<fill><patternFill patternType="none"/></fill>""", """<fill><patternFill patternType="gray125"/></fill>""" };
        int Indice(List<string> lista, string item)
        {
            int i = lista.IndexOf(item);
            if (i < 0)
            {
                lista.Add(item);
                i = lista.Count - 1;
            }

            return i;
        }

        var xfs = new StringBuilder("""<xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/>""");
        foreach (var e in estilos)
        {
            string fonte = string.Create(CultureInfo.InvariantCulture,
                $"""<font>{(e.Negrito ? "<b/>" : "")}<sz val="{Math.Max(6, e.Fonte):0.#}"/><color rgb="FF{e.Cor ?? "000000"}"/><name val="Calibri"/><family val="2"/></font>""");
            int idFonte = Indice(fontes, fonte);
            int idFundo = e.Fundo is null ? 0 : Indice(fundos, $"""<fill><patternFill patternType="solid"><fgColor rgb="FF{e.Fundo}"/><bgColor indexed="64"/></patternFill></fill>""");
            string horizontal = e.Alinhamento switch
            {
                AlinhamentoCelula.Centro => """ horizontal="center" """,
                AlinhamentoCelula.Direita => """ horizontal="right" """,
                AlinhamentoCelula.Esquerda => """ horizontal="left" """,
                _ => " ",
            };
            xfs.Append(CultureInfo.InvariantCulture,
                $"""<xf numFmtId="{e.Formato}" fontId="{idFonte}" fillId="{idFundo}" borderId="{(e.Borda ? 1 : 0)}" xfId="0" applyNumberFormat="1" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment{horizontal}vertical="center"{(e.Quebra ? " wrapText=\"1\"" : "")}/></xf>""");
        }

        const string borda = """<border><left style="thin"><color auto="1"/></left><right style="thin"><color auto="1"/></right><top style="thin"><color auto="1"/></top><bottom style="thin"><color auto="1"/></bottom><diagonal/></border>""";
        return """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">"""
               + $"""<numFmts count="3"><numFmt numFmtId="{FmtContabil}" formatCode="_-&quot;R$&quot;\ * #,##0.00_-;\-&quot;R$&quot;\ * #,##0.00_-;_-&quot;R$&quot;\ * &quot;-&quot;??_-;_-@_-"/><numFmt numFmtId="{FmtNumero}" formatCode="#,##0.00"/><numFmt numFmtId="{FmtData}" formatCode="dd/mm/yyyy"/></numFmts>"""
               + $"""<fonts count="{fontes.Count}">{string.Concat(fontes)}</fonts>"""
               + $"""<fills count="{fundos.Count}">{string.Concat(fundos)}</fills>"""
               + $"""<borders count="2"><border><left/><right/><top/><bottom/><diagonal/></border>{borda}</borders>"""
               + """<cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>"""
               + $"""<cellXfs count="{estilos.Count + 1}">{xfs}</cellXfs>"""
               + """<cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles></styleSheet>""";
    }

    private static void Gravar(OpenXmlPart parte, string xml)
    {
        using var fluxo = parte.GetStream(FileMode.Create, FileAccess.Write);
        using var escritor = new StreamWriter(fluxo, new UTF8Encoding(false));
        escritor.Write(xml);
    }

    private static string Letras(int coluna)
    {
        string letras = "";
        for (int n = coluna; n > 0; n = (n - 1) / 26)
        {
            letras = (char)('A' + (n - 1) % 26) + letras;
        }

        return letras;
    }

    private static string Ref(int linha, int coluna) => Letras(coluna) + linha.ToString(CultureInfo.InvariantCulture);

    private static string Xml(string texto)
    {
        var limpo = new StringBuilder(texto.Length);
        foreach (char ch in texto)
        {
            if (ch == '\n' || ch == '\t' || (ch >= 0x20 && ch != 0xFFFE && ch != 0xFFFF))
            {
                limpo.Append(ch);
            }
        }

        return SecurityElement.Escape(limpo.ToString()) ?? "";
    }
}
