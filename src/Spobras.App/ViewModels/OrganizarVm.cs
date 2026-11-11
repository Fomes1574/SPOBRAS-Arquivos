using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Spobras.App.Servicos;
using Spobras.Core;
using Spobras.Core.Abstracoes;
using Spobras.Core.Operacoes;
using Spobras.Core.Organizar;

namespace Spobras.App.ViewModels;

public sealed partial class LinhaOrganizarVm(Sugestao sugestao) : ObservableObject
{
    [ObservableProperty]
    private Sugestao _sugestao = sugestao;

    [ObservableProperty]
    private bool _selecionado = sugestao.PodeMover;

    /// <summary>O usuario escolheu a pasta na mao: o app pode guardar a escolha, se ele pedir.</summary>
    [ObservableProperty]
    private bool _podeLembrar;

    public string Caminho => Sugestao.Caminho;
    public string Nome => Path.GetFileName(Sugestao.Caminho);
    public string Tipo => Sugestao.Tipo;
    public string Achou => Sugestao.Achou;
    public bool VoceDecide => !Sugestao.PodeMover;
    public string Destino => Sugestao.PodeMover ? Path.GetFileName(Sugestao.PastaDestino!.TrimEnd('\\', '/')) : "Você decide";
    public string DestinoCompleto => Sugestao.PastaDestino ?? "";
    public string Motivo => Sugestao.Motivo;
    public string TextoLembrar => $"Lembrar para arquivos {Path.GetExtension(Nome).ToLowerInvariant()}";

    partial void OnSugestaoChanged(Sugestao value)
    {
        OnPropertyChanged(nameof(Achou));
        OnPropertyChanged(nameof(VoceDecide));
        OnPropertyChanged(nameof(Destino));
        OnPropertyChanged(nameof(DestinoCompleto));
        OnPropertyChanged(nameof(Motivo));
    }
}

public sealed partial class RegraVm : ObservableObject
{
    public Guid Id { get; init; } = Guid.NewGuid();

    [ObservableProperty]
    private string _nome = "";

    [ObservableProperty]
    private string _nomeContem = "";

    [ObservableProperty]
    private string _extensoes = "";

    [ObservableProperty]
    private string _destino = "";

    [ObservableProperty]
    private string _prioridade = "0";

    [ObservableProperty]
    private bool _ativa = true;

    public static RegraVm De(Regra r) => new()
    {
        Id = r.Id,
        Nome = r.Nome,
        NomeContem = string.Join(", ", r.NomeContem),
        Extensoes = string.Join(", ", r.Extensoes),
        Destino = r.Destino,
        Prioridade = r.Prioridade.ToString(),
        Ativa = r.Ativa,
    };

    public Regra ParaRegra() => new()
    {
        Id = Id,
        Nome = string.IsNullOrWhiteSpace(Nome) ? "Regra sem nome" : Nome.Trim(),
        NomeContem = Lista(NomeContem),
        Extensoes = [.. Lista(Extensoes).Select(e => e.TrimStart('.', '*'))],
        Destino = Destino.Trim(),
        Prioridade = int.TryParse(Prioridade, out int p) ? p : 0,
        Ativa = Ativa,
    };

    private static List<string> Lista(string texto) =>
        [.. texto.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
}

public sealed partial class OrganizarVm : TelaVm
{
    public const string Modulo = "organizar";

    private readonly IArquivos _arquivos;
    private readonly IDialogos _dialogos;
    private readonly IInterface _interface;
    private readonly ServicoOperacoes _operacoes;
    private readonly ServicoNovidades _novidades;
    private readonly ServicoPreferencias _preferencias;
    private readonly IRelogio _relogio;
    private bool _pastaToda;
    private Lote? _ultimoLote;

