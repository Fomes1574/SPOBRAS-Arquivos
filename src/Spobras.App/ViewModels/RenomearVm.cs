using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Spobras.App.Servicos;
using Spobras.Core.Abstracoes;
using Spobras.Core.Operacoes;
using Spobras.Core.Renomear;
using Spobras.Dados.Configuracao;

namespace Spobras.App.ViewModels;

public sealed partial class LinhaRenomearVm(string caminho, Action aoEditar) : ObservableObject
{
    private bool _atualizando;

    public string Caminho { get; } = caminho;
    public string NomeAtual => Path.GetFileName(Caminho);

    /// <summary>Nome digitado pelo usuario nesta linha (passa na frente do calculado).</summary>
    public string? Editado { get; private set; }

    [ObservableProperty]
    private string _nomeNovo = "";

    [ObservableProperty]
    private string _complemento = "";

    [ObservableProperty]
    private string _situacao = "";

    [ObservableProperty]
    private string _motivo = "";

    [ObservableProperty]
    private bool _impedido;

    [ObservableProperty]
    private bool _ajustado;

    public bool FoiEditado => Editado is not null;

    partial void OnNomeNovoChanged(string value)
    {
        if (_atualizando)
        {
            return;
        }

        Editado = value;
        OnPropertyChanged(nameof(FoiEditado));
        aoEditar();
    }

    partial void OnComplementoChanged(string value)
    {
        if (!_atualizando)
        {
            aoEditar();
        }
    }

    public void LimparEdicao()
    {
        Editado = null;
        OnPropertyChanged(nameof(FoiEditado));
    }

    public void DefinirEditado(string nome)
    {
        Editado = nome;
        OnPropertyChanged(nameof(FoiEditado));
    }

    public void Mostrar(LinhaRenomear linha)
    {
        _atualizando = true;
        NomeNovo = linha.NomeNovo;
        Impedido = linha.Situacao == SituacaoRenomear.Impedido;
        Ajustado = linha.Situacao == SituacaoRenomear.Ajustado;
        Situacao = linha.Situacao switch
        {
            SituacaoRenomear.Ok => "Pronto",
            SituacaoRenomear.Ajustado => "Ajustado",
            SituacaoRenomear.SemMudanca => "Sem mudança",
            _ => "Impedido",
        };
        Motivo = linha.Motivo ?? "";
        _atualizando = false;
    }
}

public sealed partial class RenomearVm : TelaVm
{
    public const string Modulo = "renomear";

    private readonly IArquivos _arquivos;
    private readonly IDialogos _dialogos;
    private readonly ServicoOperacoes _operacoes;
    private readonly ServicoPreferencias _preferencias;
    private readonly PerfilRepositorio _perfis;
    private readonly IRelogio _relogio;
    private readonly PlanejadorRenomear _planejador;
    private PlanoRenomear? _plano;
    private Lote? _ultimoLote;
    private bool _aplicandoPerfil;

    public RenomearVm(
        IArquivos arquivos, IDialogos dialogos, ServicoOperacoes operacoes, ServicoPreferencias preferencias,
        PerfilRepositorio perfis, IRelogio relogio)
    {
        _arquivos = arquivos;
        _dialogos = dialogos;
        _operacoes = operacoes;
        _preferencias = preferencias;
        _perfis = perfis;
        _relogio = relogio;
        _planejador = new PlanejadorRenomear(arquivos);
        RecarregarPerfis();
    }

    public override string Titulo => "Renomear em lote";

    public ObservableCollection<LinhaRenomearVm> Linhas { get; } = [];
    public ObservableCollection<AvisoVm> Avisos { get; } = [];
    public ObservableCollection<PerfilGuardado> Perfis { get; } = [];

    [ObservableProperty]
    private ModoRenomear _modo = ModoRenomear.NomeNumero;

    [ObservableProperty]
    private string _nomeBase = "";

    [ObservableProperty]
    private string _separador = " ";

    [ObservableProperty]
    private bool _numerar = true;

    [ObservableProperty]
    private string _numeroInicial = "1";

    [ObservableProperty]
    private bool _zeroEsquerda = true;

    [ObservableProperty]
    private bool _continuarNumeracao;

    [ObservableProperty]
    private OrdemRenomear _ordem = OrdemRenomear.NomeNatural;

    [ObservableProperty]
    private PosicaoData _posicaoData = PosicaoData.Nenhuma;

