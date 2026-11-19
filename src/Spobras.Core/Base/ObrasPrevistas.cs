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

/// <summary>Ficha e exportacao das bases sem contrato: so os campos que cada aba tem, com nomes claros.</summary>
public static class FichaPrevista
{
    private static readonly System.Globalization.CultureInfo PtBr = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");

    public static string Reais(decimal? v) => v is { } x ? x.ToString("C2", PtBr) : "Sem informação";

    /// <summary>"015/SIURB/2025 (Portaria 3)": o contrato de receita, separado do contrato da obra.</summary>
    public static string ContratoReceita(ObraPrevista o) =>
        o.ContratoReceita is null ? "Sem informação"
        : o.SemContratoReceita ? o.ContratoReceita
        : o.ContratoReceita + (o.PortariaTexto is { } p ? $" ({p})" : "");

    public static string Empresa(ObraPrevista o) => string.IsNullOrWhiteSpace(o.Empresa) ? FiltroPrevistas.SemEmpresa : o.Empresa!;

    /// <summary>Segunda linha da lista: lote, modalidade e situacao (e a empresa, na licitacao).</summary>
    public static string Detalhe(ObraPrevista o) => string.Join(" · ", new[]
    {
        o.Lote ?? "sem lote", o.Modalidade, o.Status,
        o.Base == BaseDeObras.EmLicitacao ? Empresa(o) : null,
    }.Where(v => !string.IsNullOrWhiteSpace(v)));

    public static IReadOnlyList<KeyValuePair<string, string>> Campos(ObraPrevista o)
    {
        var c = new List<KeyValuePair<string, string>>();
        void Add(string rotulo, string? valor) => c.Add(new(rotulo, string.IsNullOrWhiteSpace(valor) ? "Sem informação" : valor!));
        Add("Situação", o.Status);
        if (o.Base == BaseDeObras.EmLicitacao)
        {
            Add("Empresa", Empresa(o));
        }

        Add(o.Base == BaseDeObras.ALicitar ? "Valor da obra (estimado)" : "Valor da obra", Reais(o.ValorObra));
        Add("Com os 10% (recursos)", Reais(o.ValorComRemuneracao));
        Add(o.Base == BaseDeObras.ALicitar ? "Lote ou agrupamento" : "Lote", o.Lote);
        Add("Modalidade", o.Modalidade);
        Add("Portaria", o.PortariaTexto);
        Add("Dotação", o.Dotacao);
        Add("Contrato de receita", ContratoReceita(o));
        Add("Processo SEI", o.Processo);
        return c;
    }

    /// <summary>Colunas da exportacao de cada base (modelo proprio, so com o que a base tem).</summary>
    public static IReadOnlyList<string> Titulos(BaseDeObras qual) => qual == BaseDeObras.EmLicitacao
        ? ["Nome da obra", "Portaria", "Contrato de receita", "Dotação", "Lote", "Modalidade", "Empresa", "Situação", "Valor da obra (antes dos 10%)", "Com os 10% (recursos)", "Processo SEI"]
        : ["Nome da obra", "Portaria", "Contrato de receita", "Dotação", "Lote ou agrupamento", "Modalidade", "Processo SEI", "Situação", "Valor da obra (estimado, antes dos 10%)", "Com os 10%"];

    public static object?[] Linha(ObraPrevista o) => o.Base == BaseDeObras.EmLicitacao
        ? [o.Nome, o.PortariaTexto, o.ContratoReceita, o.Dotacao, o.Lote, o.Modalidade, o.Empresa ?? FiltroPrevistas.SemEmpresa, o.Status, o.ValorObra, o.ValorComRemuneracao, o.Processo]
        : [o.Nome, o.PortariaTexto, o.ContratoReceita, o.Dotacao, o.Lote, o.Modalidade, o.Processo, o.Status, o.ValorObra, o.ValorComRemuneracao];
}

/// <summary>
/// Grupo de obras sem contrato para o cronograma: sempre base + lote + modalidade (o mesmo numero de lote pode ser
/// outra obra em outra base ou com outra modalidade, ex.: LOTE 64 Manutencao contratado e LOTE 64 Manutencao CEU em licitacao).
/// </summary>
public sealed record GrupoPrevisto(BaseDeObras Base, string? Lote, string? Modalidade, IReadOnlyList<ObraPrevista> Obras)
{
    public string Titulo => string.Join(" · ", new[] { Lote ?? "Sem lote", Modalidade }.Where(v => !string.IsNullOrWhiteSpace(v)));

