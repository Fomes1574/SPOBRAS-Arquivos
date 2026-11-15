using Spobras.Core.Base;
using Spobras.Core.Planilhas;

namespace Spobras.Dados.Base;

/// <summary>Coluna que existe na planilha, com o primeiro valor preenchido como exemplo.</summary>
public sealed record ColunaDaPlanilha(string Nome, string? Exemplo);

public sealed record EstadoTabelaMestra(TabelaMestra Tabela, string Nome, bool Encontrada, int Linhas, IReadOnlyList<ColunaDaPlanilha> Colunas);

public sealed record EstadoCampoMestra(CampoMestra Campo, string Coluna, bool Encontrada);

/// <summary>Como a planilha bate com o mapeamento: o que existe, o que falta e o que da para escolher.</summary>
public sealed record ConferenciaMapeamento(
    IReadOnlyList<EstadoTabelaMestra> Tabelas, IReadOnlyList<EstadoCampoMestra> Campos, IReadOnlyList<string> TabelasDaPlanilha)
{
    /// <summary>Tudo o que e obrigatorio foi encontrado: a planilha pode ser lida.</summary>
    public bool Ok => Tabelas.All(t => t.Encontrada) && Campos.All(c => c.Encontrada || !c.Campo.Obrigatorio);

    public IReadOnlyList<EstadoCampoMestra> Faltando => [.. Campos.Where(c => !c.Encontrada && c.Campo.Obrigatorio)];
}

public static class ConferenciaMestra
{
    /// <summary>Confere a planilha contra o mapeamento sem importar nada (rapido: so le cabecalhos e algumas linhas).</summary>
    public static ConferenciaMapeamento Conferir(ILeitorPlanilha planilha, MapeamentoMestra mapa)
    {
        var descricoes = planilha.Tabelas();
        var tabelas = new List<EstadoTabelaMestra>();
        var campos = new List<EstadoCampoMestra>();
        foreach (var qual in Enum.GetValues<TabelaMestra>())
        {
            string nome = mapa.Tabela(qual);
            var d = descricoes.FirstOrDefault(x => string.Equals(x.Nome, nome, StringComparison.OrdinalIgnoreCase));
            if (d is null)
            {
                tabelas.Add(new EstadoTabelaMestra(qual, nome, false, 0, []));
                campos.AddRange(CamposMestra.Todos.Where(c => c.Tabela == qual).Select(c => new EstadoCampoMestra(c, mapa.Coluna(c), false)));
                continue;
            }

            var t = planilha.LerTabela(d.Nome);
            var colunas = t.Colunas.Select((c, i) => new ColunaDaPlanilha(c, Exemplo(t, i))).ToList();
            tabelas.Add(new EstadoTabelaMestra(qual, d.Nome, true, t.Linhas.Count, colunas));
            campos.AddRange(CamposMestra.Todos.Where(c => c.Tabela == qual)
                .Select(c => new EstadoCampoMestra(c, mapa.Coluna(c), t.Indice(mapa.Coluna(c)) >= 0)));
        }

        return new ConferenciaMapeamento(tabelas, campos, [.. descricoes.Select(x => x.Nome)]);
    }

    private static string? Exemplo(TabelaLida t, int coluna)
    {
        foreach (var linha in t.Linhas.Take(200))
        {
            if (coluna < linha.Length && ImportadorMestra.TextoDeExemplo(linha[coluna]) is { } texto)
            {
                return texto.Length > 40 ? texto[..40] + "…" : texto;
            }
        }

        return null;
    }
}
