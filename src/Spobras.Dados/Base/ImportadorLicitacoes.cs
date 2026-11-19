using Spobras.Core.Base;
using Spobras.Core.Planilhas;

namespace Spobras.Dados.Base;

/// <summary>Uma base prevista lida: null em Obras quer dizer "a aba nao foi lida" (os dados anteriores ficam).</summary>
public sealed record PrevistasLidas(IReadOnlyList<ObraPrevista>? Obras, IReadOnlyList<string> Avisos);

/// <summary>
/// Le as abas "Em licitação" e "A Licitar" da mestra. Nao sao tabelas nomeadas: o cabecalho e achado pelo nome das
/// colunas (sem acento, caixa e espacos nas pontas), em qualquer linha. Aba ou coluna que falta so gera aviso daquela
/// base; a leitura das obras em andamento nunca e impedida por elas (docs/PLANO_LICITACOES.md).
/// </summary>
public static class ImportadorLicitacoes
{
    public const string AbaEmLicitacao = "Em licitação";
    public const string AbaALicitar = "A Licitar";

    private sealed record Coluna(string Campo, string[] Nomes, bool Obrigatoria, bool Prefixo = false);

    // Em licitacao: "Valor do Contrato com ADM" e o valor ANTES dos 10% (Recursos = valor x 1,1, pela formula da aba).
    private static readonly Coluna[] ColunasLicitacao =
    [
        new("nome", ["nome da obra"], true), new("valor", ["valor do contrato com adm"], true),
        new("com10", ["recursos para"], false, Prefixo: true), new("portaria", ["portaria"], false), new("receita", ["contrato receita"], false),
        new("dotacao", ["dotacao"], false), new("lote", ["lote"], false), new("modalidade", ["modalidade"], false),
        new("empresa", ["empresa"], false), new("status", ["status"], false), new("processo", ["processo"], false),
    ];

    // A licitar: "Valor do Contrato" e o valor antes dos 10%; "Valor com ADM" ja tem os 10%.
    private static readonly Coluna[] ColunasALicitar =
    [
        new("nome", ["nome da obra"], true), new("valor", ["valor do contrato"], true),
        new("com10", ["valor com adm"], false), new("portaria", ["portaria"], false), new("receita", ["contrato receita"], false),
        new("dotacao", ["dotacao"], false), new("lote", ["lote"], false), new("modalidade", ["modalidade"], false),
        new("status", ["status"], false), new("processo", ["processo"], false),
    ];

