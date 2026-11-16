using System.Globalization;

namespace Spobras.Core;

/// <summary>Horario de Brasilia para mostrar datas e horas (igual no computador e no celular, onde quer que estejam).</summary>
public static class HoraDeBrasilia
{
    private static readonly TimeZoneInfo? Fuso = Achar("America/Sao_Paulo") ?? Achar("E. South America Standard Time");

    private static TimeZoneInfo? Achar(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return null;
        }
    }

    /// <summary>O mesmo instante no horario de Brasilia (sem fuso no aparelho: UTC-3, que vale desde 2019).</summary>
    public static DateTimeOffset De(DateTimeOffset instante) =>
        Fuso is null ? instante.ToOffset(TimeSpan.FromHours(-3)) : TimeZoneInfo.ConvertTime(instante, Fuso);

    /// <summary>"16/11/2026 às 14:32".</summary>
    public static string Texto(DateTimeOffset instante) =>
        De(instante).ToString("dd/MM/yyyy 'às' HH:mm", CultureInfo.InvariantCulture);
}
