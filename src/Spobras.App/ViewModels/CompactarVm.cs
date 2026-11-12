using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Spobras.App.Servicos;
using Spobras.Core;
using Spobras.Core.Abstracoes;
using Spobras.Core.Compactar;
using Spobras.Core.Tarefas;
using Spobras.Documentos.Zip;

namespace Spobras.App.ViewModels;

public enum ModoCompactar
{
    UmPorPasta,
    Unico,
    PorLimite,
}

public sealed record EntradaCompactarVm(string Caminho, bool EhPasta)
{
    public string Nome => Path.GetFileName(Caminho.TrimEnd('\\', '/'));
    public string Descricao => EhPasta ? $"Pasta “{Nome}”" : Nome;
}

public sealed record ZipPreviaVm(string Nome, string Detalhe);

public sealed partial class CompactarVm : TelaVm
{
    public const string Modulo = "compactar";

    private readonly IArquivos _arquivos;
    private readonly IDialogos _dialogos;
    private readonly ServicoOperacoes _operacoes;
    private readonly ServicoPreferencias _preferencias;
    private readonly PlanejadorZip _planejador;
    private PlanoZip? _plano;
    private string? _ultimaPastaDeResultado;

    private readonly string _pastaCache;
    private PreparoFotos? _preparo;

    public CompactarVm(IArquivos arquivos, IDialogos dialogos, ServicoOperacoes operacoes, ServicoPreferencias preferencias, string? pastaCache = null)
    {
        _pastaCache = pastaCache ?? Path.Combine(Path.GetTempPath(), "ArquivosSPOBRAS", "fotos-reduzidas");
        _arquivos = arquivos;
        _dialogos = dialogos;
        _operacoes = operacoes;
        _preferencias = preferencias;
        _planejador = new PlanejadorZip(arquivos);
        _pastaDestino = preferencias.DestinoDe(Modulo);
    }

    public override string Titulo => "Compactar";

    public ObservableCollection<EntradaCompactarVm> Entradas { get; } = [];
    public ObservableCollection<ZipPreviaVm> Previa { get; } = [];
    public ObservableCollection<AvisoVm> Avisos { get; } = [];

    [ObservableProperty]
    private ModoCompactar _modo = ModoCompactar.UmPorPasta;

    [ObservableProperty]
    private string _nomeZip = "";

    [ObservableProperty]
    private string _limiteMb = "20";

    [ObservableProperty]
    private string _pastaDestino;

    [ObservableProperty]
    private string _textoBotao = "Criar .zip";

    [ObservableProperty]
    private bool _podeAbrirResultado;

    [ObservableProperty]
    private bool _reduzirFotos;

    [ObservableProperty]
    private QualidadeFotos _qualidadeFotos = QualidadeFotos.Media;

    /// <summary>Preparacao das copias reduzidas em andamento (os testes aguardam por ela).</summary>
    public Task Preparando { get; private set; } = Task.CompletedTask;

    partial void OnReduzirFotosChanged(bool value) => PrepararFotos();

    partial void OnQualidadeFotosChanged(QualidadeFotos value) => PrepararFotos();