    public OrganizarVm(
        IArquivos arquivos, IDialogos dialogos, IInterface @interface, ServicoOperacoes operacoes,
        ServicoNovidades novidades, ServicoPreferencias preferencias, IRelogio relogio)
    {
        _arquivos = arquivos;
        _dialogos = dialogos;
        _interface = @interface;
        _operacoes = operacoes;
        _novidades = novidades;
        _preferencias = preferencias;
        _relogio = relogio;
        _novidades.Mudou += () => _interface.Executar(() =>
        {
            AtualizarEstado();
            if (!_pastaToda && !Ocupado)
            {
                Recarregar();
            }
        });
        CarregarRegras();
        AtualizarEstado();
    }

    public override string Titulo => "Organizar arquivos";

    public ObservableCollection<LinhaOrganizarVm> Linhas { get; } = [];
    public ObservableCollection<RegraVm> Regras { get; } = [];
    public ObservableCollection<AvisoVm> AvisosRegras { get; } = [];

    [ObservableProperty]
    private bool _modoAutomatico;

    [ObservableProperty]
    private string _textoModoAutomatico = "";

    [ObservableProperty]
    private string _pastaVigiada = "";

    [ObservableProperty]
    private string _tituloLista = "";

    [ObservableProperty]
    private string _textoVazio = "";

    [ObservableProperty]
    private string _textoBotaoMover = "Mover sugeridos";

    [ObservableProperty]
    private bool _regrasAbertas;

    [ObservableProperty]
    private bool _podeDesfazerUltima;

    public bool TemLinhas => Linhas.Count > 0;
    public bool TemVoceDecide => Linhas.Any(l => l.VoceDecide);

    public override void AoEntrar()
    {
        _pastaToda = false;
        Recarregar();
    }

    [RelayCommand]
    private void AlternarModoAutomatico()
    {
        _preferencias.Alterar(p => p with { ModoAutomatico = !p.ModoAutomatico });
        _novidades.Aplicar();
    }

    [RelayCommand]
    private void EscolherPastaVigiada()
    {
        if (_dialogos.EscolherPasta("Escolha a pasta de trabalho (a que o app vigia)", _preferencias.Atual.PastaTrabalho) is { } pasta)
        {
            _preferencias.Alterar(p => p with { PastaTrabalho = pasta, NovidadesVistasAte = _relogio.Agora });
            _novidades.Aplicar();
            Recarregar();
        }
    }

    /// <summary>Olha todos os arquivos da pasta, nao so os que acabaram de chegar.</summary>
    [RelayCommand]
    private void ProcurarDeNovo()
    {
        _pastaToda = true;
        Resultado = "";
        Recarregar();
    }

    [RelayCommand]
    private void SoNovidades()
    {
        _pastaToda = false;
        Recarregar();
    }

    [RelayCommand]
    private void EscolherDestino(LinhaOrganizarVm linha)
    {
        if (_dialogos.EscolherPasta($"Para onde vai \"{linha.Nome}\"?", _preferencias.DestinoDe(Modulo)) is not { } pasta)
        {
            return;
        }

        _preferencias.LembrarDestino(Modulo, pasta);
        linha.Sugestao = _novidades.Planejador.ComDestinoEscolhido(linha.Sugestao, pasta);
        linha.Selecionado = true;
        linha.PodeLembrar = Path.GetExtension(linha.Nome).Length > 1;
        AtualizarBotao();
    }

    /// <summary>Uma pasta so para todos os "Você decide" (escolha em lote).</summary>
    [RelayCommand]
    private void EscolherDestinoDosIndecisos()
    {
        var indecisos = Linhas.Where(l => l.VoceDecide).ToList();
        if (indecisos.Count == 0
            || _dialogos.EscolherPasta($"Para onde vão os {indecisos.Count} arquivos sem sugestão?", _preferencias.DestinoDe(Modulo)) is not { } pasta)
        {
            return;
        }

        _preferencias.LembrarDestino(Modulo, pasta);
        foreach (var linha in indecisos)
        {
            linha.Sugestao = _novidades.Planejador.ComDestinoEscolhido(linha.Sugestao, pasta);
            linha.Selecionado = true;
        }

        AtualizarBotao();
    }

