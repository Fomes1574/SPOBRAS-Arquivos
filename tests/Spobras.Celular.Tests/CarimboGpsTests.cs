using System.Security.Cryptography;
using Spobras.Celular.Servicos;
using Spobras.Core.Relatorio;
using Spobras.Documentos.Imagens;
using Spobras.Documentos.Relatorio;
using Spobras.Testes;

namespace Spobras.Celular.Tests;

/// <summary>
/// Celular: fotos "Salvando…", carimbo nas copias e na galeria, endereco pelo GPS guardado por escola e contrato, e a
/// regra de ouro: nenhuma dessas coisas mexe nas fotos e legendas do fiscal.
/// </summary>
public sealed class CarimboGpsTests : IDisposable
{
    private static readonly DateTimeOffset Hoje = new(2026, 11, 18, 14, 32, 0, TimeSpan.FromHours(-3));
    private readonly string _raiz = Directory.CreateTempSubdirectory("spobras-carimbo-gps-").FullName;
    private readonly PlataformaDeTeste _p;

    public CarimboGpsTests() => _p = new PlataformaDeTeste(_raiz);

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

    private static readonly PontoGps Escola = new(-23.5505, -46.6333, 12, Hoje);
    private const double MetroDeLatitude = 1 / 111_195.0;

    private AppCelular NovoApp() => new(_p, () => Hoje);

    private byte[] Jpeg(int n, int largura = 1600, int altura = 1200) =>
        File.ReadAllBytes(AmostraCelular.Foto(Path.Combine(_raiz, "modelos"), $"m{n}.jpg", largura, altura, n));

    private static void Escolher(AppCelular app, string unidade, string contrato) =>
        app.Rascunho.Alterar(d => d with { Unidade = unidade, Contrato = contrato });

    /// <summary>Impressao de tudo o que e do fiscal: cada foto do rascunho (conteudo) e cada legenda e descricao.</summary>
    private static string Impressao(RascunhoRelatorio r) => string.Join("\n", r.Dados.Fotos.Select(f =>
        $"{f.Arquivo}|{f.Legenda}|{f.Descricao}|{Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(r.Caminho(f))))}"));

    // ---- Salvando ----

    [Fact]
    public void Foto_salvando_aparece_na_hora_e_vira_foto_de_verdade_sem_duplicar()
    {
        var app = NovoApp();
        int avisos = 0;
        app.Rascunho.Mudou += () => avisos++;
        app.Camera.FotoPega(11, "data:image/jpeg;base64,AAA");
        app.Camera.FotoPega(12, "data:image/jpeg;base64,BBB");
        Assert.Equal([11L, 12L], app.Rascunho.Salvando.Select(s => s.Id));
        Assert.Equal(2, app.Rascunho.SalvandoAgora);
        Assert.True(avisos >= 2);

        app.Rascunho.AdicionarFoto(Jpeg(1), 11);
        Assert.Single(app.Rascunho.Dados.Fotos);
        Assert.Equal([12L], app.Rascunho.Salvando.Select(s => s.Id));

        // o aviso de "pega" que chega atrasado (depois da foto salva) nao cria um lugar fantasma
        app.Rascunho.AdicionarFoto(Jpeg(2), 13);
        app.Camera.FotoPega(13, "atrasado");
        Assert.DoesNotContain(app.Rascunho.Salvando, s => s.Id == 13);
        Assert.Equal(2, app.Rascunho.Dados.Fotos.Count);

        // a que falhou fica com o aviso, nao some; "tentar de novo" volta a salvando; "tirar" tira
        app.Camera.FalhaAoTirar(12, "sem espaço");
        Assert.Equal("sem espaço", app.Rascunho.Salvando.Single().Erro);
        Assert.Equal(0, app.Rascunho.SalvandoAgora);
        app.Rascunho.SalvarDeNovo(12);
        Assert.Equal(1, app.Rascunho.SalvandoAgora);
        app.Rascunho.FalhouAoSalvar(12, "de novo");
        app.Rascunho.EsquecerSalvando(12);
        Assert.Empty(app.Rascunho.Salvando);
    }

