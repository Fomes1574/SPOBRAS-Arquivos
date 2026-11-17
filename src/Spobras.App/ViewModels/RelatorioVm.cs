using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Spobras.App.Servicos;
using Spobras.Core;
using Spobras.Core.Abstracoes;
using Spobras.Core.Nomes;
using Spobras.Core.Relatorio;
using Spobras.Core.Tarefas;
using Spobras.Dados.Configuracao;
using Spobras.Documentos.Imagens;
using Spobras.Documentos.Relatorio;

namespace Spobras.App.ViewModels;

public enum QualidadeFotos
{
    /// <summary>Para imprimir grande: arquivo maior.</summary>
    Alta,
    Media,

    /// <summary>Para enviar por e-mail: arquivo menor.</summary>
    Leve,
}

/// <summary>O que o relatorio gera: o PDF e sempre montado pelo proprio app (A4 exato), sem depender do Excel.</summary>
public enum FormatoRelatorio
{
    ExcelEPdf,
    SoPdf,
    SoExcel,
}

public sealed record FotoRascunho(string Caminho, string Legenda, string Descricao, int Rotacao);

/// <summary>Uma opcao de "fotos por pagina": com descricao, 6 e 8 ficam apagadas.</summary>
public sealed record OpcaoCapacidade(int Numero, bool Habilitada, bool Marcada)
{
    public string Texto => Numero.ToString(CultureInfo.InvariantCulture);
    public string Dica => Habilitada ? $"{Numero} por página" : "Com legenda e descrição do serviço, cabem no máximo 4 fotos por página.";
}

public sealed record RascunhoRelatorio
{
    public DadosRelatorio Dados { get; init; } = new();
    public string DataTexto { get; init; } = "";
    public string DataOsTexto { get; init; } = "";
    public int FotosPorPagina { get; init; } = 6;
    public ModoLegenda Modo { get; init; }
    public bool Assinaturas { get; init; }
    public string AssinaturaFiscal { get; init; } = "";
    public string AssinaturaContratada { get; init; } = "";
    public QualidadeFotos Qualidade { get; init; } = QualidadeFotos.Media;

    /// <summary>Rascunhos antigos (o PDF era feito pelo Excel). Hoje vale o formato das preferencias.</summary>
    public bool GerarPdf { get; init; }
    public List<FotoRascunho> Fotos { get; init; } = [];
}

public sealed partial class FotoVm(string caminho, Action aoMudar) : ObservableObject
{
    private bool _carregando;

    [ObservableProperty]
    private string _caminho = caminho;

    [ObservableProperty]
    private string _legenda = "";

    [ObservableProperty]
    private string _descricao = "";

    [ObservableProperty]
    private int _rotacao;

    [ObservableProperty]
    private string? _miniatura;

    [ObservableProperty]
    private string _rotulo = "";

    [ObservableProperty]
    private bool _faltando;

    [ObservableProperty]
    private bool _selecionada;

    public int Largura { get; set; }
    public int Altura { get; set; }
    public DateTime? CapturadaEm { get; set; }

    public string Nome => Path.GetFileName(Caminho);

    public string Detalhe => Faltando
        ? "Foto não encontrada neste lugar. Use Localizar."
        : CapturadaEm is { } data
            ? $"Tirada em {data.ToString("dd/MM/yyyy 'às' HH:mm", Formato.PtBr)}"
            : "Sem data de captura no arquivo";

    partial void OnLegendaChanged(string value)
    {
        OnPropertyChanged(nameof(ContadorLegenda));
        OnPropertyChanged(nameof(LegendaPertoDoLimite));
        Avisar();
    }

    partial void OnDescricaoChanged(string value)
    {
        OnPropertyChanged(nameof(ContadorDescricao));
        OnPropertyChanged(nameof(DescricaoPertoDoLimite));
        Avisar();
    }

    partial void OnSelecionadaChanged(bool value) => AoMarcar?.Invoke();

    /// <summary>Avisa a tela quando a foto e marcada ou desmarcada (contagem da barra "Mesmo texto").</summary>
    public Action? AoMarcar { get; set; }

    public static int MaximoDaLegenda => OpcoesRelatorio.MaximoDaLegenda;
    public static int MaximoDaDescricao => OpcoesRelatorio.MaximoDaDescricao;
    public string ContadorLegenda => $"{Legenda.Length} de {MaximoDaLegenda}";
    public string ContadorDescricao => $"{Descricao.Length} de {MaximoDaDescricao}";
    public bool LegendaPertoDoLimite => Legenda.Length >= MaximoDaLegenda * 9 / 10;
    public bool DescricaoPertoDoLimite => Descricao.Length >= MaximoDaDescricao * 9 / 10;

    partial void OnCaminhoChanged(string value) => OnPropertyChanged(nameof(Nome));

    partial void OnFaltandoChanged(bool value) => OnPropertyChanged(nameof(Detalhe));

    public void Carregar(Action acao)
    {
        _carregando = true;
        acao();
        _carregando = false;
        OnPropertyChanged(nameof(Detalhe));
    }

    private void Avisar()
    {
        if (!_carregando)
        {
            aoMudar();
        }
    }
}

/// <summary>Um elemento desenhado na previa da pagina (mesmas medidas do arquivo gerado).</summary>
/// <summary>Uma peca da previa da pagina (vem de <see cref="DesenhoRelatorio"/>, o mesmo desenho do PDF). Medidas em pontos.</summary>
public sealed record ElementoPrevia(double X, double Y, double Largura, double Altura)
{
    public string Texto { get; init; } = "";
    public string? Imagem { get; init; }
    public bool Negrito { get; init; }
    public double Fonte { get; init; } = 10;
    public double AlturaDaLinha { get; init; } = 12;
    public string Cor { get; init; } = "#1D1D1F";
    public string? Fundo { get; init; }
    public string? Borda { get; init; }
    public double Espessura { get; init; }
    public bool Centro { get; init; }
    public bool Meio { get; init; }
    public bool Faltando { get; init; }
    public bool TemImagem { get; init; }
}

public sealed record PaginaPreviaVm(int Numero, int Total, IReadOnlyList<ElementoPrevia> Elementos)
{
    public string Titulo => $"Página {Numero} de {Total}";
    public double Largura => GradeRelatorio.LarguraDaFolha;
    public double Altura => GradeRelatorio.AlturaDaFolha;
}

public sealed partial class RelatorioVm : TelaVm
{
    public const string Modulo = "relatorio";
    private const string ChaveRascunho = "relatorio-atual";

    private readonly IDialogos _dialogos;
    private readonly IInterface _interface;
    private readonly ServicoOperacoes _operacoes;
    private readonly ServicoPreferencias _preferencias;
    private readonly ServicoModelos _modelos;
    private readonly RascunhoRepositorio _rascunhos;
    private readonly IRelogio _relogio;
    private PlanoRelatorio? _plano;
    private bool _restaurando;
    private bool _nomeEditado;
    private string? _ultimoResultado;

    public RelatorioVm(
        IDialogos dialogos, IInterface @interface, ServicoOperacoes operacoes, ServicoPreferencias preferencias,
        ServicoModelos modelos, RascunhoRepositorio rascunhos, IRelogio relogio)
    {
        _dialogos = dialogos;
        _interface = @interface;
        _operacoes = operacoes;
        _preferencias = preferencias;
        _modelos = modelos;
        _rascunhos = rascunhos;
        _relogio = relogio;
        _pastaDestino = preferencias.DestinoDe(Modulo);
        _formatoDeSaida = preferencias.Atual.FormatoDoRelatorio;
        Restaurar();
    }

