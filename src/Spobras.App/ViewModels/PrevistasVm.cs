using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Spobras.Core;
using Spobras.Core.Base;
using Spobras.Dados.Base;

namespace Spobras.App.ViewModels;

/// <summary>Uma obra das bases Em licitacao ou A licitar, na lista e na ficha.</summary>
public sealed class PrevistaVm(ObraPrevista obra)
{
    public ObraPrevista Obra { get; } = obra;
    public string Nome => Obra.Nome;
    public string Detalhe => FichaPrevista.Detalhe(Obra);
    public string Valor => FichaPrevista.Reais(Obra.ValorObra);
    public string Selo => Obra.NomeDaBase;
    public IReadOnlyList<KeyValuePair<string, string>> Campos => FichaPrevista.Campos(Obra);
}

/// <summary>
/// Tela Base para as obras sem contrato (Em licitacao ou A licitar): lista, filtros, ficha, "tambem esta em" e
/// exportacao com o modelo da base. Os dados nunca se misturam com as obras em andamento.
/// </summary>
public sealed partial class PrevistasVm : ObservableObject
{
    private readonly BaseRepositorio _base;
    private readonly ApoioConsultas? _apoio;
    private IReadOnlyList<ObraPrevista> _todas = [];
    private bool _atualizando;
    private string? _ultimaExportacao;

    public PrevistasVm(BaseRepositorio @base, BaseDeObras qual, ApoioConsultas? apoio = null)
    {
        if (qual == BaseDeObras.EmAndamento)
        {
            throw new ArgumentException("Em andamento usa a tela de obras contratadas.", nameof(qual));
        }

        _base = @base;
        Qual = qual;
        _apoio = apoio;
    }

    public BaseDeObras Qual { get; }
    public string Titulo => BasesDeObras.Nome(Qual);
    public bool EhLicitacao => Qual == BaseDeObras.EmLicitacao;
    public string RotuloLote => Qual == BaseDeObras.ALicitar ? "Lote ou agrupamento" : "Lote";

    /// <summary>Logo das planilhas exportadas.</summary>
    public Func<string?>? Logo { get; set; }

    /// <summary>Leva a obra para o cronograma (pedir recurso antes do contrato).</summary>
    public Action<ObraPrevista>? AoUsarNoCronograma { get; set; }

    /// <summary>Abre a mesma escola em outra base.</summary>
    public Action<OutraBase>? AoAbrirOutra { get; set; }

    public ObservableCollection<PrevistaVm> Obras { get; } = [];
    public ObservableCollection<string> Portarias { get; } = [];
    public ObservableCollection<string> Lotes { get; } = [];
    public ObservableCollection<string> Modalidades { get; } = [];
    public ObservableCollection<string> Dotacoes { get; } = [];
    public ObservableCollection<string> Situacoes { get; } = [];
    public ObservableCollection<string> Empresas { get; } = [];
    public ObservableCollection<OutraBase> TambemEm { get; } = [];

    [ObservableProperty]
    private string _texto = "";

    [ObservableProperty]
    private IReadOnlyList<string> _portaria = [];

    [ObservableProperty]
    private IReadOnlyList<string> _lote = [];

    [ObservableProperty]
    private IReadOnlyList<string> _modalidade = [];

    [ObservableProperty]
    private IReadOnlyList<string> _dotacao = [];

    [ObservableProperty]
    private IReadOnlyList<string> _status = [];

    [ObservableProperty]
    private IReadOnlyList<string> _empresa = [];

    [ObservableProperty]
    private PrevistaVm? _selecionada;

    [ObservableProperty]
    private string _resumo = "";

    [ObservableProperty]
    private string _totais = "";

    [ObservableProperty]
    private string _resultado = "";

    [ObservableProperty]
    private bool _resultadoComFalha;

    [ObservableProperty]
    private bool _podeAbrirExportacao;

    public bool TemObras => _todas.Count > 0;
    public bool TemTambemEm => TambemEm.Count > 0;
    public bool PodeUsarNoCronograma => Selecionada is not null && AoUsarNoCronograma is not null;

