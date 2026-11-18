using System.Text.Json;
using System.Text.Json.Nodes;
using Spobras.Celular.Servicos;
using Spobras.Core.Base;
using Spobras.Testes;

namespace Spobras.Celular.Tests;

/// <summary>
/// Defeitos achados na revisao das fotos do relatorio (16/11/2026): cada teste falhava no codigo de antes.
/// Foco em nunca perder foto: mudancas ao mesmo tempo, rascunho estragado, app fechado no meio.
/// </summary>
public sealed class RascunhoRobustezTests : IDisposable
{
    private static readonly DateTimeOffset Hoje = new(2026, 11, 16, 10, 0, 0, TimeSpan.FromHours(-3));
    private readonly string _raiz = Directory.CreateTempSubdirectory("spobras-robustez-").FullName;
    private readonly byte[] _jpeg;

    public RascunhoRobustezTests()
    {
        _jpeg = File.ReadAllBytes(AmostraCelular.Foto(Path.Combine(_raiz, "modelo"), "a.jpg", 800, 600, 1));
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

    private string App => Path.Combine(_raiz, "app");
    private RascunhoRelatorio Novo() => new(App, () => Hoje);
    private string Json => Path.Combine(App, "rascunho", "rascunho.json");

    /// <summary>Cada arquivo de foto aparece uma vez so: na lista ou esperando o Desfazer. Nenhum arquivo solto.</summary>
    private static void SemFotoPerdida(RascunhoRelatorio r)
    {
        var nomes = r.Dados.Fotos.Select(f => f.Arquivo).Concat(r.Dados.Removidas.Select(x => x.Foto.Arquivo)).ToList();
        Assert.Equal(nomes.Count, nomes.Distinct().Count());
        var arquivos = Directory.GetFiles(r.PastaDasFotos).Select(f => Path.GetFileName(f)!).Order().ToList();
        Assert.Equal(arquivos, nomes.Order().ToList());
    }

    [Fact]
    public async Task Ordenar_enquanto_a_camera_grava_nao_perde_nenhuma_foto()
    {
        var r = Novo();
        var camera = Task.Run(() => { for (int i = 0; i < 150; i++) r.AdicionarFoto(_jpeg); });
        while (!camera.IsCompleted)
        {
            r.OrdenarFotos(porData: true);
            r.OrdenarFotos(porData: false);
        }

        await camera;
        Assert.Equal(150, r.Dados.Fotos.Count);
        SemFotoPerdida(r);
        Assert.Equal(150, Novo().Dados.Fotos.Count); // e continuam la ao reabrir
    }

    [Fact]
    public async Task Atualizar_dados_enquanto_a_camera_grava_nao_perde_nenhuma_foto()
    {
        var p = new PlataformaDeTeste(_raiz);
        AmostraCelular.GravarMestra(p.Mestra);
        var app = new AppCelular(p, () => Hoje);
        Assert.True(await app.Base.EscolherPlanilhaAsync());
        var obra = app.Base.Buscar(new FiltroObras { Contrato = ["044/EX/2026"] }, 1).Obras[0];
        var camera = Task.Run(() => { for (int i = 0; i < 150; i++) app.Rascunho.AdicionarFoto(_jpeg); });
        while (!camera.IsCompleted)
        {
            app.Rascunho.AplicarObra(obra);
        }

        await camera;
        Assert.Equal(150, app.Rascunho.Dados.Fotos.Count);
        Assert.Equal("044/EX/2026", app.Rascunho.Dados.Contrato);
    }

    [Fact]
    public async Task Tudo_ao_mesmo_tempo_camera_galeria_girar_mover_tirar_desfazer_e_ordenar()
    {
        var r = Novo();
        var sorte = new Random(11);
        var camera = Task.Run(() => { for (int i = 0; i < 120; i++) r.AdicionarFoto(_jpeg); });
        var galeria = Task.Run(() =>
        {
            for (int i = 0; i < 20; i++)
            {
                string f = AmostraCelular.Foto(r.PastaDasFotos, $"g{i:00}.jpg", 640, 480, 100 + i);
                r.AdicionarCopiadas([f]);
            }
        });
        int removidasDeVez = 0;
        while (!camera.IsCompleted || !galeria.IsCompleted)
        {
            int n = r.Dados.Fotos.Count;
            if (n < 3) continue;
            switch (sorte.Next(6))
            {
                case 0: r.Girar(sorte.Next(n)); break;
                case 1: r.Mover(sorte.Next(n), sorte.Next(n)); break;
                case 2: r.Legendar(sorte.Next(n), "L" + sorte.Next(1000)); break;
                case 3: r.OrdenarFotos(sorte.Next(2) == 0); break;
                case 4: r.Remover([sorte.Next(n), sorte.Next(n)]); r.Desfazer(); break;
                default:
                    removidasDeVez += r.Remover([sorte.Next(n)]);
                    r.FinalizarRemocoes();
                    break;
            }
        }

        await Task.WhenAll(camera, galeria);
        Assert.Equal(140 - removidasDeVez, r.Dados.Fotos.Count);
        SemFotoPerdida(r);
    }

    [Fact]
    public void Rascunho_estragado_volta_da_copia_anterior()
    {
        var r = Novo();
        for (int i = 0; i < 5; i++)
        {
            r.AdicionarFoto(_jpeg);
            r.Legendar(i, $"L{i + 1}");
        }

        File.WriteAllText(Json, File.ReadAllText(Json)[..40]); // arquivo cortado
        var reaberto = Novo();
        Assert.Equal(5, reaberto.Dados.Fotos.Count);
        Assert.Equal(["L1", "L2", "L3", "L4", ""], reaberto.Dados.Fotos.Select(f => f.Legenda)); // so a ultima mudanca se perde
        Assert.Contains("recuperado", reaberto.AvisoDeRecuperacao);
        SemFotoPerdida(reaberto);
    }

    [Fact]
    public void Rascunho_e_copia_estragados_nao_apagam_as_fotos()
    {
        var r = Novo();
        for (int i = 0; i < 5; i++) r.AdicionarFoto(_jpeg);
        File.WriteAllText(Json, "{ isto não é json");
        File.WriteAllText(Json + ".bak", "");
        var reaberto = Novo();
        Assert.Equal(5, reaberto.Dados.Fotos.Count); // antes: 0 fotos e os arquivos apagados
        Assert.Contains("fotos foram recuperadas", reaberto.AvisoDeRecuperacao);
        SemFotoPerdida(reaberto);
    }

    [Fact]
    public void Rascunho_com_campos_vazios_ou_estranhos_abre_sem_erro()
    {
        var r = Novo();
        r.AdicionarFoto(_jpeg);
        r.AdicionarFoto(_jpeg);
        var json = JsonNode.Parse(File.ReadAllText(Json))!.AsObject();
        json["Unidade"] = null;
        json["FotosPorPagina"] = 7;
        var fotos = json["Fotos"]!.AsArray();
        fotos[0]!["Legenda"] = null;
        fotos[0]!["Rotacao"] = 450;
        fotos.Add(JsonNode.Parse(fotos[1]!.ToJsonString())); // repetida
        fotos.Add(new JsonObject { ["Arquivo"] = "../../fora.jpg" });
        fotos.Add(null);
        json["Removidas"] = null;
        File.WriteAllText(Json, json.ToJsonString());

        var reaberto = Novo();
        Assert.Equal(2, reaberto.Dados.Fotos.Count);
        Assert.Equal("", reaberto.Dados.Unidade);
        Assert.Equal("", reaberto.Dados.Fotos[0].Legenda);
        Assert.Equal(90, reaberto.Dados.Fotos[0].Rotacao);
        Assert.Equal(6, reaberto.Dados.FotosPorPagina);
        Assert.NotNull(reaberto.Plano());
    }

    [Fact]
    public void Foto_gravada_mas_fora_da_lista_volta_ao_abrir_e_lixo_sai()
    {
        var r = Novo();
        r.AdicionarFoto(_jpeg);
        // o app fechou depois de copiar da galeria e antes de pôr na lista; e uma gravacao ficou pela metade
        AmostraCelular.Foto(r.PastaDasFotos, "da-galeria.jpg", 640, 480, 7);
        File.WriteAllBytes(Path.Combine(r.PastaDasFotos, "x.jpg.tmp"), _jpeg[..100]);
        File.WriteAllText(Path.Combine(r.PastaDasFotos, "lixo.jpg"), "não é foto");

        var reaberto = Novo();
        Assert.Equal(2, reaberto.Dados.Fotos.Count);
        Assert.Equal("da-galeria.jpg", reaberto.Dados.Fotos[1].Arquivo);
        SemFotoPerdida(reaberto);
    }

    [Fact]
    public void Remocao_interrompida_termina_ao_abrir_sem_a_foto_voltar()
    {
        var r = Novo();
        for (int i = 0; i < 3; i++) r.AdicionarFoto(_jpeg);
        r.Remover([1]);
        string removida = r.Dados.Removidas[0].Foto.Arquivo;
        File.Delete(Path.Combine(r.PastaDasFotos, removida)); // o app apagou o arquivo e fechou antes de tirar da lista

        var reaberto = Novo();
        Assert.Equal(2, reaberto.Dados.Fotos.Count);
        Assert.Empty(reaberto.Dados.Removidas);
        Assert.DoesNotContain(reaberto.Dados.Fotos, f => f.Arquivo == removida);
        Assert.Equal(0, reaberto.Desfazer());
        SemFotoPerdida(reaberto);
    }

    [Fact]
    public async Task Descartar_com_miniaturas_na_fila_nao_deixa_sobras()
    {
        var r = Novo();
        for (int i = 0; i < 40; i++) r.AdicionarFoto(_jpeg);
        r.Recomecar();
        await r.MiniaturasProntasAsync(TimeSpan.FromSeconds(30));
        await Task.Delay(200);
        Assert.Empty(r.Dados.Fotos);
        Assert.Empty(Directory.GetFiles(r.PastaDasFotos));
        Assert.Empty(Directory.GetFiles(r.PastaDasMiniaturas));
        Assert.False(File.Exists(Json + ".bak"));
    }

    [Fact]
    public void Girar_varias_vezes_nao_acumula_miniaturas()
    {
        var r = Novo();
        r.AdicionarFoto(_jpeg);
        for (int i = 0; i < 8; i++)
        {
            r.Girar(0);
            Assert.NotEmpty(r.MiniaturaAgora(r.Dados.Fotos[0]));
        }

        Assert.True(Directory.GetFiles(r.PastaDasMiniaturas).Length <= 2);
    }

    [Fact]
    public void Tirar_ou_girar_uma_foto_nao_apaga_a_miniatura_de_outra_com_nome_parecido()
    {
        var r = Novo();
        r.AdicionarCopiadas([AmostraCelular.Foto(r.PastaDasFotos, "IMG.jpg", 640, 480, 1), AmostraCelular.Foto(r.PastaDasFotos, "IMG-0001.jpg", 640, 480, 2)]);
        r.MiniaturaAgora(r.Dados.Fotos[0]);
        r.MiniaturaAgora(r.Dados.Fotos[1]);
        string outra = Path.Combine(r.PastaDasMiniaturas, "IMG-0001-0.jpg");
        Assert.True(File.Exists(outra));
        r.Girar(0);
        r.Girar(0);
        r.Remover([0]);
        r.FinalizarRemocoes();
        Assert.True(File.Exists(outra));
        Assert.Equal("IMG-0001.jpg", Assert.Single(r.Dados.Fotos).Arquivo);
    }

    [Fact]
    public async Task Miniatura_que_falhou_por_um_instante_e_feita_de_novo()
    {
        var r = Novo();
        r.AdicionarFoto(_jpeg);
        await r.MiniaturasProntasAsync(TimeSpan.FromSeconds(10));
        var f = r.Dados.Fotos[0];
        // a gravacao da miniatura falha (no lugar do arquivo ha uma pasta)
        string bloqueio = Path.Combine(r.PastaDasMiniaturas, Path.GetFileNameWithoutExtension(f.Arquivo) + "-0.jpg");
        foreach (var m in Directory.GetFiles(r.PastaDasMiniaturas)) File.Delete(m);
        var limpo = Novo(); // sem nada na memoria
        Directory.CreateDirectory(bloqueio);
        Assert.Empty(limpo.MiniaturaAgora(f));
        Assert.False(limpo.SemPrevia(f));
        Directory.Delete(bloqueio);
        Assert.NotEmpty(limpo.MiniaturaAgora(f)); // antes: ficava em branco ate reiniciar o app
    }

    [Fact]
    public async Task Arquivo_que_nao_e_foto_fica_sem_previa_sem_travar()
    {
        var r = Novo();
        r.AdicionarFoto(_jpeg);
        // espera a miniatura em segundo plano: no Windows o arquivo aberto por ela nao pode ser trocado
        await r.MiniaturasProntasAsync(TimeSpan.FromSeconds(60));
        r.PrepararEmSegundoPlano = false;
        await r.MiniaturasProntasAsync(TimeSpan.FromSeconds(60));
        File.WriteAllText(r.Caminho(r.Dados.Fotos[0]), "estragou");
        foreach (var m in Directory.GetFiles(r.PastaDasMiniaturas)) File.Delete(m);
        var reaberto = Novo(); // sem a miniatura guardada
        reaberto.PrepararEmSegundoPlano = false;
        Assert.Empty(reaberto.MiniaturaAgora(reaberto.Dados.Fotos[0]));
        Assert.True(reaberto.SemPrevia(reaberto.Dados.Fotos[0]));
    }

    [Fact]
    public async Task Mesma_foto_da_galeria_duas_vezes_entra_com_aviso()
    {
        var p = new PlataformaDeTeste(_raiz);
        var r = new RascunhoRelatorio(p.PastaDoApp, () => Hoje);
        AmostraCelular.Foto(p.Entrada, "IMG_1.jpg", 1600, 1200, 1);
        AmostraCelular.Foto(p.Entrada, "IMG_2.jpg", 1600, 1200, 2);
        p.ProximaEscolha = [Path.Combine(p.Entrada, "IMG_1.jpg"), Path.Combine(p.Entrada, "IMG_2.jpg")];
        Assert.Empty(r.AdicionarCopiadas(await p.EscolherArquivosAsync("image/*", true, r.PastaDasFotos)).Avisos);

        p.ProximaEscolha = [Path.Combine(p.Entrada, "IMG_1.jpg")];
        var segunda = r.AdicionarCopiadas(await p.EscolherArquivosAsync("image/*", true, r.PastaDasFotos));
        Assert.Equal(3, r.Dados.Fotos.Count); // entra assim mesmo
        Assert.Equal(["IMG_1.jpg"], segunda.Repetidas);
        Assert.Contains("“IMG_1.jpg” já estava no relatório", Assert.Single(segunda.Avisos));
        Assert.Empty(segunda.Recusadas);

        // repetida dentro da mesma escolha tambem avisa
        p.ProximaEscolha = [Path.Combine(p.Entrada, "IMG_2.jpg"), Path.Combine(p.Entrada, "IMG_2.jpg")];
        var varias = r.AdicionarCopiadas(await p.EscolherArquivosAsync("image/*", true, r.PastaDasFotos));
        Assert.Equal(2, varias.Repetidas.Count);
        Assert.StartsWith("2 fotos já estavam no relatório", Assert.Single(varias.Avisos)); // um aviso so, nao um por foto
    }

    [Fact]
    public void Legenda_tem_no_maximo_100_e_descricao_300_caracteres()
    {
        var r = Novo();
        r.AdicionarFoto(_jpeg);
        r.AdicionarFoto(_jpeg);
        r.Legendar(0, new string('a', 1000));
        Assert.Equal(100, r.Dados.Fotos[0].Legenda.Length);
        r.Descrever(0, new string('d', 1000));
        Assert.Equal(300, r.Dados.Fotos[0].Descricao.Length);
        r.TextoEmVarias([1], new string('b', 500), new string('e', 900));
        Assert.Equal((100, 300), (r.Dados.Fotos[1].Legenda.Length, r.Dados.Fotos[1].Descricao.Length));
    }

    [Fact]
    public void Mover_marcadas_e_deitadas_primeiro_com_desfazer_sem_perder_foto()
    {
        var r = Novo();
        var deitada = _jpeg;
        var emPe = File.ReadAllBytes(AmostraCelular.Foto(Path.Combine(_raiz, "modelo"), "p.jpg", 600, 800, 2));
        for (int i = 0; i < 6; i++) r.AdicionarFoto(i % 3 == 1 ? emPe : deitada); // fotos 2 e 5 em pe
        r.Legendar(0, "primeira");
        string[] A() => [.. r.Dados.Fotos.Select(f => f.Arquivo)];
        var antes = A();

        Assert.Equal(2, r.MoverMarcadas([0, 3], paraOFim: true));
        Assert.Equal([antes[1], antes[2], antes[4], antes[5], antes[0], antes[3]], A());
        Assert.Equal(0, r.MoverMarcadas([4, 5], paraOFim: true)); // ja estao no fim: nada muda
        Assert.True(r.TemOrdemParaDesfazer);
        Assert.True(r.DesfazerOrdem());
        Assert.Equal(antes, A());
        Assert.Equal("primeira", r.Dados.Fotos[0].Legenda);
        Assert.False(r.DesfazerOrdem()); // so uma vez

        Assert.Equal(1, r.MoverMarcadas([4], paraOFim: false));
        Assert.Equal(antes[4], A()[0]);
        r.DesfazerOrdem();

        // deitadas primeiro, em pe no fim; com giro conta o formato girado
        r.Girar(0); // a primeira fica em pe
        Assert.Equal(3, r.EmPe);
        Assert.True(r.DeitadasPrimeiro());
        Assert.Equal([antes[2], antes[3], antes[5], antes[0], antes[1], antes[4]], A());
        Assert.False(r.DeitadasPrimeiro()); // de novo: nada muda

        // foto nova entra no meio do Desfazer: a ordem de antes volta e a nova fica no fim
        r.AdicionarFoto(deitada);
        string nova = A()[^1];
        Assert.True(r.DesfazerOrdem());
        Assert.Equal([.. antes, nova], A());

        // tirar uma foto depois de mover: o Desfazer da ordem deixa de valer
        r.MoverMarcadas([0], paraOFim: true);
        r.Remover(0);
        Assert.False(r.TemOrdemParaDesfazer);

        // reaberto do disco: a ordem nova ficou gravada
        Assert.Equal(A(), Novo().Dados.Fotos.Select(f => f.Arquivo));
    }

    [Fact]
    public async Task Reordenar_em_grupo_enquanto_a_camera_grava_nao_perde_nem_repete_foto()
    {
        var r = Novo();
        for (int i = 0; i < 40; i++) r.AdicionarFoto(_jpeg);
        var camera = Task.Run(() => { for (int i = 0; i < 60; i++) r.AdicionarFoto(_jpeg); });
        var sorte = new Random(3);
        while (!camera.IsCompleted)
        {
            int n = r.Dados.Fotos.Count;
            r.MoverMarcadas(Enumerable.Range(0, n).Where(_ => sorte.Next(4) == 0), sorte.Next(2) == 0);
            if (sorte.Next(3) == 0) r.DesfazerOrdem();
            r.DeitadasPrimeiro();
            await Task.Delay(1);
        }

        await camera;
        Assert.Equal(100, r.Dados.Fotos.Count);
        Assert.Equal(100, r.Dados.Fotos.Select(f => f.Arquivo).Distinct().Count());
    }

    [Fact]
    public void Mesmo_texto_em_varias_marcadas_ou_so_nas_em_branco_e_campo_vazio_nao_apaga()
    {
        var r = Novo();
        for (int i = 0; i < 4; i++) r.AdicionarFoto(_jpeg);
        r.Legendar(1, "Já tinha");
        r.Descrever(2, "Descrição já escrita");

        // sem marcadas: so onde o campo esta em branco
        Assert.Equal(4, r.AlvoDoTexto([], comLegenda: true, comDescricao: true));
        Assert.Equal(4, r.TextoEmVarias([], "Vista geral", "Serviço executado"));
        Assert.Equal(["Vista geral", "Já tinha", "Vista geral", "Vista geral"], r.Dados.Fotos.Select(f => f.Legenda));
        Assert.Equal(["Serviço executado", "Serviço executado", "Descrição já escrita", "Serviço executado"], r.Dados.Fotos.Select(f => f.Descricao));

        // com marcadas: troca nelas; campo vazio nao apaga
        Assert.Equal(2, r.TextoEmVarias([0, 3], "Fachada", ""));
        Assert.Equal(["Fachada", "Já tinha", "Vista geral", "Fachada"], r.Dados.Fotos.Select(f => f.Legenda));
        Assert.Equal("Serviço executado", r.Dados.Fotos[0].Descricao);
        Assert.Equal(0, r.TextoEmVarias([0], "  ", null));
        Assert.Equal(2, r.AlvoDoTexto([0, 1], true, false));

        // trocar de modo nao perde a descricao
        r.TrocarModo(Spobras.Core.Relatorio.ModoLegenda.DescricaoServico);
        Assert.Equal(0, r.DescricoesGuardadas);
        r.TrocarModo(Spobras.Core.Relatorio.ModoLegenda.LegendaCurta);
        Assert.Equal(4, r.DescricoesGuardadas);
        Assert.Equal("Descrição já escrita", r.Dados.Fotos[2].Descricao);
        var reaberto = Novo();
        Assert.Equal(Spobras.Core.Relatorio.ModoLegenda.LegendaCurta, reaberto.Dados.Modo);
        Assert.Equal("Descrição já escrita", reaberto.Dados.Fotos[2].Descricao);
    }

    [Fact]
    public void Foto_da_camera_avisa_a_tela()
    {
        var r = Novo();
        int avisos = 0;
        r.Mudou += () => Interlocked.Increment(ref avisos);
        r.AdicionarFoto(_jpeg);
        Assert.True(avisos >= 1);
    }

    private async Task<AppCelular> AppPronto(int fotos)
    {
        var p = new PlataformaDeTeste(_raiz);
        AmostraCelular.GravarMestra(p.Mestra);
        var app = new AppCelular(p, () => Hoje);
        Assert.True(await app.Base.EscolherPlanilhaAsync());
        app.Rascunho.AplicarObra(app.Base.Buscar(new FiltroObras { Contrato = ["044/EX/2026"] }, 1).Obras[0]);
        for (int i = 0; i < fotos; i++) app.Rascunho.AdicionarFoto(_jpeg);
        app.Configurar(c => c with { Pasta = new Escolhido("pasta:" + p.Saida, "Relatórios") });
        return app;
    }

    private sealed class Anotar(List<int> valores) : IProgress<int>
    {
        public void Report(int value)
        {
            lock (valores) valores.Add(value);
        }
    }

    [Fact]
    public async Task Progresso_de_excel_e_pdf_vai_ate_o_fim_sem_voltar()
    {
        var app = await AppPronto(9);
        var valores = new List<int>();
        await app.Rascunho.GerarAsync(new PlataformaDeTeste(_raiz), app.Config with { Formato = FormatoGerado.ExcelEPdf }, new Anotar(valores));
        Assert.Equal(Enumerable.Range(1, 18), valores); // antes: 1..9 e de novo 1..9
    }

    [Fact]
    public async Task Textos_no_limite_geram_e_cabecalho_enorme_explica_o_motivo()
    {
        var app = await AppPronto(4);
        var p = new PlataformaDeTeste(_raiz);
        for (int i = 0; i < 4; i++) app.Rascunho.Legendar(i, string.Join(" ", Enumerable.Repeat("legendalonga", 60)));
        app.Rascunho.Alterar(d => d with { Unidade = new string('U', 300), FotosPorPagina = 8 });
        var r = await app.Rascunho.GerarAsync(p, app.Config);
        Assert.All(r.Arquivos, a => Assert.True(a.Nome.Length <= 125, a.Nome));
        Assert.True(r.Onde.Length < 120, r.Onde); // a pasta da escola tambem // antes: caminho longo demais
        Assert.Equal(4, r.Fotos);

        app.Rascunho.Alterar(d => d with { Unidade = "EMEI Exemplo", Objeto = string.Join(" ", Enumerable.Repeat("texto", 3000)) });
        var erro = await Assert.ThrowsAsync<InvalidOperationException>(() => app.Rascunho.GerarAsync(p, app.Config));
        Assert.Contains("cabeçalho ficou grande demais", erro.Message); // antes: erro interno sem explicacao

        // legenda e descricao no limite (100 e 300): sempre cabem; a legenda digitada para alem do limite e cortada
        app.Rascunho.Alterar(d => d with { Objeto = "", Modo = Spobras.Core.Relatorio.ModoLegenda.DescricaoServico, FotosPorPagina = 8 });
        for (int i = 0; i < 4; i++)
        {
            app.Rascunho.Legendar(i, new string('L', 150));
            app.Rascunho.Descrever(i, new string('d', 400));
        }

        Assert.All(app.Rascunho.Dados.Fotos, f => Assert.Equal((100, 300), (f.Legenda.Length, f.Descricao.Length)));
        var r2 = await app.Rascunho.GerarAsync(p, app.Config);
        Assert.Equal(1, r2.Paginas); // com descricao, 8 por pagina vira 4
    }

    [Fact]
    public async Task Copia_do_relatorio_no_app_sai_depois_de_salvo_e_o_salvo_fica()
    {
        var app = await AppPronto(3);
        var p = new PlataformaDeTeste(_raiz);
        var r = await app.Rascunho.GerarAsync(p, app.Config);
        Assert.All(r.Locais, l => Assert.True(File.Exists(l)));
        app.Rascunho.LimparGerado();
        Assert.All(r.Locais, l => Assert.False(File.Exists(l)));
        Assert.All(r.Arquivos, a => Assert.True(File.Exists(a.Id[8..]))); // o salvo na pasta escolhida continua
    }

    [Fact]
    public async Task Excluir_relatorio_gerado_apaga_o_salvo_e_nao_toca_nas_fotos()
    {
        var app = await AppPronto(3);
        var p = (PlataformaDeTeste)app.Plataforma;
        var r = await app.Rascunho.GerarAsync(p, app.Config);
        app.Registrar(r.Arquivos);
        var fotos = Directory.GetFiles(app.Rascunho.PastaDasFotos).Length;
        var salvo = r.Arquivos[0];

        p.ApagarFalha = true;
        var erro = await app.ExcluirGeradoAsync(salvo);
        Assert.Contains("não deixa o app apagar", erro);
        Assert.Contains(app.Config.UltimosGerados, s => s.Id == salvo.Id); // nao sumiu da lista sem ter sido apagado

        p.ApagarFalha = false;
        Assert.Null(await app.ExcluirGeradoAsync(salvo));
        Assert.False(File.Exists(salvo.Id[8..]));
        Assert.DoesNotContain(app.Config.UltimosGerados, s => s.Id == salvo.Id);
        Assert.True(File.Exists(r.Arquivos[1].Id[8..]));
        Assert.Equal(fotos, Directory.GetFiles(app.Rascunho.PastaDasFotos).Length); // fotos intactas
    }

    [Fact]
    public async Task Gerar_duas_vezes_ao_mesmo_tempo_faz_um_e_explica_o_outro()
    {
        var app = await AppPronto(6);
        var p = new PlataformaDeTeste(_raiz);
        var a = app.Rascunho.GerarAsync(p, app.Config);
        var b = app.Rascunho.GerarAsync(p, app.Config);
        var erro = await Assert.ThrowsAsync<InvalidOperationException>(() => b);
        Assert.Contains("já está sendo gerado", erro.Message);
        Assert.Equal(6, (await a).Fotos);
        Assert.Equal(6, (await app.Rascunho.GerarAsync(p, app.Config)).Fotos); // depois, gera de novo normalmente
    }

    [Fact]
    public void Flash_comeca_desligado_e_quem_tinha_o_auto_padrao_passa_para_desligado()
    {
        string pasta = Path.Combine(_raiz, "pref");
        Assert.Equal(Flash.Desligado, new ArquivoPreferencias(pasta).Ler().Flash);

        Directory.CreateDirectory(pasta);
        File.WriteAllText(Path.Combine(pasta, "preferencias.json"), JsonSerializer.Serialize(new { Flash = 0 })); // Auto, nunca escolhido
        Assert.Equal(Flash.Desligado, new ArquivoPreferencias(pasta).Ler().Flash);

        var arq = new ArquivoPreferencias(pasta);
        arq.Gravar(arq.Ler() with { Flash = Flash.Auto, FlashEscolhido = true });
        arq.Gravar(arq.Ler() with { FormatoFoto = FormatoFoto.Quadrado }); // a copia guarda a gravacao anterior
        Assert.Equal(Flash.Auto, new ArquivoPreferencias(pasta).Ler().Flash); // escolha do usuario fica

        File.WriteAllText(Path.Combine(pasta, "preferencias.json"), "{ estragado");
        Assert.Equal(Flash.Auto, new ArquivoPreferencias(pasta).Ler().Flash); // volta da copia
    }
}