    public override string Titulo => "Relatório fotográfico";

    /// <summary>Obras da Base com o contrato digitado (vazio se a base nao tiver). Null = sem base ligada.</summary>
    public Func<string, IReadOnlyList<Spobras.Core.Base.ObraLida>>? ObrasDoContrato { get; set; }

    /// <summary>Obras da escola digitada (uma escola pode estar em varios contratos).</summary>
    public Func<string, IReadOnlyList<Spobras.Core.Base.ObraLida>>? ObrasDaEscola { get; set; }

    public Func<string, IReadOnlyList<Spobras.Dados.Base.ProcuraObras.Sugestao>>? BuscarSugestoesEscola { get; set; }
    public Func<string, IReadOnlyList<Spobras.Dados.Base.ProcuraObras.Sugestao>>? BuscarSugestoesContrato { get; set; }

    public void EscolherSugestao(string valor, bool escola)
    {
        var obras = escola ? ObrasDaEscola?.Invoke(valor).Where(o => MesmaEscola(o.Empreendimento, valor)).ToList()
            : ObrasDoContrato?.Invoke(valor).ToList();
        if (obras is null || obras.Count == 0) return;
        LimparEscolhas();
        if (obras.Count == 1)
        {
            PreencherDaObra(obras[0]);
            return;
        }

        _restaurando = true;
        if (escola)
        {
            Unidade = valor;
            Contrato = "";
            foreach (var obra in obras) ContratosDaEscola.Add(new OpcaoDeContrato(obra));
            OnPropertyChanged(nameof(TemContratosParaEscolher));
        }
        else
        {
            Contrato = valor;
            Unidade = "";
            foreach (var obra in obras) EscolasDoContrato.Add(obra);
            OnPropertyChanged(nameof(TemEscolasParaEscolher));
        }
        _restaurando = false;
        Replanejar();
    }

    /// <summary>Escolas do contrato digitado, quando ele tem mais de uma: o usuario escolhe qual.</summary>
    public ObservableCollection<Spobras.Core.Base.ObraLida> EscolasDoContrato { get; } = [];

    /// <summary>Contratos da escola digitada, quando ela esta em mais de um: o usuario escolhe qual.</summary>
    public ObservableCollection<OpcaoDeContrato> ContratosDaEscola { get; } = [];

    public bool TemEscolasParaEscolher => EscolasDoContrato.Count > 0;
    public bool TemContratosParaEscolher => ContratosDaEscola.Count > 0;

    [RelayCommand]
    private void EscolherObra(Spobras.Core.Base.ObraLida obra)
    {
        LimparEscolhas();
        PreencherDaObra(obra);
    }

    public void ApagarCampo(Action limpar)
    {
        _restaurando = true;
        try
        {
            limpar();
        }
        finally
        {
            _restaurando = false;
        }
        LimparEscolhas();
        Replanejar();
    }

    private void LimparEscolhas()
    {
        EscolasDoContrato.Clear();
        ContratosDaEscola.Clear();
        OnPropertyChanged(nameof(TemEscolasParaEscolher));
        OnPropertyChanged(nameof(TemContratosParaEscolher));
    }

    private static bool MesmaEscola(string a, string b) =>
        Spobras.Core.Base.Normalizacao.Busca(a.Trim()) == Spobras.Core.Base.Normalizacao.Busca(b.Trim());

