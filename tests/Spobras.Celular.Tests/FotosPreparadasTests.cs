using Spobras.Celular.Servicos;
using Spobras.Core.Relatorio;
using Spobras.Documentos.Imagens;
using Spobras.Documentos.Relatorio;
using Spobras.Testes;

namespace Spobras.Celular.Tests;

/// <summary>
/// Fotos no tamanho do relatorio preparadas em segundo plano: gerar fica so montar o arquivo. Elas acompanham
/// giro, remocao e descarte, e nunca mudam o resultado.
/// </summary>
public sealed class FotosPreparadasTests : IDisposable
{
    private static readonly DateTimeOffset Hoje = new(2026, 11, 16, 10, 0, 0, TimeSpan.FromHours(-3));
    private readonly string _raiz = Directory.CreateTempSubdirectory("spobras-preparadas-").FullName;

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

    private RascunhoRelatorio ComFotos(int n)
    {
        var r = new RascunhoRelatorio(Path.Combine(_raiz, "app"), () => Hoje);
        for (int i = 1; i <= n; i++)
        {
            r.AdicionarFoto(File.ReadAllBytes(AmostraCelular.Foto(Path.Combine(_raiz, "m"), $"f{i}.jpg", i % 2 == 0 ? 1200 : 2400, i % 2 == 0 ? 1600 : 1800, i)));
        }

        return r;
    }

    private static string[] Preparadas(RascunhoRelatorio r) =>
        Directory.Exists(r.PastaDasPreparadas) ? [.. Directory.GetFiles(r.PastaDasPreparadas).Select(Path.GetFileName).Order()!] : [];

    [Fact]
    public async Task Prepara_cada_foto_em_segundo_plano_igual_a_preparar_na_hora()
    {
        var r = ComFotos(5);
        await r.MiniaturasProntasAsync(TimeSpan.FromSeconds(60));
        Assert.Equal(5, Preparadas(r).Length);
        foreach (var f in r.Dados.Fotos)
        {
            string arquivo = Path.Combine(r.PastaDasPreparadas, PreparoDeFotos.NomeNoCache(f.Arquivo, 0, 1600, 82));
            Assert.Equal(Fotos.Preparar(r.Caminho(f), 1600, 82), File.ReadAllBytes(arquivo));
        }
    }

    [Fact]
    public async Task Girar_tirar_e_descartar_levam_as_preparadas_junto()
    {
        var r = ComFotos(3);
        await r.MiniaturasProntasAsync(TimeSpan.FromSeconds(60));
        string primeira = Path.GetFileNameWithoutExtension(r.Dados.Fotos[0].Arquivo);

        r.Girar(0);
        await r.MiniaturasProntasAsync(TimeSpan.FromSeconds(60));
        Assert.Contains($"{primeira}-90-1600-82.jpg", Preparadas(r));
        Assert.DoesNotContain($"{primeira}-0-1600-82.jpg", Preparadas(r));

        r.Remover([0]);
        Assert.Equal(3, Preparadas(r).Length); // durante o Desfazer continuam
        r.FinalizarRemocoes();
        Assert.Equal(2, Preparadas(r).Length);

        r.Recomecar();
        await r.MiniaturasProntasAsync(TimeSpan.FromSeconds(30));
        Assert.Empty(Preparadas(r));
    }

    [Fact]
    public async Task Ao_abrir_sobras_saem_e_o_que_falta_e_preparado()
    {
        var r = ComFotos(2);
        r.PrepararEmSegundoPlano = false;
        await r.MiniaturasProntasAsync(TimeSpan.FromSeconds(60));
        Directory.CreateDirectory(r.PastaDasPreparadas);
        File.WriteAllText(Path.Combine(r.PastaDasPreparadas, "nao-existe-0-1600-82.jpg"), "x");
        File.WriteAllText(Path.Combine(r.PastaDasPreparadas, "meia.jpg.abc.tmp"), "x");
        File.WriteAllText(Path.Combine(r.PastaDasPreparadas, Path.GetFileNameWithoutExtension(r.Dados.Fotos[0].Arquivo) + "-180-1600-82.jpg"), "giro velho");

        var reaberto = new RascunhoRelatorio(Path.Combine(_raiz, "app"), () => Hoje);
        await reaberto.MiniaturasProntasAsync(TimeSpan.FromSeconds(60));
        Assert.Equal(reaberto.Dados.Fotos.Select(f => PreparoDeFotos.NomeNoCache(f.Arquivo, 0, 1600, 82)).Order(), Preparadas(reaberto));
    }

