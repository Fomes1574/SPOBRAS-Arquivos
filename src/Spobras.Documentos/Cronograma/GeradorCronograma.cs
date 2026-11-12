using System.Globalization;
using System.Security;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using Spobras.Core.Cronograma;
using Spobras.Documentos.Imagens;

namespace Spobras.Documentos.Cronograma;

/// <summary>
/// Gera o cronograma do app (sem modelo): uma linha por obra com valor, remuneracao, total, inicio, fim e o valor
/// de cada mes; linha de TOTAL; total de cada ano; total solicitado; e a tabela por dotacao completa.
/// Os valores ja vem calculados; as somas vao como formulas (com o resultado gravado) para continuarem certas
/// se alguem mexer num mes no Excel.
/// </summary>
public static class GeradorCronograma
{
    public const string NomeDaAba = "Cronograma";

    private const int LinhaDosTitulos = 5;
    private const int ColunaDoPrimeiroMes = 11; // K

    // Indices dos estilos (ver Estilos()).
    private const int ETitulo = 1, ECabecalho = 2, ECabecalhoMes = 3, ETexto = 4, EDinheiro = 5, EMes = 6, ETotalRotulo = 7,
        ETotalDinheiro = 8, ESubtitulo = 9, ECentro = 10, EAno = 11, ESecao = 12;

    private sealed record Celula(int Estilo, string? Texto = null, decimal? Numero = null, string? Formula = null, double? Data = null);

