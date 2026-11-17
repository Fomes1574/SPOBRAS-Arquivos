using PdfSharp.Drawing;
using PdfSharp.Pdf;
using Spobras.Core.Relatorio;
using Spobras.Documentos.Imagens;
using Spobras.Documentos.Pdf;

namespace Spobras.Documentos.Relatorio;

/// <summary>
/// Gera o relatorio fotografico em PDF pelo proprio app (no computador e no celular), sem depender do Excel:
/// sempre A4 em pe, com o desenho de <see cref="DesenhoRelatorio"/> (o mesmo da previa) e a fonte Carlito embutida.
/// </summary>
public static class GeradorRelatorioPdf
{
    private const string SufixoParcial = ".spobras-parcial";

    public static RelatorioGerado Gerar(
        PlanoRelatorio plano, string caminhoSaida, string? caminhoLogo = null, IProgress<int>? fotosFeitas = null, CancellationToken cancelamento = default,
        PreparoDeFotos? preparo = null)
    {
        if (!plano.PodeGerar)
        {
            throw new InvalidOperationException("O relatório não pode ser gerado: " + string.Join(" ", plano.Avisos.Select(a => a.Mensagem)));
        }

        if (File.Exists(caminhoSaida))
        {
            throw new IOException($"Já existe \"{Path.GetFileName(caminhoSaida)}\" na pasta de destino. Nada foi sobrescrito.");
        }

        string? pasta = Path.GetDirectoryName(caminhoSaida);
        if (!string.IsNullOrEmpty(pasta))
        {
            Directory.CreateDirectory(pasta);
        }

        FontesPdf.Garantir();
        string parcial = caminhoSaida + SufixoParcial;
        try
        {
            Escrever(plano, parcial, caminhoLogo, fotosFeitas, cancelamento, preparo ?? new PreparoDeFotos());
            File.Move(parcial, caminhoSaida, overwrite: false);
        }
        finally
        {
            if (File.Exists(parcial))
            {
                File.Delete(parcial);
            }
        }

        return new RelatorioGerado(caminhoSaida, plano.Paginas.Count, plano.TotalDeFotos, new FileInfo(caminhoSaida).Length);
    }

    private static void Escrever(PlanoRelatorio plano, string caminho, string? caminhoLogo, IProgress<int>? fotosFeitas, CancellationToken cancelamento, PreparoDeFotos preparo)
    {
        // As fotos primeiro, todas de uma vez em paralelo (a parte demorada); depois so montar o arquivo.
        preparo.PrepararTodas(plano, fotosFeitas, cancelamento);
        using var doc = new PdfDocument();
        doc.Info.Title = CabecalhoRelatorio.NomeSugerido(plano.Dados);
        byte[]? logo = !string.IsNullOrWhiteSpace(caminhoLogo) && File.Exists(caminhoLogo) ? Fotos.Preparar(caminhoLogo, 1200, 92) : null;
        var fontes = new Dictionary<(double, bool), XFont>();
        XFont Fonte(double tamanho, bool negrito)
        {
            if (!fontes.TryGetValue((tamanho, negrito), out var f))
            {
                fontes[(tamanho, negrito)] = f = new XFont(FontesPdf.FamiliaDoRelatorio, tamanho, negrito ? XFontStyleEx.Bold : XFontStyleEx.Regular);
            }

            return f;
        }

        foreach (var pagina in plano.Paginas)
        {
            cancelamento.ThrowIfCancellationRequested();
            var p = doc.AddPage();
            p.Width = XUnit.FromPoint(GradeRelatorio.LarguraDaFolha);
            p.Height = XUnit.FromPoint(GradeRelatorio.AlturaDaFolha);
            using var g = XGraphics.FromPdfPage(p);
            foreach (var elemento in DesenhoRelatorio.Pagina(plano, pagina, logo is not null))
            {
                cancelamento.ThrowIfCancellationRequested();
                switch (elemento)
                {
                    case RetanguloDesenho r:
                        var rect = new XRect(r.Area.X, r.Area.Y, r.Area.Largura, r.Area.Altura);
                        if (r.Fundo is { } fundo)
                        {
                            g.DrawRectangle(new XSolidBrush(Cor(fundo)), rect);
                        }

                        if (r.Borda is { } borda)
                        {
                            g.DrawRectangle(new XPen(Cor(borda), r.Espessura), rect);
                        }

                        break;
                    case LinhaDesenho l:
                        g.DrawLine(new XPen(Cor(l.Cor), l.Espessura), l.X1, l.Y1, l.X2, l.Y2);
                        break;
                    case ImagemDesenho { Foto: { } foto } im:
                        using (var imagem = XImage.FromStream(new MemoryStream(preparo.Obter(foto, plano.Opcoes.LadoMaximoFoto, plano.Opcoes.QualidadeFoto))))
                        {
                            g.DrawImage(imagem, im.Area.X, im.Area.Y, im.Area.Largura, im.Area.Altura);
                        }

                        break;
                    case ImagemDesenho im when logo is not null:
                        using (var imagem = XImage.FromStream(new MemoryStream(logo)))
                        {
                            var a = PaginadorRelatorio.Encaixar(imagem.PixelWidth, imagem.PixelHeight, im.Area);
                            g.DrawImage(imagem, a.X, a.Y, a.Largura, a.Altura);
                        }

                        break;
                    case TextoDesenho t:
                        Texto(g, t, Fonte(t.Tamanho, t.Negrito));
                        break;
                }
            }
        }

        doc.Save(caminho);
    }