    public static PrevistasLidas Ler(ILeitorPlanilha planilha, BaseDeObras qual)
    {
        string nomeAba = qual == BaseDeObras.EmLicitacao ? AbaEmLicitacao : AbaALicitar;
        string base_ = BasesDeObras.Nome(qual);
        string? aba = planilha.NomesDasAbas().FirstOrDefault(a => Chave(a) == Chave(nomeAba));
        if (aba is null)
        {
            return new(null, [$"{base_}: a aba \"{nomeAba}\" não foi encontrada na planilha; as obras {Minusculo(base_)} anteriores foram mantidas."]);
        }

        var grade = planilha.LerAba(aba);
        var colunas = qual == BaseDeObras.EmLicitacao ? ColunasLicitacao : ColunasALicitar;
        var (linhaCab, indices) = AcharCabecalho(grade, colunas);
        if (linhaCab < 0)
        {
            string faltam = string.Join(", ", colunas.Where(c => c.Obrigatoria).Select(c => $"\"{Titulo(c)}\""));
            return new(null, [$"{base_}: na aba \"{aba}\" não foram encontradas as colunas {faltam}; as obras {Minusculo(base_)} anteriores foram mantidas."]);
        }

        var avisos = new List<string>();
        var obras = new List<ObraPrevista>();
        string? Valor(object?[] l, string campo) => indices.TryGetValue(campo, out int i) && i < l.Length ? ImportadorMestra.Texto(l[i]) : null;
        decimal? Reais(object?[] l, string campo) => indices.TryGetValue(campo, out int i) && i < l.Length ? ImportadorMestra.Reais(l[i]) : null;
        long id = qual == BaseDeObras.EmLicitacao ? 1_000_000 : 2_000_000;

        for (int r = linhaCab + 1; r < grade.Count; r++)
        {
            var l = grade[r];
            string? nome = Valor(l, "nome");
            if (nome is null)
            {
                continue;
            }

            if (Chave(nome) is "total" or "totais")
            {
                continue;
            }

            decimal? valor = Reais(l, "valor"), com10 = Reais(l, "com10");
            int linha = r + 1;
            if (indices.ContainsKey("valor") && valor is null && Valor(l, "valor") is { } textoValor)
            {
                avisos.Add($"{base_}, linha {linha}: o valor \"{textoValor}\" não é um número; a obra entrou sem valor.");
            }

            // Conferencia da regra dos 10%: a coluna com os 10% deve ser o valor x 1,1 (formula da aba). Nunca e usada como valor da obra.
            if (valor is { } v && com10 is { } c && Math.Abs(c - Math.Round(v * 1.1m, 2, MidpointRounding.AwayFromZero)) > 0.05m)
            {
                avisos.Add($"{base_}, linha {linha}: o valor com os 10% ({Formato(c)}) não confere com o valor da obra ({Formato(v)}) × 1,1. O app usa o valor da obra.");
            }

            obras.Add(new ObraPrevista
            {
                Id = id + linha,
                Base = qual,
                Linha = linha,
                Nome = nome,
                Portaria = Valor(l, "portaria"),
                ContratoReceita = Valor(l, "receita"),
                Dotacao = Valor(l, "dotacao"),
                Lote = Valor(l, "lote"),
                Modalidade = Valor(l, "modalidade"),
                Empresa = Valor(l, "empresa"),
                Status = Valor(l, "status"),
                Processo = Valor(l, "processo"),
                ValorObra = valor,
                ValorComRemuneracao = com10,
            });
        }

        if (obras.Count == 0)
        {
            avisos.Add($"{base_}: a aba \"{aba}\" não tem nenhuma obra.");
        }

        return new(obras, avisos);
    }

    /// <summary>Linha do cabecalho: a primeira em que todas as colunas obrigatorias aparecem. Cada coluna serve a um campo so.</summary>
    private static (int Linha, Dictionary<string, int> Indices) AcharCabecalho(IReadOnlyList<object?[]> grade, Coluna[] colunas)
    {
        for (int r = 0; r < Math.Min(grade.Count, 30); r++)
        {
            var textos = grade[r].Select(v => v is string s ? Chave(s) : "").ToArray();
            var indices = new Dictionary<string, int>();
            var usadas = new HashSet<int>();
            foreach (var c in colunas)
            {
                for (int i = 0; i < textos.Length; i++)
                {
                    if (!usadas.Contains(i) && textos[i].Length > 0 && c.Nomes.Any(n => c.Prefixo ? textos[i].StartsWith(n, StringComparison.Ordinal) : textos[i] == n))
                    {
                        indices[c.Campo] = i;
                        usadas.Add(i);
                        break;
                    }
                }
            }

            if (colunas.Where(c => c.Obrigatoria).All(c => indices.ContainsKey(c.Campo)))
            {
                return (r, indices);
            }
        }

        return (-1, []);
    }

    /// <summary>Sem acento, minusculo, espacos repetidos e das pontas tirados ("Portaria " = "portaria").</summary>
    internal static string Chave(string texto) =>
        string.Join(' ', Normalizacao.Busca(texto).Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static string Titulo(Coluna c) => c.Campo switch
    {
        "nome" => "Nome da Obra",
        "valor" => c.Nomes[0] == "valor do contrato com adm" ? "Valor do Contrato com ADM" : "Valor do Contrato",
        _ => c.Nomes[0],
    };

    private static string Minusculo(string base_) => base_ == "Em licitação" ? "em licitação" : "a licitar";

    private static string Formato(decimal v) => v.ToString("C2", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));
}
