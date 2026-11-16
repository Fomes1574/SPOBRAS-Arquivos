using System.Diagnostics;
using SkiaSharp;
using Spobras.Celular.Servicos;
using Spobras.Core.Base;
using Spobras.Testes;

namespace Spobras.Celular.Tests;

/// <summary>
/// Estresse do relatorio fotografico com 300 fotos do tamanho de uma camera de celular (12 MP, com ruido
/// para pesarem como fotos reais). Demora e usa cerca de 1,5 GB de disco, por isso so roda quando pedido:
/// SPOBRAS_ESTRESSE=1 dotnet test --project tests/Spobras.Celular.Tests --filter-class "*EstresseTests"
/// </summary>
[Collection(nameof(DesempenhoTests))]
public sealed class EstresseTests : IDisposable
{
    private const int Total = 300;
    private static readonly DateTimeOffset Hoje = new(2026, 11, 16, 10, 0, 0, TimeSpan.FromHours(-3));
    private readonly string _raiz = Directory.CreateTempSubdirectory("spobras-estresse-").FullName;

    public void Dispose()
    {
        for (int tentativa = 0; ; tentativa++)
        {
            try
            {
                Directory.Delete(_raiz, recursive: true);
                return;
            }
            catch (IOException) when (tentativa < 30)
            {
                Thread.Sleep(100);
            }
        }
    }

    private static void Anotar(string texto) => TestContext.Current.TestOutputHelper?.WriteLine(texto);

    /// <summary>Foto 4000x3000 (ou deitada/em pe) com ruido: o JPEG fica com 3 a 5 MB, como os de um celular.</summary>
    private static string FotoDeCamera(string pasta, string nome, bool emPe, int semente)
    {
        int l = emPe ? 3000 : 4000, a = emPe ? 4000 : 3000;
        using var bmp = new SKBitmap(l, a, SKColorType.Rgba8888, SKAlphaType.Opaque);
        var px = bmp.GetPixelSpan();
        var sorte = new Random(semente);
        for (int y = 0; y < a; y++)
        {
            int baseR = y < a * 0.55 ? 150 + y * 50 / a : 160, baseG = y < a * 0.55 ? 175 : 140, baseB = y < a * 0.55 ? 200 : 110;
            for (int x = 0; x < l; x++)
            {
                int i = (y * l + x) * 4;
                int ruido = sorte.Next(-18, 19);
                px[i] = (byte)Math.Clamp(baseR + ruido + x * 30 / l, 0, 255);
                px[i + 1] = (byte)Math.Clamp(baseG + ruido, 0, 255);
                px[i + 2] = (byte)Math.Clamp(baseB + ruido, 0, 255);
                px[i + 3] = 255;
            }
        }

        Directory.CreateDirectory(pasta);
        string caminho = Path.Combine(pasta, nome);
        using var dados = bmp.Encode(SKEncodedImageFormat.Jpeg, 90);
        File.WriteAllBytes(caminho, dados.ToArray());
        return caminho;
    }

