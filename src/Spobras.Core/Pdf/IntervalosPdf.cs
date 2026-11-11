using System.Globalization;

namespace Spobras.Core.Pdf;

public readonly record struct IntervaloPdf(int Inicio, int Fim)
{
    public int Paginas => Fim - Inicio + 1;
    public override string ToString() => Inicio == Fim ? Inicio.ToString(CultureInfo.InvariantCulture) : $"{Inicio}-{Fim}";
}

public sealed record IntervalosLidos(IReadOnlyList<IntervaloPdf> Intervalos, string? Erro)
{
    public bool Valido => Erro is null && Intervalos.Count > 0;
}

/// <summary>Le intervalos de paginas digitados pelo usuario: "1-3, 5, 8-" (8 ate o fim).</summary>
public static class IntervalosPdf
{
    public static IntervalosLidos Interpretar(string texto, int totalDePaginas)
    {
        var intervalos = new List<IntervaloPdf>();
        if (string.IsNullOrWhiteSpace(texto))
        {
            return new IntervalosLidos([], "Digite as páginas. Exemplo: 1-3, 5, 8-");
        }

        foreach (string parte in texto.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] pontas = parte.Split('-', StringSplitOptions.TrimEntries);
            if (pontas.Length > 2
                || !Numero(pontas[0], out int inicio)
                || (pontas.Length == 2 && pontas[1].Length > 0 && !Numero(pontas[1], out _)))
            {
                return new IntervalosLidos([], $"Não entendi \"{parte}\". Use números e traço, como 1-3, 5, 8-");
            }

            int fim = pontas.Length == 1 ? inicio : pontas[1].Length == 0 ? totalDePaginas : int.Parse(pontas[1], CultureInfo.InvariantCulture);
            if (inicio < 1 || fim < 1)
            {
                return new IntervalosLidos([], "As páginas começam no número 1.");
            }

            if (inicio > fim)
            {
                return new IntervalosLidos([], $"Em \"{parte}\" a primeira página é maior que a última.");
            }

            if (fim > totalDePaginas)
            {
                return new IntervalosLidos([], $"\"{parte}\" passa do fim: o PDF tem {totalDePaginas} página(s).");
            }

            intervalos.Add(new IntervaloPdf(inicio, fim));
        }

        return new IntervalosLidos(intervalos, intervalos.Count == 0 ? "Digite as páginas. Exemplo: 1-3, 5, 8-" : null);
    }

    /// <summary>Um intervalo por pagina (separar todas as paginas).</summary>
    public static IReadOnlyList<IntervaloPdf> CadaPagina(int totalDePaginas) =>
        [.. Enumerable.Range(1, totalDePaginas).Select(p => new IntervaloPdf(p, p))];

    private static bool Numero(string texto, out int valor) =>
        int.TryParse(texto, NumberStyles.None, CultureInfo.InvariantCulture, out valor);
}