    private static bool MesmoContrato(string a, string b) =>
        string.Equals(a.Trim().Replace('-', '/'), b.Trim().Replace('-', '/'), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Contrato digitado: se a base conhece, completa o resto. Com varias escolas no contrato, usa a escola ja
    /// digitada se ela for uma delas; senao lista as escolas para escolher.
    /// </summary>
    private void CompletarPeloContrato()
    {
        LimparEscolhas();
        if (ObrasDoContrato is null || Contrato.Trim().Length < 5)
        {
            return;
        }

        var obras = ObrasDoContrato(Contrato.Trim());
        if (obras.Count == 0)
        {
            return;
        }

        var daEscola = Unidade.Trim().Length > 0 ? obras.Where(o => MesmaEscola(o.Empreendimento, Unidade)).ToList() : [];
        if (obras.Count == 1 || daEscola.Count == 1)
        {
            var obra = daEscola.Count == 1 ? daEscola[0] : obras[0];
            if (obras.Count == 1 && Unidade.Trim().Length > 0 && !MesmaEscola(obra.Empreendimento, Unidade) && ObrasDaEscola?.Invoke(Unidade).Count > 0)
            {
                // A escola digitada existe na base, mas em outro contrato: nao troca nada sem o usuario ver.
                Avisar($"A escola “{Unidade.Trim()}” não aparece no contrato {obra.Contrato} na Base de dados (lá ele é de {obra.Empreendimento}). Confira o contrato ou a escola.");
                return;
            }

            PreencherDaObra(obra);
            return;
        }

        foreach (var obra in obras)
        {
            EscolasDoContrato.Add(obra);
        }

        OnPropertyChanged(nameof(TemEscolasParaEscolher));
        Orientar($"O contrato {obras[0].Contrato} tem {obras.Count} escolas na Base de dados. Escolha qual é a deste relatório.");
    }

    /// <summary>
    /// Escola digitada: com um contrato so, completa o resto; em varios contratos, lista os contratos para escolher.
    /// Se o contrato ja digitado for um deles, usa esse.
    /// </summary>
    private void CompletarPelaEscola()
    {
        if (ObrasDaEscola is null || Unidade.Trim().Length < 4)
        {
            ContratosDaEscola.Clear();
            OnPropertyChanged(nameof(TemContratosParaEscolher));
            return;
        }

        var obras = ObrasDaEscola(Unidade.Trim());
        LimparEscolhas();
        if (obras.Count == 0)
        {
            return;
        }

        var doContrato = Contrato.Trim().Length > 0 ? obras.Where(o => MesmoContrato(o.Contrato, Contrato)).ToList() : [];
        if (doContrato.Count == 1)
        {
            if (!MesmaEscola(doContrato[0].Empreendimento, Unidade) || Lote.Length == 0)
            {
                PreencherDaObra(doContrato[0]);
            }

            return;
        }

        if (Contrato.Trim().Length > 0 && ObrasDoContrato?.Invoke(Contrato.Trim()).Count > 0)
        {
            Avisar($"O contrato {Contrato.Trim()} não tem a escola “{Unidade.Trim()}” na Base de dados. Confira o contrato ou a escola.");
            return;
        }

        if (obras.Count == 1)
        {
            PreencherDaObra(obras[0]);
            return;
        }

        foreach (var obra in obras)
        {
            ContratosDaEscola.Add(new OpcaoDeContrato(obra));
        }

        OnPropertyChanged(nameof(TemContratosParaEscolher));
        Orientar(obras.Select(o => o.Empreendimento).Distinct().Count() == 1
            ? $"{obras[0].Empreendimento} está em {obras.Count} contratos na Base de dados. Escolha qual é o deste relatório."
            : $"{obras.Count} obras combinam com “{Unidade.Trim()}” na Base de dados. Escolha a deste relatório.");
    }

    private void Avisar(string texto)
    {
        Resultado = texto;
        ResultadoComFalha = true;
    }

    public ObservableCollection<FotoVm> Fotos { get; } = [];
    public ObservableCollection<AvisoVm> Problemas { get; } = [];
    public ObservableCollection<AvisoVm> Avisos { get; } = [];
    public ObservableCollection<PaginaPreviaVm> Paginas { get; } = [];
    public IReadOnlyList<int> Capacidades => OpcoesRelatorio.Capacidades;

    [ObservableProperty]
    private int _etapa;

    [ObservableProperty]
    private string _unidade = "";

    [ObservableProperty]
    private string _contratada = "";

    [ObservableProperty]
    private string _contrato = "";

    [ObservableProperty]
    private string _lote = "";

    [ObservableProperty]
    private string _portaria = "";

    [ObservableProperty]
    private string _processoSei = "";

    [ObservableProperty]
    private string _objeto = "";

    [ObservableProperty]
    private string _responsavel = "";

    /// <summary>Descricao geral, opcional, no fim do cabecalho.</summary>
    [ObservableProperty]
    private string _descricao = "";

    [ObservableProperty]
    private string _dataTexto = "";

    [ObservableProperty]
    private string _dataOsTexto = "";

    [ObservableProperty]
    private int _fotosPorPagina = 6;

    [ObservableProperty]
    private ModoLegenda _modo = ModoLegenda.LegendaCurta;

    [ObservableProperty]
    private bool _assinaturas;

    [ObservableProperty]
    private string _assinaturaFiscal = "";

    [ObservableProperty]
    private string _assinaturaContratada = "";

    [ObservableProperty]
    private QualidadeFotos _qualidade = QualidadeFotos.Media;

    /// <summary>Excel e PDF, so PDF ou so Excel; lembrado para os proximos relatorios.</summary>
    [ObservableProperty]
    private FormatoRelatorio _formatoDeSaida;

    /// <summary>Descricao do servico para aplicar em varias fotos de uma vez.</summary>
    [ObservableProperty]
    private string _descricaoEmLote = "";

    /// <summary>Texto da barra de Desfazer ("3 fotos tiradas do relatório"); vazio quando nao ha o que desfazer.</summary>
    [ObservableProperty]
    private string _textoDesfazer = "";

    [ObservableProperty]
    private string _pastaDestino;

    [ObservableProperty]
    private string _nomeArquivo = "Relatório Fotográfico";

    [ObservableProperty]
    private string _legendaEmLote = "";

    [ObservableProperty]
    private string _resumo = "";

    [ObservableProperty]
    private string _textoFotos = "Nenhuma foto escolhida";

    [ObservableProperty]
    private bool _podeAbrirResultado;

    public bool EtapaDados => Etapa == 0;
    public bool EtapaFotos => Etapa == 1;
    public bool EtapaConferencia => Etapa == 2;
    public bool TemFotos => Fotos.Count > 0;
    public bool ModoDescricao => Modo == ModoLegenda.DescricaoServico;
    public bool ModoSoLegenda => Modo == ModoLegenda.LegendaCurta;

    /// <summary>Fotos por pagina como botoes: com descricao, 6 e 8 apagadas; marcada a que vale de fato.</summary>
    public IReadOnlyList<OpcaoCapacidade> OpcoesDeCapacidade
    {
        get
        {
            int efetiva = Opcoes().Capacidade;
            var validas = OpcoesRelatorio.CapacidadesDoModo(Modo);
            return [.. OpcoesRelatorio.Capacidades.Select(n => new OpcaoCapacidade(n, validas.Contains(n), n == efetiva))];
        }
    }

    public string TextoDoModoNaConferencia => ModoDescricao
        ? "Texto de cada foto: legenda e descrição do serviço, por isso no máximo 4 fotos por página."
        : "Texto de cada foto: só a legenda, até 6 fotos por página (ou 8, menores).";

    /// <summary>No modo so legenda, quantas fotos tem descricao guardada (que nao sai no relatorio).</summary>
    public int DescricoesGuardadas => ModoSoLegenda ? Fotos.Count(f => f.Descricao.Trim().Length > 0) : 0;

    public string AvisoDescricoesGuardadas => DescricoesGuardadas == 0 ? "" :
        $"{Formato.Plural(DescricoesGuardadas, "foto tem", "fotos têm")} descrição guardada. Com “Só legenda” ela não sai no relatório, mas continua aqui: volte para “Legenda e descrição” e ela reaparece.";

    public int QuantasMarcadas => Fotos.Count(f => f.Selecionada);
    public bool TemMarcadas => QuantasMarcadas > 0;
    public string TextoMarcadas => TemMarcadas ? Formato.Plural(QuantasMarcadas, "foto marcada", "fotos marcadas") : "Nenhuma foto marcada";
    public string TextoTirarMarcadas => $"Tirar {(QuantasMarcadas == 1 ? "a marcada" : $"as {QuantasMarcadas} marcadas")} do relatório";

    /// <summary>Quantas fotos o "Mesmo texto" vai mudar: as marcadas ou, sem marcadas, as com o campo em branco.</summary>
    private List<FotoVm> AlvoDoLote()
    {
        bool legenda = LegendaEmLote.Trim().Length > 0, descricao = ModoDescricao && DescricaoEmLote.Trim().Length > 0;
        if (TemMarcadas)
        {
            return [.. Fotos.Where(f => f.Selecionada)];
        }

        return [.. Fotos.Where(f => (legenda && f.Legenda.Trim().Length == 0) || (descricao && f.Descricao.Trim().Length == 0)
                                    || (!legenda && !descricao && (f.Legenda.Trim().Length == 0 || (ModoDescricao && f.Descricao.Trim().Length == 0))))];
    }

    public string TextoBotaoLote
    {
        get
        {
            if (TemMarcadas)
            {
                return QuantasMarcadas == 1 ? "Aplicar à marcada" : $"Aplicar às {QuantasMarcadas} marcadas";
            }

            int n = AlvoDoLote().Count;
            string vazio = ModoDescricao ? "sem texto" : "sem legenda";
            return n == 0 ? $"Nenhuma foto {vazio}" : n == 1 ? $"Aplicar à foto {vazio}" : $"Aplicar às {n} fotos {vazio}";
        }
    }

    public string NotaDoLote => TemMarcadas
        ? "Campo deixado vazio aqui não apaga nada nas fotos."
        : "Sem nenhuma marcada, o texto vai só para as fotos com o campo em branco: nada já escrito é trocado.";

    public bool TemDesfazer => TextoDesfazer.Length > 0;
    public bool TemLogo => _modelos.Logo is not null;
    public string TextoBotaoAvancar => Etapa == 0 ? "Continuar para as fotos" : "Continuar para a conferência";

    public override void AoEntrar()
    {
        OnPropertyChanged(nameof(TemLogo));
        ConferirFotosFaltando();
        Replanejar();
    }

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (_restaurando)
        {
            return;
        }

        switch (e.PropertyName)
        {
            case nameof(Etapa):
                if (EtapaConferencia && _previaVelha)
                {
                    MontarPaginas();
                }

                OnPropertyChanged(nameof(EtapaDados));
                OnPropertyChanged(nameof(EtapaFotos));
                OnPropertyChanged(nameof(EtapaConferencia));
                OnPropertyChanged(nameof(TextoBotaoAvancar));
                break;
            case nameof(NomeArquivo):
                _nomeEditado = NomeArquivo != CabecalhoRelatorio.NomeSugerido(Dados());
                GerarCommand.NotifyCanExecuteChanged();
                break;
            case nameof(Modo):
                OnPropertyChanged(nameof(ModoDescricao));
                OnPropertyChanged(nameof(ModoSoLegenda));
                OnPropertyChanged(nameof(TextoDoModoNaConferencia));
                AvisarLote();
                Replanejar();
                break;
            case nameof(Contrato):
                CompletarPeloContrato();
                Replanejar();
                break;
            case nameof(Unidade):
                CompletarPelaEscola();
                Replanejar();
                break;
            case nameof(Contratada) or nameof(Lote) or nameof(Portaria) or nameof(ProcessoSei)
                or nameof(Objeto) or nameof(Responsavel) or nameof(Descricao) or nameof(DataTexto) or nameof(DataOsTexto) or nameof(FotosPorPagina)
                or nameof(Assinaturas) or nameof(AssinaturaFiscal) or nameof(AssinaturaContratada) or nameof(Qualidade):
                Replanejar();
                break;
            case nameof(FormatoDeSaida):
                _preferencias.Alterar(p => p with { FormatoDoRelatorio = FormatoDeSaida });
                break;
            case nameof(LegendaEmLote) or nameof(DescricaoEmLote):
                AvisarLote();
                break;
            case nameof(TextoDesfazer):
                OnPropertyChanged(nameof(TemDesfazer));
                break;
        }
    }

