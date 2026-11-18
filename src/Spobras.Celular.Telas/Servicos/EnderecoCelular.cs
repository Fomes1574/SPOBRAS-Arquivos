using Spobras.Core.Relatorio;

namespace Spobras.Celular.Servicos;

public enum EstadoEndereco
{
    Parado,
    Lendo,
    Pronto,
    SemPermissao,

    /// <summary>A localizacao do celular esta desligada (a tela oferece "Ligar a localizacao").</summary>
    LocalizacaoDesligada,
    SemSinal,

    /// <summary>O ponto do GPS chegou, mas sem internet para virar endereco: fica guardado para tentar de novo.</summary>
    SemInternet,

    /// <summary>O celular nao tem o servico de endereco (ou nao achou): o endereco e digitado.</summary>
    SemServico,
}

/// <summary>Uma leitura de localizacao, para o diagnostico nas Configuracoes.</summary>
public sealed record LeituraDeLocalizacao(DateTimeOffset Quando, string Fonte, double? Precisao, double Segundos, string Resultado);

/// <summary>
/// Endereco das fotos pelo GPS, so no celular, so com o carimbo usando o endereco e so se o fiscal ligar "Usar o
/// endereco pelo GPS" (desligado no comeco). Antes disso o app nunca pede nem usa a localizacao.
/// <para>
/// A leitura e feita uma vez por relatorio, em 3 etapas: a ultima posicao que o celular ja conhece (na hora), a
/// leitura atual por GPS, Wi-Fi e rede juntos (ate 20 s, para antes com 20 m) e o endereco do melhor ponto (so o
/// primeiro, o mais proximo). Nenhuma leitura e descartada pela margem de erro: a tela mostra a precisao real.
/// </para>
/// O endereco fica guardado por escola e contrato: na proxima visita aparece direto, mesmo sem internet. Escola com o
/// mesmo nome e outro contrato nunca e usada sozinha (pode ser outro predio); a tela pergunta.
/// </summary>
public sealed class EnderecoCelular(IPlataforma plataforma, RascunhoRelatorio rascunho, EnderecosGuardados guardados, Func<DateTimeOffset> agora, Func<bool>? usarGps = null)
{
    /// <summary>Espera maxima pela leitura atual e a precisao com que ela para antes (metros).</summary>
    public static readonly TimeSpan Espera = TimeSpan.FromSeconds(20);

    public const double PrecisaoDesejada = 20;

    /// <summary>Ultima posicao conhecida aceita na hora: ate 2 minutos e ate 50 m.</summary>
    public static readonly TimeSpan IdadeDaUltima = TimeSpan.FromMinutes(2);

    public const double PrecisaoDaUltima = 50;

    /// <summary>Sem leitura atual, uma posicao conhecida de ate 10 minutos ainda serve (com a precisao mostrada).</summary>
    public static readonly TimeSpan IdadeDaReserva = TimeSpan.FromMinutes(10);

    /// <summary>Tentativas do servico de endereco quando ele falha de passagem, e a espera entre elas.</summary>
    public const int TentativasDoEndereco = 3;

    public static TimeSpan EsperaEntreTentativas { get; set; } = TimeSpan.FromSeconds(1.5);

    private readonly SemaphoreSlim _lendo = new(1, 1);
    private readonly Func<bool> _usarGps = usarGps ?? (() => true);
    private readonly List<LeituraDeLocalizacao> _leituras = [];

    public EstadoEndereco Estado { get; private set; }

    /// <summary>Precisao da leitura (metros), durante e depois de ler.</summary>
    public double? Precisao { get; private set; }

    /// <summary>A leitura de agora ficou longe do endereco guardado desta escola (metros); nulo sem aviso.</summary>
    public double? Distancia { get; private set; }

    private PontoGps? _pontoDoAviso;
    private bool _avisoDispensado;

    /// <summary>O app so tem a permissao de localizacao aproximada (Android 12+): o endereco pode sair errado.</summary>
    public bool Aproximada { get; private set; }

    /// <summary>A localizacao por Wi-Fi e rede esta desligada: dentro de predio fica dificil achar a posicao.</summary>
    public bool SemRede { get; private set; }

    public event Action? Mudou;

    public bool Lendo => Estado == EstadoEndereco.Lendo;

    /// <summary>O fiscal ligou "Usar o endereco pelo GPS".</summary>
    public bool UsarGps => _usarGps();

    public string? Chave => EnderecoDasFotos.Chave(rascunho.Dados.Unidade, rascunho.Dados.Contrato);

