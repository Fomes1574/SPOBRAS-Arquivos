using Spobras.Celular.Servicos;
using Spobras.Core.Relatorio;
using Spobras.Testes;

namespace Spobras.Celular.Tests;

/// <summary>
/// Endereco pelo GPS revisado: opcional e desligado no comeco (sem pedir permissao de cara), leitura em 3 etapas
/// sem descartar leitura pela margem de erro, so o primeiro endereco, servico de endereco com novas tentativas,
/// localizacao desligada e permissao so aproximada.
/// </summary>
public sealed class GpsOpcionalTests : IDisposable
{
    private static readonly DateTimeOffset Hoje = new(2026, 11, 18, 14, 32, 0, TimeSpan.FromHours(-3));
    private readonly string _raiz = Directory.CreateTempSubdirectory("spobras-gps-").FullName;
    private readonly PlataformaDeTeste _p;
    private static readonly AjusteCarimbo Carimbo = new() { Ligado = true };

    public GpsOpcionalTests()
    {
        _p = new PlataformaDeTeste(_raiz);
        EnderecoCelular.EsperaEntreTentativas = TimeSpan.FromMilliseconds(10);
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

    private static PontoGps Ponto(double precisao, string fonte = "Wi-Fi e rede", double metrosAoNorte = 0, TimeSpan? idade = null) =>
        new(-23.5505 + metrosAoNorte / 111_195.0, -46.6333, precisao, Hoje - (idade ?? TimeSpan.Zero)) { Fonte = fonte };

    private AppCelular App(bool gps)
    {
        var app = new AppCelular(_p, () => Hoje);
        app.Configurar(c => c with { EnderecoPeloGps = gps, Carimbo = Carimbo });
        app.Rascunho.Alterar(d => d with { Unidade = "EMEF Escola Exemplo", Contrato = "044/EX/2026" });
        return app;
    }

    [Fact]
    public async Task Desligado_no_comeco_o_app_nunca_pede_nem_usa_a_localizacao()
    {
        _p.LocalizacaoPermitida = false;
        _p.Pontos.Add(Ponto(10));
        var app = new AppCelular(_p, () => Hoje);
        Assert.False(app.Config.EnderecoPeloGps); // padrao: desligado
        app.Configurar(c => c with { Carimbo = Carimbo });
        app.Rascunho.Alterar(d => d with { Unidade = "EMEF Escola Exemplo", Contrato = "044/EX/2026" });
        for (int i = 0; i < 3; i++) await app.Endereco.AoAbrirCameraAsync(app.Config.Carimbo);
        await app.Endereco.LerAsync(atualizar: true); // nem um toque perdido le com o GPS desligado
        Assert.Equal(0, _p.PedidosDePermissao);
        Assert.Equal(0, _p.Leituras);
        Assert.Equal(0, _p.Buscas);
        Assert.Equal(EstadoEndereco.Parado, app.Endereco.Estado);
        // digitar continua valendo sem GPS
        app.Endereco.Editar("Rua Digitada, 10");
        Assert.Equal("Rua Digitada, 10", app.Rascunho.Dados.Endereco);
        Assert.Equal("Rua Digitada, 10", app.Rascunho.CarimboDe(new FotoRascunho("x.jpg") { CapturadaEm = Hoje.DateTime })!.Endereco);
    }

    [Fact]
    public async Task Ligar_pede_a_permissao_uma_vez_e_le_na_hora_e_a_camera_nunca_pede()
    {
        _p.LocalizacaoPermitida = false;
        _p.Pontos.Add(Ponto(15));
        var app = App(gps: false);
        // a camera com o GPS ligado mas sem permissao: nao pede
        app.Configurar(c => c with { EnderecoPeloGps = true });
        await app.Endereco.AoAbrirCameraAsync(Carimbo);
        Assert.Equal(0, _p.PedidosDePermissao);
        Assert.Equal(EstadoEndereco.SemPermissao, app.Endereco.Estado);

        app.DesligarEnderecoPeloGps();
        await app.LigarEnderecoPeloGpsAsync(); // o fiscal ligou no passo 1: so agora a janela do Android
        Assert.Equal(1, _p.PedidosDePermissao);
        Assert.True(app.Config.EnderecoPeloGps);
        Assert.Equal("Rua Exemplo, 123 – Vila Exemplo", app.Rascunho.Dados.Endereco);
        await app.Endereco.AoAbrirCameraAsync(Carimbo); // ja leu neste relatorio
        Assert.Equal(1, _p.Leituras);
        Assert.Equal(1, _p.PedidosDePermissao);

        // negou a permissao: fica "sem permissao", sem insistir
        var outro = new PlataformaDeTeste(Path.Combine(_raiz, "outro")) { LocalizacaoPermitida = false, PermitirAoPedir = false };
        var app2 = new AppCelular(outro, () => Hoje);
        app2.Configurar(c => c with { Carimbo = Carimbo });
        app2.Rascunho.Alterar(d => d with { Unidade = "EMEF Escola Exemplo", Contrato = "044/EX/2026" });
        await app2.LigarEnderecoPeloGpsAsync();
        Assert.Equal(EstadoEndereco.SemPermissao, app2.Endereco.Estado);
        Assert.Equal(0, outro.Leituras);
        await app2.Endereco.AoAbrirCameraAsync(Carimbo);
        Assert.Equal(1, outro.PedidosDePermissao);
    }

    [Fact]
    public async Task Ultima_posicao_boa_e_recente_vale_na_hora_sem_ligar_o_GPS()
    {
        _p.UltimaConhecida = Ponto(25, "última conhecida (Wi-Fi e rede)", idade: TimeSpan.FromSeconds(40));
        _p.Pontos.Add(Ponto(8, "GPS"));
        var app = App(gps: true);
        await app.Endereco.LerAsync(atualizar: false);
        Assert.Equal(0, _p.Leituras);
        Assert.Equal(25, app.Rascunho.Dados.Ponto!.Precisao);
        Assert.Equal("Rua Exemplo, 123 – Vila Exemplo", app.Rascunho.Dados.Endereco);
        Assert.Contains("última conhecida", app.Endereco.Leituras[^1].Fonte, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(300, 40)]  // velha demais: le de novo
    [InlineData(30, 120)]  // margem grande: le de novo
    public async Task Ultima_posicao_velha_ou_imprecisa_nao_basta_e_a_leitura_atual_e_feita(int segundos, double precisao)
    {
        _p.UltimaConhecida = Ponto(precisao, "última conhecida", idade: TimeSpan.FromSeconds(segundos));
        _p.Pontos.Add(Ponto(18, "GPS"));
        var app = App(gps: true);
        await app.Endereco.LerAsync(atualizar: false);
        Assert.Equal(1, _p.Leituras);
        Assert.Equal(18, app.Rascunho.Dados.Ponto!.Precisao);
    }

    [Fact]
    public async Task Dentro_do_predio_leitura_de_80_m_nao_e_descartada_e_pede_para_conferir()
    {
        // so Wi-Fi, com margem acima de 50 m: era exatamente o que o modo antigo jogava fora
        _p.Pontos.Add(Ponto(140, "Wi-Fi e rede"));
        _p.Pontos.Add(Ponto(80, "Wi-Fi e rede"));
        var app = App(gps: true);
        var precisoes = new List<double?>();
        app.Endereco.Mudou += () => precisoes.Add(app.Endereco.Precisao);
        await app.Endereco.LerAsync(atualizar: false);
        Assert.Equal(EstadoEndereco.Pronto, app.Endereco.Estado);
        Assert.Equal(80, app.Rascunho.Dados.Ponto!.Precisao);
        Assert.Contains(140.0, precisoes); // a tela mostrou a melhora 140 -> 80
        Assert.Contains(80.0, precisoes);
        Assert.False(app.Endereco.Conferir); // 80 m ainda acerta a rua
        _p.Pontos.Clear();
        _p.Pontos.Add(Ponto(260));
        await app.Endereco.LerAsync(atualizar: true);
        Assert.True(app.Endereco.Conferir); // margem acima de 100 m: pede para conferir
        app.Endereco.Editar("Rua Conferida, 12");
        Assert.False(app.Endereco.Conferir); // o fiscal confirmou
    }

    [Fact]
    public async Task Para_antes_ao_chegar_a_20_m_e_fica_com_a_melhor_leitura()
    {
        _p.Pontos.AddRange([Ponto(90), Ponto(35), Ponto(12, "GPS"), Ponto(5, "GPS")]);
        var app = App(gps: true);
        await app.Endereco.LerAsync(atualizar: false);
        Assert.Equal(3, _p.LeiturasEntregues); // parou na de 12 m, sem esperar a de 5 m
        Assert.Equal(12, app.Rascunho.Dados.Ponto!.Precisao);
        Assert.Equal("GPS", app.Rascunho.Dados.Ponto!.Fonte);
    }

    [Fact]
    public async Task Sem_leitura_atual_usa_a_posicao_conhecida_de_ate_10_minutos_e_sem_nada_diz_sem_sinal()
    {
        _p.UltimaConhecida = Ponto(70, "última conhecida", idade: TimeSpan.FromMinutes(6));
        var app = App(gps: true);
        await app.Endereco.LerAsync(atualizar: false);
        Assert.Equal(70, app.Rascunho.Dados.Ponto!.Precisao);
        Assert.Equal(EstadoEndereco.Pronto, app.Endereco.Estado);

        var outro = new PlataformaDeTeste(Path.Combine(_raiz, "nada"));
        var app2 = new AppCelular(outro, () => Hoje);
        app2.Configurar(c => c with { EnderecoPeloGps = true, Carimbo = Carimbo });
        app2.Rascunho.Alterar(d => d with { Unidade = "EMEF Escola Exemplo", Contrato = "044/EX/2026" });
        outro.UltimaConhecida = Ponto(70, idade: TimeSpan.FromMinutes(30)); // velha demais
        await app2.Endereco.LerAsync(atualizar: false);
        Assert.Equal(EstadoEndereco.SemSinal, app2.Endereco.Estado);
        Assert.Equal("nenhuma leitura", app2.Endereco.Leituras[^1].Resultado);
    }

    [Fact]
    public async Task Localizacao_do_celular_desligada_oferece_ligar_e_depois_le()
    {
        _p.LocalizacaoLigada = false;
        _p.Pontos.Add(Ponto(15));
        var app = App(gps: true);
        await app.Endereco.LerAsync(atualizar: false);
        Assert.Equal(EstadoEndereco.LocalizacaoDesligada, app.Endereco.Estado);
        Assert.Equal(0, _p.Leituras);
        await app.Endereco.LigarLocalizacaoAsync();
        Assert.Equal(1, _p.AberturasDeConfiguracoes);
        _p.LocalizacaoLigada = true;
        await app.Endereco.LerAsync(atualizar: true);
        Assert.Equal(EstadoEndereco.Pronto, app.Endereco.Estado);
    }

    [Fact]
    public async Task Permissao_so_aproximada_e_wifi_desligado_viram_avisos()
    {
        _p.LocalizacaoPermitida = false;
        _p.PermitirSoAproximada = true;
        _p.RedeLigada = false;
        _p.Pontos.Add(Ponto(15));
        var app = App(gps: false);
        await app.LigarEnderecoPeloGpsAsync();
        Assert.True(app.Endereco.Aproximada);
        Assert.True(app.Endereco.SemRede);
        Assert.True(app.Endereco.Conferir); // a aproximada vem com mais de 1 km de margem
        _p.PermitirSoAproximada = false;
        _p.LocalizacaoPrecisa = true;
        _p.RedeLigada = true;
        await app.Endereco.LerAsync(atualizar: true);
        Assert.False(app.Endereco.Aproximada);
        Assert.False(app.Endereco.SemRede);
    }

    [Fact]
    public async Task Servico_de_endereco_que_falha_de_passagem_e_tentado_de_novo_ate_3_vezes()
    {
        _p.Pontos.Add(Ponto(15));
        _p.Respostas.AddRange([new ResultadoEndereco(null, FalhaEndereco.Falhou), new ResultadoEndereco(null, FalhaEndereco.Falhou), new ResultadoEndereco("Avenida Terceira, 3 – Centro")]);
        var app = App(gps: true);
        await app.Endereco.LerAsync(atualizar: false);
        Assert.Equal(3, _p.Buscas);
        Assert.Equal("Avenida Terceira, 3 – Centro", app.Rascunho.Dados.Endereco);

        // falhou as 3: fica o ponto e o fiscal pode tentar de novo depois
        var outro = new PlataformaDeTeste(Path.Combine(_raiz, "falha"));
        outro.Pontos.Add(Ponto(15));
        outro.Respostas.Add(new ResultadoEndereco(null, FalhaEndereco.Falhou));
        var app2 = new AppCelular(outro, () => Hoje);
        app2.Configurar(c => c with { EnderecoPeloGps = true, Carimbo = Carimbo });
        app2.Rascunho.Alterar(d => d with { Unidade = "EMEF Escola Exemplo", Contrato = "044/EX/2026" });
        await app2.Endereco.LerAsync(atualizar: false);
        Assert.Equal(3, outro.Buscas);
        Assert.Equal(EstadoEndereco.SemServico, app2.Endereco.Estado);
        Assert.NotNull(app2.Rascunho.Dados.Ponto);
        Assert.Contains("falhou 3 vezes", app2.Endereco.Leituras[^1].Resultado, StringComparison.Ordinal);

        // sem internet nao insiste: guarda o ponto na hora
        var semRede = new PlataformaDeTeste(Path.Combine(_raiz, "semrede")) { SemConexao = true };
        semRede.Pontos.Add(Ponto(15));
        var app3 = new AppCelular(semRede, () => Hoje);
        app3.Configurar(c => c with { EnderecoPeloGps = true, Carimbo = Carimbo });
        app3.Rascunho.Alterar(d => d with { Unidade = "EMEF Escola Exemplo", Contrato = "044/EX/2026" });
        await app3.Endereco.LerAsync(atualizar: false);
        Assert.Equal(1, semRede.Buscas);
        Assert.Equal(EstadoEndereco.SemInternet, app3.Endereco.Estado);
    }

    [Fact]
    public async Task Diagnostico_guarda_as_ultimas_10_leituras_com_fonte_precisao_e_resultado()
    {
        _p.Pontos.Add(Ponto(15, "GPS"));
        var app = App(gps: true);
        for (int i = 0; i < 12; i++) await app.Endereco.LerAsync(atualizar: true);
        var l = app.Endereco.Leituras;
        Assert.Equal(10, l.Count);
        Assert.All(l, x => Assert.Equal("GPS", x.Fonte));
        Assert.All(l, x => Assert.Equal(15, x.Precisao));
        Assert.All(l, x => Assert.Equal("endereço encontrado", x.Resultado));
    }

    [Fact]
    public void Linha_completa_do_endereco_vira_texto_curto()
    {
        Assert.Equal("R. Exemplo, 123 – Vila Exemplo", EnderecoDasFotos.EncurtarLinha("R. Exemplo, 123 - Vila Exemplo, São Paulo - SP, 01234-567, Brasil"));
        Assert.Equal("Av. Exemplo – Centro", EnderecoDasFotos.EncurtarLinha("Av. Exemplo - Centro, São Paulo - SP, 01234567, Brazil"));
        Assert.Equal("Rua Só Nome", EnderecoDasFotos.EncurtarLinha("Rua Só Nome"));
        Assert.Null(EnderecoDasFotos.EncurtarLinha(" , Brasil"));
        Assert.Null(EnderecoDasFotos.EncurtarLinha(null));
    }
}
