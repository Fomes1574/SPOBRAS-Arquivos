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

public sealed record FotoRascunho(string Caminho, string Legenda, string Descricao, int Rotacao);

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

    partial void OnLegendaChanged(string value) => Avisar();

    partial void OnDescricaoChanged(string value) => Avisar();

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
public sealed record ElementoPrevia(double X, double Y, double Largura, double Altura)
{
    public string Texto { get; init; } = "";
    public string? Imagem { get; init; }
    public bool Negrito { get; init; }
    public double Fonte { get; init; } = 14.67; // 11 pt
    public bool Borda { get; init; }
    public bool Fundo { get; init; }
    public bool Centro { get; init; }
    public bool Meio { get; init; }
    public bool LinhaEmCima { get; init; }
    public bool Faltando { get; init; }
}

public sealed record PaginaPreviaVm(int Numero, int Total, IReadOnlyList<ElementoPrevia> Elementos)
{
    public string Titulo => $"Página {Numero} de {Total}";
    public double Largura => GradeRelatorio.Largura;
    public double Altura => GradeRelatorio.AlturaDaPagina;
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
    private readonly IExportadorPdf _exportadorPdf;
    private readonly IRelogio _relogio;
    private PlanoRelatorio? _plano;
    private bool _restaurando;
    private bool _nomeEditado;
    private string? _ultimoResultado;

