using PdfSharp.Drawing;
using PdfSharp.Drawing.Layout;
using PdfSharp.Pdf;
using Spobras.Core.Relatorio;
using Spobras.Documentos.Imagens;
using Spobras.Documentos.Pdf;

namespace Spobras.Documentos.Relatorio;

/// <summary>
/// Gera o relatorio fotografico direto em PDF, sem Excel (no celular nao ha Excel). Usa o mesmo plano de paginas
/// do arquivo .xlsx: mesmas colunas, cabecalho em grade, fotos no mesmo quadro e legendas. A pagina sai em A4 em pe,
/// na mesma escala de impressao do Excel (margens estreitas), entao fica igual ao PDF que o Excel faria.
/// </summary>
public static class GeradorRelatorioPdf
{
    private const double A4Largura = 595.28, A4Altura = 841.89;     // pontos
    private const double Topo = 0.4 * 72;                             // margem de cima, como no .xlsx
    private static double Escala => GradeRelatorio.EscalaDeImpressao / 100.0 * 0.75; // pixel do plano -> ponto impresso
    private const string SufixoParcial = ".spobras-parcial";

    public static RelatorioGerado Gerar(
        PlanoRelatorio plano, string caminhoSaida, string? caminhoLogo = null, IProgress<int>? fotosFeitas = null, CancellationToken cancelamento = default)
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
            Escrever(plano, parcial, caminhoLogo, fotosFeitas, cancelamento);
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

