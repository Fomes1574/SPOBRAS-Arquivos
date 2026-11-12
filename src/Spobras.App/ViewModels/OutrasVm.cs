using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Spobras.App.Servicos;
using Spobras.Core.Abstracoes;
using Spobras.Core.Operacoes;
using Spobras.Core.Tarefas;

namespace Spobras.App.ViewModels;

public sealed class TarefaVm(Tarefa tarefa)
{
    public Tarefa Tarefa { get; } = tarefa;
    public string Titulo => Tarefa.Titulo;

    public string Estado => Tarefa.Estado switch
    {
        EstadoTarefa.NaFila => "Na fila",
        EstadoTarefa.EmExecucao => Tarefa.Progresso is { } p ? $"Em andamento: {p.Feitos} de {p.Total}" : "Em andamento",
        EstadoTarefa.Concluida => "Concluída",
        EstadoTarefa.Falhou => "Falhou",
        _ => "Cancelada",
    };

    public string Mensagem => Tarefa.Mensagem ?? Tarefa.Progresso?.Detalhe ?? "";
    public bool PodeCancelar => Tarefa.Estado is EstadoTarefa.NaFila or EstadoTarefa.EmExecucao;
    public bool Falhou => Tarefa.Estado == EstadoTarefa.Falhou;
}

/// <summary>Historico de lotes e fila de tarefas (especificacao 7.2 e 7.3).</summary>
public sealed partial class HistoricoVm : TelaVm
{
    private readonly ServicoOperacoes _operacoes;
    private readonly IDialogos _dialogos;
    private readonly IInterface _interface;
    private IReadOnlyList<Lote> _todos = [];

    public HistoricoVm(ServicoOperacoes operacoes, IDialogos dialogos, IInterface @interface)
    {
        _operacoes = operacoes;
        _dialogos = dialogos;
        _interface = @interface;
        operacoes.HistoricoMudou += () => _interface.Executar(Recarregar);
        operacoes.Fila.Mudou += _ => _interface.Executar(RecarregarTarefas);
    }

    public override string Titulo => "Histórico e tarefas";

    public ObservableCollection<LoteVm> Lotes { get; } = [];
    public ObservableCollection<TarefaVm> Tarefas { get; } = [];

    [ObservableProperty]
    private string _filtro = "";

    [ObservableProperty]
    private LoteVm? _selecionado;

    [ObservableProperty]
    private bool _soComPendencia;

    public bool TemTarefas => Tarefas.Count > 0;
    public bool TemLotes => Lotes.Count > 0;

    public override void AoEntrar()
    {
        Recarregar();
        RecarregarTarefas();
    }

    partial void OnFiltroChanged(string value) => Filtrar();

    partial void OnSoComPendenciaChanged(bool value) => Filtrar();

    [RelayCommand]
    private async Task DesfazerAsync(LoteVm lote)
    {
        var plano = _operacoes.PlanejarDesfazer(lote.Lote);
        if (!plano.PodeDesfazer)
        {
            // Nao ha reversao segura: explica o motivo em vez de tentar.
            _dialogos.Avisar("Não é possível desfazer", plano.Linhas.FirstOrDefault()?.Motivo ?? "Não há o que desfazer neste lote.");
            return;
        }

        string texto = $"{Formato.Plural(plano.Reversiveis, "arquivo volta", "arquivos voltam")} ao estado anterior.";
        if (plano.Conflitos > 0)
        {
            texto += $"\n{Formato.Plural(plano.Conflitos, "arquivo fica", "arquivos ficam")} como está: {plano.Linhas.First(l => !l.Pode).Motivo}";
        }

        if (!_dialogos.Confirmar("Desfazer: " + lote.Descricao, texto, "Desfazer"))
        {
            return;
        }

        Ocupado = true;
        try
        {
            var volta = await _operacoes.DesfazerAsync(plano);
            Resultado = $"Desfeito: {Formato.Plural(volta.Contar(EstadoItem.Concluido), "arquivo voltou", "arquivos voltaram")}.";
            ResultadoComFalha = volta.Contar(EstadoItem.Falhou) > 0;
        }
        finally
        {
            Ocupado = false;
        }
    }