    public RelatorioVm(
        IDialogos dialogos, IInterface @interface, ServicoOperacoes operacoes, ServicoPreferencias preferencias,
        ServicoModelos modelos, RascunhoRepositorio rascunhos, IExportadorPdf exportadorPdf, IRelogio relogio)
    {
        _dialogos = dialogos;
        _interface = @interface;
        _operacoes = operacoes;
        _preferencias = preferencias;
        _modelos = modelos;
        _rascunhos = rascunhos;
        _exportadorPdf = exportadorPdf;
        _relogio = relogio;
        _pastaDestino = preferencias.DestinoDe(Modulo);
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

    [ObservableProperty]
    private bool _gerarPdf;

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
    public bool PdfDisponivel => _exportadorPdf.Disponivel;
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
                or nameof(Assinaturas) or nameof(AssinaturaFiscal) or nameof(AssinaturaContratada) or nameof(Qualidade) or nameof(GerarPdf):
                Replanejar();
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
                var foto = new FotoVm(caminho, Replanejar) { Largura = info.Largura, Altura = info.Altura, CapturadaEm = info.CapturadaEm };
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

    [RelayCommand]
    private void Remover(FotoVm foto)
    {
        Fotos.Remove(foto);
        Replanejar();
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

    /// <summary>Aplica a legenda digitada as fotos marcadas (ou, sem nenhuma marcada, as que estao sem legenda).</summary>
    [RelayCommand]
    private void AplicarLegendaEmLote()
    {
        var alvo = Fotos.Any(f => f.Selecionada) ? Fotos.Where(f => f.Selecionada).ToList() : Fotos.Where(f => f.Legenda.Length == 0).ToList();
        foreach (var foto in alvo)
        {
            foto.Carregar(() => foto.Legenda = LegendaEmLote.Trim());
        }

        Replanejar();
        Resultado = $"Legenda aplicada a {Formato.Plural(alvo.Count, "foto", "fotos")}.";
        ResultadoComFalha = false;
    }

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

        string xlsx = Path.Combine(PastaDestino, Spobras.Core.Nomes.NomeArquivo.Livre(nome + ".xlsx", n => File.Exists(Path.Combine(PastaDestino, n))));
        string pdf = Path.ChangeExtension(xlsx, ".pdf");
        bool querPdf = GerarPdf && _exportadorPdf.Disponivel;
        string? logo = _modelos.Logo;
        var criados = new List<string>();
        string? erro = null;

        Ocupado = true;
        GerarCommand.NotifyCanExecuteChanged();
        try
        {
            var tarefa = _operacoes.Fila.Enfileirar($"Relatório fotográfico: {Path.GetFileName(xlsx)}", (progresso, cancelamento) =>
            {
                try
                {
                    int total = plano.TotalDeFotos;
                    GeradorRelatorio.Gerar(plano, xlsx, logo, new Progresso(n => progresso.Report(new ProgressoTarefa(n, total, "Preparando as fotos"))), cancelamento);
                    criados.Add(xlsx);
                    if (querPdf)
                    {
                        // Chamada ao Excel: nao da para cancelar no meio, so esperar terminar.
                        progresso.Report(new ProgressoTarefa(total, total, "Aguardando o Excel gerar o PDF"));
                        if (File.Exists(pdf))
                        {
                            pdf = Path.Combine(PastaDestino, Spobras.Core.Nomes.NomeArquivo.Livre(Path.GetFileName(pdf), n => File.Exists(Path.Combine(PastaDestino, n))));
                        }

                        _exportadorPdf.Exportar(xlsx, pdf);
                        criados.Add(pdf);
                    }
                }
                catch (OperationCanceledException)
                {
                    erro = "A geração foi cancelada. Nenhum arquivo incompleto ficou na pasta.";
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or FotoInvalidaException
                                               or System.Runtime.InteropServices.COMException)
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
        Resultado = criados.Count switch
        {
            0 => "O relatório não foi gerado: " + erro,
            1 when erro is not null => $"O Excel foi gerado ({Path.GetFileName(xlsx)}), mas o PDF não: {erro}",
            1 => $"Relatório gerado: {Path.GetFileName(xlsx)} ({Formato.Plural(plano.Paginas.Count, "página", "páginas")}, {Formato.Plural(plano.TotalDeFotos, "foto", "fotos")}).",
            _ => $"Relatório gerado em Excel e PDF: {Path.GetFileNameWithoutExtension(xlsx)} ({Formato.Plural(plano.Paginas.Count, "página", "páginas")}).",
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

        if (GerarPdf && !_exportadorPdf.Disponivel)
        {
            Avisos.Add(new AvisoVm(Gravidade.PrecisaConferir, "O PDF é feito pelo Excel, que não foi encontrado neste computador. Será gerado só o arquivo Excel."));
        }

        bool erroDeData = Avisos.Any(a => a.Impede && a.Texto.Contains("data", StringComparison.OrdinalIgnoreCase) && a.Texto.Contains("não existe"));
        if (erroDeData || faltando > 0)
        {
            _plano = _plano with { Avisos = [.. _plano.Avisos, Aviso.Impede("Há pendências que impedem gerar.")] };
        }

        var porCaminho = Fotos.ToDictionary(f => f.Caminho, StringComparer.OrdinalIgnoreCase);
        foreach (var pagina in _plano.Paginas)
        {
            Paginas.Add(new PaginaPreviaVm(pagina.Numero, _plano.Paginas.Count, MontarPrevia(pagina, _plano, porCaminho)));
        }

        Resumo = total == 0
            ? "Escolha as fotos na etapa Fotos e legendas."
            : $"{Formato.Plural(total, "foto", "fotos")} em {Formato.Plural(_plano.Paginas.Count, "página", "páginas")}";
        GerarCommand.NotifyCanExecuteChanged();
        SalvarRascunho();
    }

    /// <summary>Desenha a pagina com as mesmas medidas que o gerador usa no arquivo.</summary>
    private List<ElementoPrevia> MontarPrevia(PaginaRelatorio pagina, PlanoRelatorio plano, Dictionary<string, FotoVm> fotos)
    {
        var e = new List<ElementoPrevia>();
        var dados = plano.Dados;
        double X(int coluna) => GradeRelatorio.XDaColuna(coluna);
        var cab = plano.Cabecalho;
        var h = cab.Alturas;
        double y4 = h.Take(4).Sum();
        double largura = GradeRelatorio.Largura;
        double xt = X(GradeRelatorio.ColunaDoTitulo), xk = X(GradeRelatorio.ColunaDaCaixa), xd = X(GradeRelatorio.ColunaDoValorDaData), xv = X(GradeRelatorio.ColunaDoValor);

        // Cabecalho: logo, titulo e a caixa SPOBRAS / data / contratada; depois um campo por linha.
        e.Add(new ElementoPrevia(0, 0, xt, y4) { Borda = true, Imagem = _modelos.Logo });
        e.Add(new ElementoPrevia(xt, 0, xk - xt, y4) { Borda = true, Texto = "RELATÓRIO FOTOGRÁFICO", Negrito = true, Fonte = 24, Centro = true, Meio = true });
        e.Add(new ElementoPrevia(xk, 0, largura - xk, h[0]) { Borda = true, Texto = "SÃO PAULO OBRAS - SPOBRAS", Negrito = true, Fonte = 16, Centro = true, Meio = true });
        if (dados.Data is not null)
        {
            e.Add(new ElementoPrevia(xk, h[0], xd - xk, h[1]) { Borda = true, Texto = "DATA", Negrito = true, Centro = true, Meio = true });
            e.Add(new ElementoPrevia(xd, h[0], largura - xd, h[1]) { Borda = true, Texto = CabecalhoRelatorio.Data(dados.Data), Negrito = true, Centro = true, Meio = true });
        }
        else
        {
            e.Add(new ElementoPrevia(xk, h[0], largura - xk, h[1]) { Borda = true });
        }

        bool temContratada = !string.IsNullOrWhiteSpace(dados.Contratada);
        e.Add(new ElementoPrevia(xk, h[0] + h[1], largura - xk, h[2]) { Borda = true, Texto = temContratada ? "CONTRATADA" : "", Negrito = true, Centro = true, Meio = true });
        e.Add(new ElementoPrevia(xk, h[0] + h[1] + h[2], largura - xk, h[3]) { Borda = true, Texto = temContratada ? dados.Contratada.Trim() : "", Centro = true, Meio = true });
        for (int i = 0; i < cab.Campos.Count; i++)
        {
            double y = cab.YDoCampo(i), alt = h[cab.PrimeiroCampo + i];
            var campo = cab.Campos[i];
            e.Add(new ElementoPrevia(0, y, xv, alt) { Borda = true, Texto = campo.Rotulo, Negrito = true, Meio = true });
            if (campo.Dupla)
            {
                double x2 = X(CabecalhoRelatorio.ColunaDoRotulo2), xv2 = X(CabecalhoRelatorio.ColunaDoValor2);
                e.Add(new ElementoPrevia(xv, y, x2 - xv, alt) { Borda = true, Texto = campo.Valor, Centro = true, Meio = true });
                e.Add(new ElementoPrevia(x2, y, xv2 - x2, alt) { Borda = true, Texto = campo.Rotulo2 ?? "", Negrito = true, Meio = true });
                e.Add(new ElementoPrevia(xv2, y, largura - xv2, alt) { Borda = true, Texto = campo.Valor2 ?? "", Centro = true, Meio = true });
            }
            else
            {
                e.Add(new ElementoPrevia(xv, y, largura - xv, alt) { Borda = true, Texto = campo.Valor, Meio = true });
            }
        }

        e.Add(new ElementoPrevia(0, cab.YDoTitulo + GradeRelatorio.LinhasDoTitulo[0], largura, GradeRelatorio.LinhasDoTitulo[1])
        { Borda = true, Fundo = true, Texto = "RELATÓRIO FOTOGRÁFICO", Negrito = true, Centro = true, Meio = true });

        // Fotos e textos
        foreach (var foto in pagina.Fotos)
        {
            fotos.TryGetValue(foto.Foto.Caminho, out var vm);
            e.Add(new ElementoPrevia(foto.Imagem.X, foto.Imagem.Y, foto.Imagem.Largura, foto.Imagem.Altura) { Imagem = vm?.Miniatura, Faltando = vm?.Faltando == true, Borda = vm?.Miniatura is null });
            double alturaTitulo = foto.Titulo.Length == 0 ? 0 : PaginadorRelatorio.LinhasDe(foto.Titulo, foto.Espaco.Largura) * GradeRelatorio.AlturaDaLinha;
            if (foto.Titulo.Length > 0)
            {
                e.Add(new ElementoPrevia(foto.Texto.X, foto.Texto.Y, foto.Texto.Largura, alturaTitulo) { Texto = foto.Titulo, Negrito = true, Centro = true });
            }
            if (foto.Descricao.Length > 0)
            {
                e.Add(new ElementoPrevia(foto.Texto.X, foto.Texto.Y + alturaTitulo, foto.Texto.Largura, foto.Texto.Altura - alturaTitulo) { Texto = foto.Descricao });
            }
        }

        foreach (var continuacao in pagina.Continuacoes)
        {
            e.Add(new ElementoPrevia(continuacao.Texto_.X, continuacao.Texto_.Y, continuacao.Texto_.Largura, GradeRelatorio.AlturaDaLinha) { Texto = continuacao.Titulo, Negrito = true });
            e.Add(new ElementoPrevia(continuacao.Texto_.X, continuacao.Texto_.Y + GradeRelatorio.AlturaDaLinha, continuacao.Texto_.Largura, continuacao.Texto_.Altura - GradeRelatorio.AlturaDaLinha) { Texto = continuacao.Texto });
        }

        if (pagina.Assinaturas is { } assinaturas)
        {
            double y = assinaturas.Espaco.Y + 3 * GradeRelatorio.AlturaDaLinha;
            e.Add(new ElementoPrevia(0, y, X(4), GradeRelatorio.AlturaDaLinha) { Texto = "Fiscalização SPOBRAS", Centro = true, LinhaEmCima = true });
            e.Add(new ElementoPrevia(X(6), y, largura - X(6), GradeRelatorio.AlturaDaLinha) { Texto = "Contratada", Centro = true, LinhaEmCima = true });
            e.Add(new ElementoPrevia(0, y + GradeRelatorio.AlturaDaLinha, X(4), GradeRelatorio.AlturaDaLinha) { Texto = plano.Opcoes.AssinaturaFiscal.Trim(), Centro = true });
            e.Add(new ElementoPrevia(X(6), y + GradeRelatorio.AlturaDaLinha, largura - X(6), GradeRelatorio.AlturaDaLinha) { Texto = plano.Opcoes.AssinaturaContratada.Trim(), Centro = true });
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
            foreach (var (foto, caminho, rotacao) in pendentes)
            {
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
            }

            _interface.Executar(AtualizarPrevia);
        });
    }

    private void AtualizarPrevia()
    {
        if (_plano is null)
        {
            return;
        }

        var porCaminho = Fotos.ToDictionary(f => f.Caminho, StringComparer.OrdinalIgnoreCase);
        Paginas.Clear();
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
        GerarPdf = GerarPdf,
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
        GerarPdf = r.GerarPdf;
        foreach (var f in r.Fotos)
        {
            var foto = new FotoVm(f.Caminho, Replanejar);
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
