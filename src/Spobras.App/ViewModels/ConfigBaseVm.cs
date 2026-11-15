using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Spobras.App.Servicos;
using Spobras.Core.Base;
using Spobras.Core.Planilhas;
using Spobras.Dados.Base;

namespace Spobras.App.ViewModels;

/// <summary>Uma coluna usada pelo app, com a coluna escolhida da planilha e as que existem para escolher.</summary>
public sealed partial class CampoConfigVm(CampoMestra campo, IReadOnlyList<string> opcoes, Action<CampoConfigVm> aoMudar) : ObservableObject
{
    public CampoMestra Campo { get; } = campo;
    public string Rotulo => Campo.Rotulo;
    public string Tabela => CamposMestra.NomeDaTabela(Campo.Tabela);
    public IReadOnlyList<string> Opcoes { get; } = opcoes;

    [ObservableProperty]
    private string? _coluna;

    [ObservableProperty]
    private bool _encontrada;

    [ObservableProperty]
    private string _exemplo = "";

    public bool Falta => !Encontrada && Campo.Obrigatorio;
    public string Situacao => Encontrada ? "✓" : Campo.Obrigatorio ? "!" : "–";

    partial void OnEncontradaChanged(bool value)
    {
        OnPropertyChanged(nameof(Falta));
        OnPropertyChanged(nameof(Situacao));
    }

    partial void OnColunaChanged(string? value) => aoMudar(this);
}

/// <summary>Uma tabela da mestra, com a tabela escolhida da planilha.</summary>
public sealed partial class TabelaConfigVm(TabelaMestra tabela, IReadOnlyList<string> opcoes, Action<TabelaConfigVm> aoMudar) : ObservableObject
{
    public TabelaMestra Tabela { get; } = tabela;
    public string Nome => CamposMestra.NomeDaTabela(Tabela);
    public IReadOnlyList<string> Opcoes { get; } = opcoes;

    [ObservableProperty]
    private string? _escolhida;

    [ObservableProperty]
    private bool _encontrada;

    [ObservableProperty]
    private string _linhas = "";

    public string Situacao => Encontrada ? "✓" : "!";

    partial void OnEncontradaChanged(bool value) => OnPropertyChanged(nameof(Situacao));

    partial void OnEscolhidaChanged(string? value) => aoMudar(this);
}

/// <summary>Um status do contrato da planilha e a situacao escolhida (andamento, concluido, nao mostrar).</summary>
public sealed partial class StatusConfigVm(string status, int obras, int situacao, bool novo, Action<StatusConfigVm> aoMudar) : ObservableObject
{
    public static readonly IReadOnlyList<string> Nomes = ["Andamento", "Concluído", "Não mostrar"];

    public string Status { get; } = status;
    public string Obras { get; } = obras == 1 ? "1 obra" : $"{obras:N0} obras";
    public bool Novo { get; } = novo;

    [ObservableProperty]
    private int _situacao = situacao;

    partial void OnSituacaoChanged(int value) => aoMudar(this);
}

/// <summary>
/// Configuracoes da base (engrenagem da Base de dados): onde estao as tabelas e colunas da mestra, como cada
/// status do contrato conta e o diagnostico das leituras. Vale so neste computador; a planilha nunca muda.
/// </summary>
public sealed partial class ConfigBaseVm : TelaVm
{
    public const string Modulo = "base-configuracoes";

    private readonly BaseRepositorio _base;
    private readonly ServicoFontes _fontes;
    private readonly ServicoPreferencias _preferencias;
    private readonly IDialogos _dialogos;
    private readonly Func<string, ILeitorPlanilha> _abrir;
    private readonly Action<string>? _navegar;
    private MapeamentoMestra _mapa = new();
    private bool _carregando;

    public ConfigBaseVm(BaseRepositorio @base, ServicoFontes fontes, ServicoPreferencias preferencias, IDialogos dialogos,
        Func<string, ILeitorPlanilha> abrir, Action<string>? navegar = null)
    {
        _base = @base;
        _fontes = fontes;
        _preferencias = preferencias;
        _dialogos = dialogos;
        _abrir = abrir;
        _navegar = navegar;
    }

    public override string Titulo => "Configurações da base";

    public ObservableCollection<TabelaConfigVm> Tabelas { get; } = [];
    public ObservableCollection<CampoConfigVm> Campos { get; } = [];
    public ObservableCollection<StatusConfigVm> Status { get; } = [];
    public ObservableCollection<string> Leituras { get; } = [];
    public ObservableCollection<string> Amostra { get; } = [];

    [ObservableProperty]
    private int _aba;

    [ObservableProperty]
    private string _arquivo = "";

    [ObservableProperty]
    private string _situacaoDasColunas = "";

