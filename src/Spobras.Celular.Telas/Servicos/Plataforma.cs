namespace Spobras.Celular.Servicos;

/// <summary>Arquivo ou pasta escolhida pelo usuario no seletor do Android (Id = endereco content://).</summary>
public sealed record Escolhido(string Id, string Nome, string? Local = null);

/// <summary>Arquivo salvo na pasta do usuario.</summary>
public sealed record Salvo(string Id, string Nome, string Onde, long Tamanho);

/// <summary>Por que o endereco nao veio.</summary>
public enum FalhaEndereco
{
    Nenhuma,

    /// <summary>Sem internet: o ponto do GPS fica guardado e o endereco e buscado depois.</summary>
    SemInternet,

    /// <summary>O celular nao tem o servico que transforma o ponto em endereco (ex.: sem os servicos do Google).</summary>
    SemServico,

    /// <summary>O servico respondeu, mas sem nenhum endereco para o ponto.</summary>
    NaoAchou,

    /// <summary>Falha passageira do servico (comum no Android, mesmo com internet): vale tentar de novo.</summary>
    Falhou,
}

/// <summary>
/// Como esta a localizacao do celular: permissao do app (e se e a precisa ou so a aproximada), localizacao do
/// celular ligada, e a localizacao por Wi-Fi e rede ligada (sem ela, dentro de predio quase nunca ha leitura).
/// </summary>
public sealed record SituacaoLocalizacao(bool Permitida, bool Precisa, bool Ligada, bool RedeLigada)
{
    public static readonly SituacaoLocalizacao Indisponivel = new(false, false, false, false);
}

/// <summary>Endereco de um ponto do GPS, ja no texto curto, ou o motivo de nao ter vindo.</summary>
public sealed record ResultadoEndereco(string? Texto, FalhaEndereco Falha = FalhaEndereco.Nenhuma);

public enum Copia
{
    Ok,

    /// <summary>A planilha foi renomeada, movida, apagada ou o acesso foi perdido.</summary>
    Inacessivel,

    /// <summary>Sem internet e sem copia guardada pelo app do OneDrive.</summary>
    SemConexao,
}

/// <summary>
/// O que muda entre o Android e o host de teste: seletores de arquivo, salvar na pasta do usuario, compartilhar.
/// Tudo que for regra fica fora daqui.
/// </summary>
public interface IPlataforma
{
    /// <summary>Pasta privada do app (rascunho, base, configuracoes). Nunca e a pasta do usuario.</summary>
    string PastaDoApp { get; }

    /// <summary>Escolhe a planilha mestra (.xlsx) uma vez; a permissao de leitura fica guardada.</summary>
    Task<Escolhido?> EscolherPlanilhaAsync();

    /// <summary>
    /// Apaga um arquivo que o proprio app gerou e salvou (relatorio, PDF). Falso se o local nao deixa apagar. As fotos
    /// da galeria nunca passam por aqui.
    /// </summary>
    Task<bool> ApagarAsync(Salvo arquivo);

    /// <summary>Copia a planilha escolhida para um arquivo do app, para ler sem tocar na original.</summary>
    Task<Copia> CopiarAsync(string id, string destino);

    /// <summary>
    /// Tamanho e data da planilha sem baixar o arquivo (para nao copiar de novo o que nao mudou). Nulo quando o
    /// aparelho ou o servico de nuvem nao informam: ai a planilha e copiada como sempre.
    /// </summary>
    Task<string?> AssinaturaAsync(string id) => Task.FromResult<string?>(null);

    /// <summary>Escolhe arquivos e copia para a pasta indicada (as originais nao mudam).</summary>
    Task<IReadOnlyList<Escolhido>> EscolherArquivosAsync(string tipo, bool varios, string pastaDestino);

    /// <summary>Escolhe a pasta onde os arquivos gerados serao salvos (permissao de escrita guardada).</summary>
    Task<Escolhido?> EscolherPastaAsync();

    /// <summary>Salva uma copia do arquivo na pasta do usuario, numa subpasta se pedida, sem sobrescrever.</summary>
    Task<Salvo> SalvarAsync(string pastaId, string? subpasta, string arquivo);

    /// <summary>Guarda a foto tambem na galeria do celular (opcional nas configuracoes).</summary>
    Task GuardarNaGaleriaAsync(string foto);

    Task CompartilharAsync(IReadOnlyList<string> arquivos);

    Task AbrirAsync(Salvo arquivo);

    /// <summary>Pede a permissao da camera (no Android); falso se o usuario negar.</summary>
    Task<bool> PermitirCameraAsync();

    /// <summary>Situacao da localizacao, sem pedir nada ao usuario (so confere).</summary>
    Task<SituacaoLocalizacao> SituacaoDaLocalizacaoAsync() => Task.FromResult(SituacaoLocalizacao.Indisponivel);

    /// <summary>
    /// Pede a permissao de localizacao "durante o uso" (o Android mostra a janela dele). So e chamado quando o fiscal
    /// liga o endereco pelo GPS. Falso se o usuario negar ou o aparelho nao tiver localizacao.
    /// </summary>
    Task<bool> PermitirLocalizacaoAsync() => Task.FromResult(false);

    /// <summary>Abre a tela do Android para ligar a localizacao (e a localizacao por Wi-Fi).</summary>
    Task AbrirConfiguracoesDeLocalizacaoAsync() => Task.CompletedTask;

    /// <summary>
    /// Ultima posicao que o celular ja conhece (de outro app ou leitura recente), na hora, sem ligar o GPS. Nula se
    /// nao houver nenhuma mais nova que <paramref name="idadeMaxima"/>.
    /// </summary>
    Task<Spobras.Core.Relatorio.PontoGps?> UltimaLocalizacaoAsync(TimeSpan idadeMaxima) => Task.FromResult<Spobras.Core.Relatorio.PontoGps?>(null);

    /// <summary>
    /// Le a posicao atual por GPS, Wi-Fi e rede ao mesmo tempo (uma vez so, nada fica rodando depois). Nenhuma
    /// leitura e descartada: fica com a melhor e para antes do limite quando chega a <paramref name="precisaoDesejada"/>.
    /// <paramref name="aoMelhorar"/> recebe cada leitura melhor que a anterior. Nulo se nenhuma leitura chegou.
    /// </summary>
    Task<Spobras.Core.Relatorio.PontoGps?> LocalizarAsync(TimeSpan limite, double precisaoDesejada, Action<Spobras.Core.Relatorio.PontoGps>? aoMelhorar = null, CancellationToken cancelar = default) =>
        Task.FromResult<Spobras.Core.Relatorio.PontoGps?>(null);

    /// <summary>
    /// O primeiro endereco (o mais proximo do ponto) pelo servico do proprio sistema, ja no texto curto. Precisa de
    /// internet.
    /// </summary>
    Task<ResultadoEndereco> EnderecoAsync(double latitude, double longitude) => Task.FromResult(new ResultadoEndereco(null, FalhaEndereco.SemServico));

    /// <summary>
    /// Quantas fotos o app prepara ao mesmo tempo: celular com pouca memoria prepara uma (ou duas) por vez para nao ser
    /// fechado pelo sistema.
    /// </summary>
    int FotosAoMesmoTempo => 4;
}
