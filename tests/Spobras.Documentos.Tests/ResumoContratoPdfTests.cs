using SkiaSharp;
using Spobras.Core.Base;
using Spobras.Documentos.Relatorio;
using UglyToad.PdfPig;

namespace Spobras.Documentos.Tests;

public sealed class ResumoContratoPdfTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("spobras-resumo-").FullName;
    private static readonly DateTimeOffset Agora = new(2026, 11, 16, 14, 12, 0, TimeSpan.FromHours(-3));
    public void Dispose() => Directory.Delete(_dir, true);

    private static ResumoContrato Contrato(int escolas, string codigo = "029/EX/2026", string? lote = "LOTE 207", string status = "VIGENTE")
    {
        var identificacao = new ObraLida { Contrato = codigo, Lote = lote, DataOs = new(2026, 1, 1), Termino = new(2027, 1, 1),
            Contratada = "Engenharia Exemplo", Portaria = "Portaria Exemplo", Modalidade = "Manutenção", StatusContrato = status };
        return new(identificacao, Enumerable.Range(1, escolas).Select(i => new EscolaResumo(identificacao with
        {
            Empreendimento = $"EMEI Exemplo {i:00}", TipoDeObra = "Reforma", ValorPago = 250,
            ValorEmpreendimento = 900, ValorAditado = 100, ValorContratadoComAditamento = 1000, SaldoAPagar = 750,
        }, [new("05", "Em análise", 100, 10, new(2026, 11, 1))])).ToArray());
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(6, 1)]
    [InlineData(13, 2)]
    public void Contrato_repete_cabecalho_e_total_so_na_ultima_pagina(int escolas, int paginas)
    {
        string arquivo = Path.Combine(_dir, $"contrato-{escolas}.pdf");
        GeradorResumoContratoPdf.Gerar(arquivo, [Contrato(escolas)], Agora, Agora);
        using var pdf = PdfDocument.Open(arquivo);
        Assert.Equal(paginas, pdf.NumberOfPages);
        Assert.All(pdf.GetPages(), p => Assert.Contains("Escolas do contrato", p.Text));
        Assert.Contains("Total do contrato", pdf.GetPage(paginas).Text);
        Assert.Contains($"EMEI Exemplo {escolas:00}", pdf.GetPage(paginas).Text);
        if (paginas > 1)
        {
            Assert.DoesNotContain("Total do contrato", pdf.GetPage(1).Text);
            Assert.Contains("(continuação)", pdf.GetPage(2).Text);
        }
        if (Environment.GetEnvironmentVariable("SPOBRAS_RESUMO_TESTE") is { } destino) File.Copy(arquivo, Path.Combine(destino, Path.GetFileName(arquivo)), true);
    }

    [Fact]
    public void Varios_contratos_saem_separados_e_prazo_respeita_status()
    {
        string arquivo = Path.Combine(_dir, "varios.pdf");
        var primeiro = Contrato(1, status: "ENCERRADO / TRD");
        var segundo = Contrato(1, "030/EX/2026", null, "NÃO INICIADO");
        segundo = segundo with { Identificacao = segundo.Identificacao with { DataOs = null, Termino = null },
            Escolas = [segundo.Escolas[0] with { Obra = segundo.Escolas[0].Obra with { ValorPago = 0 }, Medicoes = [] }] };
        GeradorResumoContratoPdf.Gerar(arquivo, [primeiro, segundo], Agora, Agora);
        using var pdf = PdfDocument.Open(arquivo);
        Assert.Equal(2, pdf.NumberOfPages);
        Assert.Contains("Encerrado em 01/01/2027", pdf.GetPage(1).Text);
        Assert.DoesNotContain("Vence", pdf.GetPage(1).Text);
        Assert.DoesNotContain("030/EX/2026", pdf.GetPage(1).Text);
        Assert.Contains("OS não emitida", pdf.GetPage(2).Text);
        Assert.Contains("Sem pagamento nem medição", pdf.GetPage(2).Text);
        Assert.DoesNotContain("029/EX/2026", pdf.GetPage(2).Text);
        Assert.Equal("Resumo do contrato 030-EX-2026 - 2026-11-16.pdf", GeradorResumoContratoPdf.Nome([segundo], Agora));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Escola_tem_uma_pagina_com_valores_medicoes_e_logo_opcional(bool comLogo)
    {
        string arquivo = Path.Combine(_dir, "escola.pdf");
        string? logo = null;
        if (comLogo)
        {
            using var bitmap = new SKBitmap(35, 25);
            bitmap.Erase(SKColors.Red);
            using var imagem = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            logo = Path.Combine(_dir, "logo.png");
            File.WriteAllBytes(logo, imagem.ToArray());
        }
        var escola = Contrato(1).Escolas[0];
        GeradorResumoContratoPdf.Gerar(arquivo, escola, Agora, Agora, logo);
        using var pdf = PdfDocument.Open(arquivo);
        Assert.Equal(1, pdf.NumberOfPages);
        var pagina = pdf.GetPage(1);
        Assert.Contains("Resumo da obra", pagina.Text);
        Assert.Contains("Medições em trâmite", pagina.Text);
        Assert.Equal(comLogo ? 1 : 0, pagina.GetImages().Count());
        Assert.Equal("Resumo da obra - EMEI Exemplo 01 - 2026-11-16.pdf", GeradorResumoContratoPdf.Nome(escola, Agora));
        Assert.Throws<IOException>(() => GeradorResumoContratoPdf.Gerar(arquivo, escola, Agora, Agora, logo));
    }
}