    [Fact]
    public async Task Gerar_com_as_preparadas_da_o_mesmo_relatorio_que_sem()
    {
        var p = new PlataformaDeTeste(_raiz);
        AmostraCelular.GravarMestra(p.Mestra);
        var app = new AppCelular(p, () => Hoje);
        Assert.True(await app.Base.EscolherPlanilhaAsync());
        for (int i = 1; i <= 7; i++) app.Rascunho.AdicionarFoto(File.ReadAllBytes(AmostraCelular.Foto(Path.Combine(_raiz, "m"), $"g{i}.jpg", 2000, 1500, i)));
        app.Rascunho.Girar(2);
        app.Configurar(c => c with { Pasta = new Escolhido("pasta:" + p.Saida, "R"), Formato = FormatoGerado.Pdf, PastaPorEscola = false });

        app.Rascunho.PrepararEmSegundoPlano = false;
        await app.Rascunho.MiniaturasProntasAsync(TimeSpan.FromSeconds(60));
        foreach (var f in Directory.GetFiles(app.Rascunho.PastaDasPreparadas)) File.Delete(f);
        var sem = await app.Rascunho.GerarAsync(p, app.Config);

        app.Rascunho.PrepararEmSegundoPlano = true;
        app.Rascunho.Girar(0); app.Rascunho.Girar(0); app.Rascunho.Girar(0); app.Rascunho.Girar(0);
        await app.Rascunho.MiniaturasProntasAsync(TimeSpan.FromSeconds(60));
        var com = await app.Rascunho.GerarAsync(p, app.Config);

        // as imagens dentro do PDF sao as mesmas (o PDF muda so a data de criacao)
        static List<byte[]> Imagens(string pdf)
        {
            using var doc = UglyToad.PdfPig.PdfDocument.Open(pdf);
            return [.. doc.GetPages().SelectMany(pg => pg.GetImages()).Select(i => i.RawBytes.ToArray())];
        }

        var a = Imagens(sem.Locais[0]);
        var b = Imagens(com.Locais[0]);
        Assert.Equal(a.Count, b.Count);
        for (int i = 0; i < a.Count; i++) Assert.Equal(a[i], b[i]);
    }

    [Fact]
    public async Task Mexer_nas_fotos_durante_a_geracao_nao_quebra_nem_perde_foto()
    {
        var p = new PlataformaDeTeste(_raiz);
        AmostraCelular.GravarMestra(p.Mestra);
        var app = new AppCelular(p, () => Hoje);
        Assert.True(await app.Base.EscolherPlanilhaAsync());
        for (int i = 1; i <= 24; i++) app.Rascunho.AdicionarFoto(File.ReadAllBytes(AmostraCelular.Foto(Path.Combine(_raiz, "m"), $"h{i}.jpg", 2400, 1800, i)));
        app.Configurar(c => c with { Pasta = new Escolhido("pasta:" + p.Saida, "R"), PastaPorEscola = false });

        for (int rodada = 0; rodada < 3; rodada++)
        {
            var gerar = app.Rascunho.GerarAsync(p, app.Config);
            var sorte = new Random(rodada);
            while (!gerar.IsCompleted)
            {
                int n = app.Rascunho.Dados.Fotos.Count;
                app.Rascunho.Girar(sorte.Next(n));
                app.Rascunho.Legendar(sorte.Next(n), "durante " + rodada);
                await Task.Delay(5);
            }

            var r = await gerar;
            Assert.Equal(24, r.Fotos);
        }

        app.Rascunho.Remover([0, 1]);
        app.Rascunho.FinalizarRemocoes();
        await app.Rascunho.MiniaturasProntasAsync(TimeSpan.FromSeconds(120));
        Assert.Equal(22, Preparadas(app.Rascunho).Length); // uma preparada por foto, no giro atual
        Assert.All(app.Rascunho.Dados.Fotos, f => Assert.Contains(PreparoDeFotos.NomeNoCache(f.Arquivo, f.Rotacao, 1600, 82), Preparadas(app.Rascunho)));
    }

    [Fact]
    public void Preparo_em_paralelo_avisa_em_ordem_e_a_foto_estragada_da_o_mesmo_erro()
    {
        string pasta = Path.Combine(_raiz, "fotos");
        var fotos = Enumerable.Range(1, 12).Select(i => new FotoRelatorio(AmostraCelular.Foto(pasta, $"p{i}.jpg", 1600, 1200, i), 1600, 1200)).ToList();
        var plano = PaginadorRelatorio.Paginar(fotos, new OpcoesRelatorio());
        var avisos = new List<int>();
        new PreparoDeFotos().PrepararTodas(plano, new Anotar(avisos));
        Assert.Equal(Enumerable.Range(1, 12), avisos);

        File.WriteAllText(fotos[5].Caminho, "estragou");
        Assert.Throws<FotoInvalidaException>(() => new PreparoDeFotos().PrepararTodas(plano));
    }

    private sealed class Anotar(List<int> valores) : IProgress<int>
    {
        public void Report(int value) => valores.Add(value);
    }
}