    // ---- Navegacao entre etapas (livre, sem perder conteudo) ----

    [RelayCommand]
    private void IrPara(string etapa) => Etapa = int.Parse(etapa, CultureInfo.InvariantCulture);

    [RelayCommand]
    private void Avancar() => Etapa = Math.Min(2, Etapa + 1);

    [RelayCommand]
    private void Voltar() => Etapa = Math.Max(0, Etapa - 1);

    // ---- Fotos ----

    [RelayCommand]
    private void AdicionarFotos() =>
        Adicionar(_dialogos.EscolherArquivos("Escolha as fotos", "Fotos|*.jpg;*.jpeg;*.png;*.bmp;*.webp;*.gif|Todos os arquivos|*.*", _preferencias.Atual.PastaTrabalho));

    [RelayCommand]
    private void AdicionarPasta()
    {
        if (_dialogos.EscolherPasta("Escolha a pasta com as fotos", _preferencias.Atual.PastaTrabalho) is { } pasta)
        {
            Adicionar([pasta]);
        }
    }

    /// <summary>Entrada por botao ou arraste. O que nao puder entrar aparece em Problemas, com o motivo.</summary>
    public void Adicionar(IEnumerable<string> caminhos)
    {
        Problemas.Clear();
        var existentes = new HashSet<string>(Fotos.Select(f => f.Caminho), StringComparer.OrdinalIgnoreCase);
        var novos = new List<string>();
        foreach (string caminho in caminhos)
        {
            if (Directory.Exists(caminho))
            {
                // De uma pasta entram so as fotos; os outros arquivos dela nao sao erro.
                novos.AddRange(Directory.EnumerateFiles(caminho).Where(Documentos.Imagens.Fotos.Suportada).Order(OrdemNatural.Instancia));
            }
            else
            {
                novos.Add(caminho);
            }
        }

        int repetidas = 0;
        foreach (string caminho in novos)
        {
            if (!existentes.Add(caminho))
            {
                repetidas++;
                continue;
            }

            try
            {
                var info = Documentos.Imagens.Fotos.Ler(caminho);
                var foto = new FotoVm(caminho, Replanejar) { Largura = info.Largura, Altura = info.Altura, CapturadaEm = info.CapturadaEm, AoMarcar = AvisarLote };
                Fotos.Add(foto);
            }
            catch (FotoInvalidaException ex)
            {
                existentes.Remove(caminho);
                Problemas.Add(new AvisoVm(Gravidade.PrecisaConferir, $"{Path.GetFileName(caminho)}: {ex.Message}"));
            }
        }

        if (repetidas > 0)
        {
            Problemas.Add(new AvisoVm(Gravidade.Informacao, $"{Formato.Plural(repetidas, "foto já estava", "fotos já estavam")} na lista e não foi repetida."));
        }

        Resultado = "";
        Replanejar();
        GerarMiniaturas();
    }

    /// <summary>Tempo que o Desfazer fica na tela depois de tirar fotos (5 s; os testes encurtam).</summary>
    public TimeSpan TempoDoDesfazer { get; set; } = TimeSpan.FromSeconds(5);

    private List<(FotoVm Foto, int Posicao)> _removidas = [];
    private int _rodadaDoDesfazer;

    /// <summary>O ✕ de uma foto: tira na hora, sem confirmar, com Desfazer. A foto nao e apagada do computador.</summary>
    [RelayCommand]
    private void Remover(FotoVm foto) => Tirar([foto]);

    /// <summary>Tira as fotos marcadas depois de confirmar; o Desfazer devolve todas, cada uma no seu lugar.</summary>
    [RelayCommand]
    private void TirarMarcadas()
    {
        var marcadas = Fotos.Where(f => f.Selecionada).ToList();
        if (marcadas.Count == 0)
        {
            return;
        }

        string quais = marcadas.Count == 1 ? "a foto marcada" : $"as {marcadas.Count} fotos marcadas";
        if (!_dialogos.Confirmar("Tirar fotos do relatório", $"Tirar {quais} do relatório?\nAs fotos não são apagadas do computador. Dá para desfazer logo depois.",
                marcadas.Count == 1 ? "Tirar a foto" : $"Tirar as {marcadas.Count}"))
        {
            return;
        }

        Tirar(marcadas);
    }

    private void Tirar(IReadOnlyCollection<FotoVm> fotos)
    {
        // Cada remocao nova substitui o Desfazer anterior (o anterior ja nao pode mais voltar).
        _removidas = [.. fotos.Select(f => (f, Fotos.IndexOf(f))).Where(x => x.Item2 >= 0).OrderBy(x => x.Item2)];
        foreach (var (foto, _) in _removidas)
        {
            foto.Selecionada = false;
            Fotos.Remove(foto);
        }

        TextoDesfazer = _removidas.Count == 1 ? "Foto tirada do relatório" : $"{_removidas.Count} fotos tiradas do relatório";
        int rodada = ++_rodadaDoDesfazer;
        Replanejar();
        AvisarLote();
        _ = EsquecerDepois(rodada);
    }

    private async Task EsquecerDepois(int rodada)
    {
        await Task.Delay(TempoDoDesfazer);
        if (rodada == _rodadaDoDesfazer)
        {
            _removidas = [];
            TextoDesfazer = "";
        }
    }

    private void EsquecerDesfazer()
    {
        _removidas = [];
        _rodadaDoDesfazer++;
        TextoDesfazer = "";
    }

    [RelayCommand]
    private void Desfazer()
    {
        foreach (var (foto, posicao) in _removidas)
        {
            Fotos.Insert(Math.Min(posicao, Fotos.Count), foto);
        }

        _removidas = [];
        _rodadaDoDesfazer++;
        TextoDesfazer = "";
        Replanejar();
        AvisarLote();
    }