    [ObservableProperty]
    private string _substituirDe = "";

    [ObservableProperty]
    private string _substituirPor = "";

    [ObservableProperty]
    private bool _removerEspacosExcedentes;

    [ObservableProperty]
    private bool _removerAcentos;

    [ObservableProperty]
    private PerfilGuardado? _perfilSelecionado;

    [ObservableProperty]
    private string _textoEntrada = "Nenhum arquivo escolhido";

    [ObservableProperty]
    private string _pastaEntrada = "Arraste arquivos ou uma pasta para cá";

    [ObservableProperty]
    private string _textoBotao = "Renomear";

    [ObservableProperty]
    private string _contagem = "";

    [ObservableProperty]
    private bool _podeDesfazerUltima;

    public bool ModoNomeNumero => Modo == ModoRenomear.NomeNumero;
    public bool ModoComplemento => Modo == ModoRenomear.NomeComplemento;
    public bool ModoLivre => Modo == ModoRenomear.NomesLivres;
    public bool UsaNomeBase => Modo != ModoRenomear.NomesLivres;
    public bool TemLinhas => Linhas.Count > 0;

    private OpcoesRenomear Opcoes => new()
    {
        Modo = Modo,
        NomeBase = NomeBase.Trim(),
        Separador = Separador,
        Numerar = Numerar,
        NumeroInicial = int.TryParse(NumeroInicial, out int n) ? n : 1,
        ZeroEsquerda = ZeroEsquerda,
        ContinuarNumeracao = ContinuarNumeracao,
        Ordem = Ordem,
        PosicaoData = PosicaoData,
        SubstituirDe = string.IsNullOrEmpty(SubstituirDe) ? null : SubstituirDe,
        SubstituirPor = SubstituirPor,
        RemoverEspacosExcedentes = RemoverEspacosExcedentes,
        RemoverAcentos = RemoverAcentos,
    };

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (_aplicandoPerfil)
        {
            return;
        }

