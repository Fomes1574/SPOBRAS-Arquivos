using System.Text.RegularExpressions;
using Spobras.Core.Base;

namespace Spobras.Core.Cronograma;

public enum Distribuicao
{
    /// <summary>Partes iguais em todos os meses.</summary>
    Linear,

    /// <summary>Comeca baixo, sobe ate o meio do prazo e desce de novo (pico sempre no meio).</summary>
    CurvaS,
}

public enum TipoAjuste
{
    /// <summary>Percentual do valor total da obra (obra + remuneracao) no mes.</summary>
    Percentual,

    /// <summary>Valor em reais no mes.</summary>
    Valor,
}

/// <summary>
/// Valor fixado pelo usuario num mes. Os meses sem ajuste dividem o que sobra em partes iguais.
/// <paramref name="Obra"/> e o indice da obra (0..); null vale para todas as obras que estao em execucao no mes
/// (um valor em reais para todas e repartido na proporcao do total de cada obra).
/// </summary>
public sealed record AjusteMensal(DateOnly Mes, TipoAjuste Tipo, decimal Quantia, int? Obra = null);

/// <summary>Dotacoes completas de uma obra: a do valor da obra e a da remuneracao.</summary>
public sealed record DotacoesDaObra(string Obra, string Remuneracao);

public sealed record ObraCalculada(
    ObraCronograma Obra,
    decimal ValorObra,
    decimal Remuneracao,
    IReadOnlyDictionary<DateOnly, decimal> Meses,
    DotacoesDaObra? Dotacoes)
{
    public decimal Total => ValorObra + Remuneracao;

    public decimal DoMes(DateOnly mes) => Meses.TryGetValue(mes, out decimal v) ? v : 0m;

    public decimal DoAno(int ano) => Meses.Where(m => m.Key.Year == ano).Sum(m => m.Value);
}

/// <summary>Uma linha da tabela por dotacao completa: soma das obras com a mesma dotacao.</summary>
public sealed record LinhaDotacao(string Tipo, string Dotacao, decimal Valor, IReadOnlyList<int> Obras, bool DaRemuneracao);

public sealed record CronogramaCalculado(
    IReadOnlyList<ObraCalculada> Obras,
    IReadOnlyList<DateOnly> Meses,
    IReadOnlyList<LinhaDotacao> Dotacoes,
    IReadOnlyList<AvisoDeLinha> AvisosDasObras,
    IReadOnlyList<Aviso> Avisos,
    decimal Percentual)
{
    public IReadOnlyList<int> Anos => [.. Meses.Select(m => m.Year).Distinct()];
    public decimal TotalObra => Obras.Sum(o => o.ValorObra);
    public decimal TotalRemuneracao => Obras.Sum(o => o.Remuneracao);
    public decimal Total => Obras.Sum(o => o.Total);
    public decimal DoMes(DateOnly mes) => Obras.Sum(o => o.DoMes(mes));
    public decimal DoAno(int ano) => Obras.Sum(o => o.DoAno(ano));

    public bool PodeGerar =>
        Obras.Count > 0
        && !AvisosDasObras.Any(a => a.Aviso.Gravidade == Gravidade.ImpedeGerar)
        && !Avisos.Any(a => a.Gravidade == Gravidade.ImpedeGerar);
}

/// <summary>
/// Cronograma feito pelo app, sem modelo: remuneracao sobre o valor da obra, distribuicao mensal do total,
/// ajustes por mes, totais por ano e a tabela por dotacao completa. Arredondamento sempre para cima, no centavo;
/// os centavos que sobram ficam no ultimo mes da obra.
/// </summary>
public static partial class CalculoCronograma
{
    public const decimal PadraoRemuneracao = 0.10m;

    public const string TipoObra = "Valor para Obra";
    public const string TipoRemuneracao = "Valor para Remuneração";

    /// <summary>Arredonda para cima no centavo (1,231 vira 1,24).</summary>
    public static decimal ParaCima(decimal valor) => Math.Ceiling(valor * 100m) / 100m;

