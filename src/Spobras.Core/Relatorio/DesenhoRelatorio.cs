namespace Spobras.Core.Relatorio;

public enum Alinhamento
{
    Esquerda,
    Centro,
    Justificado,
}

/// <summary>Uma peca do desenho de uma pagina. Medidas em pontos na folha A4; cores em hexadecimal (RRGGBB).</summary>
public abstract record ElementoDesenho;

/// <summary>Retangulo com fundo e/ou borda (sem cor = sem fundo / sem borda).</summary>
public sealed record RetanguloDesenho(Area Area, string? Fundo, string? Borda, double Espessura) : ElementoDesenho;

public sealed record LinhaDesenho(double X1, double Y1, double X2, double Y2, string Cor, double Espessura) : ElementoDesenho;

/// <summary>
/// Texto ja quebrado em linhas. A primeira linha comeca no alto da area (ou centralizada na altura, com
/// <paramref name="CentroNaAltura"/>); cada linha ocupa <paramref name="AlturaDaLinha"/> pontos.
/// </summary>
public sealed record TextoDesenho(
    Area Area, IReadOnlyList<string> Linhas, double Tamanho, bool Negrito, Alinhamento Alinhamento, string Cor, double AlturaDaLinha,
    bool CentroNaAltura = false) : ElementoDesenho;

/// <summary>Foto do relatorio (com o giro escolhido) ou o logo cadastrado.</summary>
public sealed record ImagemDesenho(Area Area, FotoRelatorio? Foto) : ElementoDesenho
{
    public bool Logo => Foto is null;
}

/// <summary>
/// O desenho de cada pagina, montado uma vez so a partir do plano. O PDF, a previa do computador e a do celular
/// desenham exatamente estas pecas, entao o que aparece na tela e o que sai no papel.
/// </summary>
public static class DesenhoRelatorio
{
    public const string Preto = "1D1D1F", Cinza = "444444", CinzaRodape = "555555", Borda = "1D1D1F", BordaClara = "9A9A9A";
    public const string FundoDoQuadro = "F2F2F0", BordaDoQuadro = "D2D2CE", LinhaDoRodape = "B5B5B0", Descricao = "2B2B2E";

