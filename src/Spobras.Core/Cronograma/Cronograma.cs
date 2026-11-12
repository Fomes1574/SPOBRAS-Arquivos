using System.Globalization;
using Spobras.Core.Base;

namespace Spobras.Core.Cronograma;

public enum OrigemInicio
{
    /// <summary>Digitado pelo usuario.</summary>
    Informado,

    /// <summary>Data da Ordem de Servico vinda da base.</summary>
    OrdemDeServico,

    /// <summary>Sem OS: data planejada informada pelo usuario.</summary>
    Previsto,
}

/// <summary>Uma linha do cronograma (linhas 3 a 17 do modelo). Tudo como o usuario informou; nada e calculado aqui.</summary>
public sealed record ObraCronograma
{
    public string Portaria { get; init; } = "";
    public string Lote { get; init; } = "";

    /// <summary>Dotacao resumida (ex.: 3362), como a coluna Dotação do modelo espera.</summary>
    public string Dotacao { get; init; } = "";
    public string Nome { get; init; } = "";
    public string Escopo { get; init; } = "";
    public decimal? Valor { get; init; }
    public DateOnly? Inicio { get; init; }
    public DateOnly? Fim { get; init; }
    public OrigemInicio OrigemDoInicio { get; init; }

    /// <summary>Data da OS que a base tem para esta obra, se a linha veio da base (para mostrar divergencia).</summary>
    public DateOnly? DataOsDaBase { get; init; }

    public bool Vazia =>
        Portaria.Length == 0 && Lote.Length == 0 && Dotacao.Length == 0 && Nome.Length == 0 && Escopo.Length == 0
        && Valor is null && Inicio is null && Fim is null;
}

public sealed record AvisoDeLinha(int Linha, Aviso Aviso);

public sealed record CronogramaValidado(IReadOnlyList<AvisoDeLinha> Linhas, IReadOnlyList<Aviso> Gerais)
{
    public bool PodeGerar => !Linhas.Any(l => l.Aviso.Gravidade == Gravidade.ImpedeGerar) && !Gerais.Any(a => a.Gravidade == Gravidade.ImpedeGerar);
}

/// <summary>
/// Conferencia das linhas antes de gerar (especificacao 4.5). So confere as entradas: a distribuicao mensal,
/// a remuneracao e os totais continuam sendo calculados pelas formulas do modelo.
/// </summary>
public static class ValidadorCronograma
{
    public const int ObrasPorAba = 15;

    /// <summary>O modelo tem 20 colunas de meses (L a AE), a partir do mes do primeiro inicio.</summary>
    public const int MesesDoModelo = 20;