    /// <summary>O ponto atual tem margem grande: a tela pede para conferir o endereco.</summary>
    public bool Conferir
    {
        get
        {
            var d = rascunho.Dados;
            if (d.Ponto is not { } p || p.Precisao <= PontoGps.Conferir || d.Endereco.Length == 0) return false;
            // o fiscal ja digitou ou corrigiu este endereco: esta conferido
            return guardados.Buscar(Chave) is not { Digitado: true } g || g.Endereco != d.Endereco;
        }
    }

    /// <summary>Ultimas leituras (diagnostico), da mais antiga para a mais nova.</summary>
    public IReadOnlyList<LeituraDeLocalizacao> Leituras
    {
        get
        {
            lock (_leituras)
            {
                return [.. _leituras];
            }
        }
    }

    /// <summary>O carimbo usa o endereco (digitado ou pelo GPS).</summary>
    public static bool Usa(AjusteCarimbo? a) => a is { Ligado: true, Endereco: true };

    /// <summary>Escolas com o mesmo nome e outro contrato que ja tem endereco guardado (a tela pergunta se e o mesmo local).</summary>
    public IReadOnlyList<EnderecoGuardado> MesmoNome => rascunho.Dados.Endereco.Length > 0 && rascunho.Dados.EnderecoDe == Chave ? [] : guardados.MesmoNome(Chave);

    /// <summary>
    /// Confere se o endereco do rascunho e da escola atual. Trocou de escola: vale o guardado da nova (se houver) ou
    /// fica sem endereco ate ler.
    /// </summary>
    public void Sincronizar()
    {
        string? chave = Chave;
        var d = rascunho.Dados;
        if (chave is null || d.EnderecoDe == chave)
        {
            return;
        }

        if (guardados.Buscar(chave) is { } g)
        {
            // escola nova: o endereco guardado vale, e o GPS ainda confere a distancia nesta visita
            rascunho.Alterar(x => x with { Endereco = g.Endereco, Ponto = g.Ponto, EnderecoDe = chave, EnderecoConferido = false });
        }
        else if (d.Endereco.Length > 0 || d.Ponto is not null || d.EnderecoDe.Length > 0)
        {
            rascunho.Alterar(x => x with { Endereco = "", Ponto = null, EnderecoDe = chave, EnderecoConferido = false });
        }
        else
        {
            rascunho.Alterar(x => x with { EnderecoDe = chave });
        }

        Distancia = null;
        _avisoDispensado = false;
        Estado = EstadoEndereco.Parado;
        Mudou?.Invoke();
    }

    /// <summary>
    /// A camera abriu: so com o carimbo usando o endereco, o GPS ligado pelo fiscal e a permissao ja dada, le uma vez
    /// neste relatorio, em segundo plano. A camera nunca pede permissao.
    /// </summary>
    public async Task AoAbrirCameraAsync(AjusteCarimbo? ajuste)
    {
        if (!Usa(ajuste) || !UsarGps || Chave is null)
        {
            return;
        }

        Sincronizar();
        if (rascunho.Dados.EnderecoConferido || Lendo)
        {
            return;
        }

        if (!(await plataforma.SituacaoDaLocalizacaoAsync()).Permitida)
        {
            Mudar(EstadoEndereco.SemPermissao); // a tela dos dados oferece "Permitir"; aqui nada e pedido
            return;
        }

        await LerAsync(atualizar: false, pedirPermissao: false);
    }

