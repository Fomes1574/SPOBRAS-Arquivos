namespace Spobras.Core.Planilhas;

/// <summary>Uma tabela lida: nomes das colunas e linhas com valores ja tipados (texto, decimal, data, bool ou null).</summary>
public sealed record TabelaLida(string Nome, string Aba, IReadOnlyList<string> Colunas, IReadOnlyList<object?[]> Linhas)
{
    public int Indice(string coluna)
    {
        for (int i = 0; i < Colunas.Count; i++)
        {
            if (string.Equals(Colunas[i], coluna, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }
}

public sealed record DescricaoTabela(string Nome, string Aba, int Linhas, IReadOnlyList<string> Colunas);

/// <summary>Leitura de uma planilha, sem abrir o Excel e sem alterar o arquivo.</summary>
public interface ILeitorPlanilha : IDisposable
{
    IReadOnlyList<DescricaoTabela> Tabelas();
    IReadOnlyList<string> NomesDasAbas();
    TabelaLida LerTabela(string nome);

    /// <summary>Aba inteira como grade (linhas e colunas a partir de 0).</summary>
    IReadOnlyList<object?[]> LerAba(string nomeDaAba);
}