    public FiltroPrevistas Filtro() => new()
    {
        Texto = Texto, Portaria = Portaria, Lote = Lote, Modalidade = Modalidade, Dotacao = Dotacao, Status = Status, Empresa = EhLicitacao ? Empresa : [],
    };

    /// <summary>Le a base de novo (depois de uma releitura da mestra) e reaplica os filtros.</summary>
    public void Carregar()
    {
        _todas = _base.Previstas(Qual);
        Repor(Portarias, Previstas.Opcoes(_todas, o => o.PortariaTexto));
        Repor(Lotes, Previstas.Lotes(_todas));
        Repor(Modalidades, Previstas.Opcoes(_todas, o => o.Modalidade));
        Repor(Dotacoes, Previstas.Opcoes(_todas, o => o.Dotacao));
        Repor(Situacoes, Previstas.Opcoes(_todas, o => o.Status));
        Repor(Empresas, EhLicitacao ? [FiltroPrevistas.SemEmpresa, .. Previstas.Opcoes(_todas, o => o.Empresa)] : []);
        OnPropertyChanged(nameof(TemObras));
        Buscar();
    }

    private static void Repor(ObservableCollection<string> lista, IEnumerable<string> valores)
    {
        lista.Clear();
        foreach (string v in valores) lista.Add(v);
    }

    private void Buscar()
    {
        long? antes = Selecionada?.Obra.Id;
        var achadas = Previstas.Filtrar(_todas, Filtro());
        Obras.Clear();
        foreach (var o in achadas) Obras.Add(new PrevistaVm(o));
        Selecionada = antes is { } id ? Obras.FirstOrDefault(o => o.Obra.Id == id) : null;
        var (n, valor, com10) = Previstas.Totais(achadas);
        string nome = Qual == BaseDeObras.EmLicitacao ? "obra em licitação" : "obra a licitar";
        string plural = Qual == BaseDeObras.EmLicitacao ? "obras em licitação" : "obras a licitar";
        Resumo = _todas.Count == 0
            ? $"Nenhuma obra {Titulo.ToLowerInvariant()} na base. Ela vem da aba “{(EhLicitacao ? ImportadorLicitacoes.AbaEmLicitacao : ImportadorLicitacoes.AbaALicitar)}” da planilha mestra."
            : n == 0 ? "Nenhuma obra com esses filtros." : Formato.Plural(n, nome, plural) + (n < _todas.Count ? $" de {_todas.Count}" : "");
        Totais = n == 0 ? "" : $"Valor das obras: {FichaPrevista.Reais(valor)} · com os 10%: {FichaPrevista.Reais(com10)}";
    }

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (_atualizando)
        {
            return;
        }

