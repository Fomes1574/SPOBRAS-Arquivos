using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Spobras.Documentos.Imagens;

namespace Spobras.Documentos.Pdf;

public enum PosicaoCarimbo
{
    RodapeDireita,
    RodapeCentro,
    RodapeEsquerda,
    TopoDireita,
    TopoEsquerda,
}

/// <summary>O que colocar numa copia do PDF. Paginas contadas a partir de 1.</summary>
public sealed record OpcoesCarimbo
{
    public bool Numerar { get; init; } = true;

    /// <summary>Texto da numeracao: {n} = numero da pagina, {total} = total de paginas.</summary>
    public string Texto { get; init; } = "Página {n} de {total}";
    public PosicaoCarimbo PosicaoNumero { get; init; } = PosicaoCarimbo.RodapeDireita;
    public string? Logo { get; init; }
    public PosicaoCarimbo PosicaoLogo { get; init; } = PosicaoCarimbo.TopoEsquerda;
    public double LarguraLogoMm { get; init; } = 30;

    /// <summary>Primeira pagina que recebe logo e numero (por exemplo, 2 para pular a capa).</summary>
    public int APartirDa { get; init; } = 1;
}

/// <summary>
/// Logo e numeracao de paginas numa COPIA de um PDF pronto (especificacao 4.8, item 8). Tudo fica na
/// margem, pequeno, por cima do conteudo existente (o app nao sabe o que ha embaixo: por isso a tela
/// avisa para conferir). O original nunca e alterado.
/// </summary>
public static class CarimboPdf
{
    private const double Margem = 18; // pontos, cerca de 6 mm

    /// <summary>Quais paginas seriam atingidas, para a previa (1..total).</summary>
    public static (int Primeira, int Ultima, int Total) Atingidas(int totalPaginas, OpcoesCarimbo opcoes)
    {
        int primeira = Math.Max(1, opcoes.APartirDa);
        return primeira > totalPaginas ? (0, 0, 0) : (primeira, totalPaginas, totalPaginas - primeira + 1);
    }

    public static int Carimbar(string pdf, string saida, OpcoesCarimbo opcoes)
    {
        if (!opcoes.Numerar && opcoes.Logo is null)
        {
            throw new PdfInvalidoException("Escolha numerar as páginas, colocar o logo ou os dois.");
        }

        using (var teste = FerramentasPdf.Abrir(pdf, PdfDocumentOpenMode.Import))
        {
            if (Atingidas(teste.PageCount, opcoes).Total == 0)
            {
                throw new PdfInvalidoException($"\"{Path.GetFileName(pdf)}\" tem {teste.PageCount} páginas; não há página a partir da {opcoes.APartirDa}.");
            }
        }

        FontesPdf.Garantir();
        byte[]? logo = opcoes.Logo is null ? null : Fotos.Preparar(opcoes.Logo, 800, 92);
        return FerramentasPdf.Publicar(saida, parcial =>
        {
            File.Copy(pdf, parcial, overwrite: true);
            File.SetAttributes(parcial, FileAttributes.Normal);
            using var doc = FerramentasPdf.Abrir(parcial, PdfDocumentOpenMode.Modify);
            int total = doc.PageCount;
            var fonte = new XFont(FontesPdf.Familia, 9);
            using var imagem = logo is null ? null : XImage.FromStream(new MemoryStream(logo));
            int feitas = 0;
            for (int i = Math.Max(1, opcoes.APartirDa) - 1; i < total; i++)
            {
                var pagina = doc.Pages[i];
                using var desenho = XGraphics.FromPdfPage(pagina, XGraphicsPdfPageOptions.Append);
                double largura = pagina.Width.Point, altura = pagina.Height.Point;
                if (opcoes.Numerar)
                {
                    string texto = opcoes.Texto.Replace("{n}", (i + 1).ToString()).Replace("{total}", total.ToString());
                    var tamanho = desenho.MeasureString(texto, fonte);
                    var (x, y) = Ponto(opcoes.PosicaoNumero, largura, altura, tamanho.Width, tamanho.Height);
                    // Fundo branco discreto para o numero ficar legivel sobre qualquer coisa.
                    desenho.DrawRectangle(XBrushes.White, x - 2, y - 1, tamanho.Width + 4, tamanho.Height + 2);
                    desenho.DrawString(texto, fonte, XBrushes.Black, new XRect(x, y, tamanho.Width, tamanho.Height), XStringFormats.TopLeft);
                }

                if (imagem is not null)
                {
                    double l = opcoes.LarguraLogoMm * 72 / 25.4;
                    double a = l * imagem.PixelHeight / imagem.PixelWidth;
                    var (x, y) = Ponto(opcoes.PosicaoLogo, largura, altura, l, a);
                    desenho.DrawImage(imagem, x, y, l, a);
                }

                feitas++;
            }

            doc.Save(parcial);
            return feitas;
        });
    }

    private static (double X, double Y) Ponto(PosicaoCarimbo posicao, double largura, double altura, double l, double a) => posicao switch
    {
        PosicaoCarimbo.TopoEsquerda => (Margem, Margem),
        PosicaoCarimbo.TopoDireita => (largura - Margem - l, Margem),
        PosicaoCarimbo.RodapeEsquerda => (Margem, altura - Margem - a),
        PosicaoCarimbo.RodapeCentro => ((largura - l) / 2, altura - Margem - a),
        _ => (largura - Margem - l, altura - Margem - a),
    };
}