    public static decimal Remuneracao(decimal valorObra, decimal percentual) => ParaCima(valorObra * percentual);

    /// <summary>Meses de inicio a fim, inclusive (dia 1 de cada mes).</summary>
    public static IReadOnlyList<DateOnly> MesesEntre(DateOnly inicio, DateOnly fim)
    {
        var meses = new List<DateOnly>();
        for (var m = new DateOnly(inicio.Year, inicio.Month, 1); m <= fim; m = m.AddMonths(1))
        {
            meses.Add(m);
        }

        return meses;
    }

    /// <summary>
    /// Peso de cada mes na curva S: 0,3 + seno de 0 a 180 graus, medido no meio de cada mes. Simetrico, com o
    /// primeiro e o ultimo mes menores (cerca de 10% cada em 6 meses) e o pico no meio. Linear: todos 1.
    /// </summary>
    public static IReadOnlyList<double> Pesos(int meses, Distribuicao distribuicao) =>
        [.. Enumerable.Range(0, meses).Select(i => distribuicao == Distribuicao.CurvaS ? 0.3 + Math.Sin(Math.PI * (i + 0.5) / meses) : 1.0)];

    public static CronogramaCalculado Calcular(
        IReadOnlyList<ObraCronograma> obras,
        decimal percentual,
        IReadOnlyList<AjusteMensal> ajustes,
        Func<ObraCronograma, (DotacoesDaObra? Dotacoes, string? Aviso)>? dotacoes = null,
        Distribuicao distribuicao = Distribuicao.Linear)
    {
        var avisosDasObras = new List<AvisoDeLinha>();
        var avisos = new List<Aviso>();
        var validas = new List<(int Linha, ObraCronograma Obra)>();
        for (int i = 0; i < obras.Count; i++)
        {
            var o = obras[i];
            int n = i + 1;
            if (o.Vazia)
            {
                avisosDasObras.Add(new AvisoDeLinha(n, Aviso.Info("Linha em branco: fica de fora.")));
                continue;
            }

            int antes = avisosDasObras.Count(a => a.Aviso.Gravidade == Gravidade.ImpedeGerar);
            void Impede(string texto) => avisosDasObras.Add(new AvisoDeLinha(n, Aviso.Impede(texto)));
            if (string.IsNullOrWhiteSpace(o.Nome))
            {
                Impede("Falta o nome da escola ou obra.");
            }

            if (o.Valor is null)
            {
                Impede("Falta o valor da obra.");
            }
            else if (o.Valor <= 0)
            {
                Impede("O valor da obra precisa ser maior que zero.");
            }

            if (o.Inicio is null || o.Fim is null)
            {
                Impede(o.Inicio is null && o.Fim is null ? "Faltam o início e o fim." : o.Inicio is null ? "Falta o início." : "Falta o fim.");
            }
            else if (o.Fim < o.Inicio)
            {
                Impede("O fim é anterior ao início.");
            }

            if (avisosDasObras.Count(a => a.Aviso.Gravidade == Gravidade.ImpedeGerar) == antes)
            {
                validas.Add((n, o));
            }
            else if (dotacoes?.Invoke(o).Aviso is { } avisoDaDotacao)
            {
                // Linha ainda incompleta: a dotacao errada ja aparece, sem esperar o resto.
                avisosDasObras.Add(new AvisoDeLinha(n, Aviso.Conferir(avisoDaDotacao)));
            }

            var mesDaOs = o.DataOsDaBase is { } os ? new DateOnly(os.Year, os.Month, 1) : (DateOnly?)null;
            if (o.OrigemDoInicio == OrigemInicio.Previsto)
            {
                avisosDasObras.Add(new AvisoDeLinha(n, Aviso.Info("Início previsto (a obra não tem Data da OS na base).")));
            }

            if (mesDaOs is { } mes && o.Inicio is { } inicio && inicio != mes)
            {
                avisosDasObras.Add(new AvisoDeLinha(n, Aviso.Conferir(
                    $"O início ({ColagemCronograma.Mes(inicio)}) é diferente do mês da Data da OS da base ({ColagemCronograma.Mes(mes)}). A base não é alterada.")));
            }
        }

        if (obras.All(o => o.Vazia))
        {
            avisos.Add(Aviso.Impede("Informe pelo menos uma obra."));
        }

        if (percentual < 0 || percentual > 1)
        {
            avisos.Add(Aviso.Impede("A remuneração precisa ficar entre 0 e 100%."));
        }

        // Totais antes da distribuicao (o ajuste "valor para todas" reparte pelo total de cada obra).
        var totais = validas.Select(v => v.Obra.Valor!.Value + Remuneracao(v.Obra.Valor!.Value, percentual)).ToList();
        var calculadas = new List<ObraCalculada>();
        for (int k = 0; k < validas.Count; k++)
        {
            var (linha, o) = validas[k];
            int indice = linha - 1;
            decimal valor = o.Valor!.Value, rem = Remuneracao(valor, percentual), total = valor + rem;
            var meses = MesesEntre(o.Inicio!.Value, o.Fim!.Value);

            // Valor fixado em cada mes (o ajuste da propria obra vale mais que o de todas).
            var fixos = new Dictionary<DateOnly, decimal>();
            foreach (var a in ajustes.Where(a => a.Obra is null).Concat(ajustes.Where(a => a.Obra == indice)))
            {
                if (!meses.Contains(a.Mes))
                {
                    continue;
                }

                decimal quantia;
                if (a.Tipo == TipoAjuste.Percentual)
                {
                    quantia = ParaCima(total * a.Quantia / 100m);
                }
                else if (a.Obra is not null)
                {
                    quantia = a.Quantia;
                }
                else
                {
                    // Valor para todas: cada obra em execucao no mes leva a sua parte, pelo tamanho do total.
                    decimal soma = 0;
                    for (int j = 0; j < validas.Count; j++)
                    {
                        if (validas[j].Obra.Inicio <= a.Mes && a.Mes <= validas[j].Obra.Fim)
                        {
                            soma += totais[j];
                        }
                    }

                    quantia = soma == 0 ? 0 : ParaCima(a.Quantia * total / soma);
                }

                fixos[a.Mes] = quantia;
            }

            var porMes = new Dictionary<DateOnly, decimal>();
            decimal fixado = fixos.Values.Sum();
            var livres = meses.Where(m => !fixos.ContainsKey(m)).ToList();
            void Impede(string texto) => avisosDasObras.Add(new AvisoDeLinha(linha, Aviso.Impede(texto)));
            if (fixado > total)
            {
                Impede($"Os ajustes somam {FormatoValor(fixado)}, mais que o total da obra ({FormatoValor(total)}).");
            }

            foreach (var m in meses)
            {
                if (fixos.TryGetValue(m, out decimal v))
                {
                    porMes[m] = v;
                }
            }

            decimal resto = total - fixado;
            if (livres.Count == 0)
            {
                if (resto != 0 && fixado <= total)
                {
                    // Todos os meses fixados: a diferenca fica no ultimo mes, para o total fechar.
                    porMes[meses[^1]] += resto;
                    avisosDasObras.Add(new AvisoDeLinha(linha, Aviso.Conferir(
                        $"Todos os meses têm ajuste; a diferença de {FormatoValor(resto)} foi para {ColagemCronograma.Mes(meses[^1])} para fechar o total.")));
                }
            }
            else if (resto >= 0)
            {
                // O que nao foi fixado divide-se pelos meses livres conforme a distribuicao (linear: partes iguais;
                // curva S: o peso de cada mes na curva da obra inteira). Para cima no centavo; a sobra fica no ultimo.
                var pesos = Pesos(meses.Count, o.Distribuicao ?? distribuicao);
                var dosLivres = livres.Select(m => (decimal)pesos[meses.ToList().IndexOf(m)]).ToList();
                decimal soma = dosLivres.Sum();
                var partes = dosLivres.Select(p => ParaCima(resto * p / soma)).ToList();
                if (resto - partes.Take(livres.Count - 1).Sum() < 0)
                {
                    // Valor muito pequeno para tantos meses: arredondar para cima esgotaria o total antes do fim.
                    partes = [.. dosLivres.Select(p => Math.Floor(resto * p / soma * 100m) / 100m)];
                }

                for (int j = 0; j < livres.Count; j++)
                {
                    porMes[livres[j]] = j == livres.Count - 1 ? resto - partes.Take(livres.Count - 1).Sum() : partes[j];
                }
            }

            calculadas.Add(new ObraCalculada(o, valor, rem,
                meses.ToDictionary(m => m, m => porMes.GetValueOrDefault(m)), null));
        }

        // Dotacoes completas e a tabela por dotacao (iguais somadas; obra e remuneracao separadas).
        var linhasDotacao = new List<LinhaDotacao>();
        var semDotacao = new List<string>();
        for (int k = 0; k < calculadas.Count; k++)
        {
            var o = calculadas[k];
            var (d, aviso) = dotacoes?.Invoke(o.Obra) ?? (Direta(o.Obra.Dotacao), null);
            calculadas[k] = o with { Dotacoes = d };
            if (aviso is not null)
            {
                avisosDasObras.Add(new AvisoDeLinha(validas[k].Linha, Aviso.Conferir(aviso)));
            }

            if (d is null)
            {
                semDotacao.Add(o.Obra.Nome);
            }
        }

        foreach (var grupo in calculadas.Select((o, i) => (o, i)).Where(x => x.o.Dotacoes is not null).GroupBy(x => x.o.Dotacoes!.Obra))
        {
            linhasDotacao.Add(new LinhaDotacao(TipoObra, grupo.Key, grupo.Sum(x => x.o.ValorObra), [.. grupo.Select(x => x.i)], false));
            foreach (var rem in grupo.GroupBy(x => x.o.Dotacoes!.Remuneracao))
            {
                linhasDotacao.Add(new LinhaDotacao(TipoRemuneracao, rem.Key, rem.Sum(x => x.o.Remuneracao), [.. rem.Select(x => x.i)], true));
            }
        }

        if (semDotacao.Count > 0)
        {
            avisos.Add(Aviso.Conferir($"Sem dotação completa, fora da tabela por dotação: {string.Join(", ", semDotacao)}."));
        }

        var todosOsMeses = calculadas.SelectMany(o => o.Meses.Keys).Distinct().Order().ToList();
        if (todosOsMeses.Count > 0)
        {
            todosOsMeses = [.. MesesEntre(todosOsMeses[0], todosOsMeses[^1])];
        }

        foreach (var a in ajustes)
        {
            if (!calculadas.Any(o => o.Meses.ContainsKey(a.Mes) && (a.Obra is null || ReferenceEquals(o.Obra, obras.ElementAtOrDefault(a.Obra.Value)))))
            {
                avisos.Add(Aviso.Conferir($"O ajuste de {ColagemCronograma.Mes(a.Mes)} não cai dentro do período de nenhuma obra e não foi usado."));
            }
        }

        return new CronogramaCalculado(calculadas, todosOsMeses, linhasDotacao, avisosDasObras, avisos, percentual);
    }

