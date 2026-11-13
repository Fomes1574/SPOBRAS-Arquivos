namespace Spobras.Celular.Servicos;

/// <summary>Arquivo ou pasta escolhida pelo usuario no seletor do Android (Id = endereco content://).</summary>
public sealed record Escolhido(string Id, string Nome, string? Local = null);

/// <summary>Arquivo salvo na pasta do usuario.</summary>
public sealed record Salvo(string Id, string Nome, string Onde, long Tamanho);

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

    /// <summary>Copia a planilha escolhida para um arquivo do app, para ler sem tocar na original.</summary>
    Task<Copia> CopiarAsync(string id, string destino);

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
}
