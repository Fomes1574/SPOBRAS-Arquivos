using System.Globalization;
using System.Security;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using Spobras.Core.Relatorio;
using Spobras.Documentos.Imagens;

namespace Spobras.Documentos.Relatorio;

public sealed record RelatorioGerado(string Caminho, int Paginas, int Fotos, long Tamanho);

/// <summary>
/// Gera o relatorio fotografico em .xlsx (Open XML, sem abrir o Excel) com o mesmo layout do PDF: as linhas da
/// planilha seguem o plano (cabecalho, quadro, legenda, descricao, espaco), os quadros e as fotos sao desenhos
/// na posicao exata, e a impressao usa "ajustar todas as colunas em uma pagina" com a quebra no fim de cada pagina.
/// </summary>
public static class GeradorRelatorio
{
    public const string NomeDaAba = "Relatório";
    private const int PrimeiraColuna = 3; // coluna C
    private const int UltimaColuna = PrimeiraColuna + 10; // coluna M
    private const long EmuPorPonto = 12700;
    private const string SufixoParcial = ".spobras-parcial";

    // Indices dos estilos (ver Estilos()).
    private const int ETopo = 1, ERotulo = 2, EValor = 3, ELegenda = 4, EDescricao = 5, ENome = 6, ECargo = 7, ECargoComLinha = 8;

    private sealed record Celula(int Estilo, string? Xml = null);