    public string Detalhe => string.Join(" · ", new[]
    {
        string.Join(", ", Obras.Select(o => o.Status).Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.OrdinalIgnoreCase)),
        Obras.Count == 1 ? Obras[0].Nome : $"{Obras.Count} escolas",
    }.Where(v => v.Length > 0));

    public static IReadOnlyList<GrupoPrevisto> Montar(IEnumerable<ObraPrevista> obras, string texto, int maximo = 20)
    {
        string alvo = Normalizacao.Busca(texto.Trim());
        if (alvo.Length == 0)
        {
            return [];
        }

        static string Chave(string? v) => Normalizacao.Busca((v ?? "").Trim());
        return [.. obras
            .GroupBy(o => (o.Base, Lote: Chave(o.Lote), Modalidade: Chave(o.Modalidade)))
            .Select(g => new GrupoPrevisto(g.Key.Base, g.First().Lote, g.First().Modalidade, [.. g]))
            .Select(g => (g, nota: Nota(g, alvo)))
            .Where(x => x.nota < 3)
            .OrderBy(x => x.nota).ThenBy(x => x.g.Base).ThenBy(x => Previstas.ChaveDoLote(x.g.Lote)).ThenBy(x => x.g.Titulo, StringComparer.OrdinalIgnoreCase)
            .Take(maximo).Select(x => x.g)];
    }

    // 0: o lote comeca pelo texto (com ou sem a palavra LOTE); 1: lote ou modalidade contem; 2: alguma escola do grupo contem.
    private static int Nota(GrupoPrevisto g, string alvo)
    {
        string lote = Normalizacao.Busca(g.Lote ?? "");
        string numero = System.Text.RegularExpressions.Regex.Replace(lote, @"^lote\s*", "");
        string alvoSemLote = System.Text.RegularExpressions.Regex.Replace(alvo, @"^lote\s*", "");
        if (lote.StartsWith(alvo, StringComparison.Ordinal) || (alvoSemLote.Length > 0 && numero.TrimStart('0').StartsWith(alvoSemLote.TrimStart('0'), StringComparison.Ordinal) && alvoSemLote.TrimStart('0').Length > 0))
        {
            return 0;
        }

        if (lote.Contains(alvo, StringComparison.Ordinal) || Normalizacao.Busca(g.Modalidade ?? "").Contains(alvo, StringComparison.Ordinal))
        {
            return 1;
        }

        return g.Obras.Any(o => Normalizacao.Busca(o.Nome).Contains(alvo, StringComparison.Ordinal)) ? 2 : 3;
    }
}

/// <summary>Obra sem contrato no formato do cronograma.</summary>
public static class PrevistaNoCronograma
{
    /// <summary>
    /// Portaria "Nº 03 (2021)". A aba traz so o numero ("3") ou numero e ano ("3 (2025)"); sem ano, o ano vem da base de
    /// dotacoes: a portaria com esse numero ligada ao mesmo contrato de receita. Se a base nao decidir, fica so "Nº 03".
    /// </summary>
    public static string Portaria(ObraPrevista o, IReadOnlyList<VinculoBase> vinculos)
    {
        string texto = Cronograma.ColagemCronograma.Portaria(o.Portaria ?? "");
        var lida = Normalizacao.Portaria("Portaria " + System.Text.RegularExpressions.Regex.Replace(texto, @"^N[ºo°.]?\s*", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase));
        if (lida.Numero is not { } numero || lida.Ano is not null)
        {
            return texto;
        }

        var doNumero = vinculos.Select(v => (v, p: Normalizacao.Portaria(v.Portaria))).Where(x => x.p.Numero == numero && x.p.Ano is not null).ToList();
        var anos = doNumero.Where(x => string.Equals(x.v.ContratoReceita.Trim(), (o.ContratoReceita ?? "").Trim(), StringComparison.OrdinalIgnoreCase))
            .Select(x => x.p.Ano!.Value).Distinct().ToList();
        if (anos.Count == 0)
        {
            anos = [.. doNumero.Select(x => x.p.Ano!.Value).Distinct()];
        }

        return anos.Count == 1 ? $"Nº {numero:00} ({anos[0]})" : texto;
    }

    /// <summary>Lote como no cronograma: sem a palavra LOTE ("LOTE 64" = "64"); agrupamentos ficam inteiros.</summary>
    public static string Lote(string? lote)
    {
        string l = (lote ?? "").Trim();
        return l.StartsWith("LOTE ", StringComparison.OrdinalIgnoreCase) ? l["LOTE ".Length..].Trim() : l;
    }
}