    [Fact]
    public async Task Gerar_e_novo_relatorio_esperam_a_foto_salvando()
    {
        var app = NovoApp();
        app.Camera.FotoPega(21, "");
        var espera = app.Rascunho.EsperarSalvarAsync(TimeSpan.FromSeconds(10));
        await Task.Delay(150);
        Assert.False(espera.IsCompleted);
        await app.Camera.FotoTirada(21, Jpeg(1), 5, 80);
        Assert.True(await espera);
        Assert.True(await new RascunhoRelatorio(Path.Combine(_raiz, "outro"), () => Hoje).EsperarSalvarAsync(TimeSpan.Zero)); // nada salvando: nao espera

        app.Camera.FotoPega(22, "");
        Assert.False(await app.Rascunho.EsperarSalvarAsync(TimeSpan.FromMilliseconds(100)));
        app.Camera.FalhaAoTirar(22, "erro");
        app.Rascunho.Recomecar(); // a que falhou era do relatorio que saiu
        Assert.Empty(app.Rascunho.Salvando);
    }

    [Fact]
    public async Task Fotos_seguidas_entram_todas_na_ordem_dos_toques_e_o_diagnostico_guarda_os_tempos()
    {
        var app = NovoApp();
        for (long id = 1; id <= 12; id++)
        {
            app.Camera.FotoPega(id, "");
        }

        for (long id = 1; id <= 12; id++)
        {
            await app.Camera.FotoTirada(id, Jpeg((int)id), 3, 40 + (int)id);
            app.Camera.Enviado(id, 25);
        }

        Assert.Equal(12, app.Rascunho.Dados.Fotos.Count);
        Assert.Empty(app.Rascunho.Salvando);
        var tempos = app.Camera.Tempos;
        Assert.Equal(12, tempos.Count);
        Assert.Equal(52, tempos[^1].Codificar);
        Assert.All(tempos, t => Assert.Equal(25, t.Enviar));
        Assert.All(tempos, t => Assert.Equal((1600, 1200), (t.Largura, t.Altura)));
        Assert.Equal(6, app.Camera.MaximoNaFila);
        _p.FotosAoMesmoTempo = 1;
        Assert.Equal(3, app.Camera.MaximoNaFila); // celular com pouca memoria: menos fotos esperando na memoria
    }

    // ---- Carimbo nas copias ----

    [Fact]
    public async Task Copia_carimbada_e_feita_em_segundo_plano_e_refeita_quando_o_carimbo_muda()
    {
        var app = NovoApp();
        Escolher(app, "EMEF Escola Exemplo", "044/EX/2026");
        for (int i = 1; i <= 3; i++) app.Rascunho.AdicionarFoto(Jpeg(i));
        app.Rascunho.Legendar(0, "Fachada");
        await app.Rascunho.MiniaturasProntasAsync(TimeSpan.FromSeconds(60));
        string[] Preparadas() => [.. Directory.GetFiles(app.Rascunho.PastaDasPreparadas).Select(Path.GetFileName).Order()!];
        Assert.All(Preparadas(), n => Assert.DoesNotContain("c", Path.GetFileNameWithoutExtension(n).Split('-')[^1]));
        string antes = Impressao(app.Rascunho);

        app.Carimbar(a => a with { Ligado = true });
        await app.Rascunho.MiniaturasProntasAsync(TimeSpan.FromSeconds(60));
        var carimbadas = Preparadas();
        Assert.Equal(3, carimbadas.Length); // as sem carimbo sairam: so a versao atual fica
        Assert.All(carimbadas, n => Assert.Matches("-0-1600-82c[0-9a-f]{10}\\.jpg$", n));
        var plano = app.Rascunho.Plano();
        Assert.All(plano.Paginas.SelectMany(p => p.Fotos), f => Assert.Equal("EMEF Escola Exemplo", f.Foto.Carimbo!.Escola));
        var f0 = app.Rascunho.Dados.Fotos[0];
        Assert.Equal(Fotos.Preparar(app.Rascunho.Caminho(f0), 1600, 82, 0, app.Rascunho.CarimboDe(f0)),
            File.ReadAllBytes(Path.Combine(app.Rascunho.PastaDasPreparadas, PreparoDeFotos.NomeNoCache(f0.Arquivo, 0, 1600, 82, app.Rascunho.CarimboDe(f0)))));

        // mudou a escola: o carimbo muda e as copias sao refeitas
        Escolher(app, "EMEF Outra Escola", "044/EX/2026");
        await app.Rascunho.MiniaturasProntasAsync(TimeSpan.FromSeconds(60));
        var novas = Preparadas();
        Assert.Equal(3, novas.Length);
        Assert.Empty(novas.Intersect(carimbadas));

        // so esta foto sem carimbo
        app.Rascunho.SemCarimbo(1, true);
        Assert.Null(app.Rascunho.Plano().Paginas.SelectMany(p => p.Fotos).Single(f => f.Numero == 2).Foto.Carimbo);
        await app.Rascunho.MiniaturasProntasAsync(TimeSpan.FromSeconds(60));
        Assert.Single(Preparadas(), n => !Path.GetFileNameWithoutExtension(n).Split('-')[^1].Contains('c'));

        // nada disso mexeu nas fotos e legendas do fiscal
        Assert.Equal(antes, Impressao(app.Rascunho));
        Assert.Equal(antes, Impressao(new RascunhoRelatorio(Path.Combine(_raiz, "app"), () => Hoje)));
    }

