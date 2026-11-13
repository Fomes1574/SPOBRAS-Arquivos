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
        Rascunho = new RascunhoRelatorio(plataforma.PastaDoApp, Agora);
        Pdf = new PdfCelular(plataforma, Preferencias);
    }

    public IPlataforma Plataforma { get; }
    public Func<DateTimeOffset> Agora { get; }
    public ArquivoPreferencias Preferencias { get; }
    public BaseCelular Base { get; }
    public RascunhoRelatorio Rascunho { get; }
    public PdfCelular Pdf { get; }

    /// <summary>Filtro da tela Base (continua igual ao voltar da ficha).</summary>
    public Spobras.Core.Base.FiltroObras FiltroBase { get; set; } = new();

    public RelatorioPronto? UltimoRelatorio { get; set; }
    public PdfPronto? UltimoPdf { get; set; }

    public Preferencias Config => Preferencias.Ler();

    public void Configurar(Func<Preferencias, Preferencias> mudar) => Preferencias.Gravar(mudar(Preferencias.Ler()));

    /// <summary>Guarda os ultimos arquivos gerados (para a tela inicial).</summary>
    public void Registrar(IEnumerable<Salvo> salvos) =>
        Configurar(p => p with { UltimosGerados = [.. salvos.Concat(p.UltimosGerados).Take(6)] });

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
