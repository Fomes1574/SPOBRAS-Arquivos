using System.Security.Cryptography;
using Spobras.Celular.Servicos;
using Spobras.Documentos.Relatorio;
using Spobras.Testes;

namespace Spobras.Celular.Tests;

/// <summary>
/// Celular: fotos da camera "Salvando…", galeria em segundo plano, celular com pouca memoria, e a regra de ouro:
/// nada disso mexe nas fotos e legendas do fiscal.
/// </summary>
public sealed class CameraSalvandoTests : IDisposable
{
    private static readonly DateTimeOffset Hoje = new(2026, 11, 18, 14, 32, 0, TimeSpan.FromHours(-3));
    private readonly string _raiz = Directory.CreateTempSubdirectory("spobras-salvando-").FullName;
    private readonly PlataformaDeTeste _p;

    public CameraSalvandoTests() => _p = new PlataformaDeTeste(_raiz);

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

    private AppCelular NovoApp() => new(_p, () => Hoje);

    private byte[] Jpeg(int n, int largura = 1600, int altura = 1200) =>
        File.ReadAllBytes(AmostraCelular.Foto(Path.Combine(_raiz, "modelos"), $"m{n}.jpg", largura, altura, n));

    /// <summary>Impressao de tudo o que e do fiscal: cada foto do rascunho (conteudo) e cada legenda e descricao.</summary>
    private static string Impressao(RascunhoRelatorio r) => string.Join("\n", r.Dados.Fotos.Select(f =>
        $"{f.Arquivo}|{f.Legenda}|{f.Descricao}|{Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(r.Caminho(f))))}"));

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

    [Fact]
    public async Task Galeria_recebe_a_foto_igual_a_do_rascunho_em_segundo_plano()
    {
        var app = NovoApp();
        app.Configurar(c => c with { FotosNaGaleria = true });
        for (long id = 1; id <= 3; id++)
        {
            app.Camera.FotoPega(id, "");
            await app.Camera.FotoTirada(id, Jpeg((int)id), 1, 1);
        }

        await app.Galeria.VaziaAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(3, _p.NaGaleria.Count);
        for (int i = 0; i < 3; i++)
        {
            Assert.Equal(File.ReadAllBytes(app.Rascunho.Caminho(app.Rascunho.Dados.Fotos[i])), File.ReadAllBytes(_p.NaGaleria[i]));
        }

        // com a opcao desligada, nada vai para a galeria
        app.Configurar(c => c with { FotosNaGaleria = false });
        app.Camera.FotoPega(9, "");
        await app.Camera.FotoTirada(9, Jpeg(9), 1, 1);
        await app.Galeria.VaziaAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(3, _p.NaGaleria.Count);
    }

    [Fact]
    public async Task Gerar_e_reabrir_nao_mexem_nas_fotos_e_legendas()
    {
        var app = NovoApp();
        app.Rascunho.Alterar(d => d with { Unidade = "EMEF Escola Exemplo", Contrato = "044/EX/2026" });
        app.Configurar(c => c with { Pasta = new Escolhido("pasta:" + _p.Saida, "Relatórios") });
        for (int i = 1; i <= 4; i++) app.Rascunho.AdicionarFoto(Jpeg(i, i % 2 == 0 ? 1200 : 1600, i % 2 == 0 ? 1600 : 1200));
        for (int i = 0; i < 4; i++) app.Rascunho.Legendar(i, $"Legenda {i + 1}");
        await app.Rascunho.MiniaturasProntasAsync(TimeSpan.FromSeconds(60));
        string antes = Impressao(app.Rascunho);
        var pronto = await app.Rascunho.GerarAsync(_p, app.Config);
        Assert.Equal(2, pronto.Arquivos.Count);
        Assert.Equal(antes, Impressao(app.Rascunho));
        Assert.Equal(antes, Impressao(new RascunhoRelatorio(Path.Combine(_raiz, "app"), () => Hoje)));
        Assert.Equal(4, Directory.GetFiles(app.Rascunho.PastaDasPreparadas).Length);
    }

    [Fact]
    public async Task Rascunho_e_configuracoes_da_versao_com_carimbo_abrem_e_as_copias_carimbadas_saem()
    {
        // celular que usou a versao com carimbo e GPS: os campos a mais no arquivo sao ignorados e nada se perde
        var app = NovoApp();
        app.Configurar(c => c with { FotosNaGaleria = true });
        app.Rascunho.Alterar(d => d with { Unidade = "EMEF Escola Exemplo" });
        app.Rascunho.AdicionarFoto(Jpeg(1));
        app.Rascunho.Legendar(0, "Fachada");
        await app.Rascunho.MiniaturasProntasAsync(TimeSpan.FromSeconds(60));
        string pastaApp = Path.Combine(_raiz, "app");
        string rascunho = Path.Combine(pastaApp, "rascunho", "rascunho.json");
        string json = File.ReadAllText(rascunho).Replace("\"Fotos\":", "\"Endereco\": \"Rua Exemplo, 1\", \"EnderecoDe\": \"x|y\", \"EnderecoConferido\": true, \"Ponto\": { \"Latitude\": -23.5, \"Longitude\": -46.6, \"Precisao\": 100 }, \"Fotos\":", StringComparison.Ordinal)
            .Replace("\"Legenda\":", "\"SemCarimbo\": true, \"Legenda\":", StringComparison.Ordinal);
        File.WriteAllText(rascunho, json);
        string prefs = Path.Combine(pastaApp, "preferencias.json");
        File.WriteAllText(prefs, File.ReadAllText(prefs).TrimEnd().TrimEnd('}') + ", \"Carimbo\": { \"Ligado\": true, \"Tamanho\": 0.06 }, \"EnderecoPeloGps\": true }");
        string carimbada = Path.Combine(app.Rascunho.PastaDasPreparadas, Path.GetFileNameWithoutExtension(app.Rascunho.Dados.Fotos[0].Arquivo) + "-0-1600-82c0123456789.jpg");
        File.WriteAllBytes(carimbada, Jpeg(5));
        File.WriteAllText(Path.Combine(pastaApp, "enderecos.json"), "[]");

        var reaberto = NovoApp();
        Assert.Equal("Fachada", reaberto.Rascunho.Dados.Fotos.Single().Legenda);
        Assert.Equal("EMEF Escola Exemplo", reaberto.Rascunho.Dados.Unidade);
        Assert.False(File.Exists(carimbada)); // copia carimbada antiga e arquivo interno: sai ao abrir
        Assert.Single(Directory.GetFiles(reaberto.Rascunho.PastaDasPreparadas));
        Assert.True(reaberto.Config.FotosNaGaleria); // as outras configuracoes continuam
        Assert.False(File.Exists(Path.Combine(pastaApp, "enderecos.json"))); // sobra da versao com GPS
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
