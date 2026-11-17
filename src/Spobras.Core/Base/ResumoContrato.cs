using System.Globalization;

namespace Spobras.Core.Base;

public sealed record SeloContrato(string Texto, string Fundo, string Cor)
{
    public static SeloContrato Verde(string texto) => new(texto, "#E7F3EA", "#1E7B34");
    public static SeloContrato Laranja(string texto) => new(texto, "#FBF0E4", "#8A4300");
    public static SeloContrato Vermelho(string texto) => new(texto, "#FBE9EC", "#A50D26");
    public static SeloContrato Cinza(string texto) => new(texto, "#EDEDEA", "#3D3D42");
}

public static class PrazoContrato
{
    private static readonly CultureInfo Brasil = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly HashSet<string> Encerrados =
    [
        "encerrado / trd", "concluido", "trp assinado", "rescindido", "rescisao em tramite",
        "em encerramento", "trd em tramitacao",
    ];

    public static SeloContrato Status(string? status)
    {
        string original = status?.Trim() ?? "";
        string texto = original.Length == 0 ? "Sem status" : string.Join(" ", original.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select((p, i) => p is "TRD" or "TRP" or "SIURB" ? p : i > 0 && Normalizacao.Busca(p) is "em" or "de" or "da" or "do"
                ? p.ToLower(Brasil) : Brasil.TextInfo.ToTitleCase(p.ToLower(Brasil))));
        return Normalizacao.Busca(original) switch
        {
            "vigente" => SeloContrato.Verde(texto),
            "vencido" => SeloContrato.Vermelho(texto),
            _ => SeloContrato.Cinza(texto),
        };
    }

    public static SeloContrato Prazo(ObraLida obra, DateOnly hoje)
    {
        if (obra.DataOs is null) return SeloContrato.Cinza("OS não emitida");
        if (obra.Termino is not { } fim) return SeloContrato.Cinza("Sem término informado");
        if (Encerrados.Contains(Normalizacao.Busca(obra.StatusContrato))) return SeloContrato.Cinza($"Encerrado em {fim:dd/MM/yyyy}");

        int dias = fim.DayNumber - hoje.DayNumber;
        return dias switch
        {
            < 0 => SeloContrato.Vermelho("Venceu há " + Intervalo(fim, hoje)),
            0 => SeloContrato.Laranja("Vence hoje"),
            <= 90 => SeloContrato.Laranja("Vence em " + Intervalo(hoje, fim)),
            _ => SeloContrato.Verde("Vence em " + Intervalo(hoje, fim)),
        };
    }

    private static string Intervalo(DateOnly inicio, DateOnly fim)
    {
        int meses = (fim.Year - inicio.Year) * 12 + fim.Month - inicio.Month;
        if (inicio.AddMonths(meses) > fim) meses--;
        int dias = fim.DayNumber - inicio.AddMonths(meses).DayNumber;
        string m = meses == 1 ? "1 mês" : $"{meses} meses";
        string d = dias == 1 ? "1 dia" : $"{dias} dias";
        return meses == 0 ? d : dias == 0 ? m : $"{m} e {d}";
    }
}

public sealed record ValoresResumo(decimal Original, decimal Aditado, decimal Contratado, decimal Medido,
    decimal SaldoAMedir, decimal Pago, decimal Tramite, decimal SaldoAPagar, int Medicoes)
{
    public decimal SemMedicao => Math.Max(0, SaldoAPagar - Tramite);
    public bool TemPizza => Pago > 0 || Medicoes > 0;
    public decimal Percentual(decimal valor) => Contratado == 0 ? 0 : valor / Contratado * 100;
}

public sealed record EscolaResumo(ObraLida Obra, IReadOnlyList<MedicaoEmTramite> Medicoes)
{
    public ValoresResumo Valores => new(Obra.ValorEmpreendimento ?? 0, Obra.ValorAditado ?? 0,
        Obra.ValorContratadoComAditamento ?? 0, Obra.ValorMedido ?? 0, Obra.SaldoAMedir ?? 0,
        Obra.ValorPago ?? 0, Medicoes.Sum(m => m.Valor), Obra.SaldoAPagar ?? 0, Medicoes.Count);
}

public sealed record ResumoContrato(ObraLida Identificacao, IReadOnlyList<EscolaResumo> Escolas)
{
    public ValoresResumo Valores
    {
        get
        {
            var v = Escolas.Select(e => e.Valores).ToArray();
            return new(v.Sum(x => x.Original), v.Sum(x => x.Aditado), v.Sum(x => x.Contratado), v.Sum(x => x.Medido),
                v.Sum(x => x.SaldoAMedir), v.Sum(x => x.Pago), v.Sum(x => x.Tramite), v.Sum(x => x.SaldoAPagar), v.Sum(x => x.Medicoes));
        }
    }
}

public sealed record OpcaoContratoResumo(string Contrato, string? Lote, string? Modalidade, string? Portaria, int Escolas)
{
    public string Titulo => $"{Contrato} · {NomeLote(Lote)}";
    public string Detalhe => string.Join(" · ", new[] { Modalidade, Escolas == 1 ? "1 escola" : $"{Escolas} escolas", Portaria }
        .Where(p => !string.IsNullOrWhiteSpace(p)));
    public static string NomeLote(string? lote) => string.IsNullOrWhiteSpace(lote) ? "sem lote"
        : lote.Trim().StartsWith("LOTE ", StringComparison.OrdinalIgnoreCase) ? "Lote " + lote.Trim()[5..] : lote.Trim();
}