    [Fact]
    public async Task Reabrir_o_app_nao_apaga_as_copias_carimbadas_atuais()
    {
        var app = NovoApp();
        Escolher(app, "EMEF Escola Exemplo", "044/EX/2026");
        app.Carimbar(a => a with { Ligado = true });
        app.Rascunho.AdicionarFoto(Jpeg(1));
        await app.Rascunho.MiniaturasProntasAsync(TimeSpan.FromSeconds(60));
        var antes = Directory.GetFiles(app.Rascunho.PastaDasPreparadas).Select(Path.GetFileName).ToList();
        Assert.Single(antes);
        var reaberto = NovoApp();
        await reaberto.Rascunho.MiniaturasProntasAsync(TimeSpan.FromSeconds(60));
        Assert.Equal(antes, Directory.GetFiles(reaberto.Rascunho.PastaDasPreparadas).Select(Path.GetFileName).ToList());
    }

    [Fact]
    public async Task Gerar_com_carimbo_mantem_fotos_legendas_e_avisa_foto_sem_data_e_sem_endereco()
    {
        var app = NovoApp();
        Escolher(app, "EMEF Escola Exemplo", "044/EX/2026");
        app.Configurar(c => c with { Pasta = new Escolhido("pasta:" + _p.Saida, "Relatórios") });
        app.Carimbar(a => a with { Ligado = true });
        for (int i = 1; i <= 4; i++) app.Rascunho.AdicionarFoto(Jpeg(i, i % 2 == 0 ? 1200 : 1600, i % 2 == 0 ? 1600 : 1200));
        // foto da galeria sem data gravada
        string semData = Path.Combine(app.Rascunho.PastaDasFotos, "galeria.jpg");
        File.Copy(AmostraCelular.Foto(Path.Combine(_raiz, "g"), "galeria.jpg", 1600, 1200, 9), semData);
        app.Rascunho.AdicionarCopiadas([semData]);
        for (int i = 0; i < 5; i++) app.Rascunho.Legendar(i, $"Legenda {i + 1}");
        string antes = Impressao(app.Rascunho);

        var avisos = app.Rascunho.AvisosDoCarimbo(app.Rascunho.Plano());
        Assert.Contains(avisos, a => a.Contains("A foto 05 não tem data de captura", StringComparison.Ordinal));
        Assert.Contains(avisos, a => a.Contains("sem endereço", StringComparison.Ordinal));
        app.Rascunho.Alterar(d => d with { FotosPorPagina = 8 });
        app.Carimbar(a => a with { Tamanho = AjusteCarimbo.TamanhoMinimo });
        Assert.Contains(app.Rascunho.AvisosDoCarimbo(app.Rascunho.Plano()), a => a.Contains("O carimbo ficou pequeno com 8 fotos por página", StringComparison.Ordinal));

        var pronto = await app.Rascunho.GerarAsync(_p, app.Config);
        Assert.Equal(2, pronto.Arquivos.Count);
        Assert.Equal(antes, Impressao(app.Rascunho));
        Assert.Equal(5, Directory.GetFiles(app.Rascunho.PastaDasPreparadas).Length);
    }

    // ---- Galeria ----