        switch (e.PropertyName)
        {
            case nameof(Texto) or nameof(Portaria) or nameof(Lote) or nameof(Modalidade) or nameof(Dotacao) or nameof(Status) or nameof(Empresa):
                Buscar();
                break;
            case nameof(Selecionada):
                TambemEm.Clear();
                if (Selecionada is { } s)
                {
                    foreach (var outra in _base.TambemEm(s.Nome, Qual, s.Obra.Id)) TambemEm.Add(outra);
                }

                OnPropertyChanged(nameof(TemTambemEm));
                OnPropertyChanged(nameof(PodeUsarNoCronograma));
                break;
        }
    }

    [RelayCommand]
    private void LimparFiltros()
    {
        _atualizando = true;
        Texto = "";
        Portaria = Lote = Modalidade = Dotacao = Status = Empresa = [];
        _atualizando = false;
        Buscar();
    }

    /// <summary>Mostra a obra pelo id (vinda do "tambem esta em" de outra base).</summary>
    public void Mostrar(long id)
    {
        if (Obras.FirstOrDefault(o => o.Obra.Id == id) is null)
        {
            LimparFiltros();
        }

        Selecionada = Obras.FirstOrDefault(o => o.Obra.Id == id);
    }

    [RelayCommand]
    private void AbrirOutra(OutraBase? outra)
    {
        if (outra is not null)
        {
            AoAbrirOutra?.Invoke(outra);
        }
    }

    [RelayCommand]
    private void UsarNoCronograma()
    {
        if (Selecionada is { } s)
        {
            AoUsarNoCronograma?.Invoke(s.Obra);
        }
    }

    /// <summary>Planilha nova com as obras do filtro, no modelo desta base, e uma aba com os filtros e a data.</summary>
    [RelayCommand]
    private void Exportar()
    {
        if (_apoio is null)
        {
            return;
        }

        var obras = Previstas.Filtrar(_todas, Filtro());
        if (obras.Count == 0)
        {
            Resultado = "Nenhuma obra com esses filtros: nada para exportar.";
            ResultadoComFalha = true;
            return;
        }

        string pasta = _apoio.Preferencias.DestinoDe(BaseVm.Modulo);
        string baseNome = Spobras.Core.Nomes.NomeArquivo.Corrigir($"Consulta - {Titulo} - " + _apoio.Relogio.Agora.ToString("yyyy-MM-dd", Formato.PtBr));
        string caminho = Path.Combine(pasta, Spobras.Core.Nomes.NomeArquivo.Livre(baseNome + ".xlsx", n => File.Exists(Path.Combine(pasta, n))));
        var (n, valor, com10) = Previstas.Totais(obras);
        List<object?[]> sobre =
        [
            ["Base", Titulo],
            ["Data da consulta", _apoio.Relogio.Agora.ToString("dd/MM/yyyy HH:mm", Formato.PtBr)],
            ["Obras exportadas", n],
            ["Valor das obras (antes dos 10%)", valor],
            ["Com os 10%", com10],
        ];
        var f = Filtro();
        void Filtrado(string nome, IReadOnlyList<string> valores)
        {
            if (valores.Count > 0) sobre.Add([$"Filtro: {nome}", string.Join(", ", valores)]);
        }

        if (f.Texto.Trim().Length > 0) sobre.Add(["Filtro: procurar", f.Texto.Trim()]);
        Filtrado("Portaria", f.Portaria);
        Filtrado(RotuloLote, f.Lote);
        Filtrado("Modalidade", f.Modalidade);
        Filtrado("Dotação", f.Dotacao);
        Filtrado("Situação", f.Status);
        Filtrado("Empresa", f.Empresa);
        if (f.Vazio) sobre.Add(["Filtros", "nenhum (todas as obras da base)"]);
        sobre.Add(["Observação", "O valor da obra é o da planilha antes dos 10%; a coluna com os 10% é a da própria planilha. Obras sem contrato: não há medições nem pagamentos."]);

        try
        {
            Spobras.Documentos.Planilhas.GravadorXlsx.Gravar(caminho,
            [
                new Spobras.Documentos.Planilhas.AbaNova("Obras", FichaPrevista.Titulos(Qual), [.. obras.Select(FichaPrevista.Linha)]),
                new Spobras.Documentos.Planilhas.AbaNova("Sobre esta consulta", ["Item", "Valor"], sobre, ComFiltro: false),
            ], Logo?.Invoke(), $"Consulta da Base de dados - {Titulo}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Resultado = $"A planilha não foi gerada: {ex.Message}";
            ResultadoComFalha = true;
            return;
        }

        _apoio.Operacoes.RegistrarCriados($"Exportou {Formato.Plural(n, "obra", "obras")} ({Titulo}) da Base de dados", [caminho]);
        _ultimaExportacao = caminho;
        PodeAbrirExportacao = true;
        Resultado = $"“{Path.GetFileName(caminho)}” gerado com {Formato.Plural(n, "obra", "obras")}.";
        ResultadoComFalha = false;
    }

    [RelayCommand]
    private void AbrirExportacao()
    {
        if (_ultimaExportacao is not null)
        {
            _apoio?.Dialogos.MostrarNaPasta(_ultimaExportacao);
        }
    }
}