    [RelayCommand]
    private async Task ContinuarAsync(LoteVm lote)
    {
        Ocupado = true;
        try
        {
            var feito = await _operacoes.ContinuarAsync(lote.Lote);
            Resultado = "Continuado: " + ServicoOperacoes.Resumo(ResultadoLote.De(feito)) + ".";
            ResultadoComFalha = feito.Contar(EstadoItem.Falhou) > 0;
        }
        finally
        {
            Ocupado = false;
        }
    }

    [RelayCommand]
    private void AbrirPasta(LoteVm lote)
    {
        if (lote.PrimeiroResultado is { } caminho)
        {
            _dialogos.MostrarNaPasta(caminho);
        }
    }

    [RelayCommand]
    private static void CancelarTarefa(TarefaVm tarefa) => tarefa.Tarefa.Cancelar();

    private void Recarregar()
    {
        _todos = _operacoes.Ultimos(300);
        Filtrar();
    }

    private void Filtrar()
    {
        Guid? selecionado = Selecionado?.Lote.Id;
        Lotes.Clear();
        string termo = Filtro.Trim();
        foreach (var lote in _todos)
        {
            if (SoComPendencia && !lote.TemPendentes && lote.Contar(EstadoItem.Falhou) == 0)
            {
                continue;
            }

            bool bate = termo.Length == 0
                        || lote.Descricao.Contains(termo, StringComparison.CurrentCultureIgnoreCase)
                        || lote.Itens.Any(i => i.Origem.Contains(termo, StringComparison.CurrentCultureIgnoreCase)
                                               || i.Destino.Contains(termo, StringComparison.CurrentCultureIgnoreCase));
            if (bate)
            {
                Lotes.Add(new LoteVm(lote));
            }
        }

        Selecionado = Lotes.FirstOrDefault(l => l.Lote.Id == selecionado);
        OnPropertyChanged(nameof(TemLotes));
    }

    private void RecarregarTarefas()
    {
        Tarefas.Clear();
        foreach (var tarefa in _operacoes.Fila.Tarefas.Reverse().Take(30))
        {
            Tarefas.Add(new TarefaVm(tarefa));
        }

        OnPropertyChanged(nameof(TemTarefas));
    }
}

public sealed record ModuloVm(string Id, string Titulo, string Descricao, string Icone);

/// <summary>Tela inicial: modulos disponiveis, ultimas acoes e estado do modo automatico.</summary>
public sealed partial class InicioVm : TelaVm
{
    private readonly ServicoOperacoes _operacoes;
    private readonly ServicoNovidades _novidades;
    private readonly ServicoPreferencias _preferencias;
    private readonly IDialogos _dialogos;
    private readonly IInterface _interface;
    private readonly Action<string> _navegar;

    public InicioVm(
        IReadOnlyList<ModuloVm> modulos, ServicoOperacoes operacoes, ServicoNovidades novidades,
        ServicoPreferencias preferencias, IDialogos dialogos, IInterface @interface, Action<string> navegar)
    {
        Modulos = modulos;
        _operacoes = operacoes;
        _novidades = novidades;
        _preferencias = preferencias;
        _dialogos = dialogos;
        _interface = @interface;
        _navegar = navegar;
        operacoes.HistoricoMudou += () => _interface.Executar(Recarregar);
        novidades.Mudou += () => _interface.Executar(AtualizarEstado);
        AtualizarEstado();
    }

    public override string Titulo => "Início";

    public IReadOnlyList<ModuloVm> Modulos { get; }
    public ObservableCollection<LoteVm> UltimasAcoes { get; } = [];

    [ObservableProperty]
    private string _estadoAutomatico = "";

    [ObservableProperty]
    private bool _automaticoLigado;

    public bool TemAcoes => UltimasAcoes.Count > 0;

    public override void AoEntrar()
    {
        Recarregar();
        AtualizarEstado();
    }

    [RelayCommand]
    private void Abrir(ModuloVm modulo) => _navegar(modulo.Id);

    [RelayCommand]
    private void AbrirOrganizar() => _navegar(OrganizarVm.Modulo);

    [RelayCommand]
    private void AbrirHistorico() => _navegar("historico");

