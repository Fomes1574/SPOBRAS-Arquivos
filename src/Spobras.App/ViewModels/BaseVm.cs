using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Spobras.App.Servicos;
using Spobras.Core.Abstracoes;
using Spobras.Dados.Configuracao;
using Spobras.Core;
using Spobras.Core.Base;
using Spobras.Dados.Base;

namespace Spobras.App.ViewModels;

public static class FormatoBase
{
    public static string Reais(decimal? valor) => valor is { } v ? v.ToString("C2", Formato.PtBr) : "Sem informação";

    public static string Data(DateOnly? data) => data?.ToString("dd/MM/yyyy", Formato.PtBr) ?? "";

    public static (DateOnly? Data, bool Invalida) LerData(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            return (null, false);
        }

        return DateOnly.TryParseExact(texto.Trim(), ["d/M/yyyy", "dd/MM/yyyy", "d/M/yy"], Formato.PtBr, DateTimeStyles.None, out var data)
            ? (data, false)
            : (null, true);
    }
}

/// <summary>Uma linha do resultado: a obra com os valores ja no formato brasileiro.</summary>
public sealed class ObraVm(ObraLida obra)
{
    public ObraLida Obra { get; } = obra;
    public string Contrato => Obra.Contrato;
    public string Escola => Obra.Empreendimento;
    public string Portaria => Obra.Portaria ?? "";
    public string Lote => Obra.Lote ?? "";
    public string Status => Obra.Status ?? "";
    public string DataOs => Obra.DataOs is null ? "Sem data da OS" : FormatoBase.Data(Obra.DataOs);
    public string SaldoAPagar => FormatoBase.Reais(Obra.SaldoAPagar);
    public string ValorPago => FormatoBase.Reais(Obra.ValorPago);
    public string ValorContratado => FormatoBase.Reais(Obra.ValorContratadoComAditamento);

    /// <summary>Segunda linha da lista: contrato · status do contrato · lote · Data da OS.</summary>
    public string Detalhe => string.Join(" · ", new[] { Obra.Contrato, Obra.StatusContrato ?? Obra.Status, Obra.Lote, Obra.DataOs is null ? "sem Data da OS" : "OS " + FormatoBase.Data(Obra.DataOs) }
        .Where(t => !string.IsNullOrWhiteSpace(t)));

    /// <summary>"incluída no app", "2 campos editados no app" ou vazio.</summary>
    public string Marca => Obra.IncluidaNoApp ? "Incluída no app" : Obra.CamposEditados > 0 ? Formato.Plural(Obra.CamposEditados, "campo editado no app", "campos editados no app") : "";

    /// <summary>Todos os campos da obra, com o nome da planilha, para a ficha ao lado.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Campos
    {
        get
        {
            var campos = new List<KeyValuePair<string, string>>();
            void Somar(string rotulo, string? valor)
            {
                if (!string.IsNullOrWhiteSpace(valor))
                {
                    campos.Add(new(rotulo, valor));
                }
            }

            Somar("Contrato", Obra.Contrato);
            Somar("Escola / empreendimento", Obra.Empreendimento);
            Somar("Contratada", Obra.Contratada);
            Somar("Portaria", Obra.Portaria);
            Somar("Contrato de receita (do contrato, pelas medições)", Obra.ContratoReceita);
            Somar("Dotação", Obra.Dotacao);
            Somar("Atividade (dotação resumida)", Obra.Atividade);
            Somar("Lote", Obra.Lote);
            Somar("Processo SEI", Obra.ProcessoSei);
            Somar("Modalidade", Obra.Modalidade);
            Somar("Tipo de obra", Obra.TipoDeObra);
            Somar("Status", Obra.Status);
            Somar("Status do contrato", Obra.StatusContrato);
            Somar("Situação", Obra.Situacao);
            Somar("Data de assinatura", FormatoBase.Data(Obra.DataAssinatura));
            campos.Add(new("Data da OS (início da execução)", DataOs));
            Somar("Término da execução", FormatoBase.Data(Obra.Termino));
            foreach (var campo in Enum.GetValues<CampoValor>())
            {
                campos.Add(new(CamposValor.Rotulo(campo), FormatoBase.Reais(Obra.Valor(campo))));
            }

            Somar("Escopo", Obra.Escopo);
            return campos;
        }
    }

    /// <summary>Os mesmos campos agrupados em blocos, para a ficha larga (duas colunas por bloco).</summary>
    public IReadOnlyList<GrupoFicha> Grupos
    {
        get
        {
            List<KeyValuePair<string, string>> Bloco(params (string Rotulo, string? Valor)[] itens) =>
                [.. itens.Where(i => !string.IsNullOrWhiteSpace(i.Valor)).Select(i => new KeyValuePair<string, string>(i.Rotulo, i.Valor!))];

            var grupos = new List<GrupoFicha>
            {
                new("Prazo", Bloco(("Data da OS (início da execução)", DataOs), ("Término da execução", FormatoBase.Data(Obra.Termino)))),
                new("Identificação", Bloco(
                    ("Contrato", Obra.Contrato), ("Escola / empreendimento", Obra.Empreendimento), ("Contratada", Obra.Contratada),
                    ("Lote", Obra.Lote), ("Status", Obra.Status), ("Status do contrato", Obra.StatusContrato), ("Situação", Obra.Situacao),
                    ("Modalidade", Obra.Modalidade), ("Tipo de obra", Obra.TipoDeObra))),
                new("Orçamento", Bloco(
                    ("Portaria", Obra.Portaria), ("Contrato de receita (do contrato, pelas medições)", Obra.ContratoReceita), ("Dotação", Obra.Dotacao),
                    ("Atividade (dotação resumida)", Obra.Atividade), ("Processo SEI", Obra.ProcessoSei))),
                new("Escopo", Bloco(("Escopo", Obra.Escopo))),
            };
            return [Valores, .. grupos.Where(g => g.Campos.Count > 0)];
        }
    }

    /// <summary>
    /// Valores como estao na planilha: original, aditado (com o percentual sobre o original), contratado com
    /// aditamento, pago e saldo a pagar (pago + saldo = contratado). Medido e a medir nao entram: pago mais as
    /// medicoes em tramite ja e o medido.
    /// </summary>
    public GrupoFicha Valores => new("Valores",
    [
        new("Valor original (empreendimento)", FormatoBase.Reais(Obra.ValorEmpreendimento)),
        new("Aditado", Obra.ValorAditado is { } a
            ? FormatoBase.Reais(a) + (MedicoesEmTramite.PercentualAditado(Obra) is { } pct ? $" ({pct.ToString("0.0", Formato.PtBr)}%)" : "")
            : FormatoBase.Reais(null)),
        new("Contratado com aditamento", FormatoBase.Reais(Obra.ValorContratadoComAditamento)),
        new("Pago", FormatoBase.Reais(Obra.ValorPago)),
        new("Saldo a pagar", FormatoBase.Reais(Obra.SaldoAPagar)),
    ]);

    /// <summary>Os blocos depois das medicoes em tramite (Prazo, Identificacao, Orcamento, Escopo).</summary>
    public IReadOnlyList<GrupoFicha> GruposSecundarios => [.. Grupos.Skip(1)];

    /// <summary>"VIGENTE · em andamento", "ENCERRADO / TRD · concluído"...</summary>
    public string Situacao => (Obra.StatusContrato ?? "Sem status do contrato") + " · " + MedicoesEmTramite.Classificar(Obra.StatusContrato) switch
    {
        SituacaoContrato.Concluido => "concluído",
        SituacaoContrato.Ignorado => "fora dos relatórios",
        SituacaoContrato.Desconhecido => "status novo, tratado como andamento",
        _ => "em andamento",
    };
}

