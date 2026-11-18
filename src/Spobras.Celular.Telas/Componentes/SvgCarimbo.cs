using System.Globalization;
using System.Net;
using System.Text;
using Spobras.Core.Relatorio;

namespace Spobras.Celular.Componentes;

/// <summary>
/// O carimbo em SVG, com a mesma conta da gravacao (<see cref="GeometriaCarimbo"/>): previa do editor, da foto e da
/// pagina. Montado como texto porque o Razor reserva a etiqueta "text" (tudo escapado).
/// </summary>
public static class SvgCarimbo
{
    private static string N(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>Carimbo de uma foto de <paramref name="largura"/> x <paramref name="altura"/> px desenhado com escala e deslocamento.</summary>
    public static string Markup(GeometriaCarimbo g, double escala = 1, double x0 = 0, double y0 = 0)
    {
        var sb = new StringBuilder();
        sb.Append($"<rect x=\"{N(x0 + g.X * escala)}\" y=\"{N(y0 + g.Y * escala)}\" width=\"{N(g.Largura * escala)}\" height=\"{N(g.Altura * escala)}\" ")
          .Append($"rx=\"{N(g.Raio * escala)}\" fill=\"#000\" fill-opacity=\"0.549\" />");
        foreach (var l in g.Linhas)
        {
            sb.Append($"<text x=\"{N(x0 + l.X * escala)}\" y=\"{N(y0 + l.Base * escala)}\" font-size=\"{N(g.Letra * escala)}\" font-weight=\"{(l.Negrito ? 700 : 400)}\" ")
              .Append("fill=\"#fff\" font-family=\"'SPOBRAS Relatorio', Carlito, Calibri, sans-serif\" style=\"white-space:pre\">")
              .Append(WebUtility.HtmlEncode(l.Texto)).Append("</text>");
        }

        return sb.ToString();
    }

    /// <summary>Carimbo por cima de uma foto encaixada (sem cortar) numa area da pagina.</summary>
    public static string NaArea(FotoRelatorio foto, Area area)
    {
        if (GeometriaCarimbo.Calcular(foto.Largura, foto.Altura, foto.Carimbo) is not { } g)
        {
            return "";
        }

        double escala = Math.Min(area.Largura / foto.Largura, area.Altura / foto.Altura);
        double x0 = area.X + (area.Largura - foto.Largura * escala) / 2, y0 = area.Y + (area.Altura - foto.Altura * escala) / 2;
        return Markup(g, escala, x0, y0);
    }
}
