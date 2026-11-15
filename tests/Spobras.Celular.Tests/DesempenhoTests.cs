using System.Diagnostics;
using Spobras.Celular.Servicos;
using Spobras.Core.Base;
using Spobras.Dados.Base;
using Spobras.Testes;

namespace Spobras.Celular.Tests;

/// <summary>
/// Desempenho e uso intenso com uma base grande inventada (2.000 obras, 8.000 medicoes) e 60 fotos. Os tempos
/// maximos tem folga para maquinas lentas de CI; se passarem disso, algo ficou travando de verdade.
/// Roda sozinha (sem outros testes ao mesmo tempo) para medir sem disputa de processador.
/// </summary>
[Collection(nameof(DesempenhoTests))]
[CollectionDefinition(nameof(DesempenhoTests), DisableParallelization = true)]
public sealed class DesempenhoTests : IDisposable
{
    private static readonly DateTimeOffset Hoje = new(2026, 11, 15, 10, 0, 0, TimeSpan.FromHours(-3));
    private readonly string _raiz = Directory.CreateTempSubdirectory("spobras-desempenho-").FullName;
    private readonly PlataformaDeTeste _p;

    public DesempenhoTests()
    {
        _p = new PlataformaDeTeste(_raiz);
        XlsxSintetico.Gravar(AmostraCelular.MestraGrande(2000), _p.Mestra);
    }

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

    /// <summary>Mede a melhor de 3 execucoes (a primeira pode pagar o aquecimento).</summary>
    private static long Medir(Action acao)
    {
        acao();
        long melhor = long.MaxValue;
        for (int i = 0; i < 3; i++)
        {
            var r = Stopwatch.StartNew();
            acao();
            melhor = Math.Min(melhor, r.ElapsedMilliseconds);
        }

        return melhor;
    }

    private static void NoMaximo(long ms, long limite, string acao) =>
        Assert.True(ms <= limite, $"{acao}: {ms} ms (máximo {limite} ms)");

    [Fact]
    public async Task Base_grande_le_rapido_e_cada_consulta_fica_abaixo_do_limite()
    {
        var app = new AppCelular(_p, () => Hoje);
        var leitura = Stopwatch.StartNew();
        await app.Base.EscolherPlanilhaAsync();
        NoMaximo(leitura.ElapsedMilliseconds, 30_000, "ler a planilha de 2.000 obras");
        Assert.Equal(2000, app.Base.TotalDeObras);

        var todas = new FiltroObras();
        var lote = new FiltroObras { Lote = ["LOTE 7"] };
        NoMaximo(Medir(() => app.Base.Buscar(todas, 40)), 150, "lista da Base (40 de 2.000)");
        NoMaximo(Medir(() => app.Base.Buscar(new FiltroObras { Texto = "exemplo 12" }, 40)), 150, "busca por texto");
        NoMaximo(Medir(() => app.Base.Opcoes(todas)), 400, "opções de todos os filtros");
        NoMaximo(Medir(() => app.Base.Totais(lote)), 400, "totais de um lote");
        var obra = app.Base.Buscar(todas, 1).Obras[0];
        NoMaximo(Medir(() => app.Base.EmTramite(obra)), 50, "medições em trâmite da ficha");
        NoMaximo(Medir(() => ProcuraObras.SugestoesDeEscola(app.Base.Repositorio, "exemplo 12")), 150, "sugestões de escola");
        NoMaximo(Medir(() => ProcuraObras.SugestoesDeContrato(app.Base.Repositorio, "012/")), 150, "sugestões de contrato");

        // SIURB nao aparece, e os totais batem com a soma das obras visiveis
        var (visiveis, total) = app.Base.Buscar(lote, int.MaxValue);
        Assert.Equal(total, visiveis.Count);
        Assert.DoesNotContain(visiveis, o => o.StatusContrato == "SOB GESTÃO DA SIURB");
        var t = app.Base.Totais(lote);
        Assert.Equal(visiveis.Sum(o => o.ValorPago ?? 0), t.Pago);
        Assert.Equal(t.Contratado, t.Pago + t.SaldoAPagar);
    }

