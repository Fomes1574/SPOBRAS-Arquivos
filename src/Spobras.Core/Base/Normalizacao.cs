using System.Globalization;
using System.Text.RegularExpressions;

namespace Spobras.Core.Base;

public sealed record DotacaoNormalizada(string Completa, string Original, bool Corrigida);

public sealed record PortariaLida(int? Numero, int? Ano, string? Complemento);

/// <summary>Padronizacao de codigos da base. Codigos sao sempre texto (zeros e pontuacao preservados).</summary>
public static partial class Normalizacao
{
    /// <summary>
    /// Dotacao completa sempre com pontos (decisao do usuario): espaco no lugar do ponto e erro de digitacao.
    /// Guarda o texto original para conferencia.
    /// </summary>
    public static DotacaoNormalizada Dotacao(string original)
    {
        string limpo = original.Trim();
        string completa = Espacos().Replace(limpo, ".");
        while (completa.Contains("..", StringComparison.Ordinal))
        {
            completa = completa.Replace("..", ".", StringComparison.Ordinal);
        }

        return new DotacaoNormalizada(completa, limpo, completa != limpo);
    }

    /// <summary>
    /// Le numero, ano e complemento de um rotulo de portaria: "Portaria 3", "Portaria 2 (2024)",
    /// "Portaria 3 (Construção Nova)", "PORTARIA CONJUNTA SME/SIURB Nº 03 (2021)".
    /// O que nao der para ler fica null (nada e adivinhado).
    /// </summary>
    public static PortariaLida Portaria(string rotulo)
    {
        int? numero = null, ano = null;
        string? complemento = null;

        var parenteses = Parenteses().Match(rotulo);
        string semParenteses = rotulo;
        if (parenteses.Success)
        {
            string dentro = parenteses.Groups[1].Value.Trim();
            if (Ano().IsMatch(dentro))
            {
                ano = int.Parse(dentro, CultureInfo.InvariantCulture);
            }
            else if (dentro.Length > 0)
            {
                complemento = dentro;
            }

            semParenteses = rotulo.Remove(parenteses.Index, parenteses.Length);
        }

        // O numero so vale logo depois de "Nº" ou de "Portaria" ("SEM PORTARIA - 96 Quadras" nao e a portaria 96).
        var achado = NumeroDaPortaria().Match(semParenteses);
        if (achado.Success)
        {
            numero = int.Parse(achado.Groups[1].Value, CultureInfo.InvariantCulture);
        }

        return new PortariaLida(numero, ano, complemento);
    }

    /// <summary>Texto para busca: minusculas e sem acentos.</summary>
    public static string Busca(string? texto) =>
        string.IsNullOrEmpty(texto) ? "" : Nomes.NomeArquivo.RemoverAcentos(texto).ToLowerInvariant();

    /// <summary>Reais com duas casas para centavos inteiros, arredondando metade para longe do zero.</summary>
    public static long? Centavos(decimal? reais) =>
        reais is { } v ? (long)Math.Round(v * 100m, 0, MidpointRounding.AwayFromZero) : null;

    public static decimal? Reais(long? centavos) => centavos is { } c ? c / 100m : null;

    /// <summary>Le "1.234,56", "R$ 1.000", "1000,5" (formato brasileiro). Null se nao for um valor.</summary>
    public static decimal? LerReais(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            return null;
        }

        string limpo = texto.Replace("R$", "", StringComparison.OrdinalIgnoreCase).Replace(" ", "").Replace("\u00A0", "").Trim();
        return decimal.TryParse(limpo, NumberStyles.Number, CultureInfo.GetCultureInfo("pt-BR"), out decimal valor) ? valor : null;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Espacos();

    [GeneratedRegex(@"\(([^)]*)\)")]
    private static partial Regex Parenteses();

    [GeneratedRegex(@"^(19|20)\d\d$")]
    private static partial Regex Ano();

    [GeneratedRegex(@"(?:n[º°o]\.?\s*|portaria\s+)(\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NumeroDaPortaria();
}
