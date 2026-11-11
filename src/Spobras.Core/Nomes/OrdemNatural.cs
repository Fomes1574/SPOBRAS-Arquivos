namespace Spobras.Core.Nomes;

/// <summary>Ordem "natural": numeros dentro do nome valem como numeros (2 antes de 10).</summary>
public sealed class OrdemNatural : IComparer<string>
{
    public static readonly OrdemNatural Instancia = new();

    public int Compare(string? a, string? b)
    {
        if (ReferenceEquals(a, b))
        {
            return 0;
        }

        if (a is null)
        {
            return -1;
        }

        if (b is null)
        {
            return 1;
        }

        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (char.IsAsciiDigit(a[i]) && char.IsAsciiDigit(b[j]))
            {
                int inicioA = i, inicioB = j;
                while (i < a.Length && char.IsAsciiDigit(a[i]))
                {
                    i++;
                }

                while (j < b.Length && char.IsAsciiDigit(b[j]))
                {
                    j++;
                }

                var numA = a.AsSpan(inicioA, i - inicioA).TrimStart('0');
                var numB = b.AsSpan(inicioB, j - inicioB).TrimStart('0');
                int porTamanho = numA.Length.CompareTo(numB.Length);
                if (porTamanho != 0)
                {
                    return porTamanho;
                }

                int porDigitos = numA.CompareTo(numB, StringComparison.Ordinal);
                if (porDigitos != 0)
                {
                    return porDigitos;
                }

                continue;
            }

            int porLetra = char.ToUpperInvariant(a[i]).CompareTo(char.ToUpperInvariant(b[j]));
            if (porLetra != 0)
            {
                return porLetra;
            }

            i++;
            j++;
        }

        int resto = (a.Length - i).CompareTo(b.Length - j);
        return resto != 0 ? resto : string.CompareOrdinal(a, b);
    }
}