    [RelayCommand]
    private void Subir(FotoVm foto) => Mover(foto, -1);

    [RelayCommand]
    private void Descer(FotoVm foto) => Mover(foto, +1);

    [RelayCommand]
    private void GirarDireita(FotoVm foto) => Girar(foto, 90);

    [RelayCommand]
    private void GirarEsquerda(FotoVm foto) => Girar(foto, 270);

    [RelayCommand]
    private void OrdenarPorNome() => Reordenar(Fotos.OrderBy(f => f.Nome, OrdemNatural.Instancia));

    /// <summary>Pela data de captura; as fotos sem essa data vao para o fim, na ordem do nome.</summary>
    [RelayCommand]
    private void OrdenarPorData() =>
        Reordenar(Fotos.OrderBy(f => f.CapturadaEm is null).ThenBy(f => f.CapturadaEm).ThenBy(f => f.Nome, OrdemNatural.Instancia));

    [RelayCommand]
    private void SelecionarTodas()
    {
        bool marcar = Fotos.Any(f => !f.Selecionada);
        foreach (var foto in Fotos)
        {
            foto.Selecionada = marcar;
        }
    }

    [RelayCommand]
    private void DesmarcarTodas()
    {
        foreach (var foto in Fotos)
        {
            foto.Selecionada = false;
        }
    }

    private void AvisarLote()
    {
        OnPropertyChanged(nameof(QuantasMarcadas));
        OnPropertyChanged(nameof(TemMarcadas));
        OnPropertyChanged(nameof(TextoMarcadas));
        OnPropertyChanged(nameof(TextoTirarMarcadas));
        OnPropertyChanged(nameof(TextoBotaoLote));
        OnPropertyChanged(nameof(NotaDoLote));
    }

    /// <summary>
    /// Mesmo texto em varias fotos: legenda e, no modo com descricao, a descricao. Com fotos marcadas, vai para elas;
    /// sem nenhuma marcada, so para as fotos com o campo em branco. Campo deixado vazio nao apaga nada.
    /// </summary>
    [RelayCommand]
    private void AplicarTextoEmLote()
    {
        string legenda = LegendaEmLote.Trim(), descricao = ModoDescricao ? DescricaoEmLote.Trim() : "";
        if (legenda.Length == 0 && descricao.Length == 0)
        {
            Resultado = ModoDescricao ? "Escreva a legenda ou a descrição para aplicar." : "Escreva a legenda para aplicar.";
            ResultadoComFalha = true;
            return;
        }

        bool soEmBranco = !TemMarcadas;
        int mudaram = 0;
        foreach (var foto in AlvoDoLote())
        {
            bool mudou = false;
            foto.Carregar(() =>
            {
                if (legenda.Length > 0 && (!soEmBranco || foto.Legenda.Trim().Length == 0) && foto.Legenda != legenda)
                {
                    foto.Legenda = legenda;
                    mudou = true;
                }

                if (descricao.Length > 0 && (!soEmBranco || foto.Descricao.Trim().Length == 0) && foto.Descricao != descricao)
                {
                    foto.Descricao = descricao;
                    mudou = true;
                }
            });
            mudaram += mudou ? 1 : 0;
        }

        Replanejar();
        AvisarLote();
        Resultado = mudaram == 0 ? "Nenhuma foto mudou: elas já tinham esse texto." : $"Texto aplicado a {Formato.Plural(mudaram, "foto", "fotos")}.";
        ResultadoComFalha = false;
    }

    [RelayCommand]
    private void EscolherCapacidade(OpcaoCapacidade opcao)
    {
        if (opcao.Habilitada)
        {
            FotosPorPagina = opcao.Numero;
        }
    }

    [RelayCommand]
    private void IrParaFotos() => Etapa = 1;

    /// <summary>Foto que mudou de lugar: o usuario aponta onde ela esta agora.</summary>
    [RelayCommand]
    private void Localizar(FotoVm foto)
    {
        string? novo = _dialogos.EscolherArquivos($"Onde está \"{foto.Nome}\"?", "Fotos|*.jpg;*.jpeg;*.png;*.bmp;*.webp;*.gif", _preferencias.Atual.PastaTrabalho).FirstOrDefault();
        if (novo is null)
        {
            return;
        }

        try
        {
            var info = Documentos.Imagens.Fotos.Ler(novo, foto.Rotacao);
            foto.Carregar(() =>
            {
                foto.Caminho = novo;
                foto.Largura = info.Largura;
                foto.Altura = info.Altura;
                foto.CapturadaEm = info.CapturadaEm;
                foto.Faltando = false;
                foto.Miniatura = null;
            });
            Replanejar();
            GerarMiniaturas();
        }
        catch (FotoInvalidaException ex)
        {
            _dialogos.Avisar("Localizar foto", ex.Message);
        }
    }

    // ---- Geracao ----

    [RelayCommand]
    private void EscolherDestino()
    {
        if (_dialogos.EscolherPasta("Salvar o relatório em", PastaDestino) is { } pasta)
        {
            PastaDestino = pasta;
            _preferencias.LembrarDestino(Modulo, pasta);
        }
    }

    private bool PodeGerar() => _plano is { PodeGerar: true } && !Fotos.Any(f => f.Faltando) && NomeArquivo.Trim().Length > 0 && !Ocupado;