    [RelayCommand]
    private async Task DesfazerAsync(LoteVm lote)
    {
        var plano = _operacoes.PlanejarDesfazer(lote.Lote);
        if (!plano.PodeDesfazer)
        {
            _dialogos.Avisar("Não é possível desfazer", plano.Linhas.FirstOrDefault()?.Motivo ?? "Não há o que desfazer.");
            return;
        }

        string texto = $"{Formato.Plural(plano.Reversiveis, "arquivo volta", "arquivos voltam")} ao estado anterior.";
        if (plano.Conflitos > 0)
        {
            texto += $"\n{Formato.Plural(plano.Conflitos, "arquivo fica", "arquivos ficam")} como está: {plano.Linhas.First(l => !l.Pode).Motivo}";
        }

        if (_dialogos.Confirmar("Desfazer: " + lote.Descricao, texto, "Desfazer"))
        {
            await _operacoes.DesfazerAsync(plano);
        }
    }

    [RelayCommand]
    private void AbrirPasta(LoteVm lote)
    {
        if (lote.PrimeiroResultado is { } caminho)
        {
            _dialogos.MostrarNaPasta(caminho);
        }
    }

    private void Recarregar()
    {
        UltimasAcoes.Clear();
        foreach (var lote in _operacoes.Ultimos(4))
        {
            UltimasAcoes.Add(new LoteVm(lote));
        }

        OnPropertyChanged(nameof(TemAcoes));
    }

    private void AtualizarEstado()
    {
        var p = _preferencias.Atual;
        string pasta = Path.GetFileName(p.PastaTrabalho.TrimEnd('\\', '/'));
        AutomaticoLigado = p.ModoAutomatico && _novidades.Vigiando;
        int n = _novidades.Quantidade;
        EstadoAutomatico = !p.ModoAutomatico
            ? "Modo automático desligado"
            : !_novidades.Vigiando
                ? "Modo automático ligado · pasta não encontrada"
                : n > 0
                    ? $"{Formato.Plural(n, "arquivo novo", "arquivos novos")} em {pasta}"
                    : $"Modo automático ligado · vigiando {pasta}";
    }
}

public sealed partial class ConfiguracoesVm : TelaVm
{
    private readonly ServicoPreferencias _preferencias;
    private readonly ServicoNovidades _novidades;
    private readonly IDialogos _dialogos;
    private readonly IArquivos _arquivos;
    private readonly IRelogio _relogio;
    private readonly ServicoModelos _modelos;
    private readonly Spobras.Dados.BancoApp? _banco;

    public ConfiguracoesVm(
        ServicoPreferencias preferencias, ServicoNovidades novidades, IDialogos dialogos, IArquivos arquivos, IRelogio relogio,
        string versao, string pastaDados, ServicoModelos? modelos = null, Spobras.Dados.BancoApp? banco = null)
    {
        _banco = banco;
        _modelos = modelos ?? new ServicoModelos(pastaDados);
        _preferencias = preferencias;
        _novidades = novidades;
        _dialogos = dialogos;
        _arquivos = arquivos;
        _relogio = relogio;
        Versao = versao;
        PastaDados = pastaDados;
        Ler();
    }

    public override string Titulo => "Configurações";

    public string Versao { get; }
    public string PastaDados { get; }

    [ObservableProperty]
    private string _pastaTrabalho = "";

    [ObservableProperty]
    private string _pastaPlanilhasBase = "";

    [ObservableProperty]
    private bool _modoAutomatico;

    [ObservableProperty]
    private string _estadoPastaTrabalho = "";

    [ObservableProperty]
    private string _estadoPastaBase = "";

    public override void AoEntrar() => Ler();

    partial void OnModoAutomaticoChanged(bool value)
    {
        if (value != _preferencias.Atual.ModoAutomatico)
        {
            _preferencias.Alterar(p => p with { ModoAutomatico = value });
            _novidades.Aplicar();
        }
    }

    [RelayCommand]
    private void EscolherPastaTrabalho()
    {
        if (_dialogos.EscolherPasta("Pasta de trabalho", PastaTrabalho) is { } pasta)
        {
            _preferencias.Alterar(p => p with { PastaTrabalho = pasta, NovidadesVistasAte = _relogio.Agora });
            _novidades.Aplicar();
            Ler();
        }
    }

    [RelayCommand]
    private void EscolherPastaBase()
    {
        if (_dialogos.EscolherPasta("Pasta das planilhas base (o app só lê desta pasta)", PastaPlanilhasBase) is { } pasta)
        {
            _preferencias.Alterar(p => p with { PastaPlanilhasBase = pasta });
            Ler();
        }
    }

