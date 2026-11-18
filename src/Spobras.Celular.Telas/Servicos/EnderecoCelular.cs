using Spobras.Core.Relatorio;

namespace Spobras.Celular.Servicos;

public enum EstadoEndereco
{
    Parado,
    Lendo,
    Pronto,
    SemPermissao,
    SemSinal,

    /// <summary>O ponto do GPS chegou, mas sem internet para virar endereco: fica guardado para tentar de novo.</summary>
    SemInternet,

    /// <summary>O celular nao tem o servico de endereco (ou nao achou): o endereco e digitado.</summary>
    SemServico,
}

/// <summary>
/// Endereco das fotos pelo GPS, so no celular e so com o carimbo ligado com o endereco. Le uma vez por relatorio (na
/// primeira vez que a camera abre), em segundo plano, sem travar a camera. O endereco fica guardado por escola e
/// contrato: na proxima visita aparece direto, mesmo sem internet. Escola com o mesmo nome e outro contrato nunca e
/// usada sozinha (pode ser outro predio); a tela pergunta.
/// </summary>
public sealed class EnderecoCelular(IPlataforma plataforma, RascunhoRelatorio rascunho, EnderecosGuardados guardados, Func<DateTimeOffset> agora)
{
    /// <summary>Espera pelo GPS e precisao desejada (metros).</summary>
    public static readonly TimeSpan Espera = TimeSpan.FromSeconds(15);

    public const double PrecisaoDesejada = 30;

    private readonly SemaphoreSlim _lendo = new(1, 1);

    public EstadoEndereco Estado { get; private set; }

    /// <summary>Precisao da ultima leitura do GPS (metros), durante e depois de ler.</summary>
    public double? Precisao { get; private set; }

    /// <summary>A leitura de agora ficou longe do endereco guardado desta escola (metros); nulo sem aviso.</summary>
    public double? Distancia { get; private set; }

    private PontoGps? _pontoDoAviso;
    private bool _avisoDispensado;

    /// <summary>O ponto veio so como localizacao aproximada (Android 12+): o endereco pode sair errado.</summary>
    public bool Aproximada { get; private set; }

    public event Action? Mudou;

    public bool Lendo => Estado == EstadoEndereco.Lendo;

    public string? Chave => EnderecoDasFotos.Chave(rascunho.Dados.Unidade, rascunho.Dados.Contrato);

    /// <summary>O carimbo usa o endereco: so entao o GPS e usado.</summary>
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

    /// <summary>A camera abriu: com o carimbo usando o endereco, le o GPS uma vez neste relatorio, em segundo plano.</summary>
    public Task AoAbrirCameraAsync(AjusteCarimbo? ajuste)
    {
        if (!Usa(ajuste) || Chave is null)
        {
            return Task.CompletedTask;
        }

        Sincronizar();
        if (rascunho.Dados.EnderecoConferido || Lendo)
        {
            return Task.CompletedTask;
        }

        return LerAsync(atualizar: false);
    }

    /// <summary>
    /// Le o GPS. Com endereco guardado e sem pedir para atualizar, a leitura so confere a distancia (nao precisa de
    /// internet). Com "Ler de novo", busca o endereco do ponto novo e troca o guardado.
    /// </summary>
    public async Task LerAsync(bool atualizar)
    {
        if (Chave is not { } chave || !await _lendo.WaitAsync(0))
        {
            return;
        }

        try
        {
            Sincronizar();
            Mudar(EstadoEndereco.Lendo);
            Precisao = null;
            Distancia = null;
            if (!await plataforma.PermitirLocalizacaoAsync())
            {
                rascunho.EnderecoFoiConferido();
                Mudar(EstadoEndereco.SemPermissao);
                return;
            }

            var ponto = await plataforma.LocalizarAsync(Espera, PrecisaoDesejada, p =>
            {
                Precisao = p;
                Mudou?.Invoke();
            });
            if (ponto is not { Valido: true })
            {
                rascunho.EnderecoFoiConferido();
                Mudar(rascunho.Dados.Endereco.Length > 0 ? EstadoEndereco.Pronto : EstadoEndereco.SemSinal);
                return;
            }

            Precisao = ponto.Precisao;
            Aproximada = ponto.Precisao > PontoGps.Aproximada;
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
                Mudar(EstadoEndereco.Pronto);
                return;
            }

            await BuscarEnderecoAsync(chave, ponto);
        }
        finally
        {
            _lendo.Release();
        }
    }

    private async Task BuscarEnderecoAsync(string chave, PontoGps ponto)
    {
        ResultadoEndereco r;
        try
        {
            r = await plataforma.EnderecoAsync(ponto.Latitude, ponto.Longitude);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException or HttpRequestException or TaskCanceledException)
        {
            r = new ResultadoEndereco(null, FalhaEndereco.SemInternet);
        }

        if (Chave != chave)
        {
            return; // trocou de escola enquanto buscava: este endereco nao e da escola de agora
        }

        if (r.Texto is { Length: > 0 } texto)
        {
            rascunho.DefinirEndereco(texto, ponto, chave);
            Guardar(chave, texto, ponto, digitado: false);
            Mudar(EstadoEndereco.Pronto);
            return;
        }

        rascunho.Alterar(d => d with { Ponto = ponto, EnderecoDe = chave, EnderecoConferido = true });
        Mudar(r.Falha == FalhaEndereco.SemInternet ? EstadoEndereco.SemInternet : EstadoEndereco.SemServico);
    }

    /// <summary>"Tentar de novo": com o ponto ja guardado e sem endereco, so busca o endereco; senao, le o GPS de novo.</summary>
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

    private void Mudar(EstadoEndereco estado)
    {
        Estado = estado;
        Mudou?.Invoke();
    }
}