    public static void Gerar(CronogramaCalculado c, string caminho, string titulo, string? logo = null)
    {
        if (!c.PodeGerar)
        {
            throw new InvalidOperationException("O cronograma tem pendências e não pode ser gerado.");
        }

        if (File.Exists(caminho))
        {
            throw new IOException($"Já existe \"{Path.GetFileName(caminho)}\" na pasta de destino. Nada foi sobrescrito.");
        }

        string? pasta = Path.GetDirectoryName(caminho);
        if (!string.IsNullOrEmpty(pasta))
        {
            Directory.CreateDirectory(pasta);
        }

        string parcial = caminho + ".spobras-parcial";
        try
        {
            Escrever(c, parcial, titulo, logo);
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

    /// <summary>"Cronograma SPOBRAS - Portaria Nº 06 (2022) / Lote 239 - 5 unidades" com o que for comum a todas as obras.</summary>
    public static string TituloSugerido(IReadOnlyList<ObraCronograma> obras)
    {
        var validas = obras.Where(o => !o.Vazia).ToList();
        var partes = new List<string>();
        string? Comum(Func<ObraCronograma, string> campo) =>
            validas.Select(o => campo(o).Trim()).Distinct().ToList() is [var unico] && unico.Length > 0 ? unico : null;
        if (Comum(o => o.Portaria) is { } portaria)
        {
            partes.Add("Portaria " + portaria);
        }

        if (Comum(o => o.Lote) is { } lote)
        {
            partes.Add("Lote " + lote);
        }

        string unidades = validas.Count == 1 ? validas[0].Nome.Trim() : $"{validas.Count} unidades";
        return "Cronograma SPOBRAS" + (partes.Count > 0 ? " - " + string.Join(" / ", partes) : "") + (unidades.Length > 0 ? " - " + unidades : "");
    }

    private static void Escrever(CronogramaCalculado c, string caminho, string titulo, string? logo)
    {
        var celulas = new SortedDictionary<(int Linha, int Coluna), Celula>();
        void Por(int linha, int coluna, Celula celula) => celulas[(linha, coluna)] = celula;

        var meses = c.Meses;
        var anos = c.Anos;
        int colAno0 = ColunaDoPrimeiroMes + meses.Count;
        int colSolicitado = colAno0 + anos.Count;
        int primeira = LinhaDosTitulos + 1, ultima = LinhaDosTitulos + c.Obras.Count, linhaTotal = ultima + 1;
        string pct = (c.Percentual * 100).ToString("0.##", CultureInfo.GetCultureInfo("pt-BR"));

        // ---- Topo ----
        Por(2, 4, new Celula(ETitulo, titulo));
        Por(3, 4, new Celula(ESubtitulo, $"Remuneração de {pct}% sobre o valor da obra. Valores mensais arredondados para cima no centavo; a sobra fica no último mês de cada obra."));

        // ---- Titulos ----
        string[] fixos = ["Portaria", "Lote", "Dotação", "Nome da Obra", "Breve escopo", "Valores para Obras", $"Valores de Remuneração ({pct}%)", "Valores Total", "Início", "Fim"];
        for (int i = 0; i < fixos.Length; i++)
        {
            Por(LinhaDosTitulos, i + 1, new Celula(ECabecalho, fixos[i]));
        }

        for (int m = 0; m < meses.Count; m++)
        {
            Por(LinhaDosTitulos, ColunaDoPrimeiroMes + m, new Celula(ECabecalhoMes, Data: Serial(meses[m])));
        }

        for (int a = 0; a < anos.Count; a++)
        {
            Por(LinhaDosTitulos, colAno0 + a, new Celula(ECabecalho, $"Total {anos[a]}"));
        }

        Por(LinhaDosTitulos, colSolicitado, new Celula(ECabecalho, "Total solicitado"));

        // ---- Obras ----
        for (int k = 0; k < c.Obras.Count; k++)
        {
            var o = c.Obras[k];
            int l = primeira + k;
            string resumida = o.Dotacoes is { } d ? CalculoCronograma.Resumida(d.Obra) ?? o.Obra.Dotacao : o.Obra.Dotacao;
            Por(l, 1, new Celula(ECentro, o.Obra.Portaria));
            Por(l, 2, LoteOuNumero(o.Obra.Lote));
            Por(l, 3, LoteOuNumero(resumida));
            Por(l, 4, new Celula(ETexto, o.Obra.Nome));
            Por(l, 5, new Celula(ETexto, o.Obra.Escopo));
            Por(l, 6, new Celula(EDinheiro, Numero: o.ValorObra));
            Por(l, 7, new Celula(EDinheiro, Numero: o.Remuneracao));
            Por(l, 8, new Celula(EDinheiro, Numero: o.Total, Formula: $"F{l}+G{l}"));
            Por(l, 9, new Celula(EMes, Data: Serial(o.Obra.Inicio!.Value)));
            Por(l, 10, new Celula(EMes, Data: Serial(o.Obra.Fim!.Value)));
            for (int m = 0; m < meses.Count; m++)
            {
                Por(l, ColunaDoPrimeiroMes + m, o.Meses.TryGetValue(meses[m], out decimal v) ? new Celula(EDinheiro, Numero: v) : new Celula(EDinheiro));
            }

            for (int a = 0; a < anos.Count; a++)
            {
                var (de, ate) = ColunasDoAno(meses, anos[a]);
                Por(l, colAno0 + a, new Celula(EAno, Numero: o.DoAno(anos[a]), Formula: $"SUM({Ref(l, de)}:{Ref(l, ate)})"));
            }

            Por(l, colSolicitado, new Celula(EAno, Numero: o.Total,
                Formula: $"SUM({Ref(l, ColunaDoPrimeiroMes)}:{Ref(l, ColunaDoPrimeiroMes + meses.Count - 1)})"));
        }

        // ---- TOTAL ----
        Por(linhaTotal, 1, new Celula(ETotalRotulo, "TOTAL"));
        for (int col = 2; col <= 5; col++)
        {
            Por(linhaTotal, col, new Celula(ETotalRotulo));
        }

        void Soma(int coluna, decimal valor) =>
            Por(linhaTotal, coluna, new Celula(ETotalDinheiro, Numero: valor, Formula: $"SUM({Ref(primeira, coluna)}:{Ref(ultima, coluna)})"));
        Soma(6, c.TotalObra);
        Soma(7, c.TotalRemuneracao);
        Soma(8, c.Total);
        Por(linhaTotal, 9, new Celula(ETotalRotulo));
        Por(linhaTotal, 10, new Celula(ETotalRotulo));
        for (int m = 0; m < meses.Count; m++)
        {
            Soma(ColunaDoPrimeiroMes + m, c.DoMes(meses[m]));
        }

        for (int a = 0; a < anos.Count; a++)
        {
            Soma(colAno0 + a, c.DoAno(anos[a]));
        }

        Soma(colSolicitado, c.Total);

        // ---- Tabela por dotacao completa ----
        int linha = linhaTotal + 3;
        Por(linha - 1, 4, new Celula(ESecao, "Resumo por dotação"));
        Por(linha, 4, new Celula(ECabecalho, "Tipo"));
        Por(linha, 5, new Celula(ECabecalho, "Dotação Completa"));
        Por(linha, 6, new Celula(ECabecalho, "Valores"));
        int primeiraDot = linha + 1;
        foreach (var d in c.Dotacoes)
        {
            linha++;
            string coluna = d.DaRemuneracao ? "G" : "F";
            Por(linha, 4, new Celula(ETexto, d.Tipo));
            Por(linha, 5, new Celula(ETexto, d.Dotacao));
            Por(linha, 6, new Celula(EDinheiro, Numero: d.Valor, Formula: string.Join("+", d.Obras.Select(i => $"{coluna}{primeira + i}"))));
        }

        linha++;
        Por(linha, 4, new Celula(ETotalRotulo));
        Por(linha, 5, new Celula(ETotalRotulo, "TOTAL"));
        Por(linha, 6, new Celula(ETotalDinheiro, Numero: c.Dotacoes.Sum(d => d.Valor),
            Formula: c.Dotacoes.Count == 0 ? null : $"SUM(F{primeiraDot}:F{linha - 1})"));
        int ultimaLinha = linha;

        // ---- Arquivo ----
        using var doc = SpreadsheetDocument.Create(caminho, DocumentFormat.OpenXml.SpreadsheetDocumentType.Workbook);
        var pastaDeTrabalho = doc.AddWorkbookPart();
        var planilha = pastaDeTrabalho.AddNewPart<WorksheetPart>();
        var estilos = pastaDeTrabalho.AddNewPart<WorkbookStylesPart>();
        string? desenho = null;
        if (!string.IsNullOrWhiteSpace(logo) && File.Exists(logo))
        {
            var desenhos = planilha.AddNewPart<DrawingsPart>();
            Logo(desenhos, logo);
            desenho = planilha.GetIdOfPart(desenhos);
        }

        var xml = new StringBuilder();
        xml.Append("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">""");
        xml.Append("""<sheetPr><pageSetUpPr fitToPage="1"/></sheetPr>""");
        xml.Append("""<sheetViews><sheetView showGridLines="0" zoomScale="85" zoomScaleNormal="85" workbookViewId="0"><pane xSplit="10" ySplit="5" topLeftCell="K6" activePane="bottomRight" state="frozen"/><selection pane="bottomRight" activeCell="K6" sqref="K6"/></sheetView></sheetViews>""");
        xml.Append("""<sheetFormatPr defaultRowHeight="15"/><cols>""");
        double[] larguras = [13, 9, 10, 40, 37, 17, 17, 17, 9, 9];
        for (int i = 0; i < larguras.Length; i++)
        {
            xml.Append(CultureInfo.InvariantCulture, $"""<col min="{i + 1}" max="{i + 1}" width="{larguras[i]}" customWidth="1"/>""");
        }

        xml.Append(CultureInfo.InvariantCulture, $"""<col min="{ColunaDoPrimeiroMes}" max="{colSolicitado}" width="15" customWidth="1"/></cols><sheetData>""");
        foreach (var grupo in celulas.GroupBy(k => k.Key.Linha))
        {
            int l = grupo.Key;
            string altura = l is 1 or 2 or 3 ? """ ht="22" customHeight="1" """ : l == LinhaDosTitulos ? """ ht="32" customHeight="1" """ : " ";
            xml.Append(CultureInfo.InvariantCulture, $"""<row r="{l}"{altura.TrimEnd()}>""");
            foreach (var ((_, coluna), celula) in grupo)
            {
                string r = Ref(l, coluna);
                if (celula.Formula is { } f)
                {
                    xml.Append(CultureInfo.InvariantCulture, $"""<c r="{r}" s="{celula.Estilo}"><f>{SecurityElement.Escape(f)}</f><v>{celula.Numero ?? 0}</v></c>""");
                }
                else if (celula.Numero is { } n)
                {
                    xml.Append(CultureInfo.InvariantCulture, $"""<c r="{r}" s="{celula.Estilo}"><v>{n}</v></c>""");
                }
                else if (celula.Data is { } d)
                {
                    xml.Append(CultureInfo.InvariantCulture, $"""<c r="{r}" s="{celula.Estilo}"><v>{d}</v></c>""");
                }
                else if (celula.Texto is { Length: > 0 } t)
                {
                    xml.Append(CultureInfo.InvariantCulture, $"""<c r="{r}" s="{celula.Estilo}" t="inlineStr"><is><t xml:space="preserve">{SecurityElement.Escape(t)}</t></is></c>""");
                }
                else
                {
                    xml.Append(CultureInfo.InvariantCulture, $"""<c r="{r}" s="{celula.Estilo}"/>""");
                }
            }

            xml.Append("</row>");
        }

        xml.Append("</sheetData>");
        xml.Append("""<printOptions horizontalCentered="1"/><pageMargins left="0.4" right="0.4" top="0.5" bottom="0.6" header="0.3" footer="0.3"/>""");
        xml.Append("""<pageSetup paperSize="9" orientation="landscape" fitToWidth="1" fitToHeight="0"/>""");
        xml.Append("""<headerFooter><oddFooter>&amp;C&amp;"Calibri,Regular"&amp;10Página &amp;P de &amp;N</oddFooter></headerFooter>""");
        if (desenho is not null)
        {
            xml.Append(CultureInfo.InvariantCulture, $"""<drawing r:id="{desenho}"/>""");
        }

        xml.Append("</worksheet>");
        Gravar(planilha, xml.ToString());
        Gravar(estilos, Estilos());
        Gravar(pastaDeTrabalho, string.Create(CultureInfo.InvariantCulture, $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?><workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><bookViews><workbookView/></bookViews><sheets><sheet name="{NomeDaAba}" sheetId="1" r:id="{pastaDeTrabalho.GetIdOfPart(planilha)}"/></sheets><definedNames><definedName name="_xlnm.Print_Area" localSheetId="0">'{NomeDaAba}'!$A$1:${Letras(colSolicitado)}${ultimaLinha}</definedName><definedName name="_xlnm.Print_Titles" localSheetId="0">'{NomeDaAba}'!${LinhaDosTitulos}:${LinhaDosTitulos}</definedName></definedNames><calcPr calcId="191029" fullCalcOnLoad="1"/></workbook>
            """).Trim());
    }

    /// <summary>Lote e dotacao: numero quando for so numero (o Excel nao reclama de "numero como texto").</summary>
    private static Celula LoteOuNumero(string texto) =>
        decimal.TryParse(texto.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out decimal n) && texto.Trim().Length < 10
            ? new Celula(ECentro, Numero: n)
            : new Celula(ECentro, texto.Trim());

    private static (int De, int Ate) ColunasDoAno(IReadOnlyList<DateOnly> meses, int ano)
    {
        int de = meses.ToList().FindIndex(m => m.Year == ano), ate = meses.ToList().FindLastIndex(m => m.Year == ano);
        return (ColunaDoPrimeiroMes + de, ColunaDoPrimeiroMes + ate);
    }

    private static double Serial(DateOnly data) => data.ToDateTime(TimeOnly.MinValue).ToOADate();

    private static void Logo(DrawingsPart desenhos, string logo)
    {
        const int Emu = 9525;
        var info = Fotos.Ler(logo);
        double altura = 3 * 22 * 96 / 72.0 - 8;
        double largura = Math.Min(200, altura * info.Largura / Math.Max(1, info.Altura));
        altura = largura * info.Altura / Math.Max(1, info.Largura);
        bool png = Path.GetExtension(logo).Equals(".png", StringComparison.OrdinalIgnoreCase);
        var imagem = desenhos.AddImagePart(png ? ImagePartType.Png : ImagePartType.Jpeg);
        using (var dados = png ? File.OpenRead(logo) : new MemoryStream(Fotos.Preparar(logo, 800, 92)) as Stream)
        {
            imagem.FeedData(dados);
        }

        Gravar(desenhos, string.Create(CultureInfo.InvariantCulture, $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?><xdr:wsDr xmlns:xdr="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><xdr:oneCellAnchor><xdr:from><xdr:col>0</xdr:col><xdr:colOff>{4 * Emu}</xdr:colOff><xdr:row>0</xdr:row><xdr:rowOff>{4 * Emu}</xdr:rowOff></xdr:from><xdr:ext cx="{(long)(largura * Emu)}" cy="{(long)(altura * Emu)}"/><xdr:pic><xdr:nvPicPr><xdr:cNvPr id="1" name="Logo"/><xdr:cNvPicPr><a:picLocks noChangeAspect="1"/></xdr:cNvPicPr></xdr:nvPicPr><xdr:blipFill><a:blip r:embed="{desenhos.GetIdOfPart(imagem)}"/><a:stretch><a:fillRect/></a:stretch></xdr:blipFill><xdr:spPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="{(long)(largura * Emu)}" cy="{(long)(altura * Emu)}"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom></xdr:spPr></xdr:pic><xdr:clientData/></xdr:oneCellAnchor></xdr:wsDr>
            """).Trim());
    }

    private static string Estilos()
    {
        const string borda = """<border><left style="thin"><color rgb="FF808080"/></left><right style="thin"><color rgb="FF808080"/></right><top style="thin"><color rgb="FF808080"/></top><bottom style="thin"><color rgb="FF808080"/></bottom><diagonal/></border>""";
        string Xf(int fonte, int preenchimento, int bordaId, string alinhamento, int formato = 0) =>
            $"""<xf numFmtId="{formato}" fontId="{fonte}" fillId="{preenchimento}" borderId="{bordaId}" xfId="0" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"{(formato != 0 ? " applyNumberFormat=\"1\"" : "")}><alignment {alinhamento}/></xf>""";

        string[] xfs =
        [
            """<xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/>""",
            Xf(1, 0, 0, """vertical="center" """),                                          // 1 titulo
            Xf(2, 2, 1, """horizontal="center" vertical="center" wrapText="1" """),         // 2 cabecalho
            Xf(2, 2, 1, """horizontal="center" vertical="center" """, 165),                 // 3 cabecalho de mes
            Xf(0, 0, 1, """vertical="center" wrapText="1" """),                             // 4 texto
            Xf(0, 0, 1, """vertical="center" """, 4),                                       // 5 dinheiro
            Xf(0, 0, 1, """horizontal="center" vertical="center" """, 165),                 // 6 mes
            Xf(2, 3, 1, """vertical="center" """),                                          // 7 rotulo do total
            Xf(2, 3, 1, """vertical="center" """, 4),                                       // 8 dinheiro do total
            Xf(3, 0, 0, """vertical="center" """),                                          // 9 subtitulo
            Xf(0, 0, 1, """horizontal="center" vertical="center" wrapText="1" """),         // 10 texto centralizado
            Xf(0, 3, 1, """vertical="center" """, 4),                                       // 11 dinheiro do ano
            Xf(2, 0, 0, """vertical="center" """),                                          // 12 titulo de secao
        ];

        return """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">"""
               + """<numFmts count="1"><numFmt numFmtId="165" formatCode="[$-416]mmm/yy"/></numFmts>"""
               + """<fonts count="4"><font><sz val="11"/><color rgb="FF000000"/><name val="Calibri"/><family val="2"/></font><font><b/><sz val="15"/><color rgb="FF000000"/><name val="Calibri"/><family val="2"/></font><font><b/><sz val="11"/><color rgb="FF000000"/><name val="Calibri"/><family val="2"/></font><font><sz val="10"/><color rgb="FF595959"/><name val="Calibri"/><family val="2"/></font></fonts>"""
               + """<fills count="4"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill><fill><patternFill patternType="solid"><fgColor rgb="FFD9D9D9"/><bgColor indexed="64"/></patternFill></fill><fill><patternFill patternType="solid"><fgColor rgb="FFF2F2F2"/><bgColor indexed="64"/></patternFill></fill></fills>"""
               + $"""<borders count="2"><border><left/><right/><top/><bottom/><diagonal/></border>{borda}</borders>"""
               + """<cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>"""
               + $"""<cellXfs count="{xfs.Length}">{string.Concat(xfs)}</cellXfs>"""
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
}
