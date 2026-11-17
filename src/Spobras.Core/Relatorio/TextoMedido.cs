namespace Spobras.Core.Relatorio;

/// <summary>
/// Mede e quebra texto como a fonte do relatorio (Carlito, com as mesmas medidas da Calibri do Excel) o desenha.
/// As larguras vem da propria fonte (milesimos de em, arredondados para cima), entao a quebra de linha da conta e a
/// do PDF sao iguais: o espaco reservado para a legenda e a descricao nunca fica pequeno.
/// </summary>
public static class TextoMedido
{
    // Larguras de U+0020 a U+007E, de U+00A0 a U+00FF e dos sinais de Extras, nessa ordem.
    private const string Extras = "–—‘’“”…•€ŒœŠšŽžŸƒ";

    private static readonly short[] Regular =
    [
        227,326,401,499,507,715,683,221,304,304,499,499,250,307,253,387,507,507,507,507,507,507,507,507,
        507,507,268,268,499,499,499,464,895,579,544,534,616,489,460,631,624,252,319,520,421,855,646,663,
        517,673,543,460,488,642,568,890,520,488,469,307,387,307,499,499,292,480,526,423,526,498,306,471,
        526,230,240,455,230,799,526,528,526,526,349,392,335,526,452,715,434,453,396,315,461,315,499,227,
        326,499,507,499,507,499,499,393,835,403,513,499,0,507,395,339,499,336,335,292,550,586,253,308,
        247,423,513,637,672,676,464,579,579,579,579,579,579,764,534,489,489,489,489,252,252,252,252,625,
        646,663,663,663,663,663,499,664,642,642,642,642,488,517,528,480,480,480,480,480,480,773,423,498,
        498,498,498,230,230,230,230,526,526,528,528,528,528,528,499,530,526,526,526,526,453,526,453,499,
        906,250,250,419,419,691,499,507,867,850,460,392,469,396,488,499
    ];

    private static readonly short[] Negrito =
    [
        227,326,439,499,507,730,705,234,312,312,499,499,258,307,268,430,507,507,507,507,507,507,507,507,
        507,507,276,276,499,499,499,464,899,606,561,530,631,488,459,638,631,267,332,547,423,875,659,677,
        533,687,563,473,496,653,592,907,551,520,479,325,430,325,499,499,301,494,537,419,537,504,317,475,
        537,246,256,480,246,814,537,538,537,537,356,399,347,537,474,746,460,474,398,344,476,344,499,227,
        326,499,507,499,507,499,499,415,835,417,539,499,0,507,391,343,499,338,336,301,564,598,268,304,
        253,436,539,658,691,702,464,606,606,606,606,606,606,776,530,488,488,488,488,267,267,267,267,640,
        659,677,677,677,677,677,499,681,653,653,653,653,520,533,555,494,494,494,494,494,494,775,419,504,
        504,504,504,246,246,246,246,537,537,538,538,538,538,538,499,544,537,537,537,537,474,537,474,499,
        906,258,258,436,436,712,499,507,875,843,473,399,479,398,520,499
    ];

    /// <summary>Largura de um caractere em milesimos de em (caractere fora da tabela conta como o mais largo, por seguranca).</summary>
    public static int Milesimos(char c, bool negrito)
    {
        var t = negrito ? Negrito : Regular;
        int i = c switch
        {
            >= ' ' and <= '~' => c - ' ',
            >= '\u00A0' and <= '\u00FF' => 95 + (c - '\u00A0'),
            _ => Extras.IndexOf(c) is var e and >= 0 ? 191 + e : -1,
        };
        return i >= 0 ? t[i] : 1000;
    }

    /// <summary>Largura do texto em pontos, no tamanho <paramref name="tamanho"/> (pt).</summary>
    public static double Largura(string texto, double tamanho, bool negrito)
    {
        long soma = 0;
        foreach (char c in texto)
        {
            soma += Milesimos(c, negrito);
        }

        return soma * tamanho / 1000.0;
    }

    /// <summary>
    /// Quebra o texto em linhas que cabem em <paramref name="largura"/> pontos, por palavra; palavra maior que a
    /// linha e partida. Quebras de linha do usuario sao mantidas.
    /// </summary>
    public static IReadOnlyList<string> Quebrar(string texto, double largura, double tamanho, bool negrito)
    {
        var linhas = new List<string>();
        double espaco = Largura(" ", tamanho, negrito);
        foreach (string paragrafo in texto.Replace("\r", "").Split('\n'))
        {
            var atual = new System.Text.StringBuilder();
            double usado = 0;
            foreach (string palavraInteira in paragrafo.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                string palavra = palavraInteira;
                double l = Largura(palavra, tamanho, negrito);
                if (atual.Length > 0 && usado + espaco + l > largura)
                {
                    linhas.Add(atual.ToString());
                    atual.Clear();
                    usado = 0;
                }

                // Palavra maior que a linha inteira: parte em pedacos.
                while (l > largura && palavra.Length > 1)
                {
                    int n = 1;
                    while (n < palavra.Length && Largura(palavra[..(n + 1)], tamanho, negrito) <= largura)
                    {
                        n++;
                    }

                    if (atual.Length > 0)
                    {
                        linhas.Add(atual.ToString());
                        atual.Clear();
                        usado = 0;
                    }

                    linhas.Add(palavra[..n]);
                    palavra = palavra[n..];
                    l = Largura(palavra, tamanho, negrito);
                }

                if (atual.Length > 0)
                {
                    atual.Append(' ');
                    usado += espaco;
                }

                atual.Append(palavra);
                usado += l;
            }

            linhas.Add(atual.ToString());
        }

        // Sem linhas vazias no fim (o usuario pode ter dado Enter a mais).
        while (linhas.Count > 1 && linhas[^1].Length == 0)
        {
            linhas.RemoveAt(linhas.Count - 1);
        }

        return linhas;
    }
}