    public static IReadOnlyList<ElementoDesenho> Pagina(PlanoRelatorio plano, PaginaRelatorio pagina, bool temLogo)
    {
        var e = new List<ElementoDesenho>();
        double x0 = GradeRelatorio.MargemLateral, y0 = GradeRelatorio.MargemDeCima, largura = GradeRelatorio.Largura;
        var cab = plano.Cabecalho;

        void Texto(Area a, string texto, double tamanho, bool negrito, Alinhamento alinhamento = Alinhamento.Centro, string cor = Preto, double entrelinha = 1.2, bool centroNaAltura = true) =>
            e.Add(new TextoDesenho(a, [texto], tamanho, negrito, alinhamento, cor, tamanho * entrelinha, centroNaAltura));

        {
            // ---- Faixa de cima (em todas as paginas): logo | titulo e data | SAO PAULO OBRAS - SPOBRAS ----
            double alto = GradeRelatorio.AlturaDoTopo, logo = GradeRelatorio.LarguraDoLogo, orgao = GradeRelatorio.LarguraDoOrgao;
            e.Add(new RetanguloDesenho(new Area(x0, y0, largura, alto), null, Borda, 1.2));
            e.Add(new LinhaDesenho(x0 + logo, y0, x0 + logo, y0 + alto, Borda, 1));
            e.Add(new LinhaDesenho(x0 + largura - orgao, y0, x0 + largura - orgao, y0 + alto, Borda, 1));
            if (temLogo)
            {
                e.Add(new ImagemDesenho(new Area(x0 + 3 * GradeRelatorio.Mm, y0 + 3 * GradeRelatorio.Mm, logo - 6 * GradeRelatorio.Mm, alto - 6 * GradeRelatorio.Mm), null));
            }

            double meio = largura - logo - orgao;
            bool comData = cab.DataPorExtenso.Length > 0;
            double altoTitulo = GradeRelatorio.LetraDoTitulo * 1.2, altoData = GradeRelatorio.LetraDaData * 1.3;
            double yTitulo = y0 + (alto - altoTitulo - (comData ? altoData + 1 * GradeRelatorio.Mm : 0)) / 2;
            Texto(new Area(x0 + logo, yTitulo, meio, altoTitulo), "RELATÓRIO FOTOGRÁFICO", GradeRelatorio.LetraDoTitulo, true);
            if (comData)
            {
                Texto(new Area(x0 + logo, yTitulo + altoTitulo + 1 * GradeRelatorio.Mm, meio, altoData), cab.DataPorExtenso, GradeRelatorio.LetraDaData, false, cor: Cinza);
            }

            double xo = x0 + largura - orgao, a1 = GradeRelatorio.LetraDoOrgao * 1.3, a2 = GradeRelatorio.LetraDoOrgaoForte * 1.3;
            double yo = y0 + (alto - a1 - a2) / 2;
            Texto(new Area(xo, yo, orgao, a1), "SÃO PAULO OBRAS", GradeRelatorio.LetraDoOrgao, false);
            Texto(new Area(xo, yo + a1, orgao, a2), "SPOBRAS", GradeRelatorio.LetraDoOrgaoForte, true);

            // ---- Campos em grade de duas colunas ----
            double y = CabecalhoMontado.TopoDosCampos;
            if (cab.Campos.Count > 0)
            {
                e.Add(new RetanguloDesenho(new Area(x0, y, largura, cab.AlturaDosCampos), null, Borda, 1.2));
            }

            for (int i = 0; i < cab.Campos.Count; i++)
            {
                var campo = cab.Campos[i];
                if (i > 0)
                {
                    e.Add(new LinhaDesenho(x0, y, x0 + largura, y, BordaClara, 0.6));
                }

                Par(e, x0, y, campo.Dupla ? largura / 2 : largura, campo.Altura, campo.LinhasDoRotulo, campo.LinhasDoValor);
                if (campo.Dupla)
                {
                    e.Add(new LinhaDesenho(x0 + largura / 2, y, x0 + largura / 2, y + campo.Altura, BordaClara, 0.6));
                    Par(e, x0 + largura / 2, y, largura / 2, campo.Altura, campo.LinhasDoRotulo2!, campo.LinhasDoValor2!);
                }

                y += campo.Altura;
            }
        }

        // ---- Fotos: quadro cinza claro, foto inteira e centralizada, legenda e descricao ----
        foreach (var f in pagina.Fotos)
        {
            e.Add(new RetanguloDesenho(f.Quadro, FundoDoQuadro, BordaDoQuadro, 0.5));
            e.Add(new ImagemDesenho(f.Imagem, f.Foto));
            if (f.LinhasDaLegenda.Count > 0)
            {
                e.Add(new TextoDesenho(f.Legenda, f.LinhasDaLegenda, GradeRelatorio.LetraDaLegenda, true, Alinhamento.Centro, Preto,
                    GradeRelatorio.LetraDaLegenda * GradeRelatorio.AlturaDaLinhaDaLegenda));
            }

            if (f.LinhasDaDescricao.Count > 0)
            {
                e.Add(new TextoDesenho(f.Descricao, f.LinhasDaDescricao, GradeRelatorio.LetraDaDescricao, false, Alinhamento.Justificado, Descricao,
                    GradeRelatorio.LetraDaDescricao * GradeRelatorio.AlturaDaLinhaDaDescricao));
            }
        }

        // ---- Assinaturas ----
        if (pagina.Assinaturas is { } assinaturas)
        {
            var a = assinaturas.Espaco;
            double recuo = GradeRelatorio.RecuoDasAssinaturas, meia = (a.Largura - 2 * recuo - GradeRelatorio.EspacoEntreAssinaturas) / 2;
            double aNome = GradeRelatorio.LetraDoNome * 1.3, aCargo = GradeRelatorio.LetraDoCargo * 1.3;
            double yLinha = a.Base - aNome - aCargo - 1.4 * GradeRelatorio.Mm;
            void Assinatura(double x, string nome, string cargo)
            {
                nome = TextoMedido.Limpar(nome);
                e.Add(new LinhaDesenho(x, yLinha, x + meia, yLinha, Preto, 0.8));
                double y = yLinha + 1.4 * GradeRelatorio.Mm;
                if (nome.Trim().Length > 0)
                {
                    e.Add(new TextoDesenho(new Area(x, y, meia, aNome), [Encurtar(nome.Trim(), meia, GradeRelatorio.LetraDoNome, true)], GradeRelatorio.LetraDoNome, true, Alinhamento.Centro, Preto, aNome));
                    y += aNome;
                }

                e.Add(new TextoDesenho(new Area(x, y, meia, aCargo), [cargo], GradeRelatorio.LetraDoCargo, false, Alinhamento.Centro, Cinza, aCargo));
            }

            Assinatura(a.X + recuo, plano.Opcoes.AssinaturaFiscal, "Fiscalização SPOBRAS");
            Assinatura(a.X + recuo + meia + GradeRelatorio.EspacoEntreAssinaturas, plano.Opcoes.AssinaturaContratada, "Contratada");
        }

        // ---- Rodape: escola e data a esquerda, "Pagina X de Y" a direita ----
        double yRodape = GradeRelatorio.FimDoConteudo + GradeRelatorio.EspacoAntesDoRodape;
        e.Add(new LinhaDesenho(x0, yRodape, x0 + largura, yRodape, LinhaDoRodape, 0.6));
        var areaRodape = new Area(x0, yRodape + GradeRelatorio.RespiroDoRodape, largura, GradeRelatorio.LetraDoRodape * 1.2);
        e.Add(new TextoDesenho(areaRodape, [cab.Rodape], GradeRelatorio.LetraDoRodape, false, Alinhamento.Esquerda, CinzaRodape, GradeRelatorio.LetraDoRodape * 1.2));
        string numero = $"Página {pagina.Numero} de {plano.Paginas.Count}";
        double lNumero = TextoMedido.Largura(numero, GradeRelatorio.LetraDoRodape, false) + 1;
        e.Add(new TextoDesenho(areaRodape with { X = x0 + largura - lNumero, Largura = lNumero }, [numero], GradeRelatorio.LetraDoRodape, false, Alinhamento.Esquerda, CinzaRodape, GradeRelatorio.LetraDoRodape * 1.2));
        return e;
    }