/// <summary>Uma medicao em tramite na ficha: numero, status e valor (considerado + reajuste).</summary>
public sealed record MedicaoTramiteVm(MedicaoEmTramite Medicao)
{
    public string Numero => Medicao.Numero;
    public string Status => Medicao.Status;
    public string ValorConsiderado => FormatoBase.Reais(Medicao.ValorConsiderado);
    public string Reajuste => Medicao.Reajuste == 0 ? "—" : FormatoBase.Reais(Medicao.Reajuste);
    public string Valor => FormatoBase.Reais(Medicao.Valor);
}

/// <summary>Um bloco da ficha da obra ("Prazo", "Valores"...), com os campos preenchidos.</summary>
public sealed record GrupoFicha(string Titulo, IReadOnlyList<KeyValuePair<string, string>> Campos);

public sealed record CampoValorVm(CampoValor Campo)
{
    public string Rotulo => CamposValor.Rotulo(Campo);
}

/// <summary>
/// Base de dados (especificacao 4.6): filtros vinculados que se atualizam a cada escolha, sem botao Buscar.
/// Consulta sempre a base local ja importada; a planilha nao e relida a cada clique.
/// </summary>
/// <summary>O que a Base de dados usa para salvar consultas e exportar. Sem ele, essas funcoes ficam escondidas.</summary>
public sealed record ApoioConsultas(IDialogos Dialogos, PerfilRepositorio Perfis, ServicoOperacoes Operacoes, ServicoPreferencias Preferencias, IRelogio Relogio);

/// <summary>Filtros de uma consulta salva, como o usuario os digitou (reabrir = mesmos filtros sobre a base atual).</summary>
public sealed record ConsultaSalva
{
    [System.Text.Json.Serialization.JsonConverter(typeof(ListaFiltroJson))]
    public IReadOnlyList<string> Portaria { get; init; } = [];
    [System.Text.Json.Serialization.JsonConverter(typeof(ListaFiltroJson))]
    public IReadOnlyList<string> Contrato { get; init; } = [];
    [System.Text.Json.Serialization.JsonConverter(typeof(ListaFiltroJson))]
    public IReadOnlyList<string> Escola { get; init; } = [];
    [System.Text.Json.Serialization.JsonConverter(typeof(ListaFiltroJson))]
    public IReadOnlyList<string> Lote { get; init; } = [];
    [System.Text.Json.Serialization.JsonConverter(typeof(ListaFiltroJson))]
    public IReadOnlyList<string> Empresa { get; init; } = [];
    [System.Text.Json.Serialization.JsonConverter(typeof(ListaFiltroJson))]
    public IReadOnlyList<string> ContratoReceita { get; init; } = [];
    [System.Text.Json.Serialization.JsonConverter(typeof(ListaFiltroJson))]
    public IReadOnlyList<string> Dotacao { get; init; } = [];
    [System.Text.Json.Serialization.JsonConverter(typeof(ListaFiltroJson))]
    public IReadOnlyList<string> Status { get; init; } = [];
    public string Texto { get; init; } = "";
    public CampoValor CampoValor { get; init; } = CampoValor.SaldoAPagar;
    public string ValorDe { get; init; } = "";
    public string ValorAte { get; init; } = "";
    public bool ValorSemInformacao { get; init; }
    public string OsDe { get; init; } = "";
    public string OsAte { get; init; } = "";
    public bool SemDataOs { get; init; }
    public OrdemObras Ordem { get; init; } = OrdemObras.Contrato;
}

public sealed partial class BaseVm : TelaVm
{
    public const string Modulo = "base";
    private const int Pagina = 300;

    private readonly BaseRepositorio _base;
    private readonly ServicoFontes _fontes;
    private readonly IInterface _interface;
    private readonly Action<string> _navegar;
    private readonly Action<ObraLida>? _usarNoRelatorio;
    private readonly Action<ObraLida>? _usarNoCronograma;
    private readonly Action<ObraLida>? _usarEmModelo;
    private readonly ApoioConsultas? _apoio;
    private const string ModuloConsultas = "consulta-base";
    private string? _ultimaExportacao;
    private bool _atualizando;
    private int _mostrando = Pagina;

    public BaseVm(
        BaseRepositorio @base, ServicoFontes fontes, IInterface @interface, Action<string> navegar, Action<ObraLida>? usarNoRelatorio = null,
        Action<ObraLida>? usarNoCronograma = null, Action<ObraLida>? usarEmModelo = null, ApoioConsultas? apoio = null)
    {
        _apoio = apoio;
        _usarEmModelo = usarEmModelo;
        _usarNoCronograma = usarNoCronograma;
        _base = @base;
        _fontes = fontes;
        _interface = @interface;
        _navegar = navegar;
        _usarNoRelatorio = usarNoRelatorio;
        CamposDeValor = [.. Enum.GetValues<CampoValor>().Select(c => new CampoValorVm(c))];
        _valorFiltrado = CamposDeValor.First(c => c.Campo == CampoValor.SaldoAPagar);
        fontes.Mudou += () => _interface.Executar(Buscar); // a fonte foi relida: reaplica os filtros ativos
        RecarregarConsultas();
        Buscar();
    }

    public ObservableCollection<PerfilGuardado> Consultas { get; } = [];

    /// <summary>Logo cadastrado (vai no topo das planilhas exportadas). Definido pela composicao do app.</summary>
    public Func<string?>? Logo { get; set; }

    /// <summary>Abre a busca dentro dos arquivos com um termo (contrato ou escola da obra escolhida).</summary>
    public Action<string>? AoBuscarNosArquivos { get; set; }

    [ObservableProperty]
    private EdicaoObraVm? _edicao;

    public bool Editando => Edicao is not null;

    partial void OnEdicaoChanged(EdicaoObraVm? value) => OnPropertyChanged(nameof(Editando));

    private DateTimeOffset Agora => _apoio?.Relogio.Agora ?? DateTimeOffset.Now;

    [RelayCommand]
    private void EditarObra()
    {
        if (Selecionada is { } obra)
        {
            Edicao = new EdicaoObraVm(obra.Obra, obra.Obra.IncluidaNoApp ? [] : _base.Ajustes(obra.Contrato, obra.Escola));
        }
    }

    [RelayCommand]
    private void NovaObra() => Edicao = new EdicaoObraVm(null, []);

    [RelayCommand]
    private void CancelarEdicao() => Edicao = null;

    [RelayCommand]
    private void SalvarEdicao()
    {
        if (Edicao is not { } edicao || edicao.Mudancas() is not { } mudancas)
        {
            return;
        }

        try
        {
            ObraLida gravada;
            if (edicao.Nova)
            {
                var o = new ObraLida
                {
                    Contrato = (string)mudancas["contrato"]!,
                    Empreendimento = (string)mudancas["empreendimento"]!,
                };
                gravada = _base.IncluirObra(o, Agora);
                mudancas.Remove("contrato");
                mudancas.Remove("empreendimento");
                if (mudancas.Count > 0)
                {
                    _base.GravarEdicao(gravada, mudancas, Agora);
                }

                Resultado = $"{gravada.Empreendimento} incluída no app (contrato {gravada.Contrato}). Ela aparece em todas as telas como as obras da planilha.";
            }
            else
            {
                gravada = edicao.Obra!;
                if (mudancas.Count > 0)
                {
                    _base.GravarEdicao(gravada, mudancas, Agora);
                }

                Resultado = mudancas.Count == 0
                    ? "Nada foi mudado."
                    : $"{Formato.Plural(mudancas.Count, "campo mudado", "campos mudados")} em {gravada.Empreendimento}. A planilha mestra não foi alterada.";
            }

            ResultadoComFalha = false;
            Edicao = null;
            Buscar();
            Selecionada = Obras.FirstOrDefault(o => o.Obra.Id == gravada.Id
                || (o.Contrato == gravada.Contrato && o.Escola == (mudancas.TryGetValue("empreendimento", out var e) && e is string s ? s : gravada.Empreendimento)));
        }
        catch (InvalidOperationException ex)
        {
            edicao.Problemas = ex.Message;
        }
    }