    /// <summary>Acompanha a memoria do processo enquanto a acao roda e devolve o pico (MB).</summary>
    private static async Task<(long Ms, long PicoMb)> ComMemoria(Func<Task> acao)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        using var processo = Process.GetCurrentProcess();
        long pico = 0;
        using var fim = new CancellationTokenSource();
        var vigia = Task.Run(async () =>
        {
            while (!fim.IsCancellationRequested)
            {
                processo.Refresh();
                pico = Math.Max(pico, processo.WorkingSet64);
                await Task.Delay(20);
            }
        });
        var tempo = Stopwatch.StartNew();
        await acao();
        tempo.Stop();
        await fim.CancelAsync();
        await vigia;
        return (tempo.ElapsedMilliseconds, pico / (1024 * 1024));
    }

    [Fact]
    public async Task Trezentas_fotos_de_camera_do_rascunho_ao_relatorio()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("SPOBRAS_ESTRESSE") == "1", "Só roda com SPOBRAS_ESTRESSE=1.");

        var p = new PlataformaDeTeste(_raiz);
        AmostraCelular.GravarMestra(p.Mestra);
        var app = new AppCelular(p, () => Hoje);
        Assert.True(await app.Base.EscolherPlanilhaAsync());
        app.Rascunho.AplicarObra(app.Base.Buscar(new FiltroObras { Contrato = ["044/EX/2026"] }, 1).Obras[0]);
        app.Configurar(c => c with { Pasta = new Escolhido("pasta:" + p.Saida, "Relatórios") });
        var r = app.Rascunho;

        // 12 fotos diferentes, repetidas ate 300 (o app trata cada copia como foto propria)
        var tempo = Stopwatch.StartNew();
        string galeria = Path.Combine(_raiz, "galeria");
        var modelos = Enumerable.Range(0, 12).Select(i => FotoDeCamera(galeria, $"m{i}.jpg", i % 4 == 3, i)).ToList();
        long mbPorFoto = modelos.Sum(m => new FileInfo(m).Length) / modelos.Count / 1024;
        Anotar($"fotos de teste prontas em {tempo.ElapsedMilliseconds} ms; cada uma com cerca de {mbPorFoto} KB");

        var copias = Enumerable.Range(1, Total).Select(i =>
        {
            string d = Path.Combine(r.PastaDasFotos, $"IMG_{i:0000}.jpg");
            File.Copy(modelos[i % modelos.Count], d);
            return d;
        }).ToList();

        tempo.Restart();
        Assert.Empty(r.AdicionarCopiadas(copias).Recusadas);
        Anotar($"entrar no rascunho (300 da galeria, sem esperar miniaturas): {tempo.ElapsedMilliseconds} ms");

        tempo.Restart();
        await r.MiniaturasProntasAsync(TimeSpan.FromMinutes(10));
        Assert.Equal(Total, Directory.GetFiles(r.PastaDasMiniaturas, "*.jpg").Length);
        Anotar($"300 miniaturas e fotos do relatório preparadas em segundo plano: {tempo.ElapsedMilliseconds / 1000.0:0.0} s ({tempo.ElapsedMilliseconds / Total} ms cada, um núcleo só)");

        tempo.Restart();
        for (int i = 0; i < Total; i++)
        {
            r.Legendar(i, $"Foto {i + 1} da vistoria, ambiente {i % 17 + 1}");
        }

        Anotar($"escrever 300 legendas (cada uma gravada na hora): {tempo.ElapsedMilliseconds} ms; rascunho com {new FileInfo(Path.Combine(_raiz, "app", "rascunho", "rascunho.json")).Length / 1024} KB");

        tempo.Restart();
        var reaberto = new RascunhoRelatorio(Path.Combine(_raiz, "app"), () => Hoje);
        Assert.All(reaberto.Dados.Fotos, f => Assert.NotEmpty(reaberto.Miniatura(f)));
        Anotar($"reabrir o app e pegar as 300 miniaturas: {tempo.ElapsedMilliseconds} ms");

        tempo.Restart();
        var plano = reaberto.Plano();
        Anotar($"conferir as páginas: {tempo.ElapsedMilliseconds} ms ({plano.Paginas.Count} páginas, {reaberto.Dados.FotosPorPagina} por página)");

        tempo.Restart();
        var sorte = new Random(3);
        for (int rodada = 0; rodada < 20; rodada++)
        {
            reaberto.Remover(Enumerable.Range(0, reaberto.Dados.Fotos.Count).OrderBy(_ => sorte.Next()).Take(30));
            reaberto.Desfazer();
        }

        Assert.Equal(Total, reaberto.Dados.Fotos.Count);
        Assert.Equal(Enumerable.Range(1, Total).Select(i => $"Foto {i} da vistoria, ambiente {(i - 1) % 17 + 1}"), reaberto.Dados.Fotos.Select(f => f.Legenda));
        Anotar($"20 rodadas de tirar 30 fotos e desfazer: {tempo.ElapsedMilliseconds} ms");

        // 1) sem nada preparado (o usuario gerou logo depois de colocar as fotos)
        app.Rascunho.PrepararEmSegundoPlano = false;
        reaberto.PrepararEmSegundoPlano = false;
        await app.Rascunho.MiniaturasProntasAsync(TimeSpan.FromMinutes(10));
        await reaberto.MiniaturasProntasAsync(TimeSpan.FromMinutes(10));
        foreach (var f in Directory.GetFiles(app.Rascunho.PastaDasPreparadas)) File.Delete(f);
        foreach (var (formato, nome) in new[] { (FormatoGerado.Excel, "Excel"), (FormatoGerado.Pdf, "PDF"), (FormatoGerado.ExcelEPdf, "Excel e PDF") })
        {
            if (formato == FormatoGerado.ExcelEPdf)
            {
                foreach (var f in Directory.GetFiles(app.Rascunho.PastaDasPreparadas)) File.Delete(f);
            }

            RelatorioPronto? pronto = null;
            var (ms, pico) = await ComMemoria(async () => pronto = await app.Rascunho.GerarAsync(p, app.Config with { Formato = formato, PastaPorEscola = false }));
            long tamanho = new FileInfo(pronto!.Locais[0]).Length / (1024 * 1024);
            Anotar($"gerar {nome} (fotos não preparadas antes): {ms / 1000.0:0.0} s, arquivo de {tamanho} MB, memória no pico: {pico} MB");
            Assert.Equal(Total, pronto.Fotos);
        }

        // 2) com as fotos ja preparadas em segundo plano (o normal: prepara enquanto o usuario escreve as legendas)
        app.Rascunho.PrepararEmSegundoPlano = true;
        tempo.Restart();
        app.Rascunho.Girar(0); // acorda o segundo plano
        app.Rascunho.Girar(0); app.Rascunho.Girar(0); app.Rascunho.Girar(0);
        await app.Rascunho.MiniaturasProntasAsync(TimeSpan.FromMinutes(10));
        Assert.Equal(Total, Directory.GetFiles(app.Rascunho.PastaDasPreparadas, "*.jpg").Length);
        var (msPronto, picoPronto) = await ComMemoria(async () => await app.Rascunho.GerarAsync(p, app.Config with { Formato = FormatoGerado.ExcelEPdf, PastaPorEscola = false }));
        Anotar($"gerar Excel e PDF com as fotos já preparadas: {msPronto / 1000.0:0.0} s, memória no pico: {picoPronto} MB");
    }
}
