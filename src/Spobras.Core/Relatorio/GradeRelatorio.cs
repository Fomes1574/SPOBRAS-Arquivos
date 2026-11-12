namespace Spobras.Core.Relatorio;

/// <summary>
/// Medidas da pagina do relatorio fotografico, tiradas do modelo de referencia (A4 em pe, 10 colunas,
/// cabecalho repetido). Tudo em pixels a 96 dpi: 1 linha padrao da planilha = 15 pt = 20 px.
/// A mesma grade serve a previa na tela e ao arquivo gerado.
/// </summary>
public static class GradeRelatorio
{
    public const double AlturaDaLinha = 20;

    /// <summary>Larguras das 10 colunas da area impressa (C a L), em pixels.</summary>
    public static readonly IReadOnlyList<int> Colunas = [60, 60, 60, 110, 110, 90, 90, 67, 77, 77];

    public static double Largura => Colunas.Sum();

    /// <summary>Alturas das 4 linhas de cima do cabecalho: logo, titulo e a caixa SPOBRAS / data / contratada.</summary>
    public static readonly IReadOnlyList<int> LinhasDoTopo = [27, 27, 27, 27];

    /// <summary>Linha em branco, faixa "RELATÓRIO FOTOGRÁFICO" e respiro antes das fotos.</summary>
    public static readonly IReadOnlyList<int> LinhasDoTitulo = [12, 34, 10];

    /// <summary>Altura util de cada pagina (cabe em A4 em pe na escala de impressao de 80%: 1240 x 0,8 = 992 px de 1017).</summary>
    public const double AlturaDaPagina = 1240;

    /// <summary>Colunas (indices 0 a 9) do cabecalho: logo C-E, titulo F-H, caixa I-L; rotulo C-E e valor F-L.</summary>
    public const int ColunaDoTitulo = 3, ColunaDaCaixa = 6, ColunaDoValorDaData = 7, ColunaDoValor = 3;

    /// <summary>Escala de impressao (%) usada no arquivo e na conta da altura da pagina.</summary>
    public const int EscalaDeImpressao = 80;

    /// <summary>
    /// Largura que o Excel grava para a coluna ter exatamente <paramref name="pixels"/> na tela (Calibri 11, 96 dpi).
    /// A largura do arquivo inclui a margem interna da celula: pixels = Trunc(((256*w + Trunc(128/7))/256)*7).
    /// </summary>
    public static double LarguraNoExcel(int pixels) => Math.Truncate(pixels * 256.0 / 7) / 256;

    /// <summary>Pixels que o Excel desenha para a largura gravada no arquivo (a conta do proprio Excel).</summary>
    public static int PixelsNoExcel(double largura) => (int)Math.Truncate((256 * largura + Math.Truncate(128.0 / 7)) / 256 * 7);

    public const int LinhasDasAssinaturas = 6;

    /// <summary>
    /// Espaco entre as duas colunas de fotos (e metade dele ate a borda). Folgado de proposito: com a tela do
    /// Windows em 125% ou 150%, o Excel arredonda as larguras de coluna e nada pode chegar a encostar.
    /// </summary>
    public const double Folga = 12;

    /// <summary>
    /// Largura media de um caractere Calibri 11 pt, arredondada para cima: a conta de linhas de texto
    /// erra para mais (sobra espaco) e nunca para menos (texto escondido).
    /// </summary>
    public const double LarguraDoCaractere = 7.0;

    /// <summary>Indices (0 a 9) das colunas de cada coluna de fotos: C a G e H a L (metades iguais).</summary>
    public static (int Primeira, int Ultima) ColunasDoEspaco(int coluna, int colunas) =>
        colunas == 1 ? (0, 9) : coluna == 0 ? (0, 4) : (5, 9);

    public static double XDaColuna(int indice) => Colunas.Take(indice).Sum();

    public static double LarguraDoEspaco(int coluna, int colunas)
    {
        var (primeira, ultima) = ColunasDoEspaco(coluna, colunas);
        return Colunas.Skip(primeira).Take(ultima - primeira + 1).Sum();
    }
}
