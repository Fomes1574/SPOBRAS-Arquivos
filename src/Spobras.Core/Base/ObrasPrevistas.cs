namespace Spobras.Core.Base;

/// <summary>
/// As tres bases de obras da planilha mestra. Nunca se misturam: cada uma tem a sua tabela, os seus campos e a sua
/// ficha (docs/PLANO_LICITACOES.md).
/// </summary>
public enum BaseDeObras
{
    EmAndamento,
    EmLicitacao,
    ALicitar,
}

public static class BasesDeObras
{
    public static string Nome(BaseDeObras b) => b switch
    {
        BaseDeObras.EmLicitacao => "Em licitação",
        BaseDeObras.ALicitar => "A licitar",
        _ => "Em andamento",
    };

    /// <summary>Id curto e sem acento (telas, preferencias, chaves).</summary>
    public static string Id(BaseDeObras b) => b switch
    {
        BaseDeObras.EmLicitacao => "licitacao",
        BaseDeObras.ALicitar => "a-licitar",
        _ => "andamento",
    };

    public static BaseDeObras DoId(string? id) => id switch
    {
        "licitacao" => BaseDeObras.EmLicitacao,
        "a-licitar" => BaseDeObras.ALicitar,
        _ => BaseDeObras.EmAndamento,
    };
}

/// <summary>
/// Obra ainda sem contrato, das abas "Em licitação" ou "A Licitar". So os campos que a aba tem: nada e copiado de
/// uma base para a outra. O valor da obra e sempre o valor antes dos 10%; o valor com os 10% vem da planilha so para
/// conferir e mostrar (nunca entra como valor da obra, para nao somar 10% em cima de 10%).
/// </summary>
public sealed record ObraPrevista
{
    public long Id { get; init; }

    public BaseDeObras Base { get; init; }

    /// <summary>Linha da planilha (para avisos).</summary>
    public int Linha { get; init; }

    public string Nome { get; init; } = "";

    public string? Portaria { get; init; }

    /// <summary>Contrato de receita SPOBRAS/SIURB (ex.: 015/SIURB/2025). Nao e o contrato da obra.</summary>
    public string? ContratoReceita { get; init; }

    /// <summary>Dotacao resumida (atividade), como na aba: 3364.</summary>
    public string? Dotacao { get; init; }

    public string? Lote { get; init; }

    public string? Modalidade { get; init; }

    /// <summary>So Em licitacao; vazia quando a licitacao ainda nao tem vencedora.</summary>
    public string? Empresa { get; init; }

    public string? Status { get; init; }

    public string? Processo { get; init; }

    /// <summary>Valor da obra, antes dos 10%: "Valor do Contrato com ADM" (Em licitacao) ou "Valor do Contrato" (A licitar).</summary>
    public decimal? ValorObra { get; init; }

    /// <summary>Valor com os 10% da planilha: "Recursos para 2027" (Em licitacao) ou "Valor com ADM" (A licitar).</summary>
    public decimal? ValorComRemuneracao { get; init; }

    /// <summary>Contrato de receita que nao e codigo ("Sem Contrato Receita", "Não tem").</summary>
    public bool SemContratoReceita => ContratoReceita is null || !ContratoReceita.Contains('/');

    /// <summary>Portaria como na Base: "Portaria 3" ou "Portaria 3 (2025)".</summary>
    public string? PortariaTexto => string.IsNullOrWhiteSpace(Portaria) ? null
        : Portaria.Trim().StartsWith("portaria", StringComparison.OrdinalIgnoreCase) ? Portaria.Trim() : "Portaria " + Portaria.Trim();

    public string NomeDaBase => BasesDeObras.Nome(Base);
}

/// <summary>Filtro das bases Em licitacao e A licitar (listas vazias = sem filtro).</summary>
public sealed record FiltroPrevistas
{
    public string Texto { get; init; } = "";
    public IReadOnlyList<string> Portaria { get; init; } = [];
    public IReadOnlyList<string> Lote { get; init; } = [];
    public IReadOnlyList<string> Modalidade { get; init; } = [];
    public IReadOnlyList<string> Dotacao { get; init; } = [];
    public IReadOnlyList<string> Status { get; init; } = [];
    public IReadOnlyList<string> Empresa { get; init; } = [];

    /// <summary>Texto da opcao de empresa vazia (licitacao ainda sem vencedora).</summary>
    public const string SemEmpresa = "Sem vencedora ainda";

    public bool Vazio => Texto.Trim().Length == 0 && Portaria.Count + Lote.Count + Modalidade.Count + Dotacao.Count + Status.Count + Empresa.Count == 0;

    public bool Aceita(ObraPrevista o)
    {
        static bool Em(IReadOnlyList<string> lista, string? valor) =>
            lista.Count == 0 || lista.Any(v => string.Equals(v.Trim(), (valor ?? "").Trim(), StringComparison.OrdinalIgnoreCase));

        if (!Em(Portaria, o.PortariaTexto) || !Em(Lote, o.Lote) || !Em(Modalidade, o.Modalidade) || !Em(Dotacao, o.Dotacao) || !Em(Status, o.Status))
        {
            return false;
        }

        if (Empresa.Count > 0 && !Empresa.Any(e => e == SemEmpresa ? string.IsNullOrWhiteSpace(o.Empresa)
                : string.Equals(e.Trim(), (o.Empresa ?? "").Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        string alvo = Normalizacao.Busca(Texto.Trim());
        return alvo.Length == 0 || Normalizacao.Busca(string.Join(' ', o.Nome, o.Lote, o.Modalidade, o.Empresa, o.Processo, o.ContratoReceita, o.PortariaTexto))
            .Contains(alvo, StringComparison.Ordinal);
    }
}

/// <summary>Contas e opcoes das bases previstas (iguais no computador e no celular).</summary>
public static class Previstas
{
    private static readonly StringComparer PtBr = StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("pt-BR"), true);

    public static IReadOnlyList<ObraPrevista> Filtrar(IEnumerable<ObraPrevista> obras, FiltroPrevistas filtro) =>
        [.. obras.Where(filtro.Aceita).OrderBy(o => ChaveDoLote(o.Lote)).ThenBy(o => o.Lote, PtBr).ThenBy(o => o.Nome, PtBr)];

    public static IReadOnlyList<string> Opcoes(IEnumerable<ObraPrevista> obras, Func<ObraPrevista, string?> campo) =>
        [.. obras.Select(campo).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Order(PtBr)];

    public static IReadOnlyList<string> Lotes(IEnumerable<ObraPrevista> obras) =>
        [.. Opcoes(obras, o => o.Lote).OrderBy(ChaveDoLote).ThenBy(l => l, PtBr)];

    /// <summary>Numero do lote para ordenar ("LOTE 012" = 12); sem numero vai para o fim.</summary>
    public static int ChaveDoLote(string? lote)
    {
        var m = System.Text.RegularExpressions.Regex.Match(lote ?? "", @"\d+");
        return m.Success && int.TryParse(m.Value, out int n) ? n : int.MaxValue;
    }

    public static (int Obras, decimal ValorObra, decimal ValorComRemuneracao) Totais(IEnumerable<ObraPrevista> obras)
    {
        var lista = obras as IReadOnlyCollection<ObraPrevista> ?? [.. obras];
        return (lista.Count, lista.Sum(o => o.ValorObra ?? 0m), lista.Sum(o => o.ValorComRemuneracao ?? 0m));
    }
}