    [RelayCommand(CanExecute = nameof(PodeGerar))]
    private async Task GerarAsync()
    {
        var plano = _plano!;
        string nome = NomeArquivo.Trim();
        if (NomeArquivo_Problema(nome) is { } problema)
        {
            _dialogos.Avisar("Nome do arquivo", problema);
            return;
        }

        // O mesmo nome para o Excel e o PDF; se um dos dois ja existir, os dois ganham o mesmo numero.
        bool querExcel = FormatoDeSaida != FormatoRelatorio.SoPdf, querPdf = FormatoDeSaida != FormatoRelatorio.SoExcel;
        string livre = Spobras.Core.Nomes.NomeArquivo.Livre(nome + ".xlsx", n =>
            (querExcel && File.Exists(Path.Combine(PastaDestino, n))) || (querPdf && File.Exists(Path.Combine(PastaDestino, Path.ChangeExtension(n, ".pdf")))));
        string xlsx = Path.Combine(PastaDestino, livre);
        string pdf = Path.ChangeExtension(xlsx, ".pdf");
        string? logo = _modelos.Logo;
        var criados = new List<string>();
        string? erro = null;

        Ocupado = true;
        GerarCommand.NotifyCanExecuteChanged();
        try
        {
            var tarefa = _operacoes.Fila.Enfileirar($"Relatório fotográfico: {Path.GetFileNameWithoutExtension(xlsx)}", (progresso, cancelamento) =>
            {
                try
                {
                    int total = plano.TotalDeFotos;
                    // As fotos sao preparadas uma vez so e servem ao Excel e ao PDF.
                    var preparo = new PreparoDeFotos();
                    var passo = new Progresso(n => progresso.Report(new ProgressoTarefa(n, total, "Preparando as fotos")));
                    if (querExcel)
                    {
                        GeradorRelatorio.Gerar(plano, xlsx, logo, passo, cancelamento, preparo);
                        criados.Add(xlsx);
                    }

                    if (querPdf)
                    {
                        progresso.Report(new ProgressoTarefa(total, total, "Montando o PDF"));
                        GeradorRelatorioPdf.Gerar(plano, pdf, logo, querExcel ? null : passo, cancelamento, preparo);
                        criados.Add(pdf);
                    }
                }
                catch (OperationCanceledException)
                {
                    erro = "A geração foi cancelada. Nenhum arquivo incompleto ficou na pasta.";
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or FotoInvalidaException)
                {
                    erro = ex.Message;
                }

                return Task.FromResult<string?>(erro ?? $"{plano.Paginas.Count} página(s), {plano.TotalDeFotos} foto(s)");
            });
            await FilaTarefas.Aguardar(tarefa);
        }
        finally
        {
            Ocupado = false;
            GerarCommand.NotifyCanExecuteChanged();
        }

        if (criados.Count > 0)
        {
            _operacoes.RegistrarCriados($"Gerou “{Path.GetFileName(criados[0])}”" + (criados.Count > 1 ? " e o PDF" : ""), criados);
            _ultimoResultado = criados[0];
            PodeAbrirResultado = true;
        }

        ResultadoComFalha = erro is not null;
        string paginas = $"{Formato.Plural(plano.Paginas.Count, "página", "páginas")}, {Formato.Plural(plano.TotalDeFotos, "foto", "fotos")}";
        Resultado = (criados.Count, erro) switch
        {
            (0, _) => "O relatório não foi gerado: " + erro,
            (1, not null) => $"O Excel foi gerado ({Path.GetFileName(xlsx)}), mas o PDF não: {erro}",
            (1, null) when querPdf => $"Relatório gerado em PDF: {Path.GetFileName(pdf)} ({paginas}).",
            (1, null) => $"Relatório gerado em Excel: {Path.GetFileName(xlsx)} ({paginas}).",
            _ => $"Relatório gerado em Excel e PDF: {Path.GetFileNameWithoutExtension(xlsx)} ({paginas}).",
        };
    }

    [RelayCommand]
    private void AbrirResultado()
    {
        if (_ultimoResultado is not null)
        {
            _dialogos.MostrarNaPasta(_ultimoResultado);
        }
    }

    /// <summary>Comeca do zero, sem levar escola nem contrato do relatorio anterior.</summary>
    [RelayCommand]
    private void NovoRelatorio()
    {
        bool temConteudo = Fotos.Count > 0 || Unidade.Length > 0 || Contratada.Length > 0;
        if (temConteudo && !_dialogos.Confirmar("Novo relatório",
                "Começar um relatório novo limpa os dados e a lista de fotos deste rascunho.\nAs fotos e os relatórios já gerados não são apagados.", "Começar novo"))
        {
            return;
        }

        _restaurando = true;
        Unidade = Contratada = Contrato = Lote = Portaria = ProcessoSei = Objeto = Responsavel = Descricao = DataTexto = DataOsTexto = "";
        Fotos.Clear();
        EsquecerDesfazer(); // as fotos tiradas do relatorio anterior nao podem voltar para o novo
        LegendaEmLote = DescricaoEmLote = "";
        Problemas.Clear();
        _nomeEditado = false;
        Etapa = 0;
        _restaurando = false;
        OnPropertyChanged(nameof(EtapaDados));
        OnPropertyChanged(nameof(EtapaFotos));
        OnPropertyChanged(nameof(EtapaConferencia));
        OnPropertyChanged(nameof(TextoBotaoAvancar));
        Resultado = "";
        _rascunhos.Remover(ChaveRascunho);
        Replanejar();
    }

    /// <summary>
    /// Preenche o cabecalho com os dados de uma obra escolhida na Base de dados. So entra o que a base tem;
    /// tudo continua editavel e as fotos ja escolhidas ficam.
    /// </summary>
    public void PreencherDaObra(Spobras.Core.Base.ObraLida obra)
    {
        _restaurando = true;
        AplicarDaBase(obra);
        _nomeEditado = false;
        Etapa = 0;
        _restaurando = false;
        OnPropertyChanged(nameof(EtapaDados));
        OnPropertyChanged(nameof(EtapaFotos));
        OnPropertyChanged(nameof(EtapaConferencia));
        OnPropertyChanged(nameof(TextoBotaoAvancar));
        Orientar($"Dados de {obra.Empreendimento} (contrato {obra.Contrato}) trazidos da Base de dados. Confira e ajuste o que precisar.");
        Replanejar();
    }

    /// <summary>
    /// Atualiza os campos da obra. Data do relatorio, fiscal, descricao e fotos sao preservados.
    /// </summary>
    private List<string> AplicarDaBase(Spobras.Core.Base.ObraLida obra)
    {
        var mudou = new List<string>();
        void Trocar(string rotulo, string atual, string novo, Action<string> definir)
        {
            if (!string.Equals(atual.Trim(), novo.Trim(), StringComparison.Ordinal))
            {
                mudou.Add(rotulo);
                definir(novo);
            }
        }

        Trocar("Unidade", Unidade, obra.Empreendimento, v => Unidade = v);
        Trocar("Contratada", Contratada, obra.Contratada ?? "", v => Contratada = v);
        Trocar("Contrato", Contrato, obra.Contrato, v => Contrato = v);
        Trocar("Lote", Lote, obra.Lote ?? "", v => Lote = v);
        Trocar("Portaria", Portaria, obra.Portaria ?? "", v => Portaria = v);
        Trocar("Data da OS", DataOsTexto, obra.DataOs?.ToString("dd/MM/yyyy", Formato.PtBr) ?? "", v => DataOsTexto = v);
        Trocar("Objeto", Objeto, obra.Escopo ?? "", v => Objeto = v);
        Trocar("Processo SEI", ProcessoSei, obra.ProcessoSei ?? "", v => ProcessoSei = v);
        return mudou;
    }

    /// <summary>
    /// Busca de novo na Base os dados da obra que ja esta no relatorio (pelo contrato e pela escola) e diz o que
    /// mudou. Campos apagados de proposito voltam; quem nao quiser apaga de novo.
    /// </summary>
    [RelayCommand]
    private void AtualizarDados()
    {
        Spobras.Core.Base.ObraLida? obra = null;
        string contrato = Contrato.Trim(), escola = Unidade.Trim();
        var doContrato = contrato.Length > 0 && ObrasDoContrato is not null ? ObrasDoContrato(contrato) : [];
        if (doContrato.Count > 0)
        {
            var daEscola = doContrato.Where(o => MesmaEscola(o.Empreendimento, escola)).ToList();
            obra = daEscola.Count == 1 ? daEscola[0] : doContrato.Count == 1 && escola.Length == 0 ? doContrato[0] : null;
            if (obra is null)
            {
                Avisar(escola.Length == 0
                    ? $"O contrato {contrato} tem {doContrato.Count} escolas. Escreva a escola para atualizar os dados certos."
                    : $"O contrato {contrato} não tem a escola “{escola}” na Base de dados. Nada foi trocado.");
                return;
            }
        }
        else if (escola.Length > 0 && ObrasDaEscola is not null)
        {
            var obras = ObrasDaEscola(escola).Where(o => MesmaEscola(o.Empreendimento, escola)).ToList();
            if (obras.Count > 1)
            {
                Avisar($"{escola} está em {obras.Count} contratos. Escreva o contrato para atualizar os dados certos.");
                return;
            }

            obra = obras.FirstOrDefault();
        }

        if (obra is null)
        {
            Avisar("Não achei esta obra na Base de dados. Confira o contrato e a escola.");
            return;
        }

        _restaurando = true;
        var mudou = AplicarDaBase(obra);
        _restaurando = false;
        LimparEscolhas();
        Orientar(mudou.Count == 0
            ? "Os dados já estavam iguais aos da Base de dados."
            : $"Atualizado pela Base de dados: {string.Join(", ", mudou)}.");
        Replanejar();
    }