    /// <summary>Desfaz a mudanca de um campo: volta ao valor da planilha.</summary>
    [RelayCommand]
    private void VoltarAoDaPlanilha(CampoEdicaoObraVm campo)
    {
        if (Edicao?.Obra is not { } obra)
        {
            return;
        }

        _base.VoltarAoDaPlanilha(obra, campo.Campo.Chave);
        Buscar();
        var atual = Obras.FirstOrDefault(o => o.Obra.Id == obra.Id);
        Selecionada = atual;
        Edicao = atual is null ? null : new EdicaoObraVm(atual.Obra, _base.Ajustes(atual.Contrato, atual.Escola));
        Resultado = $"{campo.Rotulo} voltou ao valor da planilha.";
        ResultadoComFalha = false;
    }

    [RelayCommand]
    private void RemoverObraIncluida()
    {
        if (Selecionada is not { Obra.IncluidaNoApp: true } obra
            || _apoio?.Dialogos.Confirmar("Tirar a obra do app", $"Tirar {obra.Escola} (contrato {obra.Contrato}), que foi incluída no app? As obras da planilha mestra não são afetadas.", "Tirar") != true)
        {
            return;
        }

        _base.RemoverObraIncluida(obra.Obra);
        Edicao = null;
        Buscar();
        Resultado = $"{obra.Escola} saiu do app.";
        ResultadoComFalha = false;
    }

    /// <summary>Abre a conferencia de portarias e dotacoes de uma planilha.</summary>
    public Action? AoConferirPlanilha { get; set; }

    [RelayCommand]
    private void ConferirPlanilha() => AoConferirPlanilha?.Invoke();
    public bool PodeSalvarConsultas => _apoio is not null;
    public bool TemConsultas => Consultas.Count > 0;

    [ObservableProperty]
    private PerfilGuardado? _consultaAberta;

    [ObservableProperty]
    private string _pendencias = "";

    [ObservableProperty]
    private bool _temPendencias;

    [ObservableProperty]
    private bool _podeAbrirExportacao;

    public override string Titulo => "Base de dados";

    public ObservableCollection<ObraVm> Obras { get; } = [];
    public ObservableCollection<AvisoVm> Avisos { get; } = [];
    public ObservableCollection<string> Portarias { get; } = [];
    public ObservableCollection<string> Contratos { get; } = [];
    public ObservableCollection<string> Escolas { get; } = [];
    public ObservableCollection<string> Lotes { get; } = [];
    public ObservableCollection<string> ContratosReceita { get; } = [];

    /// <summary>Totais das obras do lote escolhido (contratado, medido, pago, saldos), na faixa acima da lista.</summary>
    public ObservableCollection<ItemCronograma> TotaisDoLote { get; } = [];

    [ObservableProperty]
    private string _tituloDoLote = "";

    public bool TemLote => TotaisDoLote.Count > 0;

    /// <summary>Ficha ocupando a area toda, por um momento (o "Ampliar").</summary>
    [ObservableProperty]
    private bool _fichaAmpliada;

    /// <summary>Filtros recolhidos numa linha, para a lista e a ficha ganharem altura.</summary>
    [ObservableProperty]
    private bool _filtrosRecolhidos;

    [RelayCommand]
    private void AlternarAmpliar() => FichaAmpliada = Selecionada is not null && !FichaAmpliada;

    [RelayCommand]
    private void AlternarFiltros() => FiltrosRecolhidos = !FiltrosRecolhidos;

    /// <summary>Resumo dos filtros ativos, mostrado quando eles estao recolhidos.</summary>
    public string FiltrosAtivos
    {
        get
        {
            var partes = FiltrosEmUso().Select(f => f.Valor).ToList();
            return partes.Count == 0 ? "Sem filtros" : string.Join(" · ", partes);
        }
    }

    /// <summary>Parte da largura que a ficha ocupa (0,3 a 0,8); fica guardada para a proxima vez.</summary>
    public double ProporcaoDaFicha => Math.Clamp(_apoio?.Preferencias.Atual.ProporcaoDaFichaBase ?? 0.6, 0.3, 0.8);

    public void GuardarProporcaoDaFicha(double proporcao)
    {
        double valor = Math.Round(Math.Clamp(proporcao, 0.3, 0.8), 3);
        _apoio?.Preferencias.Alterar(p => p with { ProporcaoDaFichaBase = valor });
    }
    public ObservableCollection<string> Empresas { get; } = [];
    public ObservableCollection<string> Dotacoes { get; } = [];
    public ObservableCollection<string> Situacoes { get; } = [];
    public IReadOnlyList<CampoValorVm> CamposDeValor { get; }

    [ObservableProperty]
    private IReadOnlyList<string> _portaria = [];

    [ObservableProperty]
    private IReadOnlyList<string> _contrato = [];

    [ObservableProperty]
    private IReadOnlyList<string> _escola = [];

    [ObservableProperty]
    private IReadOnlyList<string> _lote = [];

    [ObservableProperty]
    private IReadOnlyList<string> _empresa = [];

    [ObservableProperty]
    private IReadOnlyList<string> _contratoReceita = [];

    [ObservableProperty]
    private IReadOnlyList<string> _dotacao = [];

    [ObservableProperty]
    private IReadOnlyList<string> _status = [];

    [ObservableProperty]
    private string _texto = "";

    [ObservableProperty]
    private CampoValorVm _valorFiltrado;

    [ObservableProperty]
    private string _valorDe = "";

    [ObservableProperty]
    private string _valorAte = "";

    [ObservableProperty]
    private bool _valorSemInformacao;

    [ObservableProperty]
    private string _osDe = "";

    [ObservableProperty]
    private string _osAte = "";

    [ObservableProperty]
    private bool _semDataOs;

    [ObservableProperty]
    private OrdemObras _ordem = OrdemObras.Contrato;

    [ObservableProperty]
    private string _resumo = "";

    [ObservableProperty]
    private string _estadoDaBase = "";

    [ObservableProperty]
    private bool _temBase;

    [ObservableProperty]
    private bool _temMais;

    [ObservableProperty]
    private ObraVm? _selecionada;

    [ObservableProperty]
    private string _resumoDasMedicoes = "";

    /// <summary>Medicoes ainda nao pagas da obra escolhida (so em contrato em andamento).</summary>
    public ObservableCollection<MedicaoTramiteVm> MedicoesEmTramiteDaObra { get; } = [];

    [ObservableProperty]
    private string _totalEmTramite = "";

    public bool TemMedicoesEmTramite => MedicoesEmTramiteDaObra.Count > 0;

    public bool TemFiltro => !Filtro().Vazio;
    public bool PodeUsarNoRelatorio => _usarNoRelatorio is not null && Selecionada is not null;
    public bool PodeUsarNoCronograma => _usarNoCronograma is not null && Selecionada is not null;

    public override void AoEntrar() => Buscar();

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (_atualizando)
        {
            return;
        }

