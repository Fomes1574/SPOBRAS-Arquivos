using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Spobras.App.Servicos;
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
}

public sealed record CampoValorVm(CampoValor Campo)
{
    public string Rotulo => CamposValor.Rotulo(Campo);
}

/// <summary>
/// Base de dados (especificacao 4.6): filtros vinculados que se atualizam a cada escolha, sem botao Buscar.
/// Consulta sempre a base local ja importada; a planilha nao e relida a cada clique.
/// </summary>
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
    private bool _atualizando;
    private int _mostrando = Pagina;

    public BaseVm(
        BaseRepositorio @base, ServicoFontes fontes, IInterface @interface, Action<string> navegar, Action<ObraLida>? usarNoRelatorio = null,
        Action<ObraLida>? usarNoCronograma = null)
    {
        _usarNoCronograma = usarNoCronograma;
        _base = @base;
        _fontes = fontes;
        _interface = @interface;
        _navegar = navegar;
        _usarNoRelatorio = usarNoRelatorio;
        CamposDeValor = [.. Enum.GetValues<CampoValor>().Select(c => new CampoValorVm(c))];
        _valorFiltrado = CamposDeValor.First(c => c.Campo == CampoValor.SaldoAPagar);
        fontes.Mudou += () => _interface.Executar(Buscar); // a fonte foi relida: reaplica os filtros ativos
        Buscar();
    }

    public override string Titulo => "Base de dados";

    public ObservableCollection<ObraVm> Obras { get; } = [];
    public ObservableCollection<AvisoVm> Avisos { get; } = [];
    public ObservableCollection<string> Portarias { get; } = [];
    public ObservableCollection<string> Contratos { get; } = [];
    public ObservableCollection<string> Escolas { get; } = [];
    public ObservableCollection<string> ContratosReceita { get; } = [];
    public ObservableCollection<string> Dotacoes { get; } = [];
    public ObservableCollection<string> Situacoes { get; } = [];
    public IReadOnlyList<CampoValorVm> CamposDeValor { get; }

    [ObservableProperty]
    private string? _portaria;

    [ObservableProperty]
    private string? _contrato;

    [ObservableProperty]
    private string? _escola;

    [ObservableProperty]
    private string? _contratoReceita;

    [ObservableProperty]
    private string? _dotacao;

    [ObservableProperty]
    private string? _status;

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
            case nameof(Portaria) or nameof(Contrato) or nameof(Escola) or nameof(ContratoReceita) or nameof(Dotacao) or nameof(Status)
                or nameof(Texto) or nameof(ValorFiltrado) or nameof(ValorDe) or nameof(ValorAte) or nameof(ValorSemInformacao)
                or nameof(OsDe) or nameof(OsAte) or nameof(SemDataOs) or nameof(Ordem):
                _mostrando = Pagina;
                Buscar();
                break;
            case nameof(Selecionada):
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
            case "portaria": Portaria = null; break;
            case "contrato": Contrato = null; break;
            case "escola": Escola = null; break;
            case "receita": ContratoReceita = null; break;
            case "dotacao": Dotacao = null; break;
            case "status": Status = null; break;
        }
    }

    [RelayCommand]
    private void LimparFiltros()
    {
        _atualizando = true;
        Portaria = Contrato = Escola = ContratoReceita = Dotacao = Status = null;
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
        Portaria = Trocar(Portarias, opcoes.Portarias, Portaria, "Portaria");
        Contrato = Trocar(Contratos, opcoes.Contratos, Contrato, "Contrato");
        Escola = Trocar(Escolas, opcoes.Escolas, Escola, "Escola");
        ContratoReceita = Trocar(ContratosReceita, opcoes.ContratosReceita, ContratoReceita, "Contrato de receita");
        Dotacao = Trocar(Dotacoes, opcoes.Dotacoes, Dotacao, "Dotação");
        Status = Trocar(Situacoes, opcoes.Status, Status, "Status");
        _atualizando = false;

        long? selecionada = Selecionada?.Obra.Id;
        var resultado = _base.Buscar(filtro, _mostrando, 0, Ordem, decrescente: Ordem == OrdemObras.SaldoAPagar);
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
        _atualizando = true;
        Selecionada = Obras.FirstOrDefault(o => o.Obra.Id == selecionada);
        _atualizando = false;
        OnPropertyChanged(nameof(PodeUsarNoRelatorio));
        OnPropertyChanged(nameof(PodeUsarNoCronograma));
        OnPropertyChanged(nameof(TemFiltro));
        AtualizarMedicoes();
    }

    /// <summary>
    /// Troca a lista de opcoes mantendo o valor escolhido. Se a base foi atualizada e o valor escolhido nao existe
    /// mais, ele continua visivel na lista (para o usuario ver e limpar) e um aviso aponta o campo.
    /// </summary>
    private string? Trocar(ObservableCollection<string> lista, IReadOnlyList<string> novas, string? escolhido, string campo)
    {
        bool falta = escolhido is not null && !novas.Contains(escolhido, StringComparer.OrdinalIgnoreCase);
        var desejada = falta ? [escolhido!, .. novas] : novas;
        if (!lista.SequenceEqual(desejada))
        {
            lista.Clear();
            foreach (string opcao in desejada)
            {
                lista.Add(opcao);
            }
        }

        if (falta)
        {
            Avisos.Add(new AvisoVm(Gravidade.PrecisaConferir, $"O filtro {campo} (\"{escolhido}\") não combina com nenhuma obra da base atual. Limpe ou troque esse filtro."));
        }

        return escolhido;
    }

    private void AtualizarMedicoes()
    {
        if (Selecionada is not { } obra)
        {
            ResumoDasMedicoes = "";
            return;
        }

        // Medicoes sao do contrato inteiro: o total nao e apresentado como se fosse da escola.
        var medicoes = _base.Medicoes(obra.Contrato);
        ResumoDasMedicoes = medicoes.Count == 0
            ? "Nenhuma medição deste contrato na planilha."
            : $"{Formato.Plural(medicoes.Count, "medição", "medições")} no contrato {obra.Contrato} (todas as escolas do contrato), "
              + $"somando {FormatoBase.Reais(medicoes.Sum(m => m.ValorPago ?? 0m))} pagos.";
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
