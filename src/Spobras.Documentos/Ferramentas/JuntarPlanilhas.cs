using Spobras.Core.Base;
using Spobras.Documentos.Planilhas;

namespace Spobras.Documentos.Ferramentas;

/// <summary>De onde vieram os dados de um arquivo: tabela nomeada ou aba, titulos e linhas.</summary>
public sealed record OrigemJuntar(string Arquivo, string Onde, IReadOnlyList<string> Titulos, IReadOnlyList<object?[]> Linhas, string? Problema)
{
    public bool Ok => Problema is null;
}

public sealed record PreviaJuntar(IReadOnlyList<OrigemJuntar> Origens, IReadOnlyList<string> Colunas)
{
    /// <summary>Colunas que faltam em cada arquivo (ficam em branco nas linhas dele).</summary>
    public IReadOnlyList<string> Faltando(OrigemJuntar origem) =>
        [.. Colunas.Where(c => !origem.Titulos.Any(t => JuntarPlanilhas.Chave(t) == JuntarPlanilhas.Chave(c)))];

    public int TotalLinhas => Origens.Where(o => o.Ok).Sum(o => o.Linhas.Count);
}

/// <summary>
/// Junta planilhas de mesma estrutura numa planilha nova: casa as colunas pelo titulo (sem acento,
/// caixa ou espacos extras), marca arquivo e aba de origem de cada linha e nao soma nem recalcula nada.
/// Formulas entram com o valor que estava salvo no arquivo.
/// </summary>
public static class JuntarPlanilhas
{
    public static string Chave(string titulo) => string.Join(" ", Normalizacao.Busca(titulo).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    public static PreviaJuntar Analisar(IReadOnlyList<string> arquivos)
    {
        var origens = arquivos.Select(Ler).ToList();
        var colunas = new List<string>();
        var vistas = new HashSet<string>();
        foreach (var titulo in origens.Where(o => o.Ok).SelectMany(o => o.Titulos))
        {
            if (titulo.Trim().Length > 0 && vistas.Add(Chave(titulo)))
            {
                colunas.Add(titulo.Trim());
            }
        }

        return new PreviaJuntar(origens, colunas);
    }

    private static OrigemJuntar Ler(string arquivo)
    {
        try
        {
            using var leitor = new LeitorXlsx(arquivo);
            var tabelas = leitor.Tabelas();
            if (tabelas.Count > 0)
            {
                var tabela = leitor.LerTabela(tabelas[0].Nome);
                string onde = $"tabela {tabela.Nome}" + (tabelas.Count > 1 ? $" (a primeira de {tabelas.Count})" : "");
                return new OrigemJuntar(arquivo, onde, tabela.Colunas, tabela.Linhas, null);
            }

            string aba = leitor.NomesDasAbas()[0];
            var grade = leitor.LerAba(aba).Where(l => l.Any(c => c is not null && c.ToString()!.Trim().Length > 0)).ToList();
            if (grade.Count == 0)
            {
                return new OrigemJuntar(arquivo, $"aba {aba}", [], [], "a primeira aba está vazia");
            }

            var titulos = grade[0].Select(c => c?.ToString()?.Trim() ?? "").ToList();
            int ultima = titulos.FindLastIndex(t => t.Length > 0);
            titulos = titulos.Take(ultima + 1).ToList();
            return new OrigemJuntar(arquivo, $"aba {aba}", titulos, grade.Skip(1).ToList(), null);
        }
        catch (Exception ex) when (ex is PlanilhaInvalidaException or IOException or UnauthorizedAccessException)
        {
            return new OrigemJuntar(arquivo, "", [], [], ex.Message);
        }
    }

    public static void Gravar(PreviaJuntar previa, string saida, string? logo = null)
    {
        var linhas = new List<object?[]>();
        foreach (var origem in previa.Origens.Where(o => o.Ok))
        {
            var posicao = previa.Colunas.Select(c => origem.Titulos.ToList().FindIndex(t => Chave(t) == Chave(c))).ToArray();
            foreach (var linha in origem.Linhas)
            {
                var nova = new object?[previa.Colunas.Count + 2];
                nova[0] = Path.GetFileName(origem.Arquivo);
                nova[1] = origem.Onde;
                for (int c = 0; c < posicao.Length; c++)
                {
                    nova[c + 2] = posicao[c] >= 0 && posicao[c] < linha.Length ? Converter(linha[posicao[c]]) : null;
                }

                linhas.Add(nova);
            }
        }

        var sobre = previa.Origens.Select(o => new object?[]
        {
            Path.GetFileName(o.Arquivo),
            o.Ok ? o.Onde : "não entrou",
            o.Ok ? o.Linhas.Count : null,
            o.Ok ? string.Join(", ", previa.Faltando(o)) : o.Problema,
        }).ToList();
        sobre.Add(["Observação", "Valores como estavam salvos nos arquivos (fórmulas entram pelo resultado). Nada foi somado nem recalculado.", null, null]);

        GravadorXlsx.Gravar(saida,
        [
            new AbaNova("Dados juntos", ["Arquivo de origem", "Tabela ou aba", .. previa.Colunas], linhas),
            new AbaNova("Origem", ["Arquivo", "De onde", "Linhas", "Colunas que faltaram ou problema"], sobre, ComFiltro: false),
        ], logo, "Planilhas juntas");
    }

    private static object? Converter(object? valor) => valor switch
    {
        DateTime d => d.TimeOfDay == TimeSpan.Zero ? DateOnly.FromDateTime(d) : d,
        bool b => b ? "VERDADEIRO" : "FALSO",
        _ => valor,
    };
}