    /// <summary>
    /// Le a posicao. Com endereco guardado e sem pedir para atualizar, a leitura so confere a distancia (nao precisa
    /// de internet). Com "Ler de novo", busca o endereco do ponto novo e troca o guardado. A permissao so e pedida com
    /// <paramref name="pedirPermissao"/> (o fiscal tocou num botao).
    /// </summary>
    public async Task LerAsync(bool atualizar, bool pedirPermissao = true)
    {
        if (!UsarGps || Chave is not { } chave || !await _lendo.WaitAsync(0))
        {
            return;
        }

        var relogio = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            Sincronizar();
            Mudar(EstadoEndereco.Lendo);
            Precisao = null;
            Distancia = null;
            var situacao = await plataforma.SituacaoDaLocalizacaoAsync();
            if (!situacao.Permitida && pedirPermissao)
            {
                await plataforma.PermitirLocalizacaoAsync();
                situacao = await plataforma.SituacaoDaLocalizacaoAsync();
            }

            Aproximada = situacao.Permitida && !situacao.Precisa;
            SemRede = situacao.Permitida && situacao.Ligada && !situacao.RedeLigada;
            if (!situacao.Permitida)
            {
                rascunho.EnderecoFoiConferido();
                Anotar("—", null, relogio, "sem permissão");
                Mudar(EstadoEndereco.SemPermissao);
                return;
            }

            if (!situacao.Ligada)
            {
                Anotar("—", null, relogio, "localização do celular desligada");
                Mudar(EstadoEndereco.LocalizacaoDesligada);
                return;
            }

            var ponto = await LocalizarAsync();
            if (ponto is not { Valido: true })
            {
                rascunho.EnderecoFoiConferido();
                Anotar("—", null, relogio, "nenhuma leitura");
                Mudar(rascunho.Dados.Endereco.Length > 0 && rascunho.Dados.EnderecoDe == chave ? EstadoEndereco.Pronto : EstadoEndereco.SemSinal);
                return;
            }

            Precisao = ponto.Precisao;
            var guardado = guardados.Buscar(chave);
            if (!atualizar && rascunho.Dados.Endereco.Length > 0 && rascunho.Dados.EnderecoDe == chave)
            {
                // ja tem endereco (guardado ou digitado): a leitura so confere se o fiscal esta mesmo nessa escola
                var referencia = rascunho.Dados.Ponto ?? guardado?.Ponto;
                if (referencia is not null && EnderecoDasFotos.Longe(referencia, ponto) && !_avisoDispensado)
                {
                    Distancia = EnderecoDasFotos.Distancia(referencia, ponto);
                    _pontoDoAviso = ponto;
                }

                rascunho.EnderecoFoiConferido();
                Anotar(ponto.Fonte, ponto.Precisao, relogio, Distancia is { } d ? $"conferido: longe ({EnderecoDasFotos.TextoDistancia(d)})" : "conferido: endereço guardado vale");
                Mudar(EstadoEndereco.Pronto);
                return;
            }

            await BuscarEnderecoAsync(chave, ponto, relogio);
        }
        finally
        {
            _lendo.Release();
        }
    }

    /// <summary>
    /// As 3 etapas: ultima posicao conhecida boa e recente (na hora); senao a leitura atual, mostrando cada melhora;
    /// sem leitura atual, uma posicao conhecida de ate 10 minutos ainda serve.
    /// </summary>
    private async Task<PontoGps?> LocalizarAsync()
    {
        PontoGps? ultima = null;
        try
        {
            ultima = await plataforma.UltimaLocalizacaoAsync(IdadeDaReserva);
            if (ultima is not null && agora() - ultima.LidoEm > IdadeDaReserva)
            {
                ultima = null; // velha demais: o fiscal pode estar em outro lugar
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException)
        {
        }

        if (ultima is { Valido: true } u && u.Precisao <= PrecisaoDaUltima && agora() - u.LidoEm <= IdadeDaUltima)
        {
            return u; // o celular ja sabia onde esta, com boa precisao: sem ligar o GPS
        }

        if (ultima is { Valido: true })
        {
            Precisao = ultima.Precisao;
            Mudou?.Invoke();
        }

        PontoGps? atual = null;
        try
        {
            atual = await plataforma.LocalizarAsync(Espera, PrecisaoDesejada, p =>
            {
                Precisao = p.Precisao;
                Mudou?.Invoke();
            });
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or TaskCanceledException)
        {
        }

        if (atual is { Valido: true } a)
        {
            return ultima is { Valido: true } r && r.Precisao < a.Precisao && agora() - r.LidoEm <= IdadeDaUltima ? r : a;
        }

        return ultima is { Valido: true } ? ultima : null;
    }

    private async Task BuscarEnderecoAsync(string chave, PontoGps ponto, System.Diagnostics.Stopwatch? relogio = null)
    {
        relogio ??= System.Diagnostics.Stopwatch.StartNew();
        ResultadoEndereco r = new(null, FalhaEndereco.Falhou);
        for (int tentativa = 1; tentativa <= TentativasDoEndereco; tentativa++)
        {
            try
            {
                r = await plataforma.EnderecoAsync(ponto.Latitude, ponto.Longitude);
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException or HttpRequestException or TaskCanceledException)
            {
                r = new ResultadoEndereco(null, FalhaEndereco.Falhou);
            }

            if (r.Texto is { Length: > 0 } || r.Falha != FalhaEndereco.Falhou || tentativa == TentativasDoEndereco)
            {
                break;
            }

            await Task.Delay(EsperaEntreTentativas); // o servico do Android falha de passagem: tenta de novo
        }

        if (Chave != chave)
        {
            return; // trocou de escola enquanto buscava: este endereco nao e da escola de agora
        }

        if (r.Texto is { Length: > 0 } texto)
        {
            rascunho.DefinirEndereco(texto, ponto, chave);
            Guardar(chave, texto, ponto, digitado: false);
            Anotar(ponto.Fonte, ponto.Precisao, relogio, "endereço encontrado");
            Mudar(EstadoEndereco.Pronto);
            return;
        }

        rascunho.Alterar(d => d with { Ponto = ponto, EnderecoDe = chave, EnderecoConferido = true });
        Anotar(ponto.Fonte, ponto.Precisao, relogio, r.Falha switch
        {
            FalhaEndereco.SemInternet => "ponto guardado: sem internet",
            FalhaEndereco.SemServico => "celular sem serviço de endereço",
            FalhaEndereco.NaoAchou => "serviço não achou endereço",
            _ => $"serviço de endereço falhou {TentativasDoEndereco} vezes",
        });
        Mudar(r.Falha == FalhaEndereco.SemInternet ? EstadoEndereco.SemInternet : EstadoEndereco.SemServico);
    }

    /// <summary>"Tentar de novo": com o ponto ja guardado e sem endereco, so busca o endereco; senao, le de novo.</summary>
    public async Task TentarDeNovoAsync()
    {
        if (Chave is { } chave && rascunho.Dados.Ponto is { } ponto && rascunho.Dados.Endereco.Length == 0 && rascunho.Dados.EnderecoDe == chave)
        {
            if (!await _lendo.WaitAsync(0)) return;
            try
            {
                Mudar(EstadoEndereco.Lendo);
                await BuscarEnderecoAsync(chave, ponto);
            }
            finally
            {
                _lendo.Release();
            }

            return;
        }

        await LerAsync(atualizar: true);
    }

    /// <summary>Abre a tela do Android para ligar a localizacao.</summary>
    public Task LigarLocalizacaoAsync() => plataforma.AbrirConfiguracoesDeLocalizacaoAsync();

    /// <summary>Endereco digitado ou corrigido pelo fiscal: vale para este relatorio e fica guardado para a escola.</summary>
    public void Editar(string texto)
    {
        if (Chave is not { } chave) return;
        string limpo = TextoMedido.Limpar(texto).Trim();
        var ponto = rascunho.Dados.EnderecoDe == chave ? rascunho.Dados.Ponto : null;
        rascunho.DefinirEndereco(limpo, ponto, chave);
        if (limpo.Length > 0)
        {
            Guardar(chave, limpo, ponto, digitado: true);
        }

        Distancia = null;
        Mudar(limpo.Length > 0 ? EstadoEndereco.Pronto : EstadoEndereco.Parado);
    }

    /// <summary>"E o mesmo local": usa o endereco guardado da escola de mesmo nome (outro contrato) e guarda para esta.</summary>
    public void UsarMesmoNome(EnderecoGuardado outro)
    {
        if (Chave is not { } chave) return;
        rascunho.DefinirEndereco(outro.Endereco, outro.Ponto, chave);
        Guardar(chave, outro.Endereco, outro.Ponto, outro.Digitado);
        Mudar(EstadoEndereco.Pronto);
    }

    /// <summary>Aviso de distancia: "Manter" nao pergunta de novo neste relatorio.</summary>
    public void ManterEndereco()
    {
        _avisoDispensado = true;
        Distancia = null;
        Mudou?.Invoke();
    }

    /// <summary>Aviso de distancia: "Atualizar" busca o endereco do ponto lido agora.</summary>
    public async Task AtualizarPeloAvisoAsync()
    {
        if (Chave is not { } chave || _pontoDoAviso is not { } ponto || !await _lendo.WaitAsync(0)) return;
        try
        {
            Distancia = null;
            Mudar(EstadoEndereco.Lendo);
            await BuscarEnderecoAsync(chave, ponto);
        }
        finally
        {
            _lendo.Release();
        }
    }

    private void Guardar(string chave, string texto, PontoGps? ponto, bool digitado)
    {
        try
        {
            guardados.Guardar(new EnderecoGuardado(chave, rascunho.Dados.Unidade.Trim(), rascunho.Dados.Contrato.Trim(), texto, ponto, agora(), digitado));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // sem guardar, so perde o atalho da proxima visita; o relatorio continua com o endereco
        }
    }

    private void Anotar(string fonte, double? precisao, System.Diagnostics.Stopwatch relogio, string resultado)
    {
        lock (_leituras)
        {
            _leituras.Add(new LeituraDeLocalizacao(agora(), string.IsNullOrEmpty(fonte) ? "—" : fonte, precisao, relogio.Elapsed.TotalSeconds, resultado));
            if (_leituras.Count > 10) _leituras.RemoveAt(0);
        }
    }

    private void Mudar(EstadoEndereco estado)
    {
        Estado = estado;
        Mudou?.Invoke();
    }
}