    private static XColor Cor(string hex) => XColor.FromArgb(Convert.ToInt32(hex[..2], 16), Convert.ToInt32(hex[2..4], 16), Convert.ToInt32(hex[4..6], 16));

    /// <summary>
    /// Desenha as linhas como o plano as quebrou. Linha um pouco mais larga que a area (fonte diferente, nunca com a
    /// Carlito) e apertada na horizontal em vez de sair da caixa.
    /// </summary>
    private static void Texto(XGraphics g, TextoDesenho t, XFont fonte)
    {
        var pincel = new XSolidBrush(Cor(t.Cor));
        double alturaDoTexto = t.Linhas.Count * t.AlturaDaLinha;
        double y = t.CentroNaAltura ? t.Area.Y + (t.Area.Altura - alturaDoTexto) / 2 : t.Area.Y;
        // A linha base fica a 80% da altura da linha (ascendente da Carlito), com a sobra dividida em cima e embaixo.
        double baseDaLetra = (t.AlturaDaLinha - t.Tamanho) / 2 + t.Tamanho * 0.8;
        for (int i = 0; i < t.Linhas.Count; i++)
        {
            string linha = t.Linhas[i];
            double yBase = y + i * t.AlturaDaLinha + baseDaLetra;
            double largura = g.MeasureString(linha, fonte).Width;
            bool justificar = t.Alinhamento == Alinhamento.Justificado && i < t.Linhas.Count - 1 && linha.Contains(' ') && largura < t.Area.Largura;
            if (justificar)
            {
                string[] palavras = linha.Split(' ');
                double ocupado = palavras.Sum(w => g.MeasureString(w, fonte).Width);
                double vao = (t.Area.Largura - ocupado) / (palavras.Length - 1);
                double x = t.Area.X;
                foreach (string palavra in palavras)
                {
                    g.DrawString(palavra, fonte, pincel, x, yBase);
                    x += g.MeasureString(palavra, fonte).Width + vao;
                }

                continue;
            }

            double escala = largura > t.Area.Largura && largura > 0 ? t.Area.Largura / largura : 1;
            double xInicio = t.Alinhamento == Alinhamento.Centro ? t.Area.X + (t.Area.Largura - largura * escala) / 2 : t.Area.X;
            if (escala < 1)
            {
                var estado = g.Save();
                g.TranslateTransform(xInicio, yBase);
                g.ScaleTransform(escala, 1);
                g.DrawString(linha, fonte, pincel, 0, 0);
                g.Restore(estado);
            }
            else
            {
                g.DrawString(linha, fonte, pincel, xInicio, yBase);
            }
        }
    }
}