        switch (e.PropertyName)
        {
            case nameof(Portaria) or nameof(Contrato) or nameof(Escola) or nameof(Lote) or nameof(Empresa) or nameof(ContratoReceita) or nameof(Dotacao) or nameof(Status)
                or nameof(Texto) or nameof(ValorFiltrado) or nameof(ValorDe) or nameof(ValorAte) or nameof(ValorSemInformacao)
                or nameof(OsDe) or nameof(OsAte) or nameof(SemDataOs) or nameof(Ordem):
                _mostrando = Pagina;
                Buscar();
                break;
            case nameof(ConsultaAberta) when ConsultaAberta is { } consulta:
                Aplicar(PerfilRepositorio.Abrir<ConsultaSalva>(consulta) ?? new ConsultaSalva());
                break;
            case nameof(Selecionada):
                if (Selecionada is null) FichaAmpliada = false;
                OnPropertyChanged(nameof(PodeUsarNoRelatorio));
                OnPropertyChanged(nameof(PodeUsarNoCronograma));
                AtualizarMedicoes();
                break;
        }
    }

    [RelayCommand]
    private void Limpar(string campo)
    {
        switch (campo)
        {
            case "portaria": Portaria = []; break;
            case "contrato": Contrato = []; break;
            case "escola": Escola = []; break;
            case "lote": Lote = []; break;
            case "empresa": Empresa = []; break;
            case "receita": ContratoReceita = []; break;
            case "dotacao": Dotacao = []; break;
            case "status": Status = []; break;
        }
    }

    [RelayCommand]
    private void LimparFiltros()
    {
        FichaAmpliada = false;
        _atualizando = true;
        Portaria = Contrato = Escola = Lote = Empresa = ContratoReceita = Dotacao = Status = [];
        Texto = ValorDe = ValorAte = OsDe = OsAte = "";
        ValorSemInformacao = SemDataOs = false;
        _atualizando = false;
        _mostrando = Pagina;
        Buscar();
    }

    [RelayCommand]
    private void MostrarMais()
    {
        _mostrando += Pagina;
        Buscar();
    }

    [RelayCommand]
    private void AbrirFontes() => _navegar(FontesVm.Modulo);

    [RelayCommand]
    private void UsarNoRelatorio()
    {
        if (Selecionada is { } obra)
        {
            _usarNoRelatorio?.Invoke(obra.Obra);
        }
    }

    [RelayCommand]
    private void UsarNoCronograma()
    {
        if (Selecionada is { } obra)
        {
            _usarNoCronograma?.Invoke(obra.Obra);
        }
    }

    [RelayCommand]
    private void BuscarNosArquivos(string? oQue)
    {
        if (Selecionada is { } obra)
        {
            AoBuscarNosArquivos?.Invoke(oQue == "escola" ? obra.Escola : obra.Contrato);
        }
    }

    [RelayCommand]
    private void UsarEmModelo()
    {
        if (Selecionada is { } obra)
        {
            _usarEmModelo?.Invoke(obra.Obra);
        }
    }

    private ConsultaSalva Atual() => new()
    {
        Portaria = Portaria, Contrato = Contrato, Escola = Escola, Lote = Lote, Empresa = Empresa, ContratoReceita = ContratoReceita, Dotacao = Dotacao, Status = Status,
        Texto = Texto, CampoValor = ValorFiltrado.Campo, ValorDe = ValorDe, ValorAte = ValorAte, ValorSemInformacao = ValorSemInformacao,
        OsDe = OsDe, OsAte = OsAte, SemDataOs = SemDataOs, Ordem = Ordem,
    };

    private void Aplicar(ConsultaSalva c)
    {
        _atualizando = true;
        Portaria = c.Portaria;
        Contrato = c.Contrato;
        Escola = c.Escola;
        Lote = c.Lote;
        Empresa = c.Empresa;
        ContratoReceita = c.ContratoReceita;
        Dotacao = c.Dotacao;
        Status = c.Status;
        Texto = c.Texto;
        ValorFiltrado = CamposDeValor.First(v => v.Campo == c.CampoValor);
        ValorDe = c.ValorDe;
        ValorAte = c.ValorAte;
        ValorSemInformacao = c.ValorSemInformacao;
        OsDe = c.OsDe;
        OsAte = c.OsAte;
        SemDataOs = c.SemDataOs;
        Ordem = c.Ordem;
        _atualizando = false;
        _mostrando = Pagina;
        Buscar();
    }

    private void RecarregarConsultas()
    {
        Consultas.Clear();
        foreach (var consulta in _apoio?.Perfis.Listar(ModuloConsultas) ?? [])
        {
            Consultas.Add(consulta);
        }

        OnPropertyChanged(nameof(TemConsultas));
    }

    /// <summary>Descricao dos filtros em uso, uma linha por filtro (para o nome sugerido e para a exportacao).</summary>
    public IReadOnlyList<(string Filtro, string Valor)> FiltrosEmUso()
    {
        var lista = new List<(string, string)>();
        void Por(string nome, string? valor)
        {
            if (!string.IsNullOrWhiteSpace(valor))
            {
                lista.Add((nome, valor.Trim()));
            }
        }

        Por("Portaria", string.Join(", ", Portaria));
        Por("Contrato", string.Join(", ", Contrato));
        Por("Escola", string.Join(", ", Escola));
        Por("Lote", string.Join(", ", Lote));
        Por("Empresa", string.Join(", ", Empresa));
        Por("Contrato de receita", string.Join(", ", ContratoReceita));
        Por("Dotação", string.Join(", ", Dotacao));
        Por("Status", string.Join(", ", Status));
        Por("Texto", Texto);
        var faixa = Filtro().Valores;
        if (faixa.TryGetValue(ValorFiltrado.Campo, out var f))
        {
            string limites = (f.Minimo, f.Maximo) switch
            {
                ({ } a, { } b) => $"de {FormatoBase.Reais(a)} a {FormatoBase.Reais(b)}",
                ({ } a, null) => $"a partir de {FormatoBase.Reais(a)}",
                (null, { } b) => $"até {FormatoBase.Reais(b)}",
                _ => "",
            };
            Por(ValorFiltrado.Rotulo, string.Join(" ", new[] { limites, f.SemInformacao ? "(inclui sem informação)" : "" }.Where(t => t.Length > 0)));
        }

        var de = FormatoBase.LerData(OsDe).Data;
        var ate = FormatoBase.LerData(OsAte).Data;
        if (de is not null || ate is not null)
        {
            Por("Data da OS", (de, ate) switch
            {
                ({ } a, { } b) => $"de {FormatoBase.Data(a)} a {FormatoBase.Data(b)}",
                ({ } a, null) => $"a partir de {FormatoBase.Data(a)}",
                _ => $"até {FormatoBase.Data(ate)}",
            });
        }

        if (SemDataOs)
        {
            lista.Add(("Data da OS", "só obras sem Data da OS"));
        }

        return lista;
    }

    [RelayCommand]
    private void SalvarConsulta()
    {
        if (_apoio is null)
        {
            return;
        }

        var filtros = FiltrosEmUso();
        string sugestao = ConsultaAberta?.Nome ?? (filtros.Count == 0 ? "Todas as obras" : string.Join(" · ", filtros.Take(2).Select(f => f.Valor)));
        string? nome = _apoio.Dialogos.PedirTexto("Salvar consulta", "Nome da consulta (se já existir uma com esse nome, ela é atualizada):", sugestao);
        if (string.IsNullOrWhiteSpace(nome))
        {
            return;
        }

        var salva = _apoio.Perfis.Salvar(ModuloConsultas, nome, Atual());
        _atualizando = true;
        RecarregarConsultas();
        ConsultaAberta = Consultas.FirstOrDefault(c => c.Id == salva.Id);
        _atualizando = false;
        Resultado = $"Consulta “{salva.Nome}” salva. Ao abrir, ela usa os dados mais recentes da base.";
        ResultadoComFalha = false;
    }

    [RelayCommand]
    private void RemoverConsulta()
    {
        if (_apoio is null || ConsultaAberta is not { } consulta
            || !_apoio.Dialogos.Confirmar("Tirar consulta", $"Tirar a consulta “{consulta.Nome}” da lista? Os filtros na tela continuam como estão.", "Tirar"))
        {
            return;
        }

        _apoio.Perfis.Remover(consulta.Id);
        _atualizando = true;
        ConsultaAberta = null;
        RecarregarConsultas();
        _atualizando = false;
        Resultado = $"Consulta “{consulta.Nome}” tirada da lista.";
        ResultadoComFalha = false;
    }

    [RelayCommand]
    private void VerObrasSemOs()
    {
        LimparFiltros();
        SemDataOs = true;
    }

    /// <summary>
    /// Exporta todas as obras do filtro atual (nao so as da tela) para uma planilha nova, com uma aba
    /// dizendo os filtros, a data da consulta e de qual leitura das fontes os dados vieram.
    /// </summary>
    [RelayCommand]
    private void Exportar()
    {
        if (_apoio is null)
        {
            return;
        }

        var resultado = _base.Buscar(Filtro(), int.MaxValue, 0, Ordem, decrescente: Ordem == OrdemObras.SaldoAPagar);
        if (resultado.Total == 0)
        {
            Resultado = "Nenhuma obra com esses filtros: nada para exportar.";
            ResultadoComFalha = true;
            return;
        }

        string pasta = _apoio.Preferencias.DestinoDe(Modulo);
        string baseNome = Spobras.Core.Nomes.NomeArquivo.Corrigir("Consulta - " + (ConsultaAberta?.Nome ?? "Base de dados") + " - "
                                                                   + _apoio.Relogio.Agora.ToString("yyyy-MM-dd", Formato.PtBr));
        string caminho = Path.Combine(pasta, Spobras.Core.Nomes.NomeArquivo.Livre(baseNome + ".xlsx", n => File.Exists(Path.Combine(pasta, n))));

        string[] titulos =
        [
            "Contrato", "Escola / empreendimento", "Contratada", "Portaria", "Contrato de receita", "Dotação", "Atividade", "Lote",
            "Modalidade", "Tipo", "Tipo de obra", "Processo SEI", "Status", "Status do contrato", "Situação", "Escopo",
            "Data de assinatura", "Data da OS (INÍCIO EXECUÇÃO)", "Término",
            .. Enum.GetValues<CampoValor>().Select(CamposValor.Rotulo),
        ];
        var linhas = resultado.Obras.Select(o => new object?[]
        {
            o.Contrato, o.Empreendimento, o.Contratada, o.Portaria, o.ContratoReceita, o.Dotacao, o.Atividade, o.Lote,
            o.Modalidade, o.Tipo, o.TipoDeObra, o.ProcessoSei, o.Status, o.StatusContrato, o.Situacao, o.Escopo,
            o.DataAssinatura, o.DataOs, o.Termino,
        }.Concat(Enum.GetValues<CampoValor>().Select(c => (object?)o.Valor(c))).ToArray()).ToList();

        var sobre = new List<object?[]> { new object?[] { "Data da consulta", _apoio.Relogio.Agora.ToString("dd/MM/yyyy HH:mm", Formato.PtBr) } };
        sobre.Add(["Obras exportadas", resultado.Total]);
        var filtros = FiltrosEmUso();
        sobre.AddRange(filtros.Count == 0 ? [["Filtros", "nenhum (todas as obras)"]] : filtros.Select(f => new object?[] { "Filtro: " + f.Filtro, f.Valor }));
        if (ConsultaAberta is { } c)
        {
            sobre.Add(["Consulta salva", c.Nome]);
        }

        foreach (var fonte in _fontes.Fontes())
        {
            string quando = fonte.UltimaLeitura is { } u ? Formato.DataHora(u) : "nunca lida";
            sobre.Add([$"Fonte: {fonte.Nome}", $"{Path.GetFileName(fonte.Caminho)} · lida em {quando}" + (fonte.Estado == EstadoFonte.Atualizada ? "" : $" · ATENÇÃO: {fonte.Mensagem}")]);
        }

        sobre.Add(["Observação", "Os valores são os da planilha mestra, por obra (contrato + escola). Medições e pagamentos são do contrato inteiro."]);

        try
        {
            Spobras.Documentos.Planilhas.GravadorXlsx.Gravar(caminho,
            [
                new Spobras.Documentos.Planilhas.AbaNova("Obras", titulos, linhas),
                new Spobras.Documentos.Planilhas.AbaNova("Sobre esta consulta", ["Item", "Valor"], sobre, ComFiltro: false),
            ], Logo?.Invoke(), "Consulta da Base de dados" + (ConsultaAberta is { } aberta ? $" - {aberta.Nome}" : ""));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Resultado = $"A planilha não foi gerada: {ex.Message}";
            ResultadoComFalha = true;
            return;
        }

        _apoio.Operacoes.RegistrarCriados($"Exportou {Formato.Plural(resultado.Total, "obra", "obras")} da Base de dados", [caminho]);
        _ultimaExportacao = caminho;
        PodeAbrirExportacao = true;
        Resultado = $"“{Path.GetFileName(caminho)}” gerado com {Formato.Plural(resultado.Total, "obra", "obras")}" + (TemMais ? " (todas, não só as da tela)." : ".");
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

    private void AtualizarPendencias()
    {
        var partes = new List<string>();
        if (TemBase)
        {
            int semOs = _base.Buscar(new FiltroObras { SemDataOs = true }, 1).Total;
            if (semOs > 0)
            {
                partes.Add(Formato.Plural(semOs, "obra sem Data da OS", "obras sem Data da OS"));
            }
        }

        foreach (var fonte in _fontes.Fontes().Where(f => f.Caminho.Length > 0 && f.Estado != EstadoFonte.Atualizada))
        {
            partes.Add($"{fonte.Nome}: {fonte.Mensagem}");
        }

        TemPendencias = partes.Count > 0;
        Pendencias = string.Join(" · ", partes);
    }

    private FiltroObras Filtro()
    {
        var valores = new Dictionary<CampoValor, FaixaValor>();
        var faixa = new FaixaValor(Normalizacao.LerReais(ValorDe), Normalizacao.LerReais(ValorAte), ValorSemInformacao);
        if (!faixa.Vazia)
        {
            valores[ValorFiltrado.Campo] = faixa;
        }

        return new FiltroObras
        {
            Portaria = Portaria,
            Contrato = Contrato,
            Escola = Escola,
            Lote = Lote,
            Empresa = Empresa,
            ContratoReceita = ContratoReceita,
            Dotacao = Dotacao,
            Status = Status,
            Texto = Texto,
            OsDe = FormatoBase.LerData(OsDe).Data,
            OsAte = FormatoBase.LerData(OsAte).Data,
            SemDataOs = SemDataOs,
            Valores = valores,
        };
    }

    private void Buscar()
    {
        Avisos.Clear();
        var mestra = _fontes.Fontes().First(f => f.Id == Fontes.Mestra);
        int total = _base.TotalDeObras();
        TemBase = total > 0;
        EstadoDaBase = !TemBase
            ? "A base ainda está vazia. Aponte a planilha mestra em Fontes de dados; depois disso o app relê sozinho sempre que ela for salva."
            : mestra.Estado == EstadoFonte.Atualizada
                ? $"{total.ToString("N0", Formato.PtBr)} obras · planilha lida em {Formato.DataHora(mestra.UltimaLeitura!.Value)}"
                : $"{total.ToString("N0", Formato.PtBr)} obras · ATENÇÃO: não é a leitura atual. {mestra.Mensagem}";

        // Valores digitados que nao sao numero ou data: avisa e nao aplica (em vez de filtrar errado).
        if (ValorDe.Trim().Length > 0 && Normalizacao.LerReais(ValorDe) is null || ValorAte.Trim().Length > 0 && Normalizacao.LerReais(ValorAte) is null)
        {
            Avisos.Add(new AvisoVm(Gravidade.PrecisaConferir, "O valor digitado não foi entendido e não está sendo aplicado. Use o formato 1.000,00."));
        }

        if (FormatoBase.LerData(OsDe).Invalida || FormatoBase.LerData(OsAte).Invalida)
        {
            Avisos.Add(new AvisoVm(Gravidade.PrecisaConferir, "A data digitada não existe e não está sendo aplicada. Use dia/mês/ano, como 10/11/2026."));
        }

        var filtro = Filtro();
        var opcoes = _base.Opcoes(filtro);
        _atualizando = true;
        // Trocar a lista faz a tela soltar a selecao; o valor escolhido e reposto logo depois.
        Trocar(Portarias, opcoes.Portarias, Portaria, "Portaria");
        Trocar(Contratos, opcoes.Contratos, Contrato, "Contrato");
        Trocar(Escolas, opcoes.Escolas, Escola, "Escola");
        Trocar(Lotes, opcoes.Lotes, Lote, "Lote");
        Trocar(Empresas, opcoes.Empresas, Empresa, "Empresa");
        Trocar(ContratosReceita, opcoes.ContratosReceita, ContratoReceita, "Contrato de receita");
        Trocar(Dotacoes, opcoes.Dotacoes, Dotacao, "Dotação");
        Trocar(Situacoes, opcoes.Status, Status, "Status");
        _atualizando = false;

        long? selecionada = Selecionada?.Obra.Id;
        var resultado = _base.Buscar(filtro, _mostrando, 0, Ordem, decrescente: Ordem == OrdemObras.SaldoAPagar);
        _atualizando = true;
        Obras.Clear();
        foreach (var obra in resultado.Obras)
        {
            Obras.Add(new ObraVm(obra));
        }

        TemMais = resultado.Total > resultado.Obras.Count;
        Resumo = !TemBase
            ? ""
            : resultado.Total == 0
                ? "Nenhuma obra com esses filtros."
                : Formato.Plural(resultado.Total, "obra encontrada", "obras encontradas")
                  + (TemMais ? $" (mostrando as primeiras {resultado.Obras.Count})" : "");
        Selecionada = Obras.FirstOrDefault(o => o.Obra.Id == selecionada);
        _atualizando = false;
        if (Selecionada is null) FichaAmpliada = false;
        OnPropertyChanged(nameof(PodeUsarNoRelatorio));
        OnPropertyChanged(nameof(PodeUsarNoCronograma));
        OnPropertyChanged(nameof(TemFiltro));
        OnPropertyChanged(nameof(FiltrosAtivos));
        AtualizarMedicoes();
        AtualizarPendencias();
        AtualizarLote(filtro);
    }

    /// <summary>
    /// Com um lote escolhido: soma dos valores de cada obra do lote (com os outros filtros ativos) e das medicoes
    /// em tramite de cada obra (contrato + escola, entao nao entram escolas de outro lote).
    /// </summary>
    private void AtualizarLote(FiltroObras filtro)
    {
        TotaisDoLote.Clear();
        if (Lote.Count == 0)
        {
            TituloDoLote = "";
            OnPropertyChanged(nameof(TemLote));
            return;
        }

        var obras = _base.Buscar(filtro, int.MaxValue).Obras;
        decimal Soma(Func<ObraLida, decimal?> valor) => obras.Sum(o => valor(o) ?? 0m);
        TituloDoLote = $"{FiltroObras.TituloLotes(Lote)} · {Formato.Plural(obras.Count, "obra", "obras")} · {Formato.Plural(obras.Select(o => o.Empreendimento).Distinct(StringComparer.OrdinalIgnoreCase).Count(), "escola", "escolas")}";
        var tramite = obras.SelectMany(o => MedicoesEmTramite.DaObra(o, _base.Medicoes(o.Contrato))).ToList();
        TotaisDoLote.Add(new ItemCronograma("Valor original", FormatoBase.Reais(Soma(o => o.ValorEmpreendimento))));
        TotaisDoLote.Add(new ItemCronograma("Aditado", FormatoBase.Reais(Soma(o => o.ValorAditado))));
        TotaisDoLote.Add(new ItemCronograma("Contratado c/ aditamento", FormatoBase.Reais(Soma(o => o.ValorContratadoComAditamento))));
        TotaisDoLote.Add(new ItemCronograma("Pago", FormatoBase.Reais(Soma(o => o.ValorPago))));
        TotaisDoLote.Add(new ItemCronograma($"Em trâmite ({tramite.Count})", FormatoBase.Reais(tramite.Sum(m => m.Valor))));
        TotaisDoLote.Add(new ItemCronograma("Saldo a pagar", FormatoBase.Reais(Soma(o => o.SaldoAPagar))));
        OnPropertyChanged(nameof(TemLote));
    }

    /// <summary>
    /// Troca a lista de opcoes mantendo o valor escolhido. Se a base foi atualizada e o valor escolhido nao existe
    /// mais, ele continua visivel na lista (para o usuario ver e limpar) e um aviso aponta o campo.
    /// </summary>
    private void Trocar(ObservableCollection<string> lista, IReadOnlyList<string> novas, IReadOnlyList<string> escolhidos, string campo)
    {
        var faltam = escolhidos.Except(novas, StringComparer.OrdinalIgnoreCase).ToList();
        var desejadas = novas.Concat(faltam);
        if (!lista.SequenceEqual(desejadas))
        {
            lista.Clear();
            foreach (string opcao in desejadas) lista.Add(opcao);
        }

        if (faltam.Count > 0)
        {
            Avisos.Add(new AvisoVm(Gravidade.PrecisaConferir,
                $"O filtro {campo} ({string.Join(", ", faltam)}) tem opções sem obras compatíveis na base atual."));
        }
    }

    private void AtualizarMedicoes()
    {
        MedicoesEmTramiteDaObra.Clear();
        TotalEmTramite = "";
        if (Selecionada is not { } obra)
        {
            ResumoDasMedicoes = "";
            OnPropertyChanged(nameof(TemMedicoesEmTramite));
            return;
        }

        // Em contrato em andamento, toda medicao desta escola que nao esta paga esta em tramite (considerado + reajuste).
        var medicoes = _base.Medicoes(obra.Contrato);
        var tramite = MedicoesEmTramite.DaObra(obra.Obra, medicoes);
        foreach (var m in tramite)
        {
            MedicoesEmTramiteDaObra.Add(new MedicaoTramiteVm(m));
        }

        decimal total = tramite.Sum(m => m.Valor);
        TotalEmTramite = FormatoBase.Reais(total);
        ResumoDasMedicoes = MedicoesEmTramite.Situacao(obra.Obra, medicoes) switch
        {
            SituacaoContrato.Concluido => $"Contrato concluído ({obra.Obra.StatusContrato}): só o pago e o saldo, sem medições.",
            SituacaoContrato.Ignorado => "Contrato fora dos relatórios (sob gestão da SIURB).",
            var situacao => (situacao == SituacaoContrato.Desconhecido && !string.IsNullOrWhiteSpace(obra.Obra.StatusContrato)
                                ? $"Status de contrato novo na planilha: {obra.Obra.StatusContrato} (tratado como andamento). "
                                : "")
                            + (tramite.Count == 0
                                ? "Nenhuma medição em trâmite desta escola."
                                : $"{Formato.Plural(tramite.Count, "medição em trâmite", "medições em trâmite")} desta escola, somando {FormatoBase.Reais(total)}."),
        };
        OnPropertyChanged(nameof(TemMedicoesEmTramite));
    }
}