    private static void Escrever(PlanoRelatorio plano, string caminho, string? caminhoLogo, IProgress<int>? fotosFeitas, CancellationToken cancelamento)
    {
        using var doc = new PdfDocument();
        doc.Info.Title = CabecalhoRelatorio.NomeSugerido(plano.Dados);
        var normal = new XFont(FontesPdf.Familia, 11 * Escala / 0.75);
        var negrito = new XFont(FontesPdf.Familia, 11 * Escala / 0.75, XFontStyleEx.Bold);
        var titulo = new XFont(FontesPdf.Familia, 18 * Escala / 0.75, XFontStyleEx.Bold);
        var orgao = new XFont(FontesPdf.Familia, 12 * Escala / 0.75, XFontStyleEx.Bold);
        var rodape = new XFont(FontesPdf.Familia, 9);
        byte[]? logo = !string.IsNullOrWhiteSpace(caminhoLogo) && File.Exists(caminhoLogo) ? Fotos.Preparar(caminhoLogo, 1200, 92) : null;
        var cab = plano.Cabecalho;
        double esquerda = (A4Largura - GradeRelatorio.Largura * Escala) / 2;
        int feitas = 0;

        foreach (var pagina in plano.Paginas)
        {
            cancelamento.ThrowIfCancellationRequested();
            var p = doc.AddPage();
            p.Width = XUnit.FromPoint(A4Largura);
            p.Height = XUnit.FromPoint(A4Altura);
            using var g = XGraphics.FromPdfPage(p);
            var caneta = new XPen(XColors.Black, 0.6);

            XRect R(double x, double y, double l, double a) => new(esquerda + x * Escala, Topo + y * Escala, l * Escala, a * Escala);
            double X(int coluna) => GradeRelatorio.XDaColuna(coluna);
            double largura = GradeRelatorio.Largura;
            void Caixa(XRect r, string? texto = null, XFont? fonte = null, XStringFormat? formato = null, bool fundo = false)
            {
                if (fundo)
                {
                    g.DrawRectangle(new XSolidBrush(XColor.FromArgb(0xD9, 0xD9, 0xD9)), r);
                }

                g.DrawRectangle(caneta, r);
                if (!string.IsNullOrWhiteSpace(texto))
                {
                    var dentro = new XRect(r.X + 3, r.Y + 1, r.Width - 6, r.Height - 2);
                    Texto(g, texto, fonte ?? normal, dentro, formato ?? XStringFormats.CenterLeft);
                }
            }

            // ---- Cabecalho: logo, titulo e a caixa SPOBRAS / data / contratada ----
            var h = cab.Alturas;
            double y4 = h.Take(4).Sum();
            double xt = X(GradeRelatorio.ColunaDoTitulo), xk = X(GradeRelatorio.ColunaDaCaixa), xd = X(GradeRelatorio.ColunaDoValorDaData), xv = X(GradeRelatorio.ColunaDoValor);
            var caixaLogo = R(0, 0, xt, y4);
            Caixa(caixaLogo);
            if (logo is not null)
            {
                using var imagem = XImage.FromStream(new MemoryStream(logo));
                var area = PaginadorRelatorio.Encaixar(imagem.PixelWidth, imagem.PixelHeight, new Area(6, 6, xt - 12, y4 - 12));
                g.DrawImage(imagem, R(area.X, area.Y, area.Largura, area.Altura));
            }

            Caixa(R(xt, 0, xk - xt, y4), "RELATÓRIO FOTOGRÁFICO", titulo, XStringFormats.Center);
            Caixa(R(xk, 0, largura - xk, h[0]), "SÃO PAULO OBRAS - SPOBRAS", orgao, XStringFormats.Center);
            if (plano.Dados.Data is { } data)
            {
                Caixa(R(xk, h[0], xd - xk, h[1]), "DATA", negrito, XStringFormats.Center);
                Caixa(R(xd, h[0], largura - xd, h[1]), CabecalhoRelatorio.Data(data), negrito, XStringFormats.Center);
            }
            else
            {
                Caixa(R(xk, h[0], largura - xk, h[1]));
            }

            bool temContratada = !string.IsNullOrWhiteSpace(plano.Dados.Contratada);
            Caixa(R(xk, h[0] + h[1], largura - xk, h[2]), temContratada ? "CONTRATADA" : null, negrito, XStringFormats.Center);
            Caixa(R(xk, h[0] + h[1] + h[2], largura - xk, h[3]), temContratada ? plano.Dados.Contratada.Trim() : null, normal, XStringFormats.Center);

            for (int i = 0; i < cab.Campos.Count; i++)
            {
                var campo = cab.Campos[i];
                double y = cab.YDoCampo(i), alt = h[cab.PrimeiroCampo + i];
                Caixa(R(0, y, xv, alt), campo.Rotulo, negrito);
                if (campo.Dupla)
                {
                    double x2 = X(CabecalhoRelatorio.ColunaDoRotulo2), xv2 = X(CabecalhoRelatorio.ColunaDoValor2);
                    Caixa(R(xv, y, x2 - xv, alt), campo.Valor, normal, XStringFormats.Center);
                    Caixa(R(x2, y, xv2 - x2, alt), campo.Rotulo2, negrito);
                    Caixa(R(xv2, y, largura - xv2, alt), campo.Valor2, normal, XStringFormats.Center);
                }
                else
                {
                    Caixa(R(xv, y, largura - xv, alt), campo.Valor, normal);
                }
            }

            Caixa(R(0, cab.YDoTitulo + GradeRelatorio.LinhasDoTitulo[0], largura, GradeRelatorio.LinhasDoTitulo[1]), "RELATÓRIO FOTOGRÁFICO", negrito, XStringFormats.Center, fundo: true);

            // ---- Fotos e textos ----
            foreach (var foto in pagina.Fotos)
            {
                cancelamento.ThrowIfCancellationRequested();
                byte[] jpeg = Fotos.Preparar(foto.Foto.Caminho, plano.Opcoes.LadoMaximoFoto, plano.Opcoes.QualidadeFoto, foto.Foto.Rotacao);
                using (var imagem = XImage.FromStream(new MemoryStream(jpeg)))
                {
                    g.DrawImage(imagem, R(foto.Imagem.X, foto.Imagem.Y, foto.Imagem.Largura, foto.Imagem.Altura));
                }

                double alturaTitulo = foto.Titulo.Length == 0 ? 0 : PaginadorRelatorio.LinhasDe(foto.Titulo, foto.Espaco.Largura) * GradeRelatorio.AlturaDaLinha;
                if (foto.Titulo.Length > 0)
                {
                    Paragrafo(g, foto.Titulo, negrito, R(foto.Texto.X + 4, foto.Texto.Y + 2, foto.Texto.Largura - 8, alturaTitulo), XParagraphAlignment.Center);
                }

                if (foto.Descricao.Length > 0)
                {
                    Paragrafo(g, foto.Descricao, normal, R(foto.Texto.X + 4, foto.Texto.Y + alturaTitulo + 2, foto.Texto.Largura - 8, foto.Texto.Altura - alturaTitulo - 2), XParagraphAlignment.Left);
                }

                fotosFeitas?.Report(++feitas);
            }

            foreach (var continuacao in pagina.Continuacoes)
            {
                var area = continuacao.Texto_;
                Paragrafo(g, continuacao.Titulo, negrito, R(area.X + 4, area.Y, area.Largura - 8, GradeRelatorio.AlturaDaLinha), XParagraphAlignment.Left);
                Paragrafo(g, continuacao.Texto, normal, R(area.X + 4, area.Y + GradeRelatorio.AlturaDaLinha, area.Largura - 8, area.Altura - GradeRelatorio.AlturaDaLinha), XParagraphAlignment.Left);
            }

            if (pagina.Assinaturas is { } assinaturas)
            {
                double y = assinaturas.Espaco.Y + 3 * GradeRelatorio.AlturaDaLinha;
                void Assinatura(double x, double l, string rotulo, string nome)
                {
                    var linha = R(x, y, l, GradeRelatorio.AlturaDaLinha);
                    g.DrawLine(caneta, linha.Left, linha.Top, linha.Right, linha.Top);
                    Texto(g, rotulo, normal, linha, XStringFormats.Center);
                    if (!string.IsNullOrWhiteSpace(nome))
                    {
                        Texto(g, nome.Trim(), normal, R(x, y + GradeRelatorio.AlturaDaLinha, l, GradeRelatorio.AlturaDaLinha), XStringFormats.Center);
                    }
                }

                Assinatura(0, X(4), "Fiscalização SPOBRAS", plano.Opcoes.AssinaturaFiscal);
                Assinatura(X(6), largura - X(6), "Contratada", plano.Opcoes.AssinaturaContratada);
            }

            // Rodape como o do Excel: "Página X de Y".
            g.DrawString($"Página {pagina.Numero} de {plano.Paginas.Count}", rodape, XBrushes.Black,
                new XRect(0, A4Altura - 0.45 * 72, A4Largura, 14), XStringFormats.Center);
        }

        doc.Save(caminho);
    }