    [ObservableProperty]
    private bool _faltaAlgo;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SalvarCommand))]
    private bool _testeOk;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SalvarCommand))]
    private bool _mudou;

    [ObservableProperty]
    private string _resultadoTeste = "";

    [ObservableProperty]
    private string _diagnostico = "";

    public override void AoEntrar()
    {
        _mapa = _preferencias.Atual.Mapeamento;
        Mudou = false;
        TesteOk = false;
        ResultadoTeste = "";
        Amostra.Clear();
        Resultado = "";
        Conferir();
        CarregarStatus();
        CarregarDiagnostico();
    }

    private string? CaminhoDaMestra => _base.Fonte(Fontes.Mestra)?.Caminho is { Length: > 0 } c && File.Exists(c) ? c : null;

    /// <summary>Le os cabecalhos da planilha e mostra o que bate com o mapeamento (sem importar nada).</summary>
    private void Conferir()
    {
        _carregando = true;
        Tabelas.Clear();
        Campos.Clear();
        var fonte = _base.Fonte(Fontes.Mestra);
        Arquivo = fonte is null ? "Nenhuma planilha escolhida (Fontes de dados)." : $"{Path.GetFileName(fonte.Caminho)} · lida em {fonte.UltimaLeitura?.ToLocalTime():dd/MM/yyyy 'às' HH:mm}";
        if (CaminhoDaMestra is not { } caminho)
        {
            SituacaoDasColunas = "A planilha mestra não foi encontrada no caminho escolhido. Confira em Fontes de dados.";
            FaltaAlgo = true;
            _carregando = false;
            return;
        }

        ConferenciaMapeamento conf;
        try
        {
            using var planilha = _abrir(caminho);
            conf = ConferenciaMestra.Conferir(planilha, _mapa);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Spobras.Documentos.Planilhas.PlanilhaInvalidaException)
        {
            SituacaoDasColunas = "Não foi possível abrir a planilha: " + ex.Message;
            FaltaAlgo = true;
            _carregando = false;
            return;
        }

        foreach (var t in conf.Tabelas)
        {
            Tabelas.Add(new TabelaConfigVm(t.Tabela, conf.TabelasDaPlanilha, TabelaMudou)
            {
                Escolhida = conf.TabelasDaPlanilha.FirstOrDefault(n => string.Equals(n, t.Nome, StringComparison.OrdinalIgnoreCase)),
                Encontrada = t.Encontrada,
                Linhas = t.Encontrada ? $"{t.Linhas:N0} linhas" : $"“{t.Nome}” não existe mais",
            });
        }

        foreach (var c in conf.Campos.OrderBy(c => c.Campo.Tabela).ThenBy(c => c.Encontrada || !c.Campo.Obrigatorio))
        {
            var colunas = conf.Tabelas.First(t => t.Tabela == c.Campo.Tabela).Colunas;
            Campos.Add(new CampoConfigVm(c.Campo, [.. colunas.Select(x => x.Nome)], CampoMudou)
            {
                Coluna = colunas.FirstOrDefault(x => string.Equals(x.Nome, c.Coluna, StringComparison.OrdinalIgnoreCase))?.Nome,
                Encontrada = c.Encontrada,
                Exemplo = colunas.FirstOrDefault(x => string.Equals(x.Nome, c.Coluna, StringComparison.OrdinalIgnoreCase))?.Exemplo ?? "",
            });
        }

        int faltam = conf.Faltando.Count + conf.Tabelas.Count(t => !t.Encontrada);
        FaltaAlgo = faltam > 0;
        SituacaoDasColunas = faltam switch
        {
            0 => $"As {conf.Campos.Count(c => c.Encontrada)} colunas usadas pelo app foram encontradas.",
            1 => "1 item não encontrado na planilha. Escolha o nome novo para o app voltar a ler tudo.",
            _ => $"{faltam} itens não encontrados na planilha. Escolha o nome novo para o app voltar a ler tudo.",
        };
        _carregando = false;
    }

    private void TabelaMudou(TabelaConfigVm t)
    {
        if (_carregando || t.Escolhida is null)
        {
            return;
        }

        _mapa = _mapa.ComTabela(t.Tabela, t.Escolhida);
        Mexeu();
    }

    private void CampoMudou(CampoConfigVm c)
    {
        if (_carregando || c.Coluna is null)
        {
            return;
        }

        _mapa = _mapa.ComColuna(c.Campo, c.Coluna);
        Mexeu();
    }

    private void Mexeu()
    {
        Mudou = true;
        TesteOk = false;
        ResultadoTeste = "";
        Amostra.Clear();
        Conferir();
    }

    [RelayCommand]
    private void Testar()
    {
        Amostra.Clear();
        if (CaminhoDaMestra is not { } caminho)
        {
            TesteOk = false;
            ResultadoTeste = "A planilha mestra não foi encontrada.";
            return;
        }

        try
        {
            using var planilha = _abrir(caminho);
            var dados = ImportadorMestra.Importar(planilha, _mapa);
            TesteOk = true;
            ResultadoTeste = $"Leitura de teste ok: {dados.Obras.Count:N0} obras, {dados.Medicoes.Count:N0} medições e {dados.Empenhos.Count:N0} empenhos.";
            foreach (var o in dados.Obras.Take(3))
            {
                Amostra.Add($"{o.Contrato} · {o.Empreendimento} · OS {FormatoBase.Data(o.DataOs)} · {FormatoBase.Reais(o.SaldoAPagar)}");
            }
        }
        catch (ImportacaoInvalidaException ex)
        {
            TesteOk = false;
            ResultadoTeste = ex.Message.Replace(" A base anterior foi mantida.", "", StringComparison.Ordinal);
        }
    }

    private bool PodeSalvar() => TesteOk && Mudou;

    /// <summary>Guarda o mapeamento e le a planilha de novo com ele.</summary>
    [RelayCommand(CanExecute = nameof(PodeSalvar))]
    private async Task Salvar()
    {
        _preferencias.Alterar(p => p with { Mapeamento = _mapa });
        Mudou = false;
        await _fontes.RelerAsync(Fontes.Mestra);
        Resultado = "Colunas salvas. A Base é lida de novo com elas.";
        ResultadoComFalha = false;
        CarregarDiagnostico();
    }

    [RelayCommand]
    private async Task VoltarAoPadrao()
    {
        if (!_dialogos.Confirmar("Voltar ao padrão?", "As colunas e os status voltam a ser os que vêm com o app. A planilha não muda, e a Base é lida de novo em seguida.", "Voltar ao padrão"))
        {
            return;
        }

        _preferencias.Alterar(p => p with { Mapeamento = new MapeamentoMestra(), RegrasStatus = [] });
        MedicoesEmTramite.DefinirRegras(null);
        await _fontes.RelerAsync(Fontes.Mestra);
        AoEntrar();
        Resultado = "Padrão do app restaurado.";
    }

    private void CarregarStatus()
    {
        _carregando = true;
        Status.Clear();
        foreach (var (status, obras) in _base.StatusDoContrato().Where(s => s.Status.Length > 0))
        {
            var atual = MedicoesEmTramite.Classificar(status);
            int indice = atual switch { SituacaoContrato.Concluido => 1, SituacaoContrato.Ignorado => 2, _ => 0 };
            Status.Add(new StatusConfigVm(status, obras, indice, atual == SituacaoContrato.Desconhecido, StatusMudou));
        }

        _carregando = false;
    }

    private void StatusMudou(StatusConfigVm s)
    {
        if (_carregando)
        {
            return;
        }

        var situacao = s.Situacao switch { 1 => SituacaoContrato.Concluido, 2 => SituacaoContrato.Ignorado, _ => SituacaoContrato.Andamento };
        var regras = new Dictionary<string, SituacaoContrato>(_preferencias.Atual.RegrasStatus, StringComparer.OrdinalIgnoreCase);
        if (MedicoesEmTramite.ClassificarPadrao(s.Status) == situacao)
        {
            regras.Remove(s.Status);
        }
        else
        {
            regras[s.Status] = situacao;
        }

        _preferencias.Alterar(p => p with { RegrasStatus = regras });
        MedicoesEmTramite.DefinirRegras(regras);
        Resultado = $"“{s.Status}” agora conta como {StatusConfigVm.Nomes[s.Situacao].ToLowerInvariant()}.";
        ResultadoComFalha = false;
    }

    private void CarregarDiagnostico()
    {
        var fonte = _base.Fonte(Fontes.Mestra);
        var linhas = new List<string>
        {
            $"Obras na base: {_base.TotalDeObras():N0}",
            $"Colunas fora do padrão: {(_preferencias.Atual.Mapeamento.EPadrao ? "nenhuma" : (_preferencias.Atual.Mapeamento.Colunas.Count + _preferencias.Atual.Mapeamento.Tabelas.Count).ToString())}",
            $"Regras de status próprias: {(MedicoesEmTramite.Personalizadas.Count == 0 ? "nenhuma" : MedicoesEmTramite.Personalizadas.Count.ToString())}",
        };
        if (fonte?.Avisos.Count > 0)
        {
            linhas.Add("Avisos da última leitura: " + string.Join(" ", fonte.Avisos));
        }

        Diagnostico = string.Join(Environment.NewLine, linhas);
        Leituras.Clear();
        foreach (var l in _base.HistoricoDeImportacoes(Fontes.Mestra, 10))
        {
            Leituras.Add($"{l.Quando.ToLocalTime():dd/MM/yyyy HH:mm} · {(l.Ok ? "ok" : "erro")} · {l.Mensagem}{(l.Ok && l.Linhas > 0 ? $" · {l.Linhas:N0} obras" : "")}");
        }
    }

    [RelayCommand]
    private void Voltar() => _navegar?.Invoke(BaseVm.Modulo);
}