    private static string FormatoValor(decimal v) => "R$ " + v.ToString("N2", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));

    /// <summary>Dotacoes em que a 3900 serve a obra e a remuneracao (decisao do usuario).</summary>
    public static readonly IReadOnlySet<string> MesmaDotacaoParaTudo = new HashSet<string> { "2830", "2878", "2879" };

    /// <summary>
    /// Regra do usuario: a obra termina sempre em 5100 e a remuneracao em 3900 (o mesmo numero, so muda o
    /// elemento), menos 2830, 2878 e 2879, em que a 3900 serve as duas. Uma completa da obra escrita com
    /// 3900 (como a 3363 na planilha) vira 5100 para a obra.
    /// </summary>
    public static DotacoesDaObra Par(string completa)
    {
        string[] partes = completa.Split('.');
        if (partes.Length < 9 || MesmaDotacaoParaTudo.Contains(partes[5]) || partes[7] is not ("5100" or "3900"))
        {
            return new DotacoesDaObra(completa, completa);
        }

        string Com(string elemento) => string.Join('.', partes.Select((p, i) => i == 7 ? elemento : p));
        return new DotacoesDaObra(Com("5100"), Com("3900"));
    }

    /// <summary>Dotacao da remuneracao pela regra (ver <see cref="Par"/>).</summary>
    public static string DotacaoDaRemuneracao(string completa) => Par(completa).Remuneracao;

    /// <summary>Dotacao resumida (3360) de uma completa (16.10.12.365.3010.3360.4490.5100.00).</summary>
    public static string? Resumida(string completa)
    {
        string[] partes = completa.Split('.');
        return partes.Length >= 7 ? partes[5] : null;
    }

    /// <summary>Quando o usuario digitou a dotacao completa na propria linha.</summary>
    private static DotacoesDaObra? Direta(string texto)
    {
        if (!PareceCompleta().IsMatch(texto.Trim()))
        {
            return null;
        }

        return Par(Normalizacao.Dotacao(texto.Trim()).Completa);
    }

    /// <summary>
    /// Acha a dotacao completa pela resumida (ex.: 3360) nos vinculos da base de dotacoes. Se houver mais de
    /// uma, fica a da mesma portaria da obra; ainda empatado, a primeira, com aviso.
    /// </summary>
    public static (DotacoesDaObra? Dotacoes, string? Aviso) Resolver(ObraCronograma obra, IReadOnlyList<VinculoBase> vinculos)
    {
        string texto = obra.Dotacao.Trim();
        if (texto.Length == 0)
        {
            return (null, null);
        }

        if (Direta(texto) is { } direta)
        {
            return (direta, null);
        }

        var candidatos = vinculos
            .Where(v => string.Equals(v.DotacaoResumida?.Trim(), texto, StringComparison.OrdinalIgnoreCase) || Resumida(v.DotacaoCompleta) == texto)
            .ToList();
        if (candidatos.Count == 0)
        {
            return (null, $"A dotação {texto} não está na base de dotações: confira o número.");
        }

        var completas = candidatos.Select(v => v.DotacaoCompleta).Distinct().ToList();
        if (completas.Count > 1 && Normalizacao.Portaria("Portaria " + Regex.Replace(obra.Portaria, @"^N[ºo°.]?\s*", "", RegexOptions.IgnoreCase)).Numero is { } numero)
        {
            var daPortaria = candidatos.Where(v => Normalizacao.Portaria(v.Portaria).Numero == numero).Select(v => v.DotacaoCompleta).Distinct().ToList();
            if (daPortaria.Count > 0)
            {
                completas = daPortaria;
            }
        }

        string escolhida = completas[0];
        string? aviso = completas.Count > 1
            ? $"A dotação {texto} tem {completas.Count} dotações completas na base; ficou {escolhida}. Para outra, digite a completa."
            : null;
        var par = Par(escolhida);
        if (par.Obra != escolhida)
        {
            aviso = (aviso is null ? "" : aviso + " ") + $"Na base a dotação da obra termina em 3900; pela regra, a obra usa {par.Obra} e a remuneração {par.Remuneracao}.";
        }

        return (par, aviso);
    }

    [GeneratedRegex(@"^\d{2}\.\d{2}\.\d{2}\.\d{3}\.\d{4}\.\d{4}\.\d{4}[.\s]\d{4}[.\s]\d{2}$")]
    private static partial Regex PareceCompleta();
}