    /// <summary>So cria regra quando o usuario pede, e com a condicao visivel (especificacao 4.1).</summary>
    [RelayCommand]
    private void LembrarEscolha(LinhaOrganizarVm linha)
    {
        string extensao = Path.GetExtension(linha.Nome).TrimStart('.').ToLowerInvariant();
        string pasta = linha.DestinoCompleto;
        if (!_dialogos.Confirmar("Lembrar esta escolha",
                $"Criar a regra: arquivos .{extensao} vão para\n{pasta}?\n\nO app só vai sugerir; mover continua dependendo da sua confirmação. A regra pode ser editada ou desligada em Regras.",
                "Criar regra"))
        {
            return;
        }

        var regras = _novidades.Regras.ToList();
        regras.Add(new Regra { Nome = $"Arquivos .{extensao}", Extensoes = [extensao], Destino = pasta });
        _novidades.GravarRegras(regras);
        CarregarRegras();
        linha.PodeLembrar = false;
    }

    [RelayCommand]
    private async Task MoverSugeridosAsync()
    {
        var confirmadas = Linhas.Where(l => l.Selecionado && l.Sugestao.PodeMover).Select(l => l.Sugestao).ToList();
        if (confirmadas.Count == 0)
        {
            return;
        }

        var lote = PlanejadorOrganizacao.CriarLote(confirmadas, _relogio);
        Ocupado = true;
        try
        {
            await _operacoes.ExecutarAsync(lote);
        }
        finally
        {
            Ocupado = false;
        }

        _ultimoLote = lote;
        PodeDesfazerUltima = lote.Contar(EstadoItem.Concluido) > 0;
        int falhas = lote.Contar(EstadoItem.Falhou);
        ResultadoComFalha = falhas > 0;
        Resultado = Formato.Plural(lote.Contar(EstadoItem.Concluido), "arquivo movido", "arquivos movidos")
                    + (falhas > 0 ? $"; {Formato.Plural(falhas, "falhou", "falharam")}: {lote.Itens.First(i => i.Estado == EstadoItem.Falhou).Mensagem}" : ".");
        Recarregar();
    }

    [RelayCommand]
    private async Task DesfazerUltimaAsync()
    {
        if (_ultimoLote is null)
        {
            return;
        }

        var plano = _operacoes.PlanejarDesfazer(_ultimoLote);
        if (!plano.PodeDesfazer)
        {
            _dialogos.Avisar("Desfazer", plano.Linhas.FirstOrDefault()?.Motivo ?? "Não há o que desfazer.");
            return;
        }

        if (plano.Conflitos > 0 && !_dialogos.Confirmar("Desfazer",
                $"{Formato.Plural(plano.Reversiveis, "arquivo volta", "arquivos voltam")} para a pasta de origem.\n" +
                $"{Formato.Plural(plano.Conflitos, "arquivo fica", "arquivos ficam")} onde está: {plano.Linhas.First(l => !l.Pode).Motivo}",
                "Desfazer os possíveis"))
        {
            return;
        }

        Ocupado = true;
        try
        {
            var volta = await _operacoes.DesfazerAsync(plano);
            Resultado = $"Desfeito: {Formato.Plural(volta.Contar(EstadoItem.Concluido), "arquivo voltou", "arquivos voltaram")} para a pasta de origem.";
            ResultadoComFalha = false;
            PodeDesfazerUltima = false;
        }
        finally
        {
            Ocupado = false;
        }

        Recarregar();
    }

    /// <summary>Tira os arquivos da lista de novidades sem mexer neles.</summary>
    [RelayCommand]
    private void DispensarNovidades()
    {
        _novidades.MarcarComoVistas();
        _pastaToda = false;
        Recarregar();
    }

    // ---- Regras ----