public sealed partial class FonteVm(FonteInfo info, string? candidato) : ObservableObject
{
    public FonteInfo Info { get; } = info;
    public string Nome => Info.Nome;
    public string Caminho => Info.Caminho.Length > 0 ? Info.Caminho : "Arquivo ainda não escolhido";
    public string? Candidato { get; } = candidato;
    public bool TemCandidato => Candidato is not null;
    public bool TemArquivo => Info.Caminho.Length > 0;

    public string Estado => Info.Estado switch
    {
        EstadoFonte.Atualizada => "Atualizada",
        EstadoFonte.Desatualizada => "Não é a leitura atual",
        EstadoFonte.ArquivoNaoEncontrado => "Arquivo não encontrado",
        _ => "Ainda não lida",
    };

    public bool Ok => Info.Estado == EstadoFonte.Atualizada;
    public string Mensagem => Info.Mensagem;
    public string UltimaLeitura => Info.UltimaLeitura is { } u ? Formato.DataHora(u) : "Nunca";
    public string ModificadoArquivo => Info.ModificadoArquivo is { } m ? Formato.DataHora(m) : "Não informada";
    public string Linhas => Info.Linhas.ToString("N0", Formato.PtBr);
    public IReadOnlyList<string> Avisos => Info.Avisos;
    public bool TemAvisos => Info.Avisos.Count > 0;
}