    /// <summary>Uma linha de texto que nao pode sair da caixa: diminui so se nao couber (nunca acontece com o plano).</summary>
    private static void Texto(XGraphics g, string texto, XFont fonte, XRect area, XStringFormat formato)
    {
        if (texto.Contains('\n') || g.MeasureString(texto, fonte).Width > area.Width)
        {
            Paragrafo(g, texto, fonte, area, formato.Alignment == XStringAlignment.Center ? XParagraphAlignment.Center : XParagraphAlignment.Left, centralizarNaAltura: true);
            return;
        }

        g.DrawString(texto, fonte, XBrushes.Black, area, formato);
    }

    /// <summary>Texto com quebra por palavra dentro da area.</summary>
    private static void Paragrafo(XGraphics g, string texto, XFont fonte, XRect area, XParagraphAlignment alinhamento, bool centralizarNaAltura = false)
    {
        var formatador = new XTextFormatter(g) { Alignment = alinhamento };
        if (centralizarNaAltura)
        {
            double linhas = Math.Ceiling(g.MeasureString(texto, fonte).Width / Math.Max(1, area.Width)) + texto.Count(c => c == '\n');
            double altura = Math.Min(area.Height, linhas * fonte.GetHeight());
            area = new XRect(area.X, area.Y + (area.Height - altura) / 2, area.Width, altura + 1);
        }

        formatador.DrawString(texto, fonte, XBrushes.Black, area, XStringFormats.TopLeft);
    }
}
