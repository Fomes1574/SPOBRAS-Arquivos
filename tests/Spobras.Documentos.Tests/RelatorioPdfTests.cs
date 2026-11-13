using SkiaSharp;
using Spobras.Core.Relatorio;
using Spobras.Documentos.Imagens;
using Spobras.Documentos.Relatorio;
using UglyToad.PdfPig;

namespace Spobras.Documentos.Tests;

/// <summary>Relatorio desenhado direto em PDF (usado no celular, onde nao ha Excel).</summary>
public class RelatorioPdfTests : IDisposable
{
    private readonly string _pasta = Directory.CreateTempSubdirectory("spobras-relpdf-").FullName;

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    private string Foto(string nome, int largura, int altura, SKEncodedImageFormat formato = SKEncodedImageFormat.Jpeg)
    {
        using var bmp = new SKBitmap(largura, altura);
        using (var c = new SKCanvas(bmp))
        {
            c.Clear(SKColors.SteelBlue);
            using var p = new SKPaint { Color = SKColors.Orange };
            c.DrawRect(0, 0, largura / 2f, altura, p);
        }

        string caminho = Path.Combine(_pasta, nome);
        using var dados = bmp.Encode(formato, 90);
        File.WriteAllBytes(caminho, dados.ToArray());
        return caminho;
    }

    private (PlanoRelatorio Plano, string Pdf) Gerar(int fotos, OpcoesRelatorio opcoes, DadosRelatorio? dados = null)
    {
        var lista = Enumerable.Range(1, fotos).Select(n =>
        {
            string caminho = Foto($"f{n}.jpg", n % 3 == 0 ? 300 : 400, n % 3 == 0 ? 400 : 300);
            var info = Fotos.Ler(caminho);
            return new FotoRelatorio(caminho, info.Largura, info.Altura) { Legenda = $"Serviço {n}", Descricao = n == 2 ? "Descrição do serviço executado na área externa." : "" };
        }).ToList();
        var plano = PaginadorRelatorio.Paginar(lista, opcoes, dados);
        string pdf = Path.Combine(_pasta, $"rel-{Guid.NewGuid():N}.pdf");
        GeradorRelatorioPdf.Gerar(plano, pdf, Foto("logo.png", 195, 138, SKEncodedImageFormat.Png));
        Guardar(pdf);
        return (plano, pdf);
    }

    /// <summary>Com SPOBRAS_SAIDA_VISUAL definida, guarda uma copia para conferencia visual.</summary>
    private static void Guardar(string pdf)
    {
        string? pasta = Environment.GetEnvironmentVariable("SPOBRAS_SAIDA_VISUAL");
        if (!string.IsNullOrWhiteSpace(pasta))
        {
            Directory.CreateDirectory(pasta);
            File.Copy(pdf, Path.Combine(pasta, Path.GetFileName(pdf)), overwrite: true);
        }
    }

    private static readonly DadosRelatorio Dados = new()
    {
        Unidade = "CEU Exemplo Alfa", Contratada = "Construtora Fictícia Ltda", Contrato = "044/EX/2026", Lote = "12",
        Portaria = "Nº 03", Objeto = "Reforma da cobertura e acessibilidade", Responsavel = "Fiscal Exemplo",
        Data = new DateOnly(2026, 11, 10), DataOs = new DateOnly(2026, 3, 2), Descricao = "Visita de rotina.",
    };

    [Fact]
    public void Gera_as_mesmas_paginas_do_plano_em_A4_com_cabecalho_legendas_e_fotos()
    {
        var (plano, pdf) = Gerar(8, new OpcoesRelatorio(), Dados);

        using var doc = PdfDocument.Open(pdf);
        Assert.Equal(plano.Paginas.Count, doc.NumberOfPages);
        Assert.Equal(2, doc.NumberOfPages);
        var pagina = doc.GetPage(1);
        Assert.InRange(pagina.Width, 595, 596);
        Assert.InRange(pagina.Height, 841, 842);
        string texto = string.Join(" ", pagina.GetWords().Select(w => w.Text));
        foreach (var esperado in new[] { "RELATÓRIO", "FOTOGRÁFICO", "SPOBRAS", "10/11/2026", "CEU", "044/EX/2026", "02/03/2026", "FISCAL", "Serviço", "Página", "1", "de", "2" })
        {
            Assert.Contains(esperado, texto);
        }

        Assert.DoesNotContain("PROCESSO", texto); // SEI vazio nao aparece
        Assert.Equal(1 + plano.Paginas[0].Fotos.Count, pagina.GetImages().Count()); // logo + fotos

        // Nada sai da folha.
        foreach (var w in pagina.GetWords())
        {
            Assert.InRange(w.BoundingBox.Left, 0, pagina.Width);
            Assert.InRange(w.BoundingBox.Right, 0, pagina.Width);
        }
    }

    [Fact]
    public void Sem_dados_e_com_assinaturas_continua_valido_e_nunca_sobrescreve()
    {
        var (plano, pdf) = Gerar(3, new OpcoesRelatorio { Assinaturas = true, AssinaturaFiscal = "Fulano", Modo = ModoLegenda.DescricaoServico });
        using (var doc = PdfDocument.Open(pdf))
        {
            Assert.Equal(plano.Paginas.Count, doc.NumberOfPages);
            string texto = string.Join(" ", doc.GetPages().SelectMany(p => p.GetWords()).Select(w => w.Text));
            Assert.Contains("Contratada", texto);
            Assert.Contains("Fulano", texto);
            Assert.DoesNotContain("UNIDADE", texto);
        }

        Assert.Throws<IOException>(() => GeradorRelatorioPdf.Gerar(plano, pdf));
        Assert.Empty(Directory.GetFiles(_pasta, "*.spobras-parcial"));
    }
}
