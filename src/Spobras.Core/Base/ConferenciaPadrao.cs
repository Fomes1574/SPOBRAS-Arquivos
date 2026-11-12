using System.Text.RegularExpressions;

namespace Spobras.Core.Base;

/// <summary>Um texto fora do padrao numa planilha: onde esta, o original e a proposta (null = so conferir).</summary>
public sealed record AchadoPadrao(string Aba, string Celula, string Original, string? Proposta, string Motivo)
{
    public bool Corrigivel => Proposta is not null;
}

/// <summary>
/// Confere portarias e dotacoes escritas numa planilha contra a Base de dados (especificacao 4.6,
/// "Identificadores e padronizacao"). Propoe so o que e inequivoco; o resto fica para o usuario conferir.
/// </summary>
public static partial class ConferenciaPadrao
{
    [GeneratedRegex(@"^\d{1,3}([.\s]\d{1,5}){5,}$")]
    private static partial Regex PareceDotacaoCompleta();

    [GeneratedRegex(@"^\d\.\d{3}\.\d{8}$")]
    private static partial Regex DotacaoAbreviada();

    [GeneratedRegex(@"^(n[ºo°.]?\s*)?\d{1,3}(\s*\(\d{4}\))?$", RegexOptions.IgnoreCase)]
    private static partial Regex PortariaCurta();

    /// <param name="grade">Celulas da aba: linha 0 = linha 1 da planilha, coluna 0 = coluna A.</param>
    public static IReadOnlyList<AchadoPadrao> Conferir(
        string aba, IReadOnlyList<object?[]> grade, IReadOnlyList<PortariaBase> portarias, IReadOnlyCollection<string> dotacoesDaBase)
    {
        var achados = new List<AchadoPadrao>();
        var dotacoes = new HashSet<string>(dotacoesDaBase, StringComparer.OrdinalIgnoreCase);
        var colunasDePortaria = new HashSet<int>();
        for (int r = 0; r < grade.Count; r++)
        {
            for (int c = 0; c < grade[r].Length; c++)
            {
                if (grade[r][c] is not string bruto || bruto.Trim().Length == 0)
                {
                    continue;
                }

                string texto = bruto.Trim();
                string celula = Coluna(c) + (r + 1);
                string normal = Normalizacao.Busca(texto);
                if (normal is "portaria" or "portarias" or "portaria/ano")
                {
                    colunasDePortaria.Add(c); // titulo: os valores curtos embaixo sao portarias
                    continue;
                }

                if (PareceDotacaoCompleta().IsMatch(texto))
                {
                    var d = Normalizacao.Dotacao(texto);
                    if (d.Corrigida)
                    {
                        achados.Add(new AchadoPadrao(aba, celula, texto, d.Completa,
                            "Dotação com espaço no lugar do ponto" + (dotacoes.Count > 0 && !dotacoes.Contains(d.Completa) ? "; a corrigida também não está na Base, confira" : "")));
                    }
                    else if (dotacoes.Count > 0 && !dotacoes.Contains(texto))
                    {
                        achados.Add(new AchadoPadrao(aba, celula, texto, null, "Dotação completa que não está na Base de dotações"));
                    }

                    continue;
                }

                if (DotacaoAbreviada().IsMatch(texto))
                {
                    achados.Add(new AchadoPadrao(aba, celula, texto, null, "Dotação no formato abreviado: o app não completa sozinho, confira qual é a completa"));
                    continue;
                }

                bool emColuna = colunasDePortaria.Contains(c) && PortariaCurta().IsMatch(texto);
                if (!emColuna && !normal.Contains("portaria", StringComparison.Ordinal))
                {
                    continue;
                }

                if (portarias.Any(p => string.Equals(p.Rotulo, texto, StringComparison.Ordinal)))
                {
                    continue; // ja esta como na base
                }

                var lida = emColuna && !normal.Contains("portaria", StringComparison.Ordinal)
                    ? Normalizacao.Portaria("Portaria " + Regex.Replace(texto, @"^(n[ºo°.]?\s*)", "", RegexOptions.IgnoreCase))
                    : Normalizacao.Portaria(texto);
                if (lida.Numero is null)
                {
                    continue;
                }

                var candidatas = portarias.Where(p => p.Numero == lida.Numero && (lida.Ano is null || p.Ano == lida.Ano)).ToList();
                if (candidatas.Count == 1)
                {
                    achados.Add(new AchadoPadrao(aba, celula, texto, candidatas[0].Rotulo, "Portaria escrita fora do padrão da Base"));
                }
                else if (candidatas.Count > 1)
                {
                    achados.Add(new AchadoPadrao(aba, celula, texto, null,
                        $"Portaria nº {lida.Numero} sem ano: na Base há {string.Join(" e ", candidatas.Select(p => p.Ano?.ToString() ?? "sem ano"))}. Confira qual é"));
                }
                else
                {
                    achados.Add(new AchadoPadrao(aba, celula, texto, null, "Portaria que não está na Base de dados"));
                }
            }
        }

        return achados;
    }

    private static string Coluna(int indice)
    {
        string nome = "";
        for (int n = indice + 1; n > 0; n = (n - 1) / 26)
        {
            nome = (char)('A' + ((n - 1) % 26)) + nome;
        }

        return nome;
    }
}