public sealed partial class ApelidoVm(string texto, string? escolhida, Action<ApelidoVm> aoMudar) : ObservableObject
{
    public string Texto { get; } = texto;

    [ObservableProperty]
    private string? _portaria = escolhida;

    partial void OnPortariaChanged(string? value) => aoMudar(this);
}

/// <summary>Fontes de dados: uma linha por planilha, com estado, datas, Reler agora e os apelidos de portaria.</summary>
public sealed partial class FontesVm : TelaVm
{
    public const string Modulo = "fontes";
    public const string SemVinculo = "(sem vínculo)";

    private readonly ServicoFontes _fontes;
    private readonly BaseRepositorio _base;
    private readonly IDialogos _dialogos;
    private readonly IInterface _interface;
    private readonly ServicoPreferencias _preferencias;
    private bool _carregando;

    private readonly Action<string>? _navegar;

    public FontesVm(
        ServicoFontes fontes, BaseRepositorio @base, IDialogos dialogos, IInterface @interface, ServicoPreferencias preferencias,
        Action<string>? navegar = null)
    {
        _navegar = navegar;
        _fontes = fontes;
        _base = @base;
        _dialogos = dialogos;
        _interface = @interface;
        _preferencias = preferencias;
        fontes.Mudou += () => _interface.Executar(Carregar);
        Carregar();
    }

