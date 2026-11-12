using System.Globalization;
using System.Security;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using Spobras.Core.Relatorio;
using Spobras.Documentos.Imagens;

namespace Spobras.Documentos.Relatorio;

public sealed record RelatorioGerado(string Caminho, int Paginas, int Fotos, long Tamanho);

/// <summary>
/// Gera o relatorio fotografico em .xlsx (Open XML, sem abrir o Excel) a partir do plano de paginas.
/// As posicoes vem todas do plano: a previa da tela e o arquivo usam a mesma conta.
/// </summary>
public static class GeradorRelatorio
{
    public const string NomeDaAba = "Relatório";
    private const int PrimeiraColuna = 3; // coluna C
    private const int Emu = 9525;         // 1 pixel a 96 dpi
    private const string SufixoParcial = ".spobras-parcial";

    // Indices dos estilos (ver Estilos()).
    private const int ETitulo = 1, EOrgao = 2, ERotulo = 3, EValor = 4, ERotuloEsq = 5, EValorEsq = 6, EFaixa = 7,
        ELegenda = 8, EDescricao = 9, EData = 10, EAssinatura = 11, ECaixa = 12, EContinuacao = 13, ENome = 14;

    private sealed record Celula(int Estilo, string? Texto = null, double? Numero = null);

    public static RelatorioGerado Gerar(
        PlanoRelatorio plano,
        string caminhoSaida,
        string? caminhoLogo = null,
        IProgress<int>? fotosFeitas = null,
        CancellationToken cancelamento = default)
    {
        if (!plano.PodeGerar)
        {
            throw new InvalidOperationException("O relatório não pode ser gerado: " + string.Join(" ", plano.Avisos.Select(a => a.Mensagem)));
        }

        if (File.Exists(caminhoSaida))
        {
            throw new IOException($"Já existe \"{Path.GetFileName(caminhoSaida)}\" na pasta de destino. Nada foi sobrescrito.");
        }

        string? pasta = Path.GetDirectoryName(caminhoSaida);
        if (!string.IsNullOrEmpty(pasta))
        {
            Directory.CreateDirectory(pasta);
        }

        string parcial = caminhoSaida + SufixoParcial;
        try
        {
            Escrever(plano, parcial, caminhoLogo, fotosFeitas, cancelamento);
            File.Move(parcial, caminhoSaida, overwrite: false); // o nome final so aparece com o arquivo completo
        }
        catch
        {
            File.Delete(parcial);
            throw;
        }

        return new RelatorioGerado(caminhoSaida, plano.Paginas.Count, plano.TotalDeFotos, new FileInfo(caminhoSaida).Length);
    }