    public static RelatorioGerado Gerar(
        PlanoRelatorio plano,
        string caminhoSaida,
        string? caminhoLogo = null,
        IProgress<int>? fotosFeitas = null,
        CancellationToken cancelamento = default,
        PreparoDeFotos? preparo = null)
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
            Escrever(plano, parcial, caminhoLogo, fotosFeitas, cancelamento, preparo ?? new PreparoDeFotos());
            File.Move(parcial, caminhoSaida, overwrite: false); // o nome final so aparece com o arquivo completo
        }
        catch
        {
            File.Delete(parcial);
            throw;
        }

        return new RelatorioGerado(caminhoSaida, plano.Paginas.Count, plano.TotalDeFotos, new FileInfo(caminhoSaida).Length);
    }

    /// <summary>
    /// Altura que o Excel realmente usa: ele arredonda cada linha para pixels inteiros (0,75 pt). Arredondando para
    /// baixo aqui, a soma das linhas de uma pagina nunca passa do planejado.
    /// </summary>
    public static double AlturaNoExcel(double pontos) => Math.Max(0.75, Math.Floor(pontos / 0.75) * 0.75);

    private static void Escrever(PlanoRelatorio plano, string caminho, string? caminhoLogo, IProgress<int>? fotosFeitas, CancellationToken cancelamento, PreparoDeFotos preparo)
    {
        // As fotos primeiro, todas de uma vez em paralelo (a parte demorada); depois so montar o arquivo.
        preparo.PrepararTodas(plano, fotosFeitas, cancelamento);
        var cab = plano.Cabecalho;
        var linhas = new List<double>();                 // altura (pt) de cada linha da planilha
        var celulas = new SortedDictionary<(int Linha, int Coluna), Celula>();
        var mesclas = new List<string>();
        var quebras = new List<int>();
        var desenhos = new StringBuilder();
        int proximoId = 1;

        using var doc = SpreadsheetDocument.Create(caminho, DocumentFormat.OpenXml.SpreadsheetDocumentType.Workbook);
        var pastaDeTrabalho = doc.AddWorkbookPart();
        var planilha = pastaDeTrabalho.AddNewPart<WorksheetPart>();
        var parteDesenhos = planilha.AddNewPart<DrawingsPart>();
        var estilos = pastaDeTrabalho.AddNewPart<WorkbookStylesPart>();

        int NovaLinha(double altura)
        {
            linhas.Add(AlturaNoExcel(altura));
            return linhas.Count; // numero da linha (1..)
        }

        // Caixa mesclada com o estilo em todas as celulas (a borda precisa estar em todas).
        void Caixa(int linha1, int coluna1, int linha2, int coluna2, int estilo, string? xmlDoTexto = null)
        {
            for (int l = linha1; l <= linha2; l++)
            {
                for (int c = coluna1; c <= coluna2; c++)
                {
                    celulas[(l, c)] = new Celula(estilo);
                }
            }

            celulas[(linha1, coluna1)] = new Celula(estilo, xmlDoTexto);
            if (linha1 != linha2 || coluna1 != coluna2)
            {
                mesclas.Add($"{Ref(linha1, coluna1)}:{Ref(linha2, coluna2)}");
            }
        }

        // Posicao (pontos dentro da area util da pagina que comeca na linha "primeira") -> celula + deslocamento.
        (int Coluna, long DeslocX, int Linha, long DeslocY) Ancora(int primeira, double x, double y)
        {
            int coluna = 0;
            double restoX = x;
            while (coluna < GradeRelatorio.Colunas.Count - 1 && restoX >= GradeRelatorio.Colunas[coluna])
            {
                restoX -= GradeRelatorio.Colunas[coluna++];
            }

            int linha = primeira - 1; // indice 0.. na lista
            double restoY = y;
            while (linha < linhas.Count - 1 && restoY >= linhas[linha])
            {
                restoY -= linhas[linha++];
            }

            return (PrimeiraColuna - 1 + coluna, (long)Math.Round(restoX * EmuPorPonto), linha, (long)Math.Round(restoY * EmuPorPonto));
        }

        string De(int primeira, Area a, double yDaPagina)
        {
            var (coluna, dx, linha, dy) = Ancora(primeira, a.X - GradeRelatorio.MargemLateral, a.Y - yDaPagina);
            return string.Create(CultureInfo.InvariantCulture,
                $"<xdr:from><xdr:col>{coluna}</xdr:col><xdr:colOff>{dx}</xdr:colOff><xdr:row>{linha}</xdr:row><xdr:rowOff>{dy}</xdr:rowOff></xdr:from><xdr:ext cx=\"{(long)Math.Round(a.Largura * EmuPorPonto)}\" cy=\"{(long)Math.Round(a.Altura * EmuPorPonto)}\"/>");
        }

        void Quadro(int primeira, Area a, double yDaPagina)
        {
            int id = proximoId++;
            desenhos.Append(CultureInfo.InvariantCulture, $"""<xdr:oneCellAnchor>{De(primeira, a, yDaPagina)}<xdr:sp macro="" textlink=""><xdr:nvSpPr><xdr:cNvPr id="{id}" name="Quadro {id}"/><xdr:cNvSpPr/></xdr:nvSpPr><xdr:spPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="{(long)Math.Round(a.Largura * EmuPorPonto)}" cy="{(long)Math.Round(a.Altura * EmuPorPonto)}"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom><a:solidFill><a:srgbClr val="{DesenhoRelatorio.FundoDoQuadro}"/></a:solidFill><a:ln w="6350"><a:solidFill><a:srgbClr val="{DesenhoRelatorio.BordaDoQuadro}"/></a:solidFill></a:ln></xdr:spPr></xdr:sp><xdr:clientData/></xdr:oneCellAnchor>""");
        }

        void Figura(int primeira, Area a, double yDaPagina, string idRelacao, string nome)
        {
            int id = proximoId++;
            desenhos.Append(CultureInfo.InvariantCulture, $"""<xdr:oneCellAnchor>{De(primeira, a, yDaPagina)}<xdr:pic><xdr:nvPicPr><xdr:cNvPr id="{id}" name="{SecurityElement.Escape(nome)}"/><xdr:cNvPicPr><a:picLocks noChangeAspect="1"/></xdr:cNvPicPr></xdr:nvPicPr><xdr:blipFill><a:blip r:embed="{idRelacao}"/><a:stretch><a:fillRect/></a:stretch></xdr:blipFill><xdr:spPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="{(long)Math.Round(a.Largura * EmuPorPonto)}" cy="{(long)Math.Round(a.Altura * EmuPorPonto)}"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom></xdr:spPr></xdr:pic><xdr:clientData/></xdr:oneCellAnchor>""");
        }

        // Logo: uma parte de imagem so, usada em todas as paginas.
        string? idLogo = null;
        (int Largura, int Altura) tamanhoLogo = (0, 0);
        if (!string.IsNullOrWhiteSpace(caminhoLogo) && File.Exists(caminhoLogo))
        {
            var info = Fotos.Ler(caminhoLogo);
            tamanhoLogo = (info.Largura, info.Altura);
            bool png = Path.GetExtension(caminhoLogo).Equals(".png", StringComparison.OrdinalIgnoreCase);
            var parte = parteDesenhos.AddImagePart(png ? ImagePartType.Png : ImagePartType.Jpeg);
            using var logo = png ? File.OpenRead(caminhoLogo) : new MemoryStream(Fotos.Preparar(caminhoLogo, 1200, 92)) as Stream;
            parte.FeedData(logo);
            idLogo = parteDesenhos.GetIdOfPart(parte);
        }

        const int c = PrimeiraColuna, m = UltimaColuna;
        int C(int indice) => c + indice;

        foreach (var pagina in plano.Paginas)
        {
            cancelamento.ThrowIfCancellationRequested();
            int primeira = linhas.Count + 1;
            double yDaPagina = GradeRelatorio.MargemDeCima; // o topo da pagina na folha

            // ---- Cabecalho (completo em todas as paginas) ----
            {
                int l = NovaLinha(GradeRelatorio.AlturaDoTopo);
                Caixa(l, C(0), l, C(2), ETopo);
                string titulo = Rico(("RELATÓRIO FOTOGRÁFICO", GradeRelatorio.LetraDoTitulo, true, DesenhoRelatorio.Preto))
                                + (cab.DataPorExtenso.Length > 0 ? Rico(("\n" + cab.DataPorExtenso, GradeRelatorio.LetraDaData, false, DesenhoRelatorio.Cinza)) : "");
                Caixa(l, C(3), l, C(8), ETopo, titulo);
                Caixa(l, C(9), l, m, ETopo, Rico(("SÃO PAULO OBRAS", GradeRelatorio.LetraDoOrgao, false, DesenhoRelatorio.Preto), ("\nSPOBRAS", GradeRelatorio.LetraDoOrgaoForte, true, DesenhoRelatorio.Preto)));
                if (idLogo is not null)
                {
                    var area = new Area(GradeRelatorio.MargemLateral + 3 * GradeRelatorio.Mm, GradeRelatorio.MargemDeCima + 3 * GradeRelatorio.Mm,
                        GradeRelatorio.LarguraDoLogo - 6 * GradeRelatorio.Mm, GradeRelatorio.AlturaDoTopo - 6 * GradeRelatorio.Mm);
                    Figura(primeira, PaginadorRelatorio.Encaixar(tamanhoLogo.Largura, tamanhoLogo.Altura, area), yDaPagina, idLogo, "Logo");
                }

                foreach (var campo in cab.Campos)
                {
                    int lc = NovaLinha(campo.Altura);
                    Caixa(lc, C(0), lc, C(1), ERotulo, Texto(campo.Rotulo));
                    if (campo.Dupla)
                    {
                        Caixa(lc, C(2), lc, C(4), EValor, Texto(campo.Valor));
                        Caixa(lc, C(5), lc, C(7), ERotulo, Texto(campo.Rotulo2!));
                        Caixa(lc, C(8), lc, m, EValor, Texto(campo.Valor2!));
                    }
                    else
                    {
                        Caixa(lc, C(2), lc, m, EValor, Texto(campo.Valor));
                    }
                }
            }

            // Respiro ate as fotos: a linha vai ate o topo das fotos que o plano calculou.
            double usado = linhas.Skip(primeira - 1).Sum();
            NovaLinha(pagina.TopoDasFotos - yDaPagina - usado);

            // ---- Fotos: linha do quadro, da legenda, da descricao e o espaco ----
            var faixas = pagina.Fotos.GroupBy(f => f.Faixa).ToList();
            for (int k = 0; k < faixas.Count; k++)
            {
                var faixa = faixas[k].ToList();
                // A linha do quadro comeca exatamente onde o plano manda (o arredondamento das linhas nao acumula).
                double topo = faixa[0].Quadro.Y - yDaPagina;
                usado = linhas.Skip(primeira - 1).Sum();
                if (topo - usado > 0.75)
                {
                    NovaLinha(topo - usado);
                }

                NovaLinha(faixa[0].Quadro.Altura + GradeRelatorio.RespiroDaLegenda);
                int lLegenda = NovaLinha(faixa.Max(f => f.Legenda.Altura));
                double descricao = faixa.Max(f => f.LinhasDaDescricao.Count > 0 ? f.Descricao.Base - f.Legenda.Base : 0);
                int? lDescricao = descricao > 0 ? NovaLinha(descricao) : null;
                foreach (var f in faixa)
                {
                    cancelamento.ThrowIfCancellationRequested();
                    var (primeiraCol, ultimaCol) = GradeRelatorio.ColunasDoEspaco(f.Coluna, f.Colunas);
                    Quadro(primeira, f.Quadro, yDaPagina);
                    var parte = parteDesenhos.AddImagePart(ImagePartType.Jpeg);
                    using (var jpeg = new MemoryStream(preparo.Obter(f.Foto, plano.Opcoes.LadoMaximoFoto, plano.Opcoes.QualidadeFoto)))
                    {
                        parte.FeedData(jpeg);
                    }

                    Figura(primeira, f.Imagem, yDaPagina, parteDesenhos.GetIdOfPart(parte), PaginadorRelatorio.Rotulo(f.Numero, plano.TotalDeFotos));
                    if (f.Titulo.Length > 0)
                    {
                        Caixa(lLegenda, C(primeiraCol), lLegenda, C(ultimaCol), ELegenda, Texto(f.Titulo));
                    }

                    if (lDescricao is { } ld && f.LinhasDaDescricao.Count > 0)
                    {
                        Caixa(ld, C(primeiraCol), ld, C(ultimaCol), EDescricao, Texto(f.TextoDaDescricao));
                    }
                }
            }

            // ---- Assinaturas, no pe da pagina ----
            if (pagina.Assinaturas is { } assinaturas)
            {
                usado = linhas.Skip(primeira - 1).Sum();
                double antes = assinaturas.Espaco.Y - yDaPagina - usado;
                if (antes > 0.75)
                {
                    NovaLinha(antes);
                }

                double aNome = GradeRelatorio.LetraDoNome * 1.3, aCargo = GradeRelatorio.LetraDoCargo * 1.3;
                NovaLinha(GradeRelatorio.AlturaDasAssinaturas - aNome - aCargo - 1.4 * GradeRelatorio.Mm);
                bool nomeFiscal = plano.Opcoes.AssinaturaFiscal.Trim().Length > 0, nomeContratada = plano.Opcoes.AssinaturaContratada.Trim().Length > 0;
                int lNome = NovaLinha(aNome + 1.4 * GradeRelatorio.Mm);
                int lCargo = NovaLinha(aCargo);
                void Assinatura(int col1, int col2, bool comNome, string nome, string cargo)
                {
                    if (comNome)
                    {
                        Caixa(lNome, col1, lNome, col2, ENome, Texto(TextoMedido.Limpar(nome)));
                        Caixa(lCargo, col1, lCargo, col2, ECargo, Texto(cargo));
                    }
                    else
                    {
                        Caixa(lNome, col1, lNome, col2, ECargoComLinha, Texto(cargo));
                    }
                }

                Assinatura(C(0), C(4), nomeFiscal, plano.Opcoes.AssinaturaFiscal, "Fiscalização SPOBRAS");
                Assinatura(C(6), m, nomeContratada, plano.Opcoes.AssinaturaContratada, "Contratada");
            }

            quebras.Add(linhas.Count);
        }

        int totalDeLinhas = linhas.Count;

        // ---- Planilha ----
        var xml = new StringBuilder();
        xml.Append("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">""");
        xml.Append("""<sheetPr><pageSetUpPr fitToPage="1"/></sheetPr>""");
        xml.Append("""<sheetViews><sheetView showGridLines="0" view="pageBreakPreview" zoomScale="85" zoomScaleNormal="85" zoomScaleSheetLayoutView="85" workbookViewId="0"/></sheetViews><sheetFormatPr defaultRowHeight="15"/>""");
        xml.Append("""<cols><col min="1" max="1" width="1.71" customWidth="1"/><col min="2" max="2" width="2.71" customWidth="1"/>""");
        for (int i = 0; i < GradeRelatorio.Colunas.Count; i++)
        {
            int pixels = (int)Math.Round(GradeRelatorio.Colunas[i] / 0.75);
            xml.Append(CultureInfo.InvariantCulture, $"""<col min="{c + i}" max="{c + i}" width="{GradeRelatorio.LarguraNoExcel(pixels):0.########}" customWidth="1"/>""");
        }

        xml.Append("</cols><sheetData>");
        var porLinha = celulas.GroupBy(k => k.Key.Linha).ToDictionary(g => g.Key, g => g.ToList());
        for (int linha = 1; linha <= totalDeLinhas; linha++)
        {
            xml.Append(CultureInfo.InvariantCulture, $"""<row r="{linha}" ht="{linhas[linha - 1]:0.##}" customHeight="1">""");
            if (porLinha.TryGetValue(linha, out var daLinha))
            {
                foreach (var ((l, coluna), celula) in daLinha)
                {
                    string r = Ref(l, coluna);
                    xml.Append(celula.Xml is { } texto
                        ? string.Create(CultureInfo.InvariantCulture, $"""<c r="{r}" s="{celula.Estilo}" t="inlineStr"><is>{texto}</is></c>""")
                        : string.Create(CultureInfo.InvariantCulture, $"""<c r="{r}" s="{celula.Estilo}"/>"""));
                }
            }

            xml.Append("</row>");
        }

        xml.Append("</sheetData>");
        if (mesclas.Count > 0)
        {
            xml.Append(CultureInfo.InvariantCulture, $"""<mergeCells count="{mesclas.Count}">""");
            foreach (string mescla in mesclas)
            {
                xml.Append(CultureInfo.InvariantCulture, $"""<mergeCell ref="{mescla}"/>""");
            }

            xml.Append("</mergeCells>");
        }

        // A4; "ajustar todas as colunas em uma pagina" (a largura serve em qualquer computador, mesmo com a fonte um
        // pouco diferente) e a altura livre, com a quebra manual no fim de cada pagina. As margens deixam a altura
        // util maior que a de cada pagina do plano; o rodape fica na margem de baixo.
        double baixo = (GradeRelatorio.MargemDeBaixo + GradeRelatorio.AlturaDoRodape) / 72 + 0.02;
        xml.Append(CultureInfo.InvariantCulture, $"""<printOptions horizontalCentered="1"/><pageMargins left="{GradeRelatorio.MargemLateral / 72 - 0.08:0.###}" right="{GradeRelatorio.MargemLateral / 72 - 0.08:0.###}" top="{GradeRelatorio.MargemDeCima / 72 - 0.03:0.###}" bottom="{baixo:0.###}" header="0.2" footer="{GradeRelatorio.MargemDeBaixo / 72:0.###}"/>""");
        xml.Append("""<pageSetup paperSize="9" fitToWidth="1" fitToHeight="0" orientation="portrait"/>""");
        string rodape = cab.Rodape.Replace("&", "&&");
        xml.Append(CultureInfo.InvariantCulture, $"""<headerFooter><oddFooter>{Xml($"&L&\"Calibri,Regular\"&8&K555555{rodape}&R&\"Calibri,Regular\"&8&K555555Página &P de &N")}</oddFooter></headerFooter>""");
        if (quebras.Count > 1)
        {
            xml.Append(CultureInfo.InvariantCulture, $"""<rowBreaks count="{quebras.Count - 1}" manualBreakCount="{quebras.Count - 1}">""");
            foreach (int q in quebras.Take(quebras.Count - 1))
            {
                xml.Append(CultureInfo.InvariantCulture, $"""<brk id="{q}" max="16383" man="1"/>""");
            }

            xml.Append("</rowBreaks>");
        }

        xml.Append(CultureInfo.InvariantCulture, $"""<drawing r:id="{planilha.GetIdOfPart(parteDesenhos)}"/></worksheet>""");
        Gravar(planilha, xml.ToString());

        Gravar(parteDesenhos,
            """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><xdr:wsDr xmlns:xdr="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">"""
            + desenhos + "</xdr:wsDr>");

        Gravar(estilos, Estilos());

        Gravar(pastaDeTrabalho, string.Create(CultureInfo.InvariantCulture, $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?><workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="{NomeDaAba}" sheetId="1" r:id="{pastaDeTrabalho.GetIdOfPart(planilha)}"/></sheets><definedNames><definedName name="_xlnm.Print_Area" localSheetId="0">'{NomeDaAba}'!$C$1:$M${totalDeLinhas}</definedName></definedNames></workbook>
            """).Trim());
    }

    private static string Texto(string texto) => $"""<t xml:space="preserve">{Xml(texto)}</t>""";

    /// <summary>Texto com trechos de tamanhos e pesos diferentes na mesma celula (titulo e data).</summary>
    private static string Rico(params (string Texto, double Tamanho, bool Negrito, string Cor)[] trechos) =>
        string.Concat(trechos.Select(t => string.Create(CultureInfo.InvariantCulture,
            $"""<r><rPr>{(t.Negrito ? "<b/>" : "")}<sz val="{t.Tamanho:0.#}"/><color rgb="FF{t.Cor}"/><rFont val="Calibri"/><family val="2"/></rPr><t xml:space="preserve">{Xml(t.Texto)}</t></r>""")));

    private static string Estilos()
    {
        string Borda(string cor) => $"""<border><left style="thin"><color rgb="FF{cor}"/></left><right style="thin"><color rgb="FF{cor}"/></right><top style="thin"><color rgb="FF{cor}"/></top><bottom style="thin"><color rgb="FF{cor}"/></bottom><diagonal/></border>""";
        string Xf(int fonte, int preenchimento, int borda, string alinhamento) =>
            $"""<xf numFmtId="0" fontId="{fonte}" fillId="{preenchimento}" borderId="{borda}" xfId="0" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment {alinhamento}/></xf>""";
        string Fonte(double tamanho, bool negrito, string cor) =>
            string.Create(CultureInfo.InvariantCulture, $"""<font>{(negrito ? "<b/>" : "")}<sz val="{tamanho:0.#}"/><color rgb="FF{cor}"/><name val="Calibri"/><family val="2"/></font>""");

        var fontes = new[]
        {
            Fonte(11, false, DesenhoRelatorio.Preto),                                          // 0 padrao
            Fonte(GradeRelatorio.LetraDoRotulo, true, DesenhoRelatorio.Preto),                 // 1 rotulo
            Fonte(GradeRelatorio.LetraDoValor, false, DesenhoRelatorio.Preto),                 // 2 valor
            Fonte(GradeRelatorio.LetraDaLegenda, true, DesenhoRelatorio.Preto),                // 3 legenda
            Fonte(GradeRelatorio.LetraDaDescricao, false, DesenhoRelatorio.Descricao),         // 4 descricao
            Fonte(GradeRelatorio.LetraDoNome, true, DesenhoRelatorio.Preto),                   // 5 nome
            Fonte(GradeRelatorio.LetraDoCargo, false, DesenhoRelatorio.Cinza),                 // 6 cargo
        };

        var xfs = new[]
        {
            """<xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/>""",
            Xf(0, 0, 1, """horizontal="center" vertical="center" wrapText="1" """),              // 1 faixa de cima
            Xf(1, 0, 2, """horizontal="left" vertical="center" wrapText="1" indent="1" """),     // 2 rotulo (sem fundo)
            Xf(2, 0, 2, """horizontal="left" vertical="center" wrapText="1" indent="1" """),     // 3 valor
            Xf(3, 0, 0, """horizontal="center" vertical="top" wrapText="1" """),                 // 4 legenda
            Xf(4, 0, 0, """horizontal="justify" vertical="top" wrapText="1" """),                // 5 descricao
            Xf(5, 0, 3, """horizontal="center" vertical="top" wrapText="1" """),                 // 6 nome (linha em cima)
            Xf(6, 0, 0, """horizontal="center" vertical="top" """),                              // 7 cargo
            Xf(6, 0, 3, """horizontal="center" vertical="top" """),                              // 8 cargo sem nome (linha em cima)
        };

        return """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">"""
               + $"""<fonts count="{fontes.Length}">{string.Concat(fontes)}</fonts>"""
               + """<fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills>"""
               + $"""<borders count="4"><border><left/><right/><top/><bottom/><diagonal/></border>{Borda(DesenhoRelatorio.Borda)}{Borda(DesenhoRelatorio.BordaClara)}<border><left/><right/><top style="thin"><color rgb="FF{DesenhoRelatorio.Preto}"/></top><bottom/><diagonal/></border></borders>"""
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
