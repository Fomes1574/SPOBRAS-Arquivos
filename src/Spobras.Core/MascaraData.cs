using System.Globalization;

namespace Spobras.Core;

/// <summary>
/// Datas sempre em dia/mes/ano, com as barras postas enquanto a pessoa digita so os numeros
/// ("21052026" vira "21/05/2026"). Ao sair do campo, completa o ano que faltar.
/// </summary>
public static class MascaraData
{
    /// <summary>So numeros: poe as barras nas posicoes certas (no maximo 8 numeros). Texto com outras coisas fica como esta.</summary>
    public static string Formatar(string texto)
    {
        if (texto.Any(c => !char.IsAsciiDigit(c) && c != '/'))
        {
            return texto;
        }

        if (texto.Contains('/'))
        {
            // A pessoa esta pondo as barras: so evita barra dupla e numero demais em cada parte.
            var partes = texto.Split('/');
            if (partes.Length > 3 || partes.Take(2).Any(p => p.Length > 2) || (partes.Length == 3 && partes[2].Length > 4))
            {
                return Formatar(new string([.. texto.Where(char.IsAsciiDigit)]));
            }

            return texto.Replace("//", "/");
        }

        string d = texto.Length > 8 ? texto[..8] : texto;
        return d.Length switch
        {
            <= 2 => d,
            <= 4 => $"{d[..2]}/{d[2..]}",
            _ => $"{d[..2]}/{d[2..4]}/{d[4..]}",
        };
    }

    /// <summary>
    /// Ao sair do campo: "21/05" ganha o ano atual, "21/05/26" vira "21/05/2026" e "1/5/2026" vira
    /// "01/05/2026". O que nao for data fica como esta (a tela avisa).
    /// </summary>
    public static string Completar(string texto, DateOnly hoje)
    {
        string t = Formatar(texto.Trim());
        var partes = t.Split('/');
        if (partes.Length is < 2 or > 3 || partes.Any(p => p.Length == 0 || !p.All(char.IsAsciiDigit)))
        {
            return texto;
        }

        string ano = partes.Length == 2 ? hoje.Year.ToString(CultureInfo.InvariantCulture)
            : partes[2].Length == 2 ? "20" + partes[2]
            : partes[2];
        string completa = $"{partes[0].PadLeft(2, '0')}/{partes[1].PadLeft(2, '0')}/{ano}";
        return DateOnly.TryParseExact(completa, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) ? completa : t;
    }
}
