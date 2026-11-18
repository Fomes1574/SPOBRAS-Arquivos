namespace Spobras.Celular.Servicos;

/// <summary>
/// Raiz do app do celular: um so por aparelho (singleton). Junta a base, o rascunho do relatorio, as ferramentas de
/// PDF e as configuracoes, e guarda o ultimo resultado para as telas de "pronto".
/// </summary>
public sealed class AppCelular
{
    public AppCelular(IPlataforma plataforma, Func<DateTimeOffset>? agora = null)
    {
        Plataforma = plataforma;
        Agora = agora ?? (() => DateTimeOffset.Now);
        Directory.CreateDirectory(plataforma.PastaDoApp);
        Preferencias = new ArquivoPreferencias(plataforma.PastaDoApp);
        Base = new BaseCelular(plataforma, Preferencias, Agora);
        // Celular com pouca memoria prepara uma (ou duas) fotos por vez: mais devagar, mas sem o sistema fechar o app.
        Spobras.Documentos.Relatorio.PreparoDeFotos.Limite = Math.Clamp(plataforma.FotosAoMesmoTempo, 1, 4);
        Rascunho = new RascunhoRelatorio(plataforma.PastaDoApp, Agora, () => Preferencias.Ler().Carimbo);
        Enderecos = new EnderecosGuardados(plataforma.PastaDoApp);
        Endereco = new EnderecoCelular(plataforma, Rascunho, Enderecos, Agora, () => Preferencias.Ler().EnderecoPeloGps);
        Camera = new FotosDaCamera(this);
        Galeria = new FilaDaGaleria(this);
        Pdf = new PdfCelular(plataforma, Preferencias);
        ResumoPdf = new ResumoPdfCelular(this);
    }

    public IPlataforma Plataforma { get; }
    public Func<DateTimeOffset> Agora { get; }
    public ArquivoPreferencias Preferencias { get; }
    public BaseCelular Base { get; }
    public RascunhoRelatorio Rascunho { get; }
    public PdfCelular Pdf { get; }
    public EnderecosGuardados Enderecos { get; }
    public EnderecoCelular Endereco { get; }
    public FotosDaCamera Camera { get; }
    public FilaDaGaleria Galeria { get; }

    /// <summary>Filtro da tela Base (continua igual ao voltar da ficha).</summary>
    public Spobras.Core.Base.FiltroObras FiltroBase { get; set; } = new();

    public RelatorioPronto? UltimoRelatorio { get; set; }
    public PdfPronto? UltimoPdf { get; set; }
    public ResumoPdfCelular ResumoPdf { get; }
    public ResumoPdfSalvo? UltimoResumoPdf { get; set; }

    public Preferencias Config => Preferencias.Ler();

    public void Configurar(Func<Preferencias, Preferencias> mudar) => Preferencias.Gravar(mudar(Preferencias.Ler()));

    /// <summary>
    /// O fiscal ligou "Usar o endereco pelo GPS": so agora o app pede a permissao (a janela do Android) e le a
    /// posicao uma vez.
    /// </summary>
    public Task LigarEnderecoPeloGpsAsync()
    {
        Configurar(p => p with { EnderecoPeloGps = true });
        return Endereco.LerAsync(atualizar: false, pedirPermissao: true);
    }

    /// <summary>Desligado: o app nao usa mais a localizacao. O endereco que ja estava (lido ou digitado) continua.</summary>
    public void DesligarEnderecoPeloGps() => Configurar(p => p with { EnderecoPeloGps = false });

    /// <summary>Muda o carimbo: as copias carimbadas antigas saem e as novas sao feitas em segundo plano.</summary>
    public void Carimbar(Func<Spobras.Core.Relatorio.AjusteCarimbo, Spobras.Core.Relatorio.AjusteCarimbo> mudar)
    {
        var antes = Config.Carimbo;
        var depois = mudar(antes).Normalizado();
        if (depois == antes) return;
        Configurar(p => p with { Carimbo = depois });
        Rascunho.CarimboMudou();
    }

    /// <summary>Guarda os ultimos arquivos gerados (para a tela inicial).</summary>
    public void Registrar(IEnumerable<Salvo> salvos) =>
        Configurar(p => p with { UltimosGerados = [.. salvos.Concat(p.UltimosGerados).Take(6)] });

    /// <summary>
    /// Exclui de vez um arquivo que o app gerou (depois do tempo do Desfazer) e tira da lista. Devolve o motivo se
    /// nao der.
    /// </summary>
    public async Task<string?> ExcluirGeradoAsync(Salvo arquivo)
    {
        bool ok;
        try
        {
            ok = await Plataforma.ApagarAsync(arquivo);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ok = false;
        }

        if (!ok)
        {
            return $"Não deu para excluir “{arquivo.Nome}”: este local não deixa o app apagar. Apague pelo gerenciador de arquivos.";
        }

        Configurar(p => p with { UltimosGerados = [.. p.UltimosGerados.Where(s => s.Id != arquivo.Id)] });
        return null;
    }

    private DateTimeOffset? _ultimaReleitura;

    /// <summary>
    /// Ao abrir o app e ao voltar para ele: rele a planilha (so importa se mudou). Voltar da camera, do seletor de
    /// arquivos ou de uma ligacao rapida nao baixa a planilha de novo: espera pelo menos 2 minutos entre releituras.
    /// </summary>
    public Task AoAbrirAsync()
    {
        var agora = Agora();
        if (_ultimaReleitura is { } u && agora - u < TimeSpan.FromMinutes(2))
        {
            return Task.CompletedTask;
        }

        _ultimaReleitura = agora;
        return Base.AtualizarAsync();
    }

    /// <summary>Logo escolhido: copia para dentro do app (a imagem original nao muda).</summary>
    public async Task<string?> EscolherLogoAsync()
    {
        string pasta = Path.Combine(Plataforma.PastaDoApp, "logo");
        Directory.CreateDirectory(pasta);
        var escolhidos = await Plataforma.EscolherArquivosAsync("image/*", false, pasta);
        if (escolhidos.Count == 0 || escolhidos[0].Local is not { } local)
        {
            return null;
        }

        try
        {
            Spobras.Documentos.Imagens.Fotos.Ler(local);
        }
        catch (Spobras.Documentos.Imagens.FotoInvalidaException ex)
        {
            File.Delete(local);
            return ex.Message;
        }

        string? antigo = Config.Logo;
        Configurar(p => p with { Logo = local });
        if (antigo is not null && antigo != local && antigo.StartsWith(pasta, StringComparison.Ordinal) && File.Exists(antigo))
        {
            File.Delete(antigo); // copia antiga do proprio app
        }

        return null;
    }

    public string? LogoUrl()
    {
        if (Config.Logo is not { } l || !File.Exists(l))
        {
            return null;
        }

        try
        {
            return "data:image/jpeg;base64," + Convert.ToBase64String(Spobras.Documentos.Imagens.Fotos.Preparar(l, 400, 90));
        }
        catch (Spobras.Documentos.Imagens.FotoInvalidaException)
        {
            return null;
        }
    }
}