    private static void Par(List<ElementoDesenho> e, double x, double y, double largura, double altura, IReadOnlyList<string> rotulo, IReadOnlyList<string> valor)
    {
        double lRotulo = GradeRelatorio.LarguraDoRotulo;
        // Rotulo e valor separados por uma linha fina, sem fundo (como no cabecalho de antes).
        e.Add(new LinhaDesenho(x + lRotulo, y, x + lRotulo, y + altura, BordaClara, 0.6));
        double aRotulo = GradeRelatorio.LetraDoRotulo * GradeRelatorio.AlturaDaLinhaDoCampo, aValor = GradeRelatorio.LetraDoValor * GradeRelatorio.AlturaDaLinhaDoCampo;
        e.Add(new TextoDesenho(new Area(x + 2 * GradeRelatorio.Mm, y, lRotulo - 4 * GradeRelatorio.Mm, altura), rotulo, GradeRelatorio.LetraDoRotulo, true, Alinhamento.Esquerda, Preto, aRotulo, CentroNaAltura: true));
        e.Add(new TextoDesenho(new Area(x + lRotulo + GradeRelatorio.RecuoDoCampo, y, largura - lRotulo - 2 * GradeRelatorio.RecuoDoCampo, altura), valor, GradeRelatorio.LetraDoValor, false, Alinhamento.Esquerda, Preto, aValor, CentroNaAltura: true));
    }

    /// <summary>Corta o texto com reticencias para caber numa linha.</summary>
    public static string Encurtar(string texto, double largura, double tamanho, bool negrito)
    {
        if (TextoMedido.Largura(texto, tamanho, negrito) <= largura)
        {
            return texto;
        }

        while (texto.Length > 1 && TextoMedido.Largura(texto + "…", tamanho, negrito) > largura)
        {
            texto = texto[..^1];
        }

        return texto.TrimEnd() + "…";
    }
}
