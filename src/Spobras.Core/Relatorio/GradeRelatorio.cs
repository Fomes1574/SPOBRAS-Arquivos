namespace Spobras.Core.Relatorio;

/// <summary>
/// Medidas da pagina do relatorio fotografico, em pontos (1/72"), na folha A4 em pe. Sao as do modelo aprovado
/// em 17/11/2026: margens de 12 mm dos lados, 11 mm em cima e 9 mm embaixo; cabecalho em faixa; fotos em quadros
/// iguais; legenda colada no quadro; rodape com a pagina. O PDF, a previa e o Excel usam as mesmas medidas.
/// </summary>
public static class GradeRelatorio
{
    public const double Mm = 72 / 25.4;

    public const double LarguraDaFolha = 595.28, AlturaDaFolha = 841.89;
    public const double MargemLateral = 12 * Mm, MargemDeCima = 11 * Mm, MargemDeBaixo = 9 * Mm;

    /// <summary>Largura util (entre as margens).</summary>
    public const double Largura = LarguraDaFolha - 2 * MargemLateral;

    /// <summary>Altura da faixa de cima do cabecalho (logo, titulo e SPOBRAS), repetido em todas as paginas.</summary>
    public const double AlturaDoTopo = 24 * Mm;

    /// <summary>Larguras da faixa de cima: logo e SPOBRAS de 44 mm.</summary>
    public const double LarguraDoLogo = 44 * Mm, LarguraDoOrgao = 44 * Mm;

    /// <summary>Largura do rotulo de cada campo do cabecalho (33 mm).</summary>
    public const double LarguraDoRotulo = 33 * Mm;

    /// <summary>Menor altura de uma linha de campo do cabecalho e o respiro do texto dentro dela.</summary>
    public const double AlturaMinimaDoCampo = 6.2 * Mm, RespiroDoCampo = 1.4 * Mm, RecuoDoCampo = 2.2 * Mm;

    /// <summary>Espaco entre o cabecalho e as fotos; entre as colunas e entre as linhas de fotos.</summary>
    public const double RespiroAntesDasFotos = 5 * Mm, EspacoEntreColunas = 6 * Mm, EspacoEntreLinhas = 4 * Mm;

    /// <summary>Do quadro ate a legenda, e da legenda ate a descricao.</summary>
    public const double RespiroDaLegenda = 1.6 * Mm, RespiroDaDescricao = 1 * Mm;

    /// <summary>Rodape: linha fina, a 2 mm do conteudo, texto 1,6 mm abaixo dela.</summary>
    public const double EspacoAntesDoRodape = 2 * Mm, RespiroDoRodape = 1.6 * Mm;

    /// <summary>Bloco das assinaturas: 4 mm de respiro e 22 mm de altura; 14 mm entre as duas linhas.</summary>
    public const double RespiroDasAssinaturas = 4 * Mm, AlturaDasAssinaturas = 22 * Mm, EspacoEntreAssinaturas = 14 * Mm, RecuoDasAssinaturas = 6 * Mm;

    // Letras (pt) e altura de linha (multiplo da letra).
    public const double LetraDoTitulo = 17, LetraDaData = 9.5, LetraDoOrgao = 9, LetraDoOrgaoForte = 12;
    public const double LetraDoRotulo = 7.6, LetraDoValor = 9, AlturaDaLinhaDoCampo = 1.25;
    public const double LetraDaLegenda = 10, AlturaDaLinhaDaLegenda = 1.22;
    public const double LetraDaDescricao = 9.2, AlturaDaLinhaDaDescricao = 1.25;
    public const double LetraDoRodape = 8, LetraDoNome = 9.6, LetraDoCargo = 8.6;

    /// <summary>Linhas reservadas para a legenda mesmo quando ela e curta ou vazia (as fotos de baixo nao "sobem").</summary>
    public const int LinhasMinimasDaLegenda = 2;

    /// <summary>Altura do rodape (da linha fina ate a margem de baixo, contando o espaco acima dela).</summary>
    public static double AlturaDoRodape => EspacoAntesDoRodape + RespiroDoRodape + LetraDoRodape * 1.2;

    /// <summary>Altura do bloco das assinaturas com o respiro de cima.</summary>
    public static double BlocoDasAssinaturas => RespiroDasAssinaturas + AlturaDasAssinaturas;

    /// <summary>Onde o conteudo da pagina termina (acima do rodape).</summary>
    public static double FimDoConteudo => AlturaDaFolha - MargemDeBaixo - AlturaDoRodape;

    /// <summary>Menor quadro aceitavel (mm): com cabecalho enorme, avisa em vez de gerar fotos minusculas.</summary>
    public const double MenorQuadro = 30 * Mm;

    /// <summary>
    /// Colunas da planilha (pontos), C a M. C-G e I-M sao as duas colunas de fotos (o quadro exato) e H e o espaco
    /// entre elas; os outros cortes caem nas bordas do rotulo, do logo e da caixa SPOBRAS, para o Excel montar o
    /// mesmo cabecalho do PDF.
    /// </summary>
    public static readonly IReadOnlyList<double> Colunas = MontarColunas();

    private static double[] MontarColunas()
    {
        double quadro = (Largura - EspacoEntreColunas) / 2;
        double rotulo2 = LarguraDoRotulo - EspacoEntreColunas;                 // rotulo da direita comeca no espaco H
        double orgao = Largura - LarguraDoOrgao;                               // x onde comeca a caixa SPOBRAS
        double direita = quadro + EspacoEntreColunas;                          // x da coluna I
        double resto = quadro - LarguraDoLogo;
        return
        [
            LarguraDoRotulo / 2, LarguraDoRotulo / 2, LarguraDoLogo - LarguraDoRotulo, resto / 2, resto / 2,
            EspacoEntreColunas,
            rotulo2 / 2, rotulo2 / 2, orgao - (direita + rotulo2), LarguraDoOrgao / 2, LarguraDoOrgao / 2,
        ];
    }

    public static double XDaColuna(int indice) => Colunas.Take(indice).Sum();

    /// <summary>Indices (0 a 10) das colunas de cada coluna de fotos: C a G e I a M; com uma por linha, C a M.</summary>
    public static (int Primeira, int Ultima) ColunasDoEspaco(int coluna, int colunas) =>
        colunas == 1 ? (0, 10) : coluna == 0 ? (0, 4) : (6, 10);

    /// <summary>Largura de uma coluna de fotos (o quadro e a legenda).</summary>
    public static double LarguraDoQuadro(int colunas) => colunas == 1 ? Largura : (Largura - EspacoEntreColunas) / 2;

    /// <summary>X (dentro da area util) do quadro da coluna.</summary>
    public static double XDoQuadro(int coluna, int colunas) => coluna == 0 ? 0 : LarguraDoQuadro(colunas) + EspacoEntreColunas;

    /// <summary>
    /// Largura que o Excel grava para a coluna ter <paramref name="pixels"/> na tela (Calibri 11, 96 dpi).
    /// A largura do arquivo inclui a margem interna da celula: pixels = Trunc(((256*w + Trunc(128/7))/256)*7).
    /// </summary>
    public static double LarguraNoExcel(int pixels) => Math.Truncate(pixels * 256.0 / 7) / 256;

    /// <summary>Pixels que o Excel desenha para a largura gravada no arquivo (a conta do proprio Excel).</summary>
    public static int PixelsNoExcel(double largura) => (int)Math.Truncate((256 * largura + Math.Truncate(128.0 / 7)) / 256 * 7);
}
