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

    /// <summary>Alturas das linhas do cabecalho (logo, titulo, data, contratada, unidade, dados), em pixels.</summary>
    public static readonly IReadOnlyList<int> LinhasDoCabecalho = [27, 27, 27, 27, 27, 40];

    /// <summary>Linha em branco, faixa "RELATÓRIO FOTOGRÁFICO" e respiro antes das fotos.</summary>
    public static readonly IReadOnlyList<int> LinhasDoTitulo = [12, 34, 10];

    public static double AlturaDoCabecalho => LinhasDoCabecalho.Sum() + LinhasDoTitulo.Sum();

    /// <summary>Linhas de 20 px disponiveis para fotos, legendas e assinaturas em cada pagina.</summary>
    public const int LinhasDoCorpo = 46;

    public static double AlturaDaPagina => AlturaDoCabecalho + LinhasDoCorpo * AlturaDaLinha;

    /// <summary>Total de linhas da planilha ocupadas por uma pagina.</summary>
    public static int LinhasPorPagina => LinhasDoCabecalho.Count + LinhasDoTitulo.Count + LinhasDoCorpo;

    public const int LinhasDasAssinaturas = 6;

    /// <summary>Espaco entre as duas colunas de fotos e entre a foto e a borda do seu espaco.</summary>
    public const double Folga = 8;

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