    [RelayCommand]
    private void AbrirPastaDados() => _dialogos.AbrirPasta(PastaDados);

    public bool TemBackups => _banco is not null;

    [ObservableProperty]
    private string _estadoBackups = "";

    [ObservableProperty]
    private bool _restauracaoPendente;

    /// <summary>Copia do historico, regras, perfis, consultas e da base lida (os arquivos do usuario nao entram).</summary>
    [RelayCommand]
    private void FazerBackup()
    {
        if (_banco is null)
        {
            return;
        }

        try
        {
            string copia = _banco.FazerBackup("manual");
            Resultado = $"Cópia de segurança feita: {Path.GetFileName(copia)}.";
            ResultadoComFalha = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        {
            Resultado = $"A cópia não foi feita: {ex.Message}";
            ResultadoComFalha = true;
        }

        LerBackups();
    }

    [RelayCommand]
    private void RestaurarBackup()
    {
        if (_banco is null
            || _dialogos.EscolherArquivos("Escolha a cópia de segurança", "Cópias do Arquivos SPOBRAS|*.db", _banco.PastaBackups).FirstOrDefault() is not { } copia)
        {
            return;
        }

        if (!_dialogos.Confirmar("Voltar a uma cópia de segurança",
                $"O app volta ao estado de “{Path.GetFileNameWithoutExtension(copia)}”: histórico, regras, perfis, consultas e modelos cadastrados. "
                + "Os seus arquivos e planilhas não mudam. O estado de agora vira mais uma cópia, para você poder voltar.\n\nA troca acontece quando o app for aberto de novo.",
                "Voltar a esta cópia"))
        {
            return;
        }

        try
        {
            _banco.PedirRestauracao(copia);
            Resultado = "Pronto. Feche o app (se ele estiver na bandeja, use Sair no ícone perto do relógio) e abra de novo para concluir.";
            ResultadoComFalha = false;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        {
            _dialogos.Avisar("Cópia não aceita", ex.Message);
        }

        LerBackups();
    }

    [RelayCommand]
    private void CancelarRestauracao()
    {
        _banco?.CancelarRestauracao();
        Resultado = "A volta à cópia foi cancelada.";
        ResultadoComFalha = false;
        LerBackups();
    }

    [RelayCommand]
    private void AbrirPastaBackups()
    {
        if (_banco is not null)
        {
            Directory.CreateDirectory(_banco.PastaBackups);
            _dialogos.AbrirPasta(_banco.PastaBackups);
        }
    }

    private void LerBackups()
    {
        if (_banco is null)
        {
            return;
        }

        var copias = _banco.Backups();
        RestauracaoPendente = _banco.RestauracaoPendente;
        EstadoBackups = copias.Count == 0
            ? "Nenhuma cópia ainda. O app faz uma sozinho antes de cada atualização."
            : $"{Formato.Plural(copias.Count, "cópia guardada", "cópias guardadas")} (as {_banco.CopiasGuardadas} mais recentes ficam). Última: {Formato.DataHora(copias[0].LastWriteTime)}.";
    }

    [ObservableProperty]
    private string? _logo;

    [ObservableProperty]
    private string _estadoLogo = "";

    public bool TemLogo => Logo is not null;

    /// <summary>Cadastro central do logo: vale para os documentos gerados daqui em diante.</summary>
    [RelayCommand]
    private void EscolherLogo()
    {
        string? arquivo = _dialogos.EscolherArquivos("Escolha a imagem do logo", "Imagens|*.png;*.jpg;*.jpeg;*.bmp", PastaTrabalho).FirstOrDefault();
        if (arquivo is null)
        {
            return;
        }

        try
        {
            _modelos.DefinirLogo(arquivo);
        }
        catch (Exception ex) when (ex is Spobras.Documentos.Imagens.FotoInvalidaException or IOException or UnauthorizedAccessException)
        {
            _dialogos.Avisar("Logo", ex.Message);
        }

        Ler();
    }

    [RelayCommand]
    private void RemoverLogo()
    {
        if (_dialogos.Confirmar("Tirar o logo", "Os próximos documentos saem sem logo. Os documentos já gerados não mudam, e o seu arquivo de imagem não é apagado.", "Tirar o logo"))
        {
            _modelos.RemoverLogo();
            Ler();
        }
    }

    private void Ler()
    {
        LerBackups();
        var p = _preferencias.Atual;
        PastaTrabalho = p.PastaTrabalho;
        PastaPlanilhasBase = p.PastaPlanilhasBase;
        ModoAutomatico = p.ModoAutomatico;
        Logo = null; // forca a tela a recarregar a imagem quando o arquivo e trocado
        Logo = _modelos.Logo;
        OnPropertyChanged(nameof(TemLogo));
        EstadoLogo = Logo is null
            ? "Nenhum logo cadastrado. Os relatórios saem sem logo até você escolher a imagem."
            : "Logo cadastrado. Ele entra no cabeçalho dos relatórios gerados daqui em diante.";
        EstadoPastaTrabalho = Testar(p.PastaTrabalho, "Pasta encontrada.");
        EstadoPastaBase = string.IsNullOrWhiteSpace(p.PastaPlanilhasBase)
            ? "Ainda não escolhida. Enquanto isso, o organizador não tem pasta protegida além da pasta de dados do app."
            : Testar(p.PastaPlanilhasBase, "Pasta encontrada. O app só lê daqui: nunca move, renomeia ou altera o que está nela.");
    }

    private string Testar(string pasta, string ok) =>
        _arquivos.PastaExiste(pasta) ? ok : "Pasta não encontrada neste momento (pen drive, rede ou OneDrive fora do ar?).";
}

/// <summary>Janela principal: menu, pagina atual e barra de estado.</summary>
public sealed partial class PrincipalVm : ObservableObject
{
    private readonly Dictionary<string, TelaVm> _telas;
    private readonly ServicoNovidades _novidades;
    private readonly ServicoOperacoes _operacoes;
    private readonly IInterface _interface;

    public PrincipalVm(
        IReadOnlyList<ModuloVm> modulos, Dictionary<string, TelaVm> telas, ServicoNovidades novidades,
        ServicoOperacoes operacoes, IInterface @interface)
    {
        Modulos = modulos;
        _telas = telas;
        _novidades = novidades;
        _operacoes = operacoes;
        _interface = @interface;
        novidades.Mudou += () => _interface.Executar(AtualizarNovidades);
        operacoes.Fila.Mudou += t => _interface.Executar(() => AtualizarTarefa(t));
        AtualizarNovidades();
        Navegar("inicio");
    }

    public IReadOnlyList<ModuloVm> Modulos { get; }

    [ObservableProperty]
    private TelaVm? _paginaAtual;

    [ObservableProperty]
    private string _idAtual = "";

    [ObservableProperty]
    private int _quantidadeNovidades;

    [ObservableProperty]
    private string _textoAutomatico = "";

    [ObservableProperty]
    private string _detalheAutomatico = "";

    [ObservableProperty]
    private string _tarefaAtiva = "";

    public bool TemTarefaAtiva => TarefaAtiva.Length > 0;
    public bool TemNovidades => QuantidadeNovidades > 0;

    [RelayCommand]
    public void Navegar(string id)
    {
        if (!_telas.TryGetValue(id, out var tela))
        {
            return;
        }

        IdAtual = id;
        PaginaAtual = tela;
        tela.AoEntrar();
    }

    [RelayCommand]
    private void CancelarTarefaAtiva() =>
        _operacoes.Fila.Tarefas.FirstOrDefault(t => t.Estado == EstadoTarefa.EmExecucao)?.Cancelar();

    private void AtualizarNovidades()
    {
        QuantidadeNovidades = _novidades.Quantidade;
        OnPropertyChanged(nameof(TemNovidades));
        TextoAutomatico = _novidades.Vigiando ? "Modo automático: ligado" : "Modo automático: desligado";
        DetalheAutomatico = _novidades.Vigiando
            ? QuantidadeNovidades > 0 ? Formato.Plural(QuantidadeNovidades, "arquivo novo", "arquivos novos") : "Vigiando a pasta de trabalho"
            : "Nada acontece sozinho";
    }

    private void AtualizarTarefa(Tarefa tarefa)
    {
        var ativa = _operacoes.Fila.Tarefas.FirstOrDefault(t => t.Estado == EstadoTarefa.EmExecucao);
        TarefaAtiva = ativa is null
            ? ""
            : ativa.Progresso is { } p ? $"{ativa.Titulo} ({p.Feitos} de {p.Total})" : ativa.Titulo;
        OnPropertyChanged(nameof(TemTarefaAtiva));
    }
}