    [RelayCommand]
    private void NovaRegra()
    {
        Regras.Add(new RegraVm { Nome = "Nova regra" });
        RegrasAbertas = true;
    }

    [RelayCommand]
    private void RemoverRegra(RegraVm regra) => Regras.Remove(regra);

    [RelayCommand]
    private void EscolherDestinoDaRegra(RegraVm regra)
    {
        if (_dialogos.EscolherPasta("Pasta de destino da regra", string.IsNullOrWhiteSpace(regra.Destino) ? _preferencias.Atual.PastaTrabalho : regra.Destino) is { } pasta)
        {
            regra.Destino = pasta;
        }
    }

    [RelayCommand]
    private void SalvarRegras()
    {
        var regras = Regras.Select(r => r.ParaRegra()).ToList();
        var avisos = PlanejadorOrganizacao.ValidarRegras(regras, [_preferencias.Atual.PastaTrabalho]);
        AvisosRegras.Clear();
        foreach (var aviso in avisos)
        {
            AvisosRegras.Add(new AvisoVm(aviso));
        }

        if (avisos.Any(a => a.Gravidade == Gravidade.ImpedeGerar))
        {
            return; // nada e gravado enquanto houver regra que impede
        }

        _novidades.GravarRegras(regras);
        AvisosRegras.Add(new AvisoVm(Gravidade.Informacao, $"{Formato.Plural(regras.Count, "regra salva", "regras salvas")}."));
        Recarregar();
    }

    private void CarregarRegras()
    {
        Regras.Clear();
        foreach (var regra in _novidades.Regras)
        {
            Regras.Add(RegraVm.De(regra));
        }
    }

    private void Recarregar()
    {
        string pasta = _preferencias.Atual.PastaTrabalho;
        var sugestoes = _pastaToda ? _novidades.SugestoesDaPasta(pasta) : _novidades.SugestoesDasNovidades();

        Linhas.Clear();
        // Sugeridos primeiro; "Você decide" agrupados no fim.
        foreach (var s in sugestoes.OrderBy(s => !s.PodeMover).ThenBy(s => Path.GetFileName(s.Caminho), Spobras.Core.Nomes.OrdemNatural.Instancia))
        {
            var linha = new LinhaOrganizarVm(s);
            linha.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(LinhaOrganizarVm.Selecionado) or nameof(LinhaOrganizarVm.Sugestao))
                {
                    AtualizarBotao();
                }
            };
            Linhas.Add(linha);
        }

        string nomePasta = Path.GetFileName(pasta.TrimEnd('\\', '/'));
        TituloLista = _pastaToda ? $"Todos os arquivos de {nomePasta}" : $"Arquivos novos em {nomePasta}";
        TextoVazio = _pastaToda
            ? "A pasta não tem arquivos para organizar."
            : "Nenhum arquivo novo. Use \"Procurar de novo\" para ver todos os arquivos da pasta.";
        OnPropertyChanged(nameof(TemLinhas));
        AtualizarBotao();
    }

    private void AtualizarBotao()
    {
        int n = Linhas.Count(l => l.Selecionado && l.Sugestao.PodeMover);
        TextoBotaoMover = n > 0 ? $"Mover {Formato.Plural(n, "arquivo", "arquivos")}" : "Mover sugeridos";
        OnPropertyChanged(nameof(TemVoceDecide));
        MoverSugeridosCommand.NotifyCanExecuteChanged();
    }

    private void AtualizarEstado()
    {
        var p = _preferencias.Atual;
        ModoAutomatico = p.ModoAutomatico;
        PastaVigiada = p.PastaTrabalho;
        TextoModoAutomatico = !p.ModoAutomatico
            ? "Nada acontece sozinho. Use o botão Procurar de novo."
            : _novidades.Vigiando
                ? "Avisa perto do relógio quando chega arquivo novo. Nada é movido sem a sua confirmação."
                : "Ligado, mas a pasta não foi encontrada. Escolha outra pasta.";
    }
}
