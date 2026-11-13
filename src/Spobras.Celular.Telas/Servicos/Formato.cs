using System.Globalization;

namespace Spobras.Celular.Servicos;

public static class Formato
{
    public static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static string Reais(decimal? valor) => valor is { } v ? v.ToString("C2", PtBr).Replace(' ', ' ') : "Sem informação";

    public static string Data(DateOnly? data) => data?.ToString("dd/MM/yyyy", PtBr) ?? "";

    public static string Plural(int n, string um, string varios) => $"{n.ToString("N0", PtBr)} {(n == 1 ? um : varios)}";

    public static string Tamanho(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} bytes",
        < 1024 * 1024 => $"{(bytes / 1024.0).ToString("0", PtBr)} KB",
        _ => $"{(bytes / 1024.0 / 1024.0).ToString("0.0", PtBr)} MB",
    };

    /// <summary>"hoje às 19:07", "ontem às 17:30" ou "10/11 às 08:12".</summary>
    public static string Quando(DateTimeOffset quando, DateTimeOffset agora)
    {
        var local = quando.ToLocalTime();
        var hoje = DateOnly.FromDateTime(agora.ToLocalTime().DateTime);
        var dia = DateOnly.FromDateTime(local.DateTime);
        string hora = local.ToString("HH:mm", PtBr);
        return dia == hoje ? $"hoje às {hora}" : dia == hoje.AddDays(-1) ? $"ontem às {hora}" : $"{local.ToString("dd/MM", PtBr)} às {hora}";
    }

    public static (DateOnly? Data, bool Invalida) LerData(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            return (null, false);
        }

        return DateOnly.TryParseExact(texto.Trim(), ["d/M/yyyy", "dd/MM/yyyy", "d/M/yy"], PtBr, DateTimeStyles.None, out var data)
            ? (data, false)
            : (null, true);
    }
}
