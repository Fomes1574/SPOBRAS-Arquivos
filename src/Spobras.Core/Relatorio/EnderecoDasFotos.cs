using System.Globalization;
using System.Text;

namespace Spobras.Core.Relatorio;

/// <summary>Uma leitura do GPS: ponto, margem de erro (metros) e quando foi lida.</summary>
public sealed record PontoGps(double Latitude, double Longitude, double Precisao, DateTimeOffset LidoEm)
{
    /// <summary>Precisao pior que isso: o Android so liberou a localizacao aproximada.</summary>
    public const double Aproximada = 200;

    public bool Valido => double.IsFinite(Latitude) && double.IsFinite(Longitude) && Math.Abs(Latitude) <= 90 && Math.Abs(Longitude) <= 180;
}

/// <summary>
/// Regras do endereco das fotos: chave da escola (nome + contrato, porque duas escolas diferentes podem ter o mesmo
/// nome na planilha), texto curto para leitura e o aviso de distancia.
/// </summary>
public static class EnderecoDasFotos
{
    /// <summary>Distancia, alem da margem de erro das duas leituras, a partir da qual o app avisa.</summary>
    public const double DistanciaDoAviso = 300;

    /// <summary>
    /// Nome normalizado: sem acento, minusculo, e qualquer sequencia de espacos, tracos e pontuacao vira um espaco.
    /// "EMEF  - Escola Gêmea" e "emef escola gemea" sao o mesmo nome.
    /// </summary>
    public static string Normalizar(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            return "";
        }

        var sb = new StringBuilder();
        bool espaco = false;
        foreach (char c in texto.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(c))
            {
                if (espaco && sb.Length > 0)
                {
                    sb.Append(' ');
                }

                espaco = false;
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                espaco = true;
            }
        }

        return sb.ToString();
    }

    /// <summary>Chave da escola: nome normalizado e contrato normalizado. Sem nome, nao ha chave.</summary>
    public static string? Chave(string? unidade, string? contrato)
    {
        string nome = Normalizar(unidade);
        return nome.Length == 0 ? null : nome + "|" + Normalizar(contrato);
    }

    /// <summary>Parte do nome de uma chave (para achar escolas com o mesmo nome e outro contrato).</summary>
    public static string NomeDaChave(string chave) => chave.Split('|')[0];

    /// <summary>
    /// Texto curto e facil de ler: "Rua Exemplo, 123 – Vila Exemplo". Sem CEP, cidade, estado e pais. Sem rua, fica o
    /// bairro (ou a cidade). Nada util: nulo.
    /// </summary>
    public static string? Formatar(string? rua, string? numero, string? bairro, string? cidade = null)
    {
        string r = Limpo(rua), n = Limpo(numero), b = Limpo(bairro), c = Limpo(cidade);
        if (n.Contains('-', StringComparison.Ordinal) && n.Length > 6)
        {
            n = ""; // faixa de numeros ("100-198"): melhor sem numero do que um numero errado
        }

        string principal = r.Length == 0 ? "" : n.Length == 0 ? r : $"{r}, {n}";
        string lugar = b.Length > 0 ? b : principal.Length == 0 ? c : "";
        string texto = principal.Length > 0 && lugar.Length > 0 && !string.Equals(principal, lugar, StringComparison.OrdinalIgnoreCase)
            ? $"{principal} – {lugar}"
            : principal.Length > 0 ? principal : lugar;
        return texto.Length == 0 ? null : texto;
    }

    private static string Limpo(string? s) => TextoMedido.Limpar(s).Trim();

    /// <summary>Distancia em metros entre dois pontos (formula de haversine).</summary>
    public static double Distancia(PontoGps a, PontoGps b)
    {
        const double raio = 6_371_000;
        double rad(double g) => g * Math.PI / 180;
        double dLat = rad(b.Latitude - a.Latitude), dLon = rad(b.Longitude - a.Longitude);
        double h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(rad(a.Latitude)) * Math.Cos(rad(b.Latitude)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * raio * Math.Asin(Math.Min(1, Math.Sqrt(h)));
    }

    /// <summary>
    /// A leitura nova esta longe do ponto guardado: mais que 300 m alem da margem de erro das duas leituras. Andar
    /// pelo terreno de um CEU do tamanho de um quarteirao nao avisa.
    /// </summary>
    public static bool Longe(PontoGps guardado, PontoGps agora) =>
        Distancia(guardado, agora) > DistanciaDoAviso + Math.Max(0, guardado.Precisao) + Math.Max(0, agora.Precisao);

    /// <summary>"350 m", "1,2 km".</summary>
    public static string TextoDistancia(double metros) =>
        metros < 1000 ? $"{Math.Round(metros / 10) * 10:0} m" : (metros / 1000).ToString(metros < 10_000 ? "0.0" : "0", new CultureInfo("pt-BR")) + " km";
}