    // ---- Interno ----

    private static string? NomeArquivo_Problema(string nome) => Spobras.Core.Nomes.NomeArquivo.Problema(nome + ".xlsx");

    private static (DateOnly? Data, bool Invalida) LerData(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            return (null, false);
        }

        return DateOnly.TryParseExact(texto.Trim(), ["d/M/yyyy", "dd/MM/yyyy", "d/M/yy"], Formato.PtBr, DateTimeStyles.None, out var data)
            ? (data, false)
            : (null, true);
    }

    private DadosRelatorio Dados() => new()
    {
        Unidade = Unidade,
        Contratada = Contratada,
        Contrato = Contrato,
        Lote = Lote,
        Portaria = Portaria,
        ProcessoSei = ProcessoSei,
        Objeto = Objeto,
        Responsavel = Responsavel,
        Descricao = Descricao,
        Data = LerData(DataTexto).Data,
        DataOs = LerData(DataOsTexto).Data,
    };

    private OpcoesRelatorio Opcoes() => new()
    {
        FotosPorPagina = FotosPorPagina,
        Modo = Modo,
        Assinaturas = Assinaturas,
        AssinaturaFiscal = AssinaturaFiscal,
        AssinaturaContratada = AssinaturaContratada,
        LadoMaximoFoto = Qualidade switch { QualidadeFotos.Alta => 2400, QualidadeFotos.Media => 1600, _ => 1100 },
        QualidadeFoto = Qualidade switch { QualidadeFotos.Alta => 90, QualidadeFotos.Media => 82, _ => 70 },
    };

    private void Mover(FotoVm foto, int passo)
    {
        int de = Fotos.IndexOf(foto);
        MoverFoto(de, de + passo);
    }

    public void MoverFoto(int de, int para)
    {
        if (de == para || de < 0 || de >= Fotos.Count || para < 0 || para >= Fotos.Count) return;
        Fotos.Move(de, para);
        Replanejar();
    }

    private void Reordenar(IEnumerable<FotoVm> ordem)
    {
        var lista = ordem.ToList();
        for (int i = 0; i < lista.Count; i++)
        {
            int atual = Fotos.IndexOf(lista[i]);
            if (atual != i)
            {
                Fotos.Move(atual, i);
            }
        }

        Replanejar();
    }

    private void Girar(FotoVm foto, int graus)
    {
        try
        {
            int rotacao = (foto.Rotacao + graus) % 360;
            var info = Documentos.Imagens.Fotos.Ler(foto.Caminho, rotacao);
            foto.Carregar(() =>
            {
                foto.Rotacao = rotacao;
                foto.Largura = info.Largura;
                foto.Altura = info.Altura;
                foto.Miniatura = null;
            });
            Replanejar();
            GerarMiniaturas();
        }
        catch (FotoInvalidaException ex)
        {
            _dialogos.Avisar("Girar foto", ex.Message);
        }
    }

    private void ConferirFotosFaltando()
    {
        foreach (var foto in Fotos)
        {
            foto.Faltando = !File.Exists(foto.Caminho);
        }
    }

    private void Replanejar()
    {
        if (_restaurando)
        {
            return;
        }

        Avisos.Clear();
        Paginas.Clear();
        OnPropertyChanged(nameof(TemFotos));

        int total = Fotos.Count;
        for (int i = 0; i < total; i++)
        {
            Fotos[i].Rotulo = PaginadorRelatorio.Rotulo(i + 1, total);
        }

        TextoFotos = total == 0 ? "Nenhuma foto escolhida" : Formato.Plural(total, "foto escolhida", "fotos escolhidas");

        var dados = Dados();
        if (!_nomeEditado)
        {
            _restaurando = true;
            NomeArquivo = CabecalhoRelatorio.NomeSugerido(dados);
            _restaurando = false;
        }

        if (LerData(DataTexto).Invalida)
        {
            Avisos.Add(new AvisoVm(Gravidade.ImpedeGerar, "A data do relatório não existe. Use dia/mês/ano, como 10/11/2026."));
        }

        if (LerData(DataOsTexto).Invalida)
        {
            Avisos.Add(new AvisoVm(Gravidade.ImpedeGerar, "A data da OS não existe. Use dia/mês/ano, como 10/11/2026."));
        }

        int faltando = Fotos.Count(f => f.Faltando);
        if (faltando > 0)
        {
            Avisos.Add(new AvisoVm(Gravidade.ImpedeGerar,
                $"{Formato.Plural(faltando, "foto não foi encontrada", "fotos não foram encontradas")} no lugar de antes. Na etapa Fotos, use Localizar ou tire da lista."));
        }

        var fotos = Fotos.Select(f => new FotoRelatorio(f.Caminho, f.Largura, f.Altura) { Legenda = f.Legenda, Descricao = f.Descricao, Rotacao = f.Rotacao }).ToList();
        _plano = PaginadorRelatorio.Paginar(fotos, Opcoes(), dados);
        foreach (var aviso in _plano.Avisos.Where(a => total > 0 || a.Gravidade != Gravidade.ImpedeGerar))
        {
            Avisos.Add(new AvisoVm(aviso));
        }

        bool erroDeData = Avisos.Any(a => a.Impede && a.Texto.Contains("data", StringComparison.OrdinalIgnoreCase) && a.Texto.Contains("não existe"));
        if (erroDeData || faltando > 0)
        {
            _plano = _plano with { Avisos = [.. _plano.Avisos, Aviso.Impede("Há pendências que impedem gerar.")] };
        }

        MontarPaginas();

        OnPropertyChanged(nameof(OpcoesDeCapacidade));
        OnPropertyChanged(nameof(DescricoesGuardadas));
        OnPropertyChanged(nameof(AvisoDescricoesGuardadas));
        OnPropertyChanged(nameof(TextoBotaoLote));
        Resumo = total == 0
            ? "Escolha as fotos na etapa Fotos e textos."
            : $"{Formato.Plural(total, "foto", "fotos")} em {Formato.Plural(_plano.Paginas.Count, "página", "páginas")}";
        GerarCommand.NotifyCanExecuteChanged();
        SalvarRascunho();
    }

    /// <summary>Desenha a pagina com as mesmas pecas que o PDF desenha (DesenhoRelatorio).</summary>
    private List<ElementoPrevia> MontarPrevia(PaginaRelatorio pagina, PlanoRelatorio plano, Dictionary<string, FotoVm> fotos)
    {
        var e = new List<ElementoPrevia>();
        string? logo = _modelos.Logo;
        foreach (var elemento in DesenhoRelatorio.Pagina(plano, pagina, logo is not null))
        {
            switch (elemento)
            {
                case RetanguloDesenho r:
                    e.Add(new ElementoPrevia(r.Area.X, r.Area.Y, r.Area.Largura, r.Area.Altura)
                    { Fundo = r.Fundo is null ? null : "#" + r.Fundo, Borda = r.Borda is null ? null : "#" + r.Borda, Espessura = r.Borda is null ? 0 : r.Espessura });
                    break;
                case LinhaDesenho l:
                    double x = Math.Min(l.X1, l.X2), y = Math.Min(l.Y1, l.Y2);
                    e.Add(new ElementoPrevia(x - l.Espessura / 2, y - l.Espessura / 2, Math.Abs(l.X2 - l.X1) + l.Espessura, Math.Abs(l.Y2 - l.Y1) + l.Espessura) { Fundo = "#" + l.Cor });
                    break;
                case ImagemDesenho { Foto: { } foto } im:
                    fotos.TryGetValue(foto.Caminho, out var vm);
                    e.Add(new ElementoPrevia(im.Area.X, im.Area.Y, im.Area.Largura, im.Area.Altura) { Imagem = vm?.Miniatura, TemImagem = true, Faltando = vm?.Faltando == true });
                    break;
                case ImagemDesenho im:
                    e.Add(new ElementoPrevia(im.Area.X, im.Area.Y, im.Area.Largura, im.Area.Altura) { Imagem = logo, TemImagem = true });
                    break;
                case TextoDesenho t:
                    e.Add(new ElementoPrevia(t.Area.X, t.Area.Y, t.Area.Largura, Math.Max(t.Area.Altura, t.Linhas.Count * t.AlturaDaLinha))
                    {
                        Texto = string.Join("\n", t.Linhas), Negrito = t.Negrito, Fonte = t.Tamanho, AlturaDaLinha = t.AlturaDaLinha, Cor = "#" + t.Cor,
                        Centro = t.Alinhamento == Alinhamento.Centro, Meio = t.CentroNaAltura,
                    });
                    break;
            }
        }

        return e;
    }

    /// <summary>Termina quando as miniaturas pedidas ate agora estiverem prontas e a previa atualizada.</summary>
    public Task Miniaturas { get; private set; } = Task.CompletedTask;

    /// <summary>Miniaturas em segundo plano, uma por vez, sem travar a tela nem carregar todas as fotos de uma vez.</summary>
    private void GerarMiniaturas()
    {
        var anterior = Miniaturas;
        var pendentes = Fotos.Where(f => f.Miniatura is null && !f.Faltando).Select(f => (Foto: f, f.Caminho, f.Rotacao)).ToList();
        if (pendentes.Count == 0)
        {
            return;
        }

        Miniaturas = Task.Run(async () =>
        {
            await anterior.ConfigureAwait(false); // uma leva de cada vez
            // Ate 3 miniaturas ao mesmo tempo (cada uma e independente; o computador tem varios nucleos).
            Parallel.ForEach(pendentes, new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount - 1, 1, 3) }, item =>
            {
                var (foto, caminho, rotacao) = item;
                try
                {
                    string miniatura = _modelos.Miniatura(caminho, rotacao);
                    _interface.Executar(() =>
                    {
                        if (foto.Caminho == caminho && foto.Rotacao == rotacao)
                        {
                            foto.Miniatura = miniatura;
                        }
                    });
                }
                catch (Exception ex) when (ex is FotoInvalidaException or IOException or UnauthorizedAccessException)
                {
                    // Sem miniatura a foto continua na lista; o problema aparece ao gerar.
                }
            });

            _interface.Executar(AtualizarPrevia);
        });
    }

    private void AtualizarPrevia() => MontarPaginas();

    private bool _previaVelha;

    /// <summary>
    /// Desenho das paginas: so com a Conferencia aberta. Digitar nos dados ou nas legendas nao refaz a previa de todas
    /// as paginas a cada letra (com muitas fotos, a digitacao engasgava); ela e montada ao entrar na Conferencia.
    /// </summary>
    private void MontarPaginas()
    {
        Paginas.Clear();
        if (_plano is null)
        {
            return;
        }

        if (!EtapaConferencia)
        {
            _previaVelha = true;
            return;
        }

        _previaVelha = false;
        var porCaminho = Fotos.GroupBy(f => f.Caminho, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        foreach (var pagina in _plano.Paginas)
        {
            Paginas.Add(new PaginaPreviaVm(pagina.Numero, _plano.Paginas.Count, MontarPrevia(pagina, _plano, porCaminho)));
        }
    }

    private void SalvarRascunho() => _rascunhos.Gravar(ChaveRascunho, new RascunhoRelatorio
    {
        Dados = Dados(),
        DataTexto = DataTexto,
        DataOsTexto = DataOsTexto,
        FotosPorPagina = FotosPorPagina,
        Modo = Modo,
        Assinaturas = Assinaturas,
        AssinaturaFiscal = AssinaturaFiscal,
        AssinaturaContratada = AssinaturaContratada,
        Qualidade = Qualidade,
        Fotos = [.. Fotos.Select(f => new FotoRascunho(f.Caminho, f.Legenda, f.Descricao, f.Rotacao))],
    }, _relogio.Agora);

    private void Restaurar()
    {
        if (_rascunhos.Ler<RascunhoRelatorio>(ChaveRascunho) is not { } r)
        {
            return;
        }

        _restaurando = true;
        Unidade = r.Dados.Unidade;
        Contratada = r.Dados.Contratada;
        Contrato = r.Dados.Contrato;
        Lote = r.Dados.Lote;
        Portaria = r.Dados.Portaria;
        ProcessoSei = r.Dados.ProcessoSei;
        Objeto = r.Dados.Objeto;
        Responsavel = r.Dados.Responsavel;
        Descricao = r.Dados.Descricao ?? "";
        DataTexto = r.DataTexto;
        DataOsTexto = r.DataOsTexto;
        FotosPorPagina = OpcoesRelatorio.Capacidades.Contains(r.FotosPorPagina) ? r.FotosPorPagina : 6;
        Modo = r.Modo;
        Assinaturas = r.Assinaturas;
        AssinaturaFiscal = r.AssinaturaFiscal;
        AssinaturaContratada = r.AssinaturaContratada;
        Qualidade = r.Qualidade;
        foreach (var f in r.Fotos)
        {
            var foto = new FotoVm(f.Caminho, Replanejar) { AoMarcar = AvisarLote };
            foto.Carregar(() =>
            {
                foto.Legenda = f.Legenda;
                foto.Descricao = f.Descricao;
                foto.Rotacao = f.Rotacao;
                try
                {
                    var info = Documentos.Imagens.Fotos.Ler(f.Caminho, f.Rotacao);
                    foto.Largura = info.Largura;
                    foto.Altura = info.Altura;
                    foto.CapturadaEm = info.CapturadaEm;
                }
                catch (FotoInvalidaException)
                {
                    // A foto saiu do lugar: fica na lista, marcada, para o usuario localizar.
                    foto.Faltando = true;
                    foto.Largura = 4;
                    foto.Altura = 3;
                }
            });
            Fotos.Add(foto);
        }

        _restaurando = false;
        OnPropertyChanged(nameof(ModoDescricao));
        Replanejar();
        GerarMiniaturas();
    }

    private sealed class Progresso(Action<int> aoMudar) : IProgress<int>
    {
        public void Report(int value) => aoMudar(value);
    }
}