    public override string Titulo => "Fontes de dados";

    public ObservableCollection<FonteVm> Fontes { get; } = [];
    public ObservableCollection<ApelidoVm> Apelidos { get; } = [];
    public ObservableCollection<string> PortariasDaBase { get; } = [];
    public ObservableCollection<VinculoBase> Vinculos { get; } = [];

    public bool TemApelidos => Apelidos.Count > 0;
    public bool TemVinculos => Vinculos.Count > 0;

    /// <summary>Logo cadastrado (vai no topo das planilhas exportadas).</summary>
    public Func<string?>? Logo { get; set; }

    /// <summary>Para registrar no historico a exportacao e datar as edicoes (definidos pela composicao do app).</summary>
    public ServicoOperacoes? Operacoes { get; set; }
    public Spobras.Core.Abstracoes.IRelogio? Relogio { get; set; }
    private DateTimeOffset Agora => Relogio?.Agora ?? DateTimeOffset.Now;

    private VinculoBase? _vinculoEmEdicao;

    [ObservableProperty]
    private bool _editandoVinculo;

    [ObservableProperty]
    private string _tituloVinculo = "";

    [ObservableProperty]
    private string _vPortaria = "";

    [ObservableProperty]
    private string _vReceita = "";

    [ObservableProperty]
    private string _vFinalidade = "";

    [ObservableProperty]
    private string _vDotacao = "";

    [ObservableProperty]
    private string _vResumida = "";

    [ObservableProperty]
    private string _vDescricao = "";

    [ObservableProperty]
    private string _problemaVinculo = "";

    [RelayCommand]
    private void NovoVinculo() => AbrirVinculo(null);

    [RelayCommand]
    private void EditarVinculo(VinculoBase vinculo) => AbrirVinculo(vinculo);

    private void AbrirVinculo(VinculoBase? v)
    {
        _vinculoEmEdicao = v;
        TituloVinculo = v is null ? "Incluir portaria e dotação" : "Editar portaria e dotação";
        VPortaria = v?.Portaria ?? "";
        VReceita = v?.ContratoReceita ?? "";
        VFinalidade = v?.Finalidade ?? "";
        VDotacao = v?.DotacaoCompleta ?? "";
        VResumida = v?.DotacaoResumida ?? "";
        VDescricao = v?.Descricao ?? "";
        ProblemaVinculo = "";
        EditandoVinculo = true;
    }

    [RelayCommand]
    private void CancelarVinculo() => EditandoVinculo = false;

    [RelayCommand]
    private void SalvarVinculo()
    {
        string? Nulo(string t) => string.IsNullOrWhiteSpace(t) ? null : t.Trim();
        var novo = new VinculoBase(0, VPortaria.Trim(), VReceita.Trim(), Nulo(VFinalidade), VDotacao.Trim(), null, Nulo(VResumida), Nulo(VDescricao));
        try
        {
            if (_vinculoEmEdicao is null)
            {
                _base.IncluirVinculo(novo, Agora);
            }
            else
            {
                _base.AlterarVinculo(_vinculoEmEdicao, novo, Agora);
            }
        }
        catch (InvalidOperationException ex)
        {
            ProblemaVinculo = ex.Message;
            return;
        }

        EditandoVinculo = false;
        Carregar();
        Resultado = "Portaria e dotação salvas no app. A planilha de dotações não foi alterada; a mudança continua valendo quando ela for lida de novo.";
        ResultadoComFalha = false;
    }

    [RelayCommand]
    private void RemoverVinculo(VinculoBase vinculo)
    {
        if (!_dialogos.Confirmar("Tirar da base", $"Tirar {vinculo.Portaria} · {vinculo.DotacaoCompleta} da base do app? A planilha de dotações não é alterada.", "Tirar"))
        {
            return;
        }

        _base.RemoverVinculo(vinculo, Agora);
        Carregar();
        Resultado = "Tirado da base do app.";
        ResultadoComFalha = false;
    }

    /// <summary>Tabela de portarias, contratos de receita e dotacoes, pronta para PROCX/SOMASES no Excel.</summary>
    [RelayCommand]
    private void ExportarVinculos()
    {
        string pasta = _preferencias.DestinoDe(BaseVm.Modulo);
        string nome = Spobras.Core.Nomes.NomeArquivo.Livre($"Portarias e dotações - {Agora:yyyy-MM-dd}.xlsx", n => File.Exists(Path.Combine(pasta, n)));
        string caminho = Path.Combine(pasta, nome);
        try
        {
            Spobras.Documentos.Planilhas.GravadorXlsx.Gravar(caminho,
            [
                new Spobras.Documentos.Planilhas.AbaNova("Portarias e dotações",
                    ["Portaria", "Contrato de receita", "Finalidade", "Dotação completa", "Dotação resumida", "Descrição", "Origem"],
                    [.. _base.Vinculos().Select(v => new object?[] { v.Portaria, v.ContratoReceita, v.Finalidade, v.DotacaoCompleta, v.DotacaoResumida, v.Descricao, v.Origem })]),
            ], Logo?.Invoke(), "Portarias, contratos de receita e dotações");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Resultado = $"A planilha não foi gerada: {ex.Message}";
            ResultadoComFalha = true;
            return;
        }

        Operacoes?.RegistrarCriados($"Exportou portarias e dotações para “{nome}”", [caminho]);
        _dialogos.MostrarNaPasta(caminho);
        Resultado = $"“{nome}” gerado em {pasta}.";
        ResultadoComFalha = false;
    }