    [Fact]
    public async Task Galeria_recebe_a_original_sem_carimbo_e_a_carimbada_com_carimbo_sem_tocar_no_rascunho()
    {
        var app = NovoApp();
        Escolher(app, "EMEF Escola Exemplo", "044/EX/2026");
        app.Configurar(c => c with { FotosNaGaleria = true });
        app.Camera.FotoPega(1, "");
        await app.Camera.FotoTirada(1, Jpeg(1), 1, 1);
        await app.Galeria.VaziaAsync(TimeSpan.FromSeconds(30));
        var f1 = app.Rascunho.Dados.Fotos[0];
        Assert.Equal(File.ReadAllBytes(app.Rascunho.Caminho(f1)), File.ReadAllBytes(_p.NaGaleria.Single()));

        app.Carimbar(a => a with { Ligado = true, Endereco = false });
        app.Camera.FotoPega(2, "");
        await app.Camera.FotoTirada(2, Jpeg(2), 1, 1);
        await app.Galeria.VaziaAsync(TimeSpan.FromSeconds(30));
        var f2 = app.Rascunho.Dados.Fotos[1];
        byte[] naGaleria = File.ReadAllBytes(_p.NaGaleria[1]);
        Assert.Equal(Fotos.Preparar(app.Rascunho.Caminho(f2), 20000, 92, 0, app.Rascunho.CarimboDe(f2)), naGaleria);
        var info = Fotos.Ler(_p.NaGaleria[1]);
        Assert.Equal((1600, 1200), (info.Largura, info.Altura)); // tamanho cheio
        Assert.False(Directory.Exists(Path.Combine(_p.PastaDoApp, "galeria-temp")) && Directory.GetFiles(Path.Combine(_p.PastaDoApp, "galeria-temp")).Length > 0);
        Assert.Equal(Jpeg(2), File.ReadAllBytes(app.Rascunho.Caminho(f2))); // a do rascunho segue a original
    }

    [Fact]
    public async Task Galeria_espera_o_endereco_que_esta_sendo_lido()
    {
        var app = NovoApp();
        Escolher(app, "EMEF Escola Exemplo", "044/EX/2026");
        app.Configurar(c => c with { FotosNaGaleria = true });
        app.Carimbar(a => a with { Ligado = true });
        _p.Pontos.Add(Escola);
        _p.AtrasoDoGps = TimeSpan.FromMilliseconds(800);
        var leitura = app.Endereco.AoAbrirCameraAsync(app.Config.Carimbo);
        app.Camera.FotoPega(1, "");
        await app.Camera.FotoTirada(1, Jpeg(1), 1, 1);
        await leitura;
        await app.Galeria.VaziaAsync(TimeSpan.FromSeconds(30));
        var f = app.Rascunho.Dados.Fotos[0];
        Assert.Equal("Rua Exemplo, 123 – Vila Exemplo", app.Rascunho.CarimboDe(f)!.Endereco);
        Assert.Equal(Fotos.Preparar(app.Rascunho.Caminho(f), 20000, 92, 0, app.Rascunho.CarimboDe(f)), File.ReadAllBytes(_p.NaGaleria.Single()));
    }

    // ---- Endereco pelo GPS ----

    [Fact]
    public async Task Le_uma_vez_por_relatorio_guarda_por_escola_e_contrato_e_reusa_sem_internet()
    {
        var app = NovoApp();
        var ajuste = new AjusteCarimbo { Ligado = true };
        Escolher(app, "EMEF  - Escola Gêmea", "039/EX/2023");
        _p.Pontos.Add(Escola);
        await app.Endereco.AoAbrirCameraAsync(ajuste);
        Assert.Equal(EstadoEndereco.Pronto, app.Endereco.Estado);
        Assert.Equal("Rua Exemplo, 123 – Vila Exemplo", app.Rascunho.Dados.Endereco);
        Assert.Equal(1, _p.Leituras);
        await app.Endereco.AoAbrirCameraAsync(ajuste); // abrir a camera de novo nao le outra vez
        Assert.Equal(1, _p.Leituras);
        Assert.Equal(1, app.Enderecos.Quantos);

        // carimbo sem endereco ou desligado: o GPS nem e usado
        app.Rascunho.Recomecar();
        Escolher(app, "EMEF Outra", "001/EX/2026");
        await app.Endereco.AoAbrirCameraAsync(ajuste with { Endereco = false });
        await app.Endereco.AoAbrirCameraAsync(new AjusteCarimbo());
        Assert.Equal(1, _p.Leituras);

        // proximo relatorio da mesma escola e contrato, sem internet: o endereco aparece na hora
        app.Rascunho.Recomecar();
        _p.SemConexao = true;
        Escolher(app, "emef escola gemea", "039/ex/2023");
        app.Endereco.Sincronizar();
        Assert.Equal("Rua Exemplo, 123 – Vila Exemplo", app.Rascunho.Dados.Endereco);
        await app.Endereco.AoAbrirCameraAsync(ajuste); // le o GPS so para conferir a distancia, sem buscar endereco
        Assert.Equal(1, _p.Buscas);
        Assert.Null(app.Endereco.Distancia);
    }