    private static void Escrever(PlanoRelatorio plano, string caminho, string? caminhoLogo, IProgress<int>? fotosFeitas, CancellationToken cancelamento)
    {
        var cabecalho = plano.Cabecalho;
        int corpo = cabecalho.LinhasDoCorpo;
        var alturas = new List<int>(cabecalho.Alturas);
        int linhasFixas = alturas.Count;
        alturas.AddRange(Enumerable.Repeat((int)GradeRelatorio.AlturaDaLinha, corpo));
        int linhasPorPagina = alturas.Count;

        var celulas = new SortedDictionary<(int Linha, int Coluna), Celula>();
        var mesclas = new List<string>();
        var figuras = new StringBuilder();
        int proximaFigura = 1;

        using var doc = SpreadsheetDocument.Create(caminho, DocumentFormat.OpenXml.SpreadsheetDocumentType.Workbook);
        var pastaDeTrabalho = doc.AddWorkbookPart();
        var planilha = pastaDeTrabalho.AddNewPart<WorksheetPart>();
        var desenhos = planilha.AddNewPart<DrawingsPart>();
        var estilos = pastaDeTrabalho.AddNewPart<WorkbookStylesPart>();

        void Por(int linha, int coluna, int estilo, string? texto = null, double? numero = null) =>
            celulas[(linha, coluna)] = new Celula(estilo, texto, numero);

        // Preenche a faixa com o estilo (a borda precisa estar em todas as celulas) e mescla.
        void Caixa(int linha1, int coluna1, int linha2, int coluna2, int estilo, string? texto = null, double? numero = null)
        {
            for (int l = linha1; l <= linha2; l++)
            {
                for (int c = coluna1; c <= coluna2; c++)
                {
                    Por(l, c, estilo);
                }
            }

            Por(linha1, coluna1, estilo, texto, numero);
            if (linha1 != linha2 || coluna1 != coluna2)
            {
                mesclas.Add($"{Ref(linha1, coluna1)}:{Ref(linha2, coluna2)}");
            }
        }

        // Posicao absoluta (pixels dentro da pagina) -> celula + deslocamento.
        (int Coluna, int DeslocX, int Linha, int DeslocY) Ancora(int basePagina, double x, double y)
        {
            int coluna = 0;
            double restoX = x;
            while (coluna < GradeRelatorio.Colunas.Count - 1 && restoX >= GradeRelatorio.Colunas[coluna])
            {
                restoX -= GradeRelatorio.Colunas[coluna++];
            }

            int linha = 0;
            double restoY = y;
            while (linha < alturas.Count - 1 && restoY >= alturas[linha])
            {
                restoY -= alturas[linha++];
            }

            return (PrimeiraColuna - 1 + coluna, (int)Math.Round(restoX * Emu), basePagina - 1 + linha, (int)Math.Round(restoY * Emu));
        }

        void Figura(int basePagina, Area area, string idRelacao, string nome)
        {
            var (coluna, dx, linha, dy) = Ancora(basePagina, area.X, area.Y);
            int id = proximaFigura++;
            figuras.Append(CultureInfo.InvariantCulture, $"""
                <xdr:oneCellAnchor><xdr:from><xdr:col>{coluna}</xdr:col><xdr:colOff>{dx}</xdr:colOff><xdr:row>{linha}</xdr:row><xdr:rowOff>{dy}</xdr:rowOff></xdr:from><xdr:ext cx="{(long)Math.Round(area.Largura * Emu)}" cy="{(long)Math.Round(area.Altura * Emu)}"/><xdr:pic><xdr:nvPicPr><xdr:cNvPr id="{id}" name="{SecurityElement.Escape(nome)}"/><xdr:cNvPicPr><a:picLocks noChangeAspect="1"/></xdr:cNvPicPr></xdr:nvPicPr><xdr:blipFill><a:blip r:embed="{idRelacao}"/><a:stretch><a:fillRect/></a:stretch></xdr:blipFill><xdr:spPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="{(long)Math.Round(area.Largura * Emu)}" cy="{(long)Math.Round(area.Altura * Emu)}"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom></xdr:spPr></xdr:pic><xdr:clientData/></xdr:oneCellAnchor>
                """);
        }

        // Logo: uma parte de imagem so, usada em todas as paginas.
        string? idLogo = null;
        (int Largura, int Altura) tamanhoLogo = (0, 0);
        if (!string.IsNullOrWhiteSpace(caminhoLogo) && File.Exists(caminhoLogo))
        {
            var info = Fotos.Ler(caminhoLogo);
            tamanhoLogo = (info.Largura, info.Altura);
            bool png = Path.GetExtension(caminhoLogo).Equals(".png", StringComparison.OrdinalIgnoreCase);
            var parte = desenhos.AddImagePart(png ? ImagePartType.Png : ImagePartType.Jpeg);
            using var logo = png ? File.OpenRead(caminhoLogo) : new MemoryStream(Fotos.Preparar(caminhoLogo, 1200, 92)) as Stream;
            parte.FeedData(logo);
            idLogo = desenhos.GetIdOfPart(parte);
        }

        var dados = plano.Dados;
        int feitas = 0;
        const int c = PrimeiraColuna; // C=3 ... L=12

        foreach (var pagina in plano.Paginas)
        {
            cancelamento.ThrowIfCancellationRequested();
            int b = (pagina.Numero - 1) * linhasPorPagina + 1; // primeira linha desta pagina

            // ---- Cabecalho (repetido em cada pagina), em grade como no modelo de referencia ----
            const int t = c + GradeRelatorio.ColunaDoTitulo, k = c + GradeRelatorio.ColunaDaCaixa, v = c + GradeRelatorio.ColunaDoValor;
            Caixa(b, c, b + 3, t - 1, ECaixa);
            if (idLogo is not null)
            {
                double larguraCaixa = GradeRelatorio.XDaColuna(GradeRelatorio.ColunaDoTitulo), alturaCaixa = alturas.Take(4).Sum();
                Figura(b, PaginadorRelatorio.Encaixar(tamanhoLogo.Largura, tamanhoLogo.Altura, new Area(6, 6, larguraCaixa - 12, alturaCaixa - 12)), idLogo, "Logo");
            }

            Caixa(b, t, b + 3, k - 1, ETitulo, "RELATÓRIO FOTOGRÁFICO");
            Caixa(b, k, b, c + 9, EOrgao, "SÃO PAULO OBRAS - SPOBRAS");
            if (dados.Data is { } data)
            {
                Caixa(b + 1, k, b + 1, k, ERotulo, "DATA");
                Caixa(b + 1, c + GradeRelatorio.ColunaDoValorDaData, b + 1, c + 9, EData, null, data.ToDateTime(TimeOnly.MinValue).ToOADate());
            }
            else
            {
                Caixa(b + 1, k, b + 1, c + 9, ECaixa);
            }

            bool temContratada = !string.IsNullOrWhiteSpace(dados.Contratada);
            Caixa(b + 2, k, b + 2, c + 9, temContratada ? ERotulo : ECaixa, temContratada ? "CONTRATADA" : null);
            Caixa(b + 3, k, b + 3, c + 9, temContratada ? EValor : ECaixa, temContratada ? dados.Contratada.Trim() : null);

            for (int i = 0; i < cabecalho.Campos.Count; i++)
            {
                int linha = b + cabecalho.PrimeiroCampo + i;
                Caixa(linha, c, linha, v - 1, ERotuloEsq, cabecalho.Campos[i].Rotulo);
                Caixa(linha, v, linha, c + 9, EValorEsq, cabecalho.Campos[i].Valor);
            }

            // ---- Faixa de titulo ----
            Caixa(b + cabecalho.PrimeiroCampo + cabecalho.Campos.Count + 1, c, b + cabecalho.PrimeiroCampo + cabecalho.Campos.Count + 1, c + 9, EFaixa, "RELATÓRIO FOTOGRÁFICO");

            int corpo0 = b + linhasFixas; // linha da planilha da primeira linha do corpo

            // ---- Fotos e textos ----
            foreach (var foto in pagina.Fotos)
            {
                cancelamento.ThrowIfCancellationRequested();
                var parte = desenhos.AddImagePart(ImagePartType.Jpeg);
                using (var jpeg = new MemoryStream(Fotos.Preparar(foto.Foto.Caminho, plano.Opcoes.LadoMaximoFoto, plano.Opcoes.QualidadeFoto, foto.Foto.Rotacao)))
                {
                    parte.FeedData(jpeg);
                }

                Figura(b, foto.Imagem, desenhos.GetIdOfPart(parte), PaginadorRelatorio.Rotulo(foto.Numero, plano.TotalDeFotos));

                var (primeira, ultima) = GradeRelatorio.ColunasDoEspaco(foto.Coluna, foto.Colunas);
                int linhaTexto = corpo0 + foto.LinhaInicial + foto.LinhasDaFoto;
                int linhasTitulo = PaginadorRelatorio.LinhasDe(foto.Titulo, foto.Espaco.Largura);
                int linhasUteis = foto.LinhasDoTexto - 1; // a ultima e o respiro
                if (foto.Descricao.Length == 0)
                {
                    TextoMesclado(linhaTexto, c + primeira, linhaTexto + Math.Max(1, linhasUteis) - 1, c + ultima, ELegenda, foto.Titulo);
                }
                else
                {
                    TextoMesclado(linhaTexto, c + primeira, linhaTexto + linhasTitulo - 1, c + ultima, ELegenda, foto.Titulo);
                    TextoMesclado(linhaTexto + linhasTitulo, c + primeira, linhaTexto + linhasUteis - 1, c + ultima, EDescricao, foto.Descricao);
                }

                fotosFeitas?.Report(++feitas);
            }

            foreach (var continuacao in pagina.Continuacoes)
            {
                int linha = corpo0 + continuacao.LinhaInicial;
                TextoMesclado(linha, c, linha, c + 9, EContinuacao, continuacao.Titulo);
                TextoMesclado(linha + 1, c, linha + Math.Max(1, continuacao.Linhas - 1), c + 9, EDescricao, continuacao.Texto);
            }

            // ---- Assinaturas ----
            if (pagina.Assinaturas is { } assinaturas)
            {
                int linha = corpo0 + assinaturas.LinhaInicial + 3;
                Caixa(linha, c, linha, c + 3, EAssinatura, "Fiscalização SPOBRAS");
                Caixa(linha, c + 6, linha, c + 9, EAssinatura, "Contratada");
                if (!string.IsNullOrWhiteSpace(plano.Opcoes.AssinaturaFiscal))
                {
                    Caixa(linha + 1, c, linha + 1, c + 3, ENome, plano.Opcoes.AssinaturaFiscal.Trim());
                }

                if (!string.IsNullOrWhiteSpace(plano.Opcoes.AssinaturaContratada))
                {
                    Caixa(linha + 1, c + 6, linha + 1, c + 9, ENome, plano.Opcoes.AssinaturaContratada.Trim());
                }
            }
        }

        void TextoMesclado(int linha1, int coluna1, int linha2, int coluna2, int estilo, string texto)
        {
            Por(linha1, coluna1, estilo, texto);
            if (linha2 < linha1)
            {
                linha2 = linha1;
            }

            if (linha1 != linha2 || coluna1 != coluna2)
            {
                mesclas.Add($"{Ref(linha1, coluna1)}:{Ref(linha2, coluna2)}");
            }
        }

        int totalDeLinhas = plano.Paginas.Count * linhasPorPagina;

        // ---- Planilha ----
        var xml = new StringBuilder();
        xml.Append("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">""");
        xml.Append("""<sheetViews><sheetView showGridLines="0" view="pageBreakPreview" zoomScale="85" zoomScaleNormal="85" zoomScaleSheetLayoutView="85" workbookViewId="0"/></sheetViews><sheetFormatPr defaultRowHeight="15"/>""");
        xml.Append("""<cols><col min="1" max="1" width="1.71" customWidth="1"/><col min="2" max="2" width="2.71" customWidth="1"/>""");
        for (int i = 0; i < GradeRelatorio.Colunas.Count; i++)
        {
            // Largura gravada pela conta do proprio Excel (inclui a margem interna): a coluna fica com os pixels planejados.
            xml.Append(CultureInfo.InvariantCulture, $"""<col min="{c + i}" max="{c + i}" width="{GradeRelatorio.LarguraNoExcel(GradeRelatorio.Colunas[i]):0.########}" customWidth="1"/>""");
        }

        xml.Append("</cols><sheetData>");
        for (int linha = 1; linha <= totalDeLinhas; linha++)
        {
            double pontos = alturas[(linha - 1) % linhasPorPagina] * 0.75;
            xml.Append(CultureInfo.InvariantCulture, $"""<row r="{linha}" ht="{pontos:0.##}" customHeight="1">""");
            foreach (var ((l, coluna), celula) in celulas.Where(k => k.Key.Linha == linha))
            {
                string r = Ref(l, coluna);
                if (celula.Numero is { } n)
                {
                    xml.Append(CultureInfo.InvariantCulture, $"""<c r="{r}" s="{celula.Estilo}"><v>{n}</v></c>""");
                }
                else if (celula.Texto is { } texto)
                {
                    xml.Append(CultureInfo.InvariantCulture, $"""<c r="{r}" s="{celula.Estilo}" t="inlineStr"><is><t xml:space="preserve">{Xml(texto)}</t></is></c>""");
                }
                else
                {
                    xml.Append(CultureInfo.InvariantCulture, $"""<c r="{r}" s="{celula.Estilo}"/>""");
                }
            }

            xml.Append("</row>");
        }

        xml.Append("</sheetData>");
        if (mesclas.Count > 0)
        {
            xml.Append(CultureInfo.InvariantCulture, $"""<mergeCells count="{mesclas.Count}">""");
            foreach (string m in mesclas)
            {
                xml.Append(CultureInfo.InvariantCulture, $"""<mergeCell ref="{m}"/>""");
            }

            xml.Append("</mergeCells>");
        }

        // Escala fixa (e nao "ajustar a pagina") para o Excel respeitar as quebras de pagina manuais.
        xml.Append("""<printOptions horizontalCentered="1"/><pageMargins left="0.5" right="0.5" top="0.5" bottom="0.6" header="0.3" footer="0.3"/>""");
        xml.Append(CultureInfo.InvariantCulture, $"""<pageSetup paperSize="9" scale="{GradeRelatorio.EscalaDeImpressao}" orientation="portrait"/>""");
        xml.Append("""<headerFooter><oddFooter>&amp;C&amp;"Calibri,Regular"&amp;11Página &amp;P de &amp;N</oddFooter></headerFooter>""");
        if (plano.Paginas.Count > 1)
        {
            xml.Append(CultureInfo.InvariantCulture, $"""<rowBreaks count="{plano.Paginas.Count - 1}" manualBreakCount="{plano.Paginas.Count - 1}">""");
            for (int p = 1; p < plano.Paginas.Count; p++)
            {
                xml.Append(CultureInfo.InvariantCulture, $"""<brk id="{p * linhasPorPagina}" max="16383" man="1"/>""");
            }

            xml.Append("</rowBreaks>");
        }

        xml.Append(CultureInfo.InvariantCulture, $"""<drawing r:id="{planilha.GetIdOfPart(desenhos)}"/></worksheet>""");
        Gravar(planilha, xml.ToString());

        Gravar(desenhos,
            """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><xdr:wsDr xmlns:xdr="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">"""
            + figuras + "</xdr:wsDr>");

        Gravar(estilos, Estilos());

        Gravar(pastaDeTrabalho, string.Create(CultureInfo.InvariantCulture, $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?><workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="{NomeDaAba}" sheetId="1" r:id="{pastaDeTrabalho.GetIdOfPart(planilha)}"/></sheets><definedNames><definedName name="_xlnm.Print_Area" localSheetId="0">'{NomeDaAba}'!$C$1:$L${totalDeLinhas}</definedName></definedNames></workbook>
            """).Trim());
    }

    private static string Estilos()
    {
        const string borda = """<border><left style="thin"><color auto="1"/></left><right style="thin"><color auto="1"/></right><top style="thin"><color auto="1"/></top><bottom style="thin"><color auto="1"/></bottom><diagonal/></border>""";
        string Xf(int fonte, int preenchimento, int bordaId, string alinhamento, int formato = 0) =>
            $"""<xf numFmtId="{formato}" fontId="{fonte}" fillId="{preenchimento}" borderId="{bordaId}" xfId="0" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"{(formato != 0 ? " applyNumberFormat=\"1\"" : "")}><alignment {alinhamento}/></xf>""";

        var xfs = new[]
        {
            """<xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/>""",
            Xf(1, 0, 1, """horizontal="center" vertical="center" wrapText="1" """),            // 1 titulo
            Xf(2, 0, 1, """horizontal="center" vertical="center" wrapText="1" """),            // 2 orgao
            Xf(3, 0, 1, """horizontal="center" vertical="center" """),                         // 3 rotulo
            Xf(0, 0, 1, """horizontal="center" vertical="center" wrapText="1" """),            // 4 valor
            Xf(3, 0, 1, """horizontal="left" vertical="center" indent="1" """),                // 5 rotulo a esquerda
            Xf(0, 0, 1, """horizontal="left" vertical="center" wrapText="1" indent="1" """),   // 6 valor a esquerda
            Xf(3, 2, 1, """horizontal="center" vertical="center" """),                         // 7 faixa
            Xf(3, 0, 0, """horizontal="center" vertical="top" wrapText="1" """),               // 8 legenda
            Xf(0, 0, 0, """horizontal="left" vertical="top" wrapText="1" """),                 // 9 descricao
            Xf(3, 0, 1, """horizontal="center" vertical="center" """, 164),                    // 10 data
            Xf(0, 0, 2, """horizontal="center" vertical="top" """),                            // 11 assinatura (linha em cima)
            Xf(0, 0, 1, """vertical="center" """),                                             // 12 caixa vazia
            Xf(3, 0, 0, """horizontal="left" vertical="top" wrapText="1" """),                 // 13 titulo de continuacao
            Xf(0, 0, 0, """horizontal="center" vertical="top" wrapText="1" """),               // 14 nome sob a assinatura
        };

        return """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">"""
               + """<numFmts count="1"><numFmt numFmtId="164" formatCode="dd/mm/yyyy"/></numFmts>"""
               + """<fonts count="4"><font><sz val="11"/><color rgb="FF000000"/><name val="Calibri"/><family val="2"/></font><font><b/><sz val="18"/><color rgb="FF000000"/><name val="Calibri"/><family val="2"/></font><font><b/><sz val="12"/><color rgb="FF000000"/><name val="Calibri"/><family val="2"/></font><font><b/><sz val="11"/><color rgb="FF000000"/><name val="Calibri"/><family val="2"/></font></fonts>"""
               + """<fills count="3"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill><fill><patternFill patternType="solid"><fgColor rgb="FFD9D9D9"/><bgColor indexed="64"/></patternFill></fill></fills>"""
               + $"""<borders count="3"><border><left/><right/><top/><bottom/><diagonal/></border>{borda}<border><left/><right/><top style="thin"><color auto="1"/></top><bottom/><diagonal/></border></borders>"""
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

    private static string Ref(int linha, int coluna)
    {
        string letras = "";
        for (int n = coluna; n > 0; n = (n - 1) / 26)
        {
            letras = (char)('A' + (n - 1) % 26) + letras;
        }

        return letras + linha.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Texto seguro para XML: escapa e tira caracteres de controle que o Excel recusa.</summary>
    private static string Xml(string texto)
    {
        var limpo = new StringBuilder(texto.Length);
        foreach (char ch in texto.Replace("\r\n", "\n"))
        {
            if (ch == '\n' || ch == '\t' || (ch >= 0x20 && ch != 0xFFFE && ch != 0xFFFF))
            {
                limpo.Append(ch);
            }
        }

        return SecurityElement.Escape(limpo.ToString()) ?? "";
    }
}