    public override void AoEntrar() => Carregar();

    [RelayCommand]
    private void Voltar() => _navegar?.Invoke(BaseVm.Modulo);

    [RelayCommand]
    private async Task EscolherArquivoAsync(FonteVm fonte)
    {
        string? inicial = fonte.TemArquivo ? Path.GetDirectoryName(fonte.Info.Caminho) : _preferencias.Atual.PastaPlanilhasBase;
        string? arquivo = _dialogos.EscolherArquivos($"Escolha o arquivo de: {fonte.Nome}", "Planilhas do Excel|*.xlsx;*.xlsm", inicial).FirstOrDefault();
        if (arquivo is null)
        {
            return;
        }

        Ocupado = true;
        try
        {
            await _fontes.DefinirAsync(fonte.Info.Id, arquivo);
        }
        finally
        {
            Ocupado = false;
        }

        Mostrar(fonte.Info.Id);
    }

    [RelayCommand]
    private async Task RelerAsync(FonteVm fonte)
    {
        Ocupado = true;
        try
        {
            await _fontes.RelerAsync(fonte.Info.Id);
        }
        finally
        {
            Ocupado = false;
        }

        Mostrar(fonte.Info.Id);
    }

    /// <summary>O arquivo sumiu e ha outro com o mesmo nome na pasta das planilhas base: so troca com confirmacao.</summary>
    [RelayCommand]
    private async Task UsarCandidatoAsync(FonteVm fonte)
    {
        if (fonte.Candidato is { } candidato
            && _dialogos.Confirmar("Usar este arquivo?", $"Passar a ler {fonte.Nome} deste arquivo?\n\n{candidato}", "Usar este arquivo"))
        {
            await _fontes.DefinirAsync(fonte.Info.Id, candidato);
            Mostrar(fonte.Info.Id);
        }
    }

    private void Mostrar(string id)
    {
        var fonte = _fontes.Fontes().First(f => f.Id == id);
        ResultadoComFalha = fonte.Estado != EstadoFonte.Atualizada;
        Resultado = fonte.Estado == EstadoFonte.Atualizada
            ? $"{fonte.Nome}: leitura concluída, {fonte.Linhas.ToString("N0", Formato.PtBr)} linhas."
            : $"{fonte.Nome}: {fonte.Mensagem}";
    }

    private void Carregar()
    {
        _carregando = true;
        Fontes.Clear();
        foreach (var fonte in _fontes.Fontes())
        {
            string? candidato = fonte.Estado == EstadoFonte.ArquivoNaoEncontrado ? _fontes.Procurar(fonte.Id) : null;
            Fontes.Add(new FonteVm(fonte, candidato));
        }

        PortariasDaBase.Clear();
        PortariasDaBase.Add(SemVinculo);
        foreach (var portaria in _base.Portarias())
        {
            PortariasDaBase.Add(portaria.Rotulo);
        }

        var apelidos = _base.Apelidos();
        Apelidos.Clear();
        if (PortariasDaBase.Count > 1)
        {
            foreach (string texto in _base.PortariasDasObras())
            {
                Apelidos.Add(new ApelidoVm(texto, apelidos.GetValueOrDefault(texto) ?? SemVinculo, GravarApelido));
            }
        }

        Vinculos.Clear();
        foreach (var vinculo in _base.Vinculos())
        {
            Vinculos.Add(vinculo);
        }

        _carregando = false;
        OnPropertyChanged(nameof(TemApelidos));
        OnPropertyChanged(nameof(TemVinculos));
    }

    /// <summary>A ligacao e sempre escolha do usuario: o app nao deduz que "Nº 03 (2021)" e a "Portaria 3".</summary>
    private void GravarApelido(ApelidoVm apelido)
    {
        if (!_carregando)
        {
            _base.GravarApelido(apelido.Texto, apelido.Portaria is null or SemVinculo ? null : apelido.Portaria);
        }
    }
}

/// <summary>Um campo no formulario de edicao: o valor em uso e, se foi mudado no app, o que a planilha diz.</summary>
public sealed partial class CampoEdicaoObraVm(CampoObra campo, string valor, AjusteObra? ajuste, bool somenteLeitura) : ObservableObject
{
    public CampoObra Campo { get; } = campo;
    public string Rotulo => Campo.Rotulo;
    public string Original { get; } = valor;
    public bool SomenteLeitura { get; } = somenteLeitura;
    public bool Editado => ajuste is not null;
    public bool PlanilhaMudou => ajuste?.PlanilhaMudou == true;

    /// <summary>"Na planilha: X", para o campo mudado no app.</summary>
    public string NaPlanilha => ajuste is null ? "" :
        (ajuste.PlanilhaMudou ? "A planilha mudou depois da sua edição e agora diz: " : "Na planilha: ") + CamposObra.Mostrar(Campo, ajuste.ValorDaPlanilha);

    [ObservableProperty]
    private string _valor = valor;

    public bool Mudou => Valor.Trim() != Original.Trim();
}

/// <summary>
/// Formulario para mudar qualquer dado de uma obra (ou incluir uma obra nova). O valor da planilha vem
/// por padrao; o que o usuario muda fica guardado no app, com o original, e pode voltar ao da planilha.
/// </summary>
public sealed partial class EdicaoObraVm : ObservableObject
{
    public EdicaoObraVm(ObraLida? obra, IReadOnlyList<AjusteObra> ajustes)
    {
        Obra = obra;
        bool nova = obra is null;
        foreach (var campo in CamposObra.Todos)
        {
            string valor = obra is null ? "" : campo.Ler(obra);
            var ajuste = ajustes.FirstOrDefault(a => a.Campo == campo.Chave);
            Campos.Add(new CampoEdicaoObraVm(campo, valor, ajuste, !nova && obra!.IncluidaNoApp == false && CamposObra.Chave(campo.Chave)));
        }
    }

    /// <summary>Null = obra nova, incluida no app.</summary>
    public ObraLida? Obra { get; }
    public bool Nova => Obra is null;
    public string Titulo => Nova ? "Incluir obra" : Obra!.IncluidaNoApp ? $"Editar {Obra.Empreendimento} (incluída no app)" : $"Editar {Obra.Empreendimento}";
    public string Explicacao => Nova
        ? "Para uma obra que ainda não está na planilha mestra. Contrato e escola são obrigatórios; ela aparece marcada como incluída no app."
        : Obra!.IncluidaNoApp
            ? "Obra incluída no app: todos os campos podem ser mudados."
            : "Os valores vêm da planilha mestra. O que você mudar vale no app e fica marcado; a planilha não é alterada e dá para voltar ao valor dela.";
    public ObservableCollection<CampoEdicaoObraVm> Campos { get; } = [];

    [ObservableProperty]
    private string _problemas = "";

    /// <summary>Campos mudados e convertidos; null (com Problemas preenchido) se algo nao foi entendido.</summary>
    public Dictionary<string, object?>? Mudancas()
    {
        var mudancas = new Dictionary<string, object?>();
        var erros = new List<string>();
        foreach (var c in Campos.Where(c => !c.SomenteLeitura && (Nova ? c.Valor.Trim().Length > 0 || CamposObra.Chave(c.Campo.Chave) : c.Mudou)))
        {
            var (valor, erro) = CamposObra.Converter(c.Campo, c.Valor);
            if (erro is not null)
            {
                erros.Add(erro);
            }
            else
            {
                mudancas[c.Campo.Chave] = valor;
            }
        }

        Problemas = string.Join("\n", erros);
        return erros.Count > 0 ? null : mudancas;
    }
}