    [Fact]
    public async Task Mesmo_nome_com_outro_contrato_nunca_e_usado_sozinho()
    {
        var app = NovoApp();
        var ajuste = new AjusteCarimbo { Ligado = true };
        Escolher(app, "EMEF Escola Gêmea", "216/EX/2023");
        _p.Pontos.Add(Escola);
        await app.Endereco.AoAbrirCameraAsync(ajuste);
        Assert.Equal("Rua Exemplo, 123 – Vila Exemplo", app.Rascunho.Dados.Endereco);

        app.Rascunho.Recomecar();
        Escolher(app, "EMEF - Escola Gemea", "039/EX/2023"); // construcao nova: outra escola com o mesmo nome
        app.Endereco.Sincronizar();
        Assert.Equal("", app.Rascunho.Dados.Endereco);
        var outro = Assert.Single(app.Endereco.MesmoNome);
        Assert.Equal("216/EX/2023", outro.Contrato);
        app.Endereco.UsarMesmoNome(outro); // o fiscal confirmou que e o mesmo local
        Assert.Equal(outro.Endereco, app.Rascunho.Dados.Endereco);
        Assert.Equal(2, app.Enderecos.Quantos);
        Assert.Empty(app.Endereco.MesmoNome);
    }

    [Fact]
    public async Task Avisa_quando_esta_longe_do_endereco_guardado_e_manter_ou_atualizar_resolvem()
    {
        var app = NovoApp();
        var ajuste = new AjusteCarimbo { Ligado = true };
        Escolher(app, "CEU Exemplo", "010/EX/2026");
        _p.Pontos.Add(Escola);
        await app.Endereco.AoAbrirCameraAsync(ajuste);

        // andando pelo terreno do CEU (250 m): sem aviso
        app.Rascunho.Recomecar();
        Escolher(app, "CEU Exemplo", "010/EX/2026");
        _p.Pontos.Clear();
        _p.Pontos.Add(Escola with { Latitude = Escola.Latitude + 250 * MetroDeLatitude, Precisao = 20 });
        await app.Endereco.AoAbrirCameraAsync(ajuste);
        Assert.Null(app.Endereco.Distancia);

        // a 1,2 km: avisa; "Manter" nao pergunta de novo
        app.Rascunho.Recomecar();
        Escolher(app, "CEU Exemplo", "010/EX/2026");
        _p.Pontos.Clear();
        _p.Pontos.Add(Escola with { Latitude = Escola.Latitude + 1200 * MetroDeLatitude, Precisao = 10 });
        await app.Endereco.AoAbrirCameraAsync(ajuste);
        Assert.InRange(app.Endereco.Distancia!.Value, 1100, 1300);
        app.Endereco.ManterEndereco();
        Assert.Null(app.Endereco.Distancia);
        Assert.Equal("Rua Exemplo, 123 – Vila Exemplo", app.Rascunho.Dados.Endereco);

        // de novo, agora "Atualizar": busca o endereco do ponto novo e troca o guardado
        app.Rascunho.Recomecar();
        Escolher(app, "CEU Exemplo", "010/EX/2026");
        var app2 = NovoApp(); // outro dia: o aviso volta
        Escolher(app2, "CEU Exemplo", "010/EX/2026");
        _p.Endereco = (_, _) => new ResultadoEndereco("Avenida Nova, 500 – Centro");
        await app2.Endereco.AoAbrirCameraAsync(ajuste);
        Assert.NotNull(app2.Endereco.Distancia);
        await app2.Endereco.AtualizarPeloAvisoAsync();
        Assert.Equal("Avenida Nova, 500 – Centro", app2.Rascunho.Dados.Endereco);
        Assert.Equal("Avenida Nova, 500 – Centro", app2.Enderecos.Buscar(EnderecoDasFotos.Chave("CEU Exemplo", "010/EX/2026"))!.Endereco);
    }