    [Fact]
    public async Task Uso_intenso_de_filtros_da_sempre_o_mesmo_resultado_que_a_conta_direta()
    {
        var app = new AppCelular(_p, () => Hoje);
        await app.Base.EscolherPlanilhaAsync();
        var todas = app.Base.Buscar(new FiltroObras(), int.MaxValue).Obras;
        var opcoes = app.Base.Opcoes(new FiltroObras());
        var sorte = new Random(15);
        for (int i = 0; i < 300; i++)
        {
            var lotes = opcoes.Lotes.OrderBy(_ => sorte.Next()).Take(sorte.Next(0, 3)).ToList();
            var empresas = opcoes.Empresas.OrderBy(_ => sorte.Next()).Take(sorte.Next(0, 3)).ToList();
            var portarias = opcoes.Portarias.OrderBy(_ => sorte.Next()).Take(sorte.Next(0, 2)).ToList();
            var filtro = new FiltroObras { Lote = lotes, Empresa = empresas, Portaria = portarias };
            int esperado = todas.Count(o => (lotes.Count == 0 || lotes.Contains(o.Lote!)) && (empresas.Count == 0 || empresas.Contains(o.Contratada!))
                                            && (portarias.Count == 0 || portarias.Contains(o.Portaria!)));
            Assert.Equal(esperado, app.Base.Buscar(filtro, 40).Total);
        }
    }

    [Fact]
    public async Task Sessenta_fotos_abrem_na_hora_e_remover_e_desfazer_em_massa_nao_perdem_nada()
    {
        var r = new RascunhoRelatorio(Path.Combine(_raiz, "app"), () => Hoje);
        var origem = Path.Combine(_raiz, "galeria");
        var caminhos = Enumerable.Range(1, 60).Select(i => AmostraCelular.Foto(origem, $"f{i:00}.jpg", i % 3 == 0 ? 1200 : 1600, i % 3 == 0 ? 1600 : 1200, i)).ToList();
        var copiar = Stopwatch.StartNew();
        var copias = caminhos.Select(c => { string d = Path.Combine(r.PastaDasFotos, Path.GetFileName(c)); File.Copy(c, d); return d; }).ToList();
        Assert.Empty(r.AdicionarCopiadas(copias));
        NoMaximo(copiar.ElapsedMilliseconds, 15_000, "colocar 60 fotos no rascunho (sem esperar miniaturas)");
        for (int i = 0; i < 60; i++)
        {
            r.Legendar(i, $"L{i + 1}");
        }

        await r.MiniaturasProntasAsync(TimeSpan.FromSeconds(120));
        Assert.Equal(60, Directory.GetFiles(r.PastaDasMiniaturas, "*.jpg").Length);

        // reabrir o app: a tela de fotos pede as 60 miniaturas de uma vez
        var reaberto = new RascunhoRelatorio(Path.Combine(_raiz, "app"), () => Hoje);
        var abrir = Stopwatch.StartNew();
        Assert.All(reaberto.Dados.Fotos, f => Assert.NotEmpty(reaberto.Miniatura(f)));
        NoMaximo(abrir.ElapsedMilliseconds, 500, "abrir a tela com 60 fotos");
        NoMaximo(Medir(() => reaberto.Plano()), 300, "conferir as páginas com 60 fotos");

        // remover e desfazer em massa, 50 rodadas, sempre conferindo contra a lista esperada
        var sorte = new Random(7);
        var esperada = reaberto.Dados.Fotos.Select(f => f.Legenda).ToList();
        var tempo = Stopwatch.StartNew();
        for (int rodada = 0; rodada < 50; rodada++)
        {
            var indices = Enumerable.Range(0, reaberto.Dados.Fotos.Count).OrderBy(_ => sorte.Next()).Take(sorte.Next(1, 20)).ToList();
            reaberto.Remover(indices);
            Assert.Equal(esperada.Count - indices.Count, reaberto.Dados.Fotos.Count);
            if (rodada % 5 == 4)
            {
                foreach (int i in indices.OrderByDescending(i => i))
                {
                    esperada.RemoveAt(i);
                }

                reaberto.FinalizarRemocoes();
            }
            else
            {
                reaberto.Desfazer();
            }

            Assert.Equal(esperada, reaberto.Dados.Fotos.Select(f => f.Legenda));
        }

        NoMaximo(tempo.ElapsedMilliseconds, 15_000, "50 rodadas de remover e desfazer");
        var final = new RascunhoRelatorio(Path.Combine(_raiz, "app"), () => Hoje);
        Assert.Equal(esperada, final.Dados.Fotos.Select(f => f.Legenda));
        Assert.Equal(esperada.Count, Directory.GetFiles(final.PastaDasFotos).Length); // as removidas de vez sairam do disco
    }
}