    /// <summary>Faz (ou reaproveita) as copias reduzidas das fotos JPG da lista e refaz a previa com o tamanho novo.</summary>
    private void PrepararFotos()
    {
        _preparo = null;
        if (!ReduzirFotos || Entradas.Count == 0)
        {
            Replanejar();
            return;
        }

        var fotos = Entradas.SelectMany(e => e.EhPasta
                ? Directory.EnumerateFiles(e.Caminho, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
                : [e.Caminho])
            .Where(FotosReduzidas.Reduzivel).ToList();
        var (lado, qualidade) = QualidadeFotos switch { QualidadeFotos.Alta => (2400, 85), QualidadeFotos.Media => (1600, 75), _ => (1200, 65) };
        string cache = _pastaCache;
        PreparoFotos? pronto = null;
        Replanejar();
        var tarefa = _operacoes.Fila.Enfileirar($"Reduzir {Formato.Plural(fotos.Count, "foto", "fotos")} para o zip", (progresso, cancelamento) =>
        {
            pronto = FotosReduzidas.Preparar(fotos, cache, lado, qualidade, new Progresso(n => progresso.Report(new ProgressoTarefa(n, fotos.Count, null))), cancelamento);
            return Task.FromResult<string?>(Formato.Plural(pronto.Copias.Count, "foto reduzida", "fotos reduzidas"));
        });
        Preparando = FilaTarefas.Aguardar(tarefa).ContinueWith(_ =>
        {
            _preparo = pronto;
            Replanejar();
        }, SynchronizationContext.Current is null ? TaskScheduler.Default : TaskScheduler.FromCurrentSynchronizationContext());
    }

    private sealed class Progresso(Action<int> aoMudar) : IProgress<int>
    {
        public void Report(int value) => aoMudar(value);
    }

    public bool ModoUmPorPasta => Modo == ModoCompactar.UmPorPasta;
    public bool ModoUnico => Modo == ModoCompactar.Unico;
    public bool ModoPorLimite => Modo == ModoCompactar.PorLimite;
    public bool PedeNome => Modo != ModoCompactar.UmPorPasta;
    public bool TemEntradas => Entradas.Count > 0;

    public override void AoEntrar() => Replanejar();

    partial void OnModoChanged(ModoCompactar value)
    {
        OnPropertyChanged(nameof(ModoUmPorPasta));
        OnPropertyChanged(nameof(ModoUnico));
        OnPropertyChanged(nameof(ModoPorLimite));
        OnPropertyChanged(nameof(PedeNome));
        Replanejar();
    }

    partial void OnNomeZipChanged(string value) => Replanejar();

    partial void OnLimiteMbChanged(string value) => Replanejar();

    partial void OnPastaDestinoChanged(string value) => Replanejar();

    [RelayCommand]
    private void AdicionarPastas() => Adicionar(_dialogos.EscolherPastas("Escolha as pastas para compactar", _preferencias.Atual.PastaTrabalho));

    [RelayCommand]
    private void AdicionarArquivos() => Adicionar(_dialogos.EscolherArquivos("Escolha os arquivos para compactar", null, _preferencias.Atual.PastaTrabalho));

    public void Adicionar(IEnumerable<string> caminhos)
    {
        var existentes = new HashSet<string>(Entradas.Select(e => e.Caminho), StringComparer.OrdinalIgnoreCase);
        foreach (string caminho in caminhos)
        {
            bool pasta = _arquivos.PastaExiste(caminho);
            if ((pasta || _arquivos.ArquivoExiste(caminho)) && existentes.Add(caminho))
            {
                Entradas.Add(new EntradaCompactarVm(caminho, pasta));
            }
        }

        Resultado = "";
        if (string.IsNullOrWhiteSpace(NomeZip) && Entradas.Count > 0)
        {
            // Nome sugerido: o da primeira entrada (o usuario pode trocar).
            NomeZip = Entradas[0].EhPasta ? Entradas[0].Nome : Path.GetFileNameWithoutExtension(Entradas[0].Nome);
        }

        PrepararFotos();
    }

    [RelayCommand]
    private void Remover(EntradaCompactarVm entrada)
    {
        Entradas.Remove(entrada);
        PrepararFotos();
    }

    [RelayCommand]
    private void Limpar()
    {
        Entradas.Clear();
        NomeZip = "";
        Replanejar();
    }

    [RelayCommand]
    private void EscolherDestino()
    {
        if (_dialogos.EscolherPasta("Salvar os .zip em", PastaDestino) is { } pasta)
        {
            PastaDestino = pasta;
            _preferencias.LembrarDestino(Modulo, pasta);
        }
    }

    [RelayCommand]
    private async Task CriarAsync()
    {
        await Preparando; // nao gera o zip com a reducao pela metade
        if (_plano is null || !_plano.PodeExecutar)
        {
            return;
        }

        var plano = _plano;
        long? limite = Modo == ModoCompactar.PorLimite ? LimiteEmBytes() : null;
        var criados = new List<ZipCriado>();
        var falhas = new List<string>();

        Ocupado = true;
        try
        {
            var tarefa = _operacoes.Fila.Enfileirar($"Compactar: {Formato.Plural(plano.Zips.Count, "arquivo .zip", "arquivos .zip")}", (progresso, cancelamento) =>
            {
                for (int i = 0; i < plano.Zips.Count; i++)
                {
                    if (cancelamento.IsCancellationRequested)
                    {
                        break; // os zips ja concluidos ficam
                    }

                    var zip = plano.Zips[i];
                    progresso.Report(new ProgressoTarefa(i, plano.Zips.Count, zip.NomeZip));
                    try
                    {
                        criados.Add(Compactador.Criar(zip, limite, cancelamento));
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException)
                    {
                        falhas.Add($"{zip.NomeZip}: {ex.Message}");
                    }
                }

                progresso.Report(new ProgressoTarefa(plano.Zips.Count, plano.Zips.Count, null));
                return Task.FromResult<string?>($"{criados.Count} de {plano.Zips.Count} .zip criados");
            });
            await FilaTarefas.Aguardar(tarefa);
        }
        finally
        {
            Ocupado = false;
        }

        if (criados.Count > 0)
        {
            string descricao = criados.Count == 1
                ? $"Gerou “{Path.GetFileName(criados[0].Caminho)}”"
                : $"Gerou {criados.Count} arquivos .zip em {Path.GetFileName(PastaDestino.TrimEnd('\\', '/'))}";
            _operacoes.RegistrarCriados(descricao, criados.Select(c => c.Caminho));
            _ultimaPastaDeResultado = criados[0].Caminho;
        }

        int naoFeitos = plano.Zips.Count - criados.Count - falhas.Count;
        ResultadoComFalha = falhas.Count > 0;
        Resultado = Formato.Plural(criados.Count, "arquivo .zip criado", "arquivos .zip criados")
                    + $" ({Tamanho.Formatar(criados.Sum(c => c.Tamanho))})"
                    + (falhas.Count > 0 ? $". {Formato.Plural(falhas.Count, "falhou", "falharam")}: {falhas[0]}" : ".")
                    + (naoFeitos > 0 ? $" {naoFeitos} não foram feitos porque a tarefa foi cancelada." : "");
        PodeAbrirResultado = criados.Count > 0;

        if (falhas.Count == 0 && naoFeitos == 0)
        {
            Entradas.Clear();
            NomeZip = "";
        }

        Replanejar();
    }

    [RelayCommand]
    private void AbrirResultado()
    {
        if (_ultimaPastaDeResultado is not null)
        {
            _dialogos.MostrarNaPasta(_ultimaPastaDeResultado);
        }
    }

    [RelayCommand]
    private async Task DescompactarVariosAsync()
    {
        var zips = _dialogos.EscolherArquivos("Escolha os .zip para descompactar", "Arquivos .zip|*.zip", _preferencias.Atual.PastaTrabalho);
        if (zips.Count == 0)
        {
            return;
        }

        string destino = PastaDestino;
        var extraidos = new List<ZipExtraido>();
        var falhas = new List<string>();
        Ocupado = true;
        try
        {
            var tarefa = _operacoes.Fila.Enfileirar($"Descompactar: {Formato.Plural(zips.Count, "arquivo .zip", "arquivos .zip")}", (progresso, cancelamento) =>
            {
                for (int i = 0; i < zips.Count && !cancelamento.IsCancellationRequested; i++)
                {
                    progresso.Report(new ProgressoTarefa(i, zips.Count, Path.GetFileName(zips[i])));
                    try
                    {
                        extraidos.Add(Descompactador.Extrair(zips[i], destino, null, cancelamento));
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
                    {
                        falhas.Add($"{Path.GetFileName(zips[i])}: {ex.Message}");
                    }
                }

                return Task.FromResult<string?>($"{extraidos.Count} de {zips.Count} .zip abertos");
            });
            await FilaTarefas.Aguardar(tarefa);
        }
        finally
        {
            Ocupado = false;
        }

        ResultadoComFalha = falhas.Count > 0;
        Resultado = $"{Formato.Plural(extraidos.Count, "arquivo .zip aberto", "arquivos .zip abertos")}, cada um na sua pasta"
                    + $" ({Formato.Plural(extraidos.Sum(e => e.Arquivos), "arquivo", "arquivos")})"
                    + (falhas.Count > 0 ? $". {Formato.Plural(falhas.Count, "falhou", "falharam")}: {falhas[0]}" : ".");
        if (extraidos.Count > 0)
        {
            _ultimaPastaDeResultado = extraidos[0].Pasta;
            PodeAbrirResultado = true;
        }
    }

    private long LimiteEmBytes() =>
        decimal.TryParse(LimiteMb.Replace('.', ','), NumberStyles.Number, Formato.PtBr, out decimal mb) && mb > 0
            ? (long)(mb * 1024 * 1024)
            : 0;

    private void Replanejar()
    {
        Avisos.Clear();
        Previa.Clear();
        OnPropertyChanged(nameof(TemEntradas));
        if (Entradas.Count == 0)
        {
            _plano = null;
            TextoBotao = "Criar .zip";
            CriarCommand.NotifyCanExecuteChanged();
            return;
        }

        var caminhos = Entradas.Select(e => e.Caminho).ToList();
        string nome = string.IsNullOrWhiteSpace(NomeZip) ? "Arquivos" : NomeZip.Trim();
        var copias = ReduzirFotos ? _preparo?.Copias : null;
        _planejador.Substituir = copias is null ? null : c => copias.TryGetValue(c, out var copia) ? copia : null;
        if (ReduzirFotos)
        {
            Avisos.Add(_preparo is null
                ? new AvisoVm(Gravidade.Informacao, "Preparando as cópias reduzidas das fotos…")
                : new AvisoVm(Gravidade.Informacao, _preparo.Fotos == 0
                    ? "Não há fotos JPG na lista para reduzir."
                    : $"Fotos: {Formato.Plural(_preparo.Fotos, "foto", "fotos")}, de {Tamanho.Formatar(_preparo.Antes)} para {Tamanho.Formatar(_preparo.Depois)}. No zip vão cópias reduzidas; as fotos originais não mudam."));
            foreach (string problema in _preparo?.Problemas ?? [])
            {
                Avisos.Add(new AvisoVm(Gravidade.PrecisaConferir, problema));
            }
        }
        switch (Modo)
        {
            case ModoCompactar.UmPorPasta:
                _plano = _planejador.UmPorPasta([.. Entradas.Where(e => e.EhPasta).Select(e => e.Caminho)], PastaDestino);
                int soltos = Entradas.Count(e => !e.EhPasta);
                if (soltos > 0)
                {
                    Avisos.Add(new AvisoVm(Gravidade.PrecisaConferir,
                        $"{Formato.Plural(soltos, "arquivo solto fica", "arquivos soltos ficam")} de fora neste modo. Use \"Tudo num zip só\" para incluí-los."));
                }

                break;
            case ModoCompactar.Unico:
                _plano = _planejador.Unico(caminhos, nome, PastaDestino);
                break;
            default:
                _plano = _planejador.PorLimite(caminhos, nome, LimiteEmBytes(), PastaDestino);
                break;
        }

        foreach (var aviso in _plano.Avisos)
        {
            Avisos.Add(new AvisoVm(aviso));
        }

        foreach (var zip in _plano.Zips)
        {
            Previa.Add(new ZipPreviaVm(zip.NomeZip,
                $"{Formato.Plural(zip.Entradas.Count, "arquivo", "arquivos")} · até {Tamanho.Formatar(zip.TamanhoMaximoEstimado)}"));
        }

        TextoBotao = _plano.Zips.Count > 0 ? $"Criar {Formato.Plural(_plano.Zips.Count, "arquivo .zip", "arquivos .zip")}" : "Criar .zip";
        CriarCommand.NotifyCanExecuteChanged();
    }
}