    [Fact]
    public async Task Sem_permissao_sem_sinal_e_sem_internet_cada_um_com_seu_estado_e_tentar_de_novo()
    {
        var app = NovoApp();
        var ajuste = new AjusteCarimbo { Ligado = true };
        Escolher(app, "EMEF Escola Exemplo", "044/EX/2026");
        _p.LocalizacaoPermitida = false;
        await app.Endereco.AoAbrirCameraAsync(ajuste);
        Assert.Equal(EstadoEndereco.SemPermissao, app.Endereco.Estado);
        Assert.True(app.Rascunho.Dados.EnderecoConferido); // nao fica pedindo de novo a cada abertura da camera

        _p.LocalizacaoPermitida = true;
        await app.Endereco.LerAsync(atualizar: true); // sem pontos: sem sinal
        Assert.Equal(EstadoEndereco.SemSinal, app.Endereco.Estado);

        _p.Pontos.Add(Escola);
        _p.SemConexao = true;
        await app.Endereco.TentarDeNovoAsync();
        Assert.Equal(EstadoEndereco.SemInternet, app.Endereco.Estado);
        Assert.Equal(Escola, app.Rascunho.Dados.Ponto);
        Assert.Equal("", app.Rascunho.Dados.Endereco);

        _p.SemConexao = false;
        int leituras = _p.Leituras;
        await app.Endereco.TentarDeNovoAsync(); // so busca o endereco do ponto guardado, sem ler o GPS
        Assert.Equal(leituras, _p.Leituras);
        Assert.Equal("Rua Exemplo, 123 – Vila Exemplo", app.Rascunho.Dados.Endereco);

        _p.Endereco = (_, _) => new ResultadoEndereco(null, FalhaEndereco.SemServico);
        await app.Endereco.LerAsync(atualizar: true);
        Assert.Equal(EstadoEndereco.SemServico, app.Endereco.Estado);
        app.Endereco.Editar("Rua Digitada, 10 👍");
        Assert.Equal("Rua Digitada, 10", app.Rascunho.Dados.Endereco);
        Assert.True(app.Enderecos.Buscar(EnderecoDasFotos.Chave("EMEF Escola Exemplo", "044/EX/2026"))!.Digitado);
    }

    [Fact]
    public async Task Trocar_de_escola_durante_a_leitura_nao_poe_o_endereco_na_escola_errada()
    {
        var app = NovoApp();
        Escolher(app, "EMEF Primeira", "001/EX/2026");
        _p.Pontos.Add(Escola);
        _p.AtrasoDoGps = TimeSpan.FromMilliseconds(400);
        var leitura = app.Endereco.AoAbrirCameraAsync(new AjusteCarimbo { Ligado = true });
        await Task.Delay(100);
        Escolher(app, "EMEF Segunda", "002/EX/2026");
        await leitura;
        app.Endereco.Sincronizar();
        Assert.Equal("", app.Rascunho.Dados.Endereco);
        Assert.Null(app.Enderecos.Buscar(EnderecoDasFotos.Chave("EMEF Segunda", "002/EX/2026")));
    }

    [Fact]
    public void Arquivo_de_enderecos_danificado_comeca_de_novo_ou_usa_a_copia_e_mil_escolas_ficam_leves()
    {
        var guardados = new EnderecosGuardados(_p.PastaDoApp);
        var inicio = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 1000; i++)
        {
            guardados.Guardar(new EnderecoGuardado(EnderecoDasFotos.Chave($"EMEF Escola Número {i}", $"{i:000}/EX/2026")!, $"EMEF Escola Número {i}", $"{i:000}/EX/2026",
                $"Rua Exemplo {i}, {i * 7} – Vila Exemplo", Escola, Hoje));
        }

        long tamanho = new FileInfo(Path.Combine(_p.PastaDoApp, "enderecos.json")).Length;
        Assert.True(tamanho < 400_000, $"{tamanho / 1024} KB para mil escolas");
        var relido = new EnderecosGuardados(_p.PastaDoApp);
        Assert.Equal(1000, relido.Quantos);
        Assert.Equal("Rua Exemplo 500, 3500 – Vila Exemplo", relido.Buscar(EnderecoDasFotos.Chave("emef escola numero 500", "500/ex/2026"))!.Endereco);

        File.WriteAllText(Path.Combine(_p.PastaDoApp, "enderecos.json"), "{ estragado");
        Assert.Equal(999, new EnderecosGuardados(_p.PastaDoApp).Quantos); // a copia anterior (antes da ultima gravacao)
        File.WriteAllText(Path.Combine(_p.PastaDoApp, "enderecos.json.bak"), "[");
        Assert.Equal(0, new EnderecosGuardados(_p.PastaDoApp).Quantos);
        Assert.True(inicio.Elapsed < TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void Celular_com_pouca_memoria_prepara_uma_foto_por_vez()
    {
        int antes = PreparoDeFotos.Limite;
        try
        {
            _p.FotosAoMesmoTempo = 1;
            _ = NovoApp();
            Assert.Equal(1, PreparoDeFotos.Paralelo);
        }
        finally
        {
            PreparoDeFotos.Limite = antes;
        }
    }
}