    /// <param name="dotacoesDoModelo">Dotacoes resumidas que existem na aba Dotacoes do modelo.</param>
    public static CronogramaValidado Validar(IReadOnlyList<ObraCronograma> obras, IReadOnlyCollection<string> dotacoesDoModelo)
    {
        var linhas = new List<AvisoDeLinha>();
        var gerais = new List<Aviso>();
        if (obras.Count == 0 || obras.All(o => o.Vazia))
        {
            gerais.Add(Aviso.Impede("Informe pelo menos uma obra."));
            return new CronogramaValidado(linhas, gerais);
        }

        var primeiroInicio = obras.Where(o => o.Inicio is not null).Select(o => o.Inicio!.Value).DefaultIfEmpty().Min();
        for (int i = 0; i < obras.Count; i++)
        {
            var o = obras[i];
            int n = i + 1;
            void Impede(string texto) => linhas.Add(new AvisoDeLinha(n, Aviso.Impede(texto)));
            void Conferir(string texto) => linhas.Add(new AvisoDeLinha(n, Aviso.Conferir(texto)));
            void Info(string texto) => linhas.Add(new AvisoDeLinha(n, Aviso.Info(texto)));

            if (o.Vazia)
            {
                Info("Linha em branco: fica de fora.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(o.Nome))
            {
                Impede("Falta o nome da obra (o modelo só considera linhas com nome).");
            }

            if (o.Valor is null)
            {
                Impede("Falta o valor para obras.");
            }
            else if (o.Valor <= 0)
            {
                Conferir("Valor zero ou negativo: o modelo aceita, mas confira se é isso mesmo.");
            }

            if (o.Inicio is null || o.Fim is null)
            {
                Impede(o.Inicio is null && o.Fim is null ? "Faltam o início e o fim." : o.Inicio is null ? "Falta o início." : "Falta o fim.");
            }
            else if (o.Fim < o.Inicio)
            {
                Impede("O fim é anterior ao início.");
            }
            else if (Meses(primeiroInicio, o.Fim.Value) > MesesDoModelo)
            {
                Impede($"O fim passa dos {MesesDoModelo} meses que o modelo mostra a partir do primeiro início ({primeiroInicio:MM/yyyy}).");
            }

            if (string.IsNullOrWhiteSpace(o.Dotacao))
            {
                Conferir("Sem dotação: a obra não entra no Resumo pedido do modelo.");
            }
            else if (dotacoesDoModelo.Count > 0 && !dotacoesDoModelo.Contains(o.Dotacao.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                Impede($"A dotação {o.Dotacao.Trim()} não existe na aba Dotacoes do modelo.");
            }

            switch (o.OrigemDoInicio)
            {
                case OrigemInicio.OrdemDeServico when o.DataOsDaBase is not null && o.Inicio == o.DataOsDaBase:
                    Info("Início = Data da OS da base.");
                    break;
                case OrigemInicio.Previsto:
                    Info("Início previsto (a obra não tem Data da OS na base).");
                    break;
            }

            if (o.DataOsDaBase is { } os && o.Inicio is { } inicio && inicio != os)
            {
                Conferir($"O início ({inicio:dd/MM/yyyy}) é diferente da Data da OS da base ({os:dd/MM/yyyy}). A base não é alterada.");
            }
        }

        int validas = obras.Count(o => !o.Vazia);
        if (validas > ObrasPorAba)
        {
            int partes = (validas + ObrasPorAba - 1) / ObrasPorAba;
            gerais.Add(Aviso.Conferir(
                $"São {validas} obras e o modelo comporta {ObrasPorAba} por cronograma. Serão gerados {partes} cronogramas, na ordem da lista; nenhuma obra fica de fora."));
        }

        return new CronogramaValidado(linhas, gerais);
    }

    /// <summary>Divide as obras em grupos de ate 15, na ordem, sem descartar nenhuma.</summary>
    public static IReadOnlyList<IReadOnlyList<ObraCronograma>> Dividir(IReadOnlyList<ObraCronograma> obras) =>
        [.. obras.Where(o => !o.Vazia).Chunk(ObrasPorAba).Select(g => (IReadOnlyList<ObraCronograma>)g)];

    /// <summary>Soma dos valores para obras (a conferencia compara com o TOTAL que o Excel calcular).</summary>
    public static decimal TotalParaObras(IEnumerable<ObraCronograma> obras) => obras.Sum(o => o.Valor ?? 0m);

    private static int Meses(DateOnly de, DateOnly ate) => (ate.Year - de.Year) * 12 + ate.Month - de.Month + 1;
}

public sealed record ColagemLida(IReadOnlyList<ObraCronograma> Obras, IReadOnlyList<string> Avisos);

/// <summary>
/// Le linhas coladas do Excel (colunas separadas por tabulacao), na ordem do modelo:
/// Portaria, Lote, Dotação, Nome da Obra, Breve escopo, Valor para Obras, Início, Fim.
/// </summary>
public static class ColagemCronograma
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly string[] Meses = ["jan", "fev", "mar", "abr", "mai", "jun", "jul", "ago", "set", "out", "nov", "dez"];

    public static ColagemLida Ler(string texto)
    {
        var obras = new List<ObraCronograma>();
        var avisos = new List<string>();
        int numero = 0;
        foreach (string bruta in texto.Replace("\r", "").Split('\n'))
        {
            numero++;
            if (string.IsNullOrWhiteSpace(bruta))
            {
                continue;
            }

            string[] c = bruta.Split('\t');
            string Col(int i) => i < c.Length ? c[i].Trim() : "";

            // Linha de cabecalho copiada junto: ignorada, com aviso.
            if (Col(3).Equals("Nome da Obra", StringComparison.OrdinalIgnoreCase) || Col(0).StartsWith("Portaria", StringComparison.OrdinalIgnoreCase) && Col(5).StartsWith("Valor", StringComparison.OrdinalIgnoreCase))
            {
                avisos.Add($"Linha {numero}: é o cabeçalho e foi ignorada.");
                continue;
            }

            if (c.Length < 4)
            {
                avisos.Add($"Linha {numero}: tem menos de 4 colunas e foi ignorada (esperado: Portaria, Lote, Dotação, Nome da Obra, Breve escopo, Valor, Início, Fim).");
                continue;
            }

            decimal? valor = Normalizacao.LerReais(Col(5));
            if (Col(5).Length > 0 && valor is null)
            {
                avisos.Add($"Linha {numero}: o valor \"{Col(5)}\" não foi entendido e ficou em branco.");
            }

            var inicio = Data(Col(6));
            var fim = Data(Col(7));
            if (Col(6).Length > 0 && inicio is null)
            {
                avisos.Add($"Linha {numero}: o início \"{Col(6)}\" não foi entendido e ficou em branco.");
            }

            if (Col(7).Length > 0 && fim is null)
            {
                avisos.Add($"Linha {numero}: o fim \"{Col(7)}\" não foi entendido e ficou em branco.");
            }

            obras.Add(new ObraCronograma
            {
                Portaria = Col(0), Lote = Col(1), Dotacao = Col(2), Nome = Col(3), Escopo = Col(4), Valor = valor, Inicio = inicio, Fim = fim,
            });
        }

        return new ColagemLida(obras, avisos);
    }

    /// <summary>Aceita 10/11/2026, 11/2026 e nov/26 (como o modelo mostra): mes e ano viram o dia 1.</summary>
    public static DateOnly? Data(string texto)
    {
        texto = texto.Trim();
        if (texto.Length == 0)
        {
            return null;
        }

        if (DateOnly.TryParseExact(texto, ["d/M/yyyy", "d/M/yy"], PtBr, DateTimeStyles.None, out var dia))
        {
            return dia;
        }

        if (DateTime.TryParseExact(texto, ["M/yyyy", "MM/yyyy"], PtBr, DateTimeStyles.None, out var mes))
        {
            return DateOnly.FromDateTime(mes);
        }

        // "nov/26", "nov-2026", "nov./26": abreviacao do mes como o modelo mostra.
        string[] partes = texto.ToLowerInvariant().Replace(".", "").Split('/', '-');
        int indice = partes.Length == 2 && partes[0].Length >= 3 ? Array.IndexOf(Meses, partes[0][..3]) : -1;
        if (indice >= 0 && int.TryParse(partes[1], NumberStyles.None, CultureInfo.InvariantCulture, out int ano) && partes[1].Length is 2 or 4)
        {
            return new DateOnly(partes[1].Length == 2 ? 2000 + ano : ano, indice + 1, 1);
        }

        return null;
    }
}

/// <summary>
/// Le obras de uma planilha qualquer (grade de celulas ja lida): acha a linha de titulos pelos nomes das
/// colunas do modelo e monta o mesmo texto que a colagem do Excel, na ordem do modelo.
/// </summary>
public static class ImportacaoCronograma
{
    private static readonly (string Campo, string[] Titulos)[] Colunas =
    [
        ("Portaria", ["portaria"]),
        ("Lote", ["lote"]),
        ("Dotação", ["dotacao", "dotação"]),
        ("Nome da Obra", ["nome da obra", "obra", "escola", "empreendimento", "unidade"]),
        ("Breve escopo", ["breve escopo", "escopo", "servico", "serviço"]),
        ("Valor", ["valores para obras", "valor para obras", "valor da obra", "valor"]),
        ("Início", ["inicio", "início", "data de inicio", "data da os", "inicio execucao"]),
        ("Fim", ["fim", "termino", "término", "data de termino"]),
    ];

    /// <summary>Texto no formato da colagem (sem a linha de titulos), ou o motivo de nao ter achado.</summary>
    public static (string? Texto, string? Problema) ParaColagem(IReadOnlyList<object?[]> grade)
    {
        for (int r = 0; r < Math.Min(grade.Count, 30); r++)
        {
            var titulos = grade[r].Select(c => Base.Normalizacao.Busca(c?.ToString()?.Trim() ?? "")).ToList();
            var posicoes = Colunas.Select(col => titulos.FindIndex(t => t.Length > 0 && col.Titulos.Any(n => t == Base.Normalizacao.Busca(n)))).ToArray();
            if (posicoes[3] < 0 || posicoes.Count(p => p >= 0) < 3)
            {
                continue; // precisa pelo menos do nome da obra e de mais duas colunas
            }

            var linhas = new List<string>();
            foreach (var linha in grade.Skip(r + 1))
            {
                if (linha.All(c => c is null || c.ToString()!.Trim().Length == 0))
                {
                    continue;
                }

                linhas.Add(string.Join('\t', posicoes.Select(p => p >= 0 && p < linha.Length ? Texto(linha[p]) : "")));
            }

            return linhas.Count == 0 ? (null, "A planilha tem os títulos, mas nenhuma linha de obra embaixo deles.") : (string.Join('\n', linhas), null);
        }

        return (null, "Não achei a linha de títulos (por exemplo “Nome da Obra”, “Portaria”, “Valor”, “Início”, “Fim”) nas primeiras 30 linhas da primeira aba.");
    }

    private static string Texto(object? valor) => valor switch
    {
        null => "",
        DateTime d => d.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture),
        decimal m => m.ToString("0.00", System.Globalization.CultureInfo.GetCultureInfo("pt-BR")),
        _ => valor.ToString()!.Trim(),
    };
}