        switch (e.PropertyName)
        {
            case nameof(Modo):
                // Trocar de modo descarta os nomes digitados na previa: eles pertenciam ao modo anterior.
                foreach (var linha in Linhas)
                {
                    linha.LimparEdicao();
                }

                OnPropertyChanged(nameof(ModoNomeNumero));
                OnPropertyChanged(nameof(ModoComplemento));
                OnPropertyChanged(nameof(ModoLivre));
                OnPropertyChanged(nameof(UsaNomeBase));
                Replanejar();
                break;
            case nameof(NomeBase) or nameof(Separador) or nameof(Numerar) or nameof(NumeroInicial) or nameof(ZeroEsquerda)
                or nameof(ContinuarNumeracao) or nameof(Ordem) or nameof(PosicaoData) or nameof(SubstituirDe)
                or nameof(SubstituirPor) or nameof(RemoverEspacosExcedentes) or nameof(RemoverAcentos):
                Replanejar();
                break;
            case nameof(PerfilSelecionado) when PerfilSelecionado is not null:
                AplicarPerfil(PerfilSelecionado);
                break;
        }
    }

    [RelayCommand]
    private void EscolherArquivos() =>
        Adicionar(_dialogos.EscolherArquivos("Escolha os arquivos para renomear", null, _preferencias.Atual.PastaTrabalho));

    [RelayCommand]
    private void EscolherPasta()
    {
        if (_dialogos.EscolherPasta("Escolha a pasta com os arquivos", _preferencias.Atual.PastaTrabalho) is { } pasta)
        {
            Adicionar([pasta]);
        }
    }

    /// <summary>Entrada por botao ou por arraste. Pasta entra com os arquivos que estao diretamente nela.</summary>
    public void Adicionar(IEnumerable<string> caminhos)
    {
        var existentes = new HashSet<string>(Linhas.Select(l => l.Caminho), StringComparer.OrdinalIgnoreCase);
        foreach (string caminho in caminhos)
        {
            var arquivos = _arquivos.PastaExiste(caminho)
                ? _arquivos.ListarArquivos(caminho).Select(n => Path.Combine(caminho, n))
                : [caminho];
            foreach (string arquivo in arquivos)
            {
                if (_arquivos.ArquivoExiste(arquivo) && !Spobras.Core.Organizar.TipoArquivo.Temporario(arquivo) && existentes.Add(arquivo))
                {
                    Linhas.Add(new LinhaRenomearVm(arquivo, Replanejar));
                }
            }
        }

        Resultado = "";
        Replanejar();
    }

    [RelayCommand]
    private void Limpar()
    {
        Linhas.Clear();
        Replanejar();
    }

    [RelayCommand]
    private void Remover(LinhaRenomearVm linha)
    {
        Linhas.Remove(linha);
        Replanejar();
    }

    [RelayCommand]
    private void Subir(LinhaRenomearVm linha) => Mover(linha, -1);

    [RelayCommand]
    private void Descer(LinhaRenomearVm linha) => Mover(linha, +1);

    [RelayCommand]
    private void VoltarAoSugerido(LinhaRenomearVm linha)
    {
        linha.LimparEdicao();
        Replanejar();
    }

    /// <summary>Modo nomes livres: cola uma lista (um nome por linha) nos arquivos, na ordem da previa.</summary>
    [RelayCommand]
    private void ColarNomes()
    {
        var nomes = (_dialogos.LerAreaDeTransferencia() ?? "")
            .Split('\n')
            .Select(n => n.Trim().Trim('\r'))
            .Where(n => n.Length > 0)
            .ToList();
        if (nomes.Count == 0)
        {
            _dialogos.Avisar("Colar lista de nomes", "Não há texto copiado. Copie os nomes (um por linha) e tente de novo.");
            return;
        }

        for (int i = 0; i < Math.Min(nomes.Count, Linhas.Count); i++)
        {
            Linhas[i].DefinirEditado(nomes[i]);
        }

        Replanejar();
        if (nomes.Count != Linhas.Count)
        {
            Avisos.Add(new AvisoVm(Spobras.Core.Gravidade.PrecisaConferir,
                $"A lista colada tem {Formato.Plural(nomes.Count, "nome", "nomes")} e há {Formato.Plural(Linhas.Count, "arquivo", "arquivos")}. Confira a prévia."));
        }
    }

    [RelayCommand]
    private async Task RenomearAsync()
    {
        if (_plano is null || !_plano.PodeExecutar)
        {
            return;
        }

        var lote = LoteRenomear.Criar(_plano, _relogio);
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
        Resultado = $"{Formato.Plural(lote.Contar(EstadoItem.Concluido), "arquivo renomeado", "arquivos renomeados")}"
                    + (falhas > 0 ? $"; {Formato.Plural(falhas, "falhou", "falharam")}: {lote.Itens.First(i => i.Estado == EstadoItem.Falhou).Mensagem}" : ".")
                    + (lote.TemPendentes ? " O restante ficou pendente; continue pelo Histórico." : "");

        // Um novo trabalho comeca com a lista vazia, sem misturar com o lote anterior.
        Linhas.Clear();
        Replanejar();
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
                $"{Formato.Plural(plano.Reversiveis, "arquivo volta", "arquivos voltam")} ao nome anterior.\n" +
                $"{Formato.Plural(plano.Conflitos, "arquivo fica", "arquivos ficam")} como está: {plano.Linhas.First(l => !l.Pode).Motivo}",
                "Desfazer os possíveis"))
        {
            return;
        }

        Ocupado = true;
        try
        {
            var volta = await _operacoes.DesfazerAsync(plano);
            Resultado = $"Desfeito: {Formato.Plural(volta.Contar(EstadoItem.Concluido), "arquivo voltou", "arquivos voltaram")} ao nome anterior.";
            ResultadoComFalha = false;
            PodeDesfazerUltima = false;
        }
        finally
        {
            Ocupado = false;
        }
    }

    [RelayCommand]
    private void SalvarPerfil()
    {
        string? nome = _dialogos.PedirTexto("Salvar estas opções", "Nome do perfil:", PerfilSelecionado?.Nome ?? NomeBase);
        if (string.IsNullOrWhiteSpace(nome))
        {
            return;
        }

        var salvo = _perfis.Salvar(Modulo, nome, Opcoes);
        RecarregarPerfis();
        _aplicandoPerfil = true;
        PerfilSelecionado = Perfis.FirstOrDefault(p => p.Id == salvo.Id);
        _aplicandoPerfil = false;
    }

    [RelayCommand]
    private void RemoverPerfil()
    {
        if (PerfilSelecionado is { } perfil && _dialogos.Confirmar("Remover perfil", $"Remover o perfil \"{perfil.Nome}\"?", "Remover"))
        {
            _perfis.Remover(perfil.Id);
            RecarregarPerfis();
        }
    }

    private void RecarregarPerfis()
    {
        Perfis.Clear();
        foreach (var perfil in _perfis.Listar(Modulo))
        {
            Perfis.Add(perfil);
        }
    }

    private void AplicarPerfil(PerfilGuardado perfil)
    {
        if (PerfilRepositorio.Abrir<OpcoesRenomear>(perfil) is not { } o)
        {
            return;
        }

        _aplicandoPerfil = true;
        Modo = o.Modo;
        NomeBase = o.NomeBase;
        Separador = o.Separador;
        Numerar = o.Numerar;
        NumeroInicial = o.NumeroInicial.ToString();
        ZeroEsquerda = o.ZeroEsquerda;
        ContinuarNumeracao = o.ContinuarNumeracao;
        Ordem = o.Ordem;
        PosicaoData = o.PosicaoData;
        SubstituirDe = o.SubstituirDe ?? "";
        SubstituirPor = o.SubstituirPor;
        RemoverEspacosExcedentes = o.RemoverEspacosExcedentes;
        RemoverAcentos = o.RemoverAcentos;
        _aplicandoPerfil = false;
        OnPropertyChanged(nameof(ModoNomeNumero));
        OnPropertyChanged(nameof(ModoComplemento));
        OnPropertyChanged(nameof(ModoLivre));
        OnPropertyChanged(nameof(UsaNomeBase));
        Replanejar();
    }

    private void Mover(LinhaRenomearVm linha, int passo)
    {
        int de = Linhas.IndexOf(linha);
        int para = de + passo;
        if (de < 0 || para < 0 || para >= Linhas.Count)
        {
            return;
        }

        // Mexer na ordem a mao passa a valer como "na ordem em que eu arrastar".
        _aplicandoPerfil = true;
        Ordem = OrdemRenomear.Manual;
        _aplicandoPerfil = false;
        Linhas.Move(de, para);
        Replanejar();
    }

    private void Replanejar()
    {
        Avisos.Clear();
        OnPropertyChanged(nameof(TemLinhas));
        if (Linhas.Count == 0)
        {
            _plano = null;
            TextoEntrada = "Nenhum arquivo escolhido";
            PastaEntrada = "Arraste arquivos ou uma pasta para cá";
            TextoBotao = "Renomear";
            Contagem = "";
            RenomearCommand.NotifyCanExecuteChanged();
            return;
        }

        var itens = Linhas.Select(l => new ItemRenomear(l.Caminho)
        {
            Data = _arquivos.Info(l.Caminho)?.ModificadoEm,
            Complemento = l.Complemento,
            NomeEditado = l.Editado,
        }).ToList();

        _plano = _planejador.Planejar(itens, Opcoes);

        // A previa aparece na ordem em que os arquivos serao numerados.
        var porCaminho = Linhas.ToDictionary(l => l.Caminho, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < _plano.Linhas.Count; i++)
        {
            var vm = porCaminho[_plano.Linhas[i].Caminho];
            int atual = Linhas.IndexOf(vm);
            if (atual != i)
            {
                Linhas.Move(atual, i);
            }

            vm.Mostrar(_plano.Linhas[i]);
        }

        foreach (var aviso in _plano.Avisos)
        {
            Avisos.Add(new AvisoVm(aviso));
        }

        var pastas = Linhas.Select(l => Path.GetDirectoryName(l.Caminho)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        TextoEntrada = Formato.Plural(Linhas.Count, "arquivo escolhido", "arquivos escolhidos");
        PastaEntrada = pastas.Count == 1 ? $"De: {pastas[0]}" : $"De {pastas.Count} pastas diferentes";
        TextoBotao = _plano.Executaveis > 0 ? $"Renomear {Formato.Plural(_plano.Executaveis, "arquivo", "arquivos")}" : "Renomear";
        int semMudanca = _plano.Linhas.Count(l => l.Situacao == SituacaoRenomear.SemMudanca);
        Contagem = string.Join(" · ", new[]
        {
            Formato.Plural(_plano.Executaveis, "válido", "válidos"),
            _plano.Impedidos > 0 ? Formato.Plural(_plano.Impedidos, "impedido", "impedidos") : null,
            semMudanca > 0 ? $"{semMudanca} sem mudança" : null,
        }.Where(t => t is not null));
        RenomearCommand.NotifyCanExecuteChanged();
    }
}
