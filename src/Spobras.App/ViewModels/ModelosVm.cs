using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Spobras.App.Servicos;
using Spobras.Core;
using Spobras.Core.Abstracoes;
using Spobras.Core.Base;
using Spobras.Core.Modelos;
using Spobras.Core.Nomes;
using Spobras.Core.Tarefas;
using Spobras.Dados.Configuracao;
using Spobras.Documentos.Modelos;
using Spobras.Documentos.Planilhas;

namespace Spobras.App.ViewModels;

public sealed class ModeloItemVm(ModeloExcel modelo)
{
    public ModeloExcel Modelo { get; } = modelo;
    public string Nome => Modelo.Nome;
    public string Detalhe => $"{Formato.Plural(Modelo.Campos.Count, "campo", "campos")} · {Path.GetExtension(Modelo.Arquivo)}";
}

/// <summary>Um valor a preencher: o campo do modelo e o que o usuario digitou.</summary>
public sealed partial class CelulaModeloVm(CampoModelo campo, Action aoMudar) : ObservableObject
{
    public CampoModelo Campo { get; } = campo;
    public string Rotulo => Campo.Nome;

    [ObservableProperty]
    private string _valor = "";

    partial void OnValorChanged(string value) => aoMudar();
}

/// <summary>Uma linha da lista: cada linha gera um arquivo.</summary>
public sealed partial class LinhaModeloVm : ObservableObject
{
    public ObservableCollection<CelulaModeloVm> Celulas { get; } = [];

    [ObservableProperty]
    private string _numero = "";

    [ObservableProperty]
    private string _avisos = "";

    [ObservableProperty]
    private bool _impedida;
}

/// <summary>Um campo no cadastro do modelo.</summary>
public sealed partial class CampoEdicaoVm(Action aoMudar) : ObservableObject
{
    public static readonly IReadOnlyList<string> Tipos = ["Texto", "Número", "Data"];

    [ObservableProperty]
    private string _nome = "";

    [ObservableProperty]
    private string? _aba;

    [ObservableProperty]
    private string _celula = "";

    [ObservableProperty]
    private string _tipo = Tipos[0];

    [ObservableProperty]
    private string _origem = OrigensCampo.Todas[0].Rotulo;

    [ObservableProperty]
    private bool _obrigatorio;

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        aoMudar();
    }

    public CampoModelo ParaCampo() => new()
    {
        Nome = Nome.Trim(),
        Aba = Aba ?? "",
        Celula = ValidadorModelo.Celula(Celula) ?? Celula.Trim(),
        Tipo = (TipoCampo)Math.Max(0, Tipos.ToList().IndexOf(Tipo)),
        Obrigatorio = Obrigatorio,
        Origem = OrigensCampo.Todas.FirstOrDefault(o => o.Rotulo == Origem).Chave ?? "",
    };

    public void Carregar(CampoModelo campo)
    {
        Nome = campo.Nome;
        Aba = campo.Aba;
        Celula = campo.Celula;
        Tipo = Tipos[(int)campo.Tipo];
        Origem = OrigensCampo.Rotulo(campo.Origem);
        Obrigatorio = campo.Obrigatorio;
    }
}

/// <summary>
/// Modelos Excel: cadastro (arquivo + campo → celula) e preenchimento de um ou varios arquivos.
/// Cada linha gera uma copia preenchida; o modelo nao e alterado e formulas nao sao sobrescritas.
/// </summary>
public sealed partial class ModelosVm : TelaVm
{
    public const string Modulo = "modelos";
    private const string FiltroExcel = "Planilhas do Excel|*.xlsx;*.xlsm";

    private readonly IDialogos _dialogos;
    private readonly ServicoOperacoes _operacoes;
    private readonly ServicoPreferencias _preferencias;
    private readonly ServicoModelosExcel _modelos;
    private readonly RascunhoRepositorio _rascunhos;
    private readonly IExportadorPdf _exportador;
    private readonly IRelogio _relogio;
    private bool _restaurando;
    private bool _montandoEdicao;
    private string? _ultimoResultado;
    private string? _problemaDoModelo;

    // Edicao em andamento
    private Guid _edId;
    private string _edArquivoAtual = "";
    private string _edAssinatura = "";
    private string? _edArquivoNovo;

    public ModelosVm(
        IDialogos dialogos, ServicoOperacoes operacoes, ServicoPreferencias preferencias, ServicoModelosExcel modelos, RascunhoRepositorio rascunhos,
        IExportadorPdf exportador, IRelogio relogio)
    {
        _dialogos = dialogos;
        _operacoes = operacoes;
        _preferencias = preferencias;
        _modelos = modelos;
        _rascunhos = rascunhos;
        _exportador = exportador;
        _relogio = relogio;
        _pastaDestino = preferencias.DestinoDe(Modulo);
        Recarregar(null);
    }

    public override string Titulo => "Modelos Excel";

    /// <summary>Leva a outra tela (definido pela composicao do app).</summary>
    public Action<string>? Navegar { get; set; }

    [RelayCommand]
    private void IrPara(string modulo) => Navegar?.Invoke(modulo);

    public ObservableCollection<ModeloItemVm> Modelos { get; } = [];
    public ObservableCollection<LinhaModeloVm> Linhas { get; } = [];
    public ObservableCollection<string> Colunas { get; } = [];
    public ObservableCollection<AvisoVm> Avisos { get; } = [];
    public ObservableCollection<CampoEdicaoVm> EdCampos { get; } = [];
    public ObservableCollection<string> EdAbas { get; } = [];
    public IReadOnlyList<string> Tipos => CampoEdicaoVm.Tipos;
    public IReadOnlyList<string> Origens { get; } = [.. OrigensCampo.Todas.Select(o => o.Rotulo)];

    [ObservableProperty]
    private ModeloItemVm? _selecionado;

    [ObservableProperty]
    private bool _editando;

    [ObservableProperty]
    private string _edNome = "";

    [ObservableProperty]
    private string _edPadraoNome = "";

    [ObservableProperty]
    private string _edArquivoTexto = "";

    [ObservableProperty]
    private string _edProblemas = "";

    [ObservableProperty]
    private string _edTitulo = "";

    [ObservableProperty]
    private string? _outroArquivo;

    [ObservableProperty]
    private bool _gerarPdf;

    [ObservableProperty]
    private string _pastaDestino;

    [ObservableProperty]
    private string _resumo = "";

    [ObservableProperty]
    private string _textoBotao = "Gerar arquivo";

    [ObservableProperty]
    private bool _podeAbrirResultado;

    public bool TemModelos => Modelos.Count > 0;
    public bool Usando => !Editando && Selecionado is not null;
    public bool SemNada => !Editando && Modelos.Count == 0;
    public bool ExcelDisponivel => _exportador.Disponivel;
    public bool UsaOutroArquivo => OutroArquivo is not null;
    public string TextoArquivoBase => OutroArquivo is null
        ? "Parte do modelo cadastrado."
        : $"Parte de “{Path.GetFileName(OutroArquivo)}” (gera uma cópia; o arquivo escolhido não é alterado).";

    public override void AoEntrar()
    {
        if (!Editando)
        {
            Recarregar(Selecionado?.Modelo.Id);
        }
    }

    private void Recarregar(Guid? selecionar)
    {
        Modelos.Clear();
        foreach (var modelo in _modelos.Listar())
        {
            Modelos.Add(new ModeloItemVm(modelo));
        }

        Selecionado = Modelos.FirstOrDefault(m => m.Modelo.Id == selecionar) ?? Modelos.FirstOrDefault();
        OnPropertyChanged(nameof(TemModelos));
        OnPropertyChanged(nameof(SemNada));
        OnPropertyChanged(nameof(Usando));
    }

    partial void OnEditandoChanged(bool value)
    {
        OnPropertyChanged(nameof(Usando));
        OnPropertyChanged(nameof(SemNada));
    }

    partial void OnGerarPdfChanged(bool value) => Conferir();

    partial void OnOutroArquivoChanged(string? value)
    {
        OnPropertyChanged(nameof(UsaOutroArquivo));
        OnPropertyChanged(nameof(TextoArquivoBase));
    }

    partial void OnSelecionadoChanged(ModeloItemVm? value)
    {
        OnPropertyChanged(nameof(Usando));
        OutroArquivo = null;
        PodeAbrirResultado = false;
        Colunas.Clear();
        Linhas.Clear();
        _problemaDoModelo = null;
        if (value is null)
        {
            Conferir();
            return;
        }

        foreach (var campo in value.Modelo.Campos)
        {
            Colunas.Add(campo.Nome);
        }

        _problemaDoModelo = ProblemaDoArquivo(value.Modelo);
        _restaurando = true;
        try
        {
            foreach (var valores in _rascunhos.Ler<List<List<string>>>(ChaveRascunho(value.Modelo)) ?? [])
            {
                Adicionar(valores);
            }
        }
        finally
        {
            _restaurando = false;
        }

        Conferir();
    }

    private static string ChaveRascunho(ModeloExcel modelo) => "modelo-" + modelo.Id.ToString("N");

    /// <summary>O arquivo guardado ainda e o mesmo do cadastro e aceita o mapeamento?</summary>
    private string? ProblemaDoArquivo(ModeloExcel modelo)
    {
        try
        {
            string caminho = _modelos.Caminho(modelo);
            if (!File.Exists(caminho))
            {
                return "A cópia do modelo não foi encontrada. Edite o modelo e escolha o arquivo de novo.";
            }

            if (PreenchedorModelo.Ler(caminho).Assinatura != modelo.Assinatura)
            {
                return "O arquivo do modelo mudou desde o cadastro. Edite o modelo e revise os campos antes de gerar.";
            }

            return PreenchedorModelo.Conferir(caminho, modelo.Campos).FirstOrDefault();
        }
        catch (Exception ex) when (ex is PlanilhaInvalidaException or IOException or UnauthorizedAccessException)
        {
            return ex.Message;
        }
    }

    // ----- Preencher -----

    private LinhaModeloVm Adicionar(IReadOnlyList<string>? valores = null)
    {
        var linha = new LinhaModeloVm();
        var campos = Selecionado!.Modelo.Campos;
        for (int i = 0; i < campos.Count; i++)
        {
            var celula = new CelulaModeloVm(campos[i], Conferir);
            if (valores is not null && i < valores.Count)
            {
                celula.Valor = valores[i];
            }

            linha.Celulas.Add(celula);
        }

        Linhas.Add(linha);
        return linha;
    }

    [RelayCommand]
    private void NovaLinha()
    {
        if (Selecionado is not null)
        {
            Adicionar();
            Conferir();
        }
    }

    [RelayCommand]
    private void RemoverLinha(LinhaModeloVm linha)
    {
        Linhas.Remove(linha);
        Conferir();
    }

    [RelayCommand]
    private void Limpar()
    {
        if (Linhas.Count == 0 || _dialogos.Confirmar("Limpar a lista", "Limpar as linhas deste modelo? Os arquivos já gerados não são apagados.", "Limpar"))
        {
            Linhas.Clear();
            Resultado = "";
            Conferir();
        }
    }

    /// <summary>Cola linhas copiadas do Excel, na ordem dos campos. A linha de titulos, se vier junto, e ignorada.</summary>
    [RelayCommand]
    private void ColarDoExcel()
    {
        if (Selecionado is null)
        {
            return;
        }

        string? texto = _dialogos.LerAreaDeTransferencia();
        if (string.IsNullOrWhiteSpace(texto))
        {
            _dialogos.Avisar("Colar do Excel", $"Não há nada copiado. No Excel, selecione as linhas com as colunas nesta ordem: {string.Join(", ", Colunas)}. Copie e tente de novo.");
            return;
        }

        var campos = Selecionado.Modelo.Campos;
        int coladas = 0;
        int sobras = 0;
        foreach (string bruta in texto.Replace("\r\n", "\n").Split('\n'))
        {
            string[] partes = bruta.Split('\t');
            if (partes.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            if (coladas == 0 && string.Equals(partes[0].Trim(), campos[0].Nome, StringComparison.OrdinalIgnoreCase))
            {
                continue; // linha de titulos
            }

            if (partes.Length > campos.Count && partes.Skip(campos.Count).Any(p => !string.IsNullOrWhiteSpace(p)))
            {
                sobras++;
            }

            Adicionar([.. partes.Select(p => p.Trim())]);
            coladas++;
        }

        Conferir();
        Resultado = Formato.Plural(coladas, "linha colada", "linhas coladas") + "."
                    + (sobras > 0 ? $" Em {Formato.Plural(sobras, "linha", "linhas")} havia mais colunas do que campos; o que sobrou foi ignorado." : "");
        ResultadoComFalha = false;
    }

    /// <summary>Acrescenta uma linha com os dados da obra nos campos que vem da Base de dados.</summary>
    public void AdicionarDaBase(ObraLida obra)
    {
        Editando = false;
        if (Selecionado is null)
        {
            Resultado = "Cadastre um modelo primeiro. Depois, os campos ligados à Base de dados são preenchidos com a obra escolhida.";
            ResultadoComFalha = true;
            return;
        }

        var campos = Selecionado.Modelo.Campos;
        if (campos.All(c => c.Origem.Length == 0))
        {
            Resultado = $"O modelo “{Selecionado.Nome}” não tem campos ligados à Base de dados. Edite o modelo e escolha de onde vem cada campo.";
            ResultadoComFalha = true;
            return;
        }

        Adicionar([.. campos.Select(c => OrigensCampo.Valor(obra, c.Origem))]);
        Conferir();
        Resultado = $"{obra.Empreendimento} entrou na lista de “{Selecionado.Nome}”. Confira os campos antes de gerar.";
        ResultadoComFalha = false;
    }

    /// <summary>Usar, so desta vez, outro arquivo com a mesma estrutura (por exemplo, um ja preenchido em parte).</summary>
    [RelayCommand]
    private void EscolherOutroArquivo()
    {
        if (Selecionado is null || _dialogos.EscolherArquivos("Escolha o arquivo a atualizar", FiltroExcel, PastaDestino).FirstOrDefault() is not { } arquivo)
        {
            return;
        }

        try
        {
            var problemas = PreenchedorModelo.Conferir(arquivo, Selecionado.Modelo.Campos);
            if (problemas.Count > 0)
            {
                _dialogos.Avisar("Este arquivo não combina com o modelo", string.Join("\n", problemas));
                return;
            }

            OutroArquivo = arquivo;
        }
        catch (Exception ex) when (ex is PlanilhaInvalidaException or IOException or UnauthorizedAccessException)
        {
            _dialogos.Avisar("Arquivo não aceito", ex.Message);
        }
    }

    [RelayCommand]
    private void VoltarAoModelo() => OutroArquivo = null;

    [RelayCommand]
    private void EscolherDestino()
    {
        if (_dialogos.EscolherPasta("Salvar os arquivos em", PastaDestino) is { } pasta)
        {
            PastaDestino = pasta;
            _preferencias.LembrarDestino(Modulo, pasta);
        }
    }

    private void Conferir()
    {
        if (_restaurando)
        {
            return;
        }

        for (int i = 0; i < Linhas.Count; i++)
        {
            var linha = Linhas[i];
            linha.Numero = (i + 1).ToString();
            var erros = linha.Celulas.Select(c => ValidadorModelo.Converter(c.Campo, c.Valor).Erro).Where(e => e is not null).ToList();
            if (linha.Celulas.All(c => string.IsNullOrWhiteSpace(c.Valor)))
            {
                erros = ["Linha em branco: preencha ou tire da lista"];
            }

            linha.Impedida = erros.Count > 0;
            linha.Avisos = erros.Count > 0 ? string.Join(" · ", erros) + "." : "";
        }

        Avisos.Clear();
        if (_problemaDoModelo is not null)
        {
            Avisos.Add(new AvisoVm(Gravidade.ImpedeGerar, _problemaDoModelo));
        }

        int impedidas = Linhas.Count(l => l.Impedida);
        if (impedidas > 0)
        {
            Avisos.Add(new AvisoVm(Gravidade.ImpedeGerar, $"{Formato.Plural(impedidas, "linha precisa", "linhas precisam")} de correção (veja o texto abaixo de cada uma)."));
        }

        if (GerarPdf && !_exportador.Disponivel)
        {
            Avisos.Add(new AvisoVm(Gravidade.PrecisaConferir, "O Excel não foi encontrado neste computador. Será gerado só o arquivo Excel, sem PDF."));
        }

        Resumo = Linhas.Count == 0
            ? "Nenhuma linha ainda"
            : $"{Formato.Plural(Linhas.Count, "linha", "linhas")} · cada linha gera um arquivo";
        TextoBotao = Linhas.Count > 1 ? $"Gerar {Linhas.Count} arquivos" : "Gerar arquivo";
        GerarCommand.NotifyCanExecuteChanged();

        if (Selecionado is not null)
        {
            var valores = Linhas.Select(l => l.Celulas.Select(c => c.Valor).ToList()).ToList();
            if (valores.Count == 0)
            {
                _rascunhos.Remover(ChaveRascunho(Selecionado.Modelo));
            }
            else
            {
                _rascunhos.Gravar(ChaveRascunho(Selecionado.Modelo), valores, _relogio.Agora);
            }
        }
    }

    private bool PodeGerar() => Usando && !Ocupado && _problemaDoModelo is null && Linhas.Count > 0 && !Linhas.Any(l => l.Impedida);

    [RelayCommand(CanExecute = nameof(PodeGerar))]
    private async Task GerarAsync()
    {
        var modelo = Selecionado!.Modelo;
        string arquivoBase = OutroArquivo ?? _modelos.Caminho(modelo);
        string extensao = Path.GetExtension(arquivoBase);
        string destino = PastaDestino;
        bool pdf = GerarPdf && _exportador.Disponivel;
        var trabalhos = Linhas.Select(l => (
            Valores: l.Celulas.Select(c => (c.Campo, ValidadorModelo.Converter(c.Campo, c.Valor).Valor)).ToList(),
            Nome: ValidadorModelo.NomeDoArquivo(modelo, l.Celulas.ToDictionary(c => c.Campo.Nome, c => c.Valor, StringComparer.OrdinalIgnoreCase)))).ToList();
        var criados = new List<string>();
        var falhas = new List<string>();

        Ocupado = true;
        GerarCommand.NotifyCanExecuteChanged();
        try
        {
            var tarefa = _operacoes.Fila.Enfileirar($"{modelo.Nome}: {Formato.Plural(trabalhos.Count, "arquivo", "arquivos")}", (progresso, cancelamento) =>
            {
                for (int i = 0; i < trabalhos.Count && !cancelamento.IsCancellationRequested; i++)
                {
                    progresso.Report(new ProgressoTarefa(i, trabalhos.Count, trabalhos[i].Nome));
                    string saida = Path.Combine(destino, NomeArquivo.Livre(trabalhos[i].Nome + extensao, n => File.Exists(Path.Combine(destino, n))));
                    try
                    {
                        PreenchedorModelo.Preencher(arquivoBase, trabalhos[i].Valores, saida);
                        criados.Add(saida);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlanilhaInvalidaException or EscritaBloqueadaException)
                    {
                        falhas.Add($"Linha {i + 1} não foi gerada: {ex.Message}");
                        continue;
                    }

                    if (pdf)
                    {
                        string saidaPdf = Path.Combine(destino, NomeArquivo.Livre(Path.GetFileNameWithoutExtension(saida) + ".pdf", n => File.Exists(Path.Combine(destino, n))));
                        try
                        {
                            _exportador.Exportar(saida, saidaPdf);
                            criados.Add(saidaPdf);
                        }
                        catch (Exception ex)
                        {
                            falhas.Add($"“{Path.GetFileName(saida)}” foi gerado, mas o PDF não: {ex.Message}");
                        }
                    }
                }

                return Task.FromResult<string?>(Formato.Plural(criados.Count, "arquivo criado", "arquivos criados"));
            });
            await FilaTarefas.Aguardar(tarefa);
        }
        finally
        {
            Ocupado = false;
            GerarCommand.NotifyCanExecuteChanged();
        }

        var planilhas = criados.Where(c => !c.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)).ToList();
        if (criados.Count > 0)
        {
            string comPdf = planilhas.Count < criados.Count ? " (com PDF)" : "";
            string pasta = Path.GetFileName(destino.TrimEnd('\\', '/'));
            _operacoes.RegistrarCriados(
                planilhas.Count == 1
                    ? $"Gerou “{Path.GetFileName(planilhas[0])}” pelo modelo {modelo.Nome}{comPdf}"
                    : $"Gerou {planilhas.Count} arquivos pelo modelo {modelo.Nome} em {pasta}{comPdf}",
                criados);
            _ultimoResultado = criados[0];
            PodeAbrirResultado = true;
        }

        ResultadoComFalha = falhas.Count > 0;
        string feito = planilhas.Count switch
        {
            0 => "Nenhum arquivo foi gerado.",
            1 => $"“{Path.GetFileName(planilhas[0])}” gerado" + (planilhas.Count < criados.Count ? ", com PDF." : "."),
            _ => $"{planilhas.Count} arquivos gerados" + (planilhas.Count < criados.Count ? ", com PDF." : "."),
        };
        Resultado = string.Join(" ", new[] { feito }.Concat(falhas));
    }

    [RelayCommand]
    private void AbrirResultado()
    {
        if (_ultimoResultado is not null)
        {
            _dialogos.MostrarNaPasta(_ultimoResultado);
        }
    }

    // ----- Cadastro -----

    [RelayCommand]
    private void NovoModelo()
    {
        if (_dialogos.EscolherArquivos("Escolha a planilha que será o modelo", FiltroExcel, _preferencias.Atual.PastaPlanilhasBase).FirstOrDefault() is not { } arquivo)
        {
            return;
        }

        if (!LerAbas(arquivo))
        {
            return;
        }

        _montandoEdicao = true;
        _edId = Guid.NewGuid();
        _edArquivoAtual = "";
        _edAssinatura = "";
        _edArquivoNovo = arquivo;
        EdTitulo = "Cadastrar modelo";
        EdNome = Path.GetFileNameWithoutExtension(arquivo);
        EdPadraoNome = "";
        EdArquivoTexto = Path.GetFileName(arquivo);
        EdCampos.Clear();
        _montandoEdicao = false;
        AdicionarCampo();
        Editando = true;
    }

    [RelayCommand]
    private void EditarModelo()
    {
        if (Selecionado is null)
        {
            return;
        }

        var modelo = Selecionado.Modelo;
        string caminho = _modelos.Caminho(modelo);
        EdAbas.Clear();
        if (File.Exists(caminho))
        {
            LerAbas(caminho);
        }

        _montandoEdicao = true;
        _edId = modelo.Id;
        _edArquivoAtual = modelo.Arquivo;
        _edAssinatura = modelo.Assinatura;
        _edArquivoNovo = null;
        EdTitulo = "Editar modelo";
        EdNome = modelo.Nome;
        EdPadraoNome = modelo.PadraoNome;
        EdArquivoTexto = File.Exists(caminho) ? "Cópia guardada pelo app" : "Arquivo não encontrado: escolha de novo";
        EdCampos.Clear();
        foreach (var campo in modelo.Campos)
        {
            var edicao = new CampoEdicaoVm(ConferirEdicao);
            edicao.Carregar(campo);
            EdCampos.Add(edicao);
        }

        _montandoEdicao = false;
        ConferirEdicao();
        Editando = true;
    }

    /// <summary>Troca o arquivo do modelo (versao nova). Os campos ficam; o app aponta o que nao encaixa mais.</summary>
    [RelayCommand]
    private void TrocarArquivo()
    {
        if (_dialogos.EscolherArquivos("Escolha a nova versão do modelo", FiltroExcel, _preferencias.Atual.PastaPlanilhasBase).FirstOrDefault() is { } arquivo && LerAbas(arquivo))
        {
            _edArquivoNovo = arquivo;
            EdArquivoTexto = Path.GetFileName(arquivo);
            ConferirEdicao();
        }
    }

    private bool LerAbas(string arquivo)
    {
        try
        {
            var estrutura = PreenchedorModelo.Ler(arquivo);
            EdAbas.Clear();
            foreach (string aba in estrutura.Abas)
            {
                EdAbas.Add(aba);
            }

            return true;
        }
        catch (Exception ex) when (ex is PlanilhaInvalidaException or IOException or UnauthorizedAccessException)
        {
            _dialogos.Avisar("Arquivo não aceito", ex.Message);
            return false;
        }
    }

    [RelayCommand]
    private void AdicionarCampo()
    {
        EdCampos.Add(new CampoEdicaoVm(ConferirEdicao) { Aba = EdCampos.LastOrDefault()?.Aba ?? EdAbas.FirstOrDefault() });
        ConferirEdicao();
    }

    [RelayCommand]
    private void RemoverCampo(CampoEdicaoVm campo)
    {
        EdCampos.Remove(campo);
        ConferirEdicao();
    }

    partial void OnEdNomeChanged(string value) => ConferirEdicao();

    partial void OnEdPadraoNomeChanged(string value) => ConferirEdicao();

    private ModeloExcel EmEdicao() => new()
    {
        Id = _edId,
        Nome = EdNome.Trim(),
        Arquivo = _edArquivoAtual,
        Assinatura = _edAssinatura,
        PadraoNome = EdPadraoNome.Trim(),
        Campos = [.. EdCampos.Select(c => c.ParaCampo())],
    };

    private void ConferirEdicao()
    {
        if (_montandoEdicao)
        {
            return;
        }

        var modelo = EmEdicao();
        var problemas = ValidadorModelo.ConferirCadastro(modelo).ToList();
        if (_modelos.Listar().Any(m => m.Id != modelo.Id && string.Equals(m.Nome, modelo.Nome, StringComparison.CurrentCultureIgnoreCase)))
        {
            problemas.Add("Já existe outro modelo com esse nome.");
        }

        string? arquivo = _edArquivoNovo ?? (_edArquivoAtual.Length > 0 ? _modelos.Caminho(modelo) : null);
        if (arquivo is null || !File.Exists(arquivo))
        {
            problemas.Add("Escolha o arquivo do modelo.");
        }
        else
        {
            try
            {
                problemas.AddRange(PreenchedorModelo.Conferir(arquivo, modelo.Campos.Where(c => c.Aba.Length > 0)));
            }
            catch (Exception ex) when (ex is PlanilhaInvalidaException or IOException or UnauthorizedAccessException)
            {
                problemas.Add(ex.Message);
            }
        }

        EdProblemas = string.Join("\n", problemas);
        SalvarModeloCommand.NotifyCanExecuteChanged();
    }

    private bool PodeSalvarModelo() => Editando && EdProblemas.Length == 0;

    [RelayCommand(CanExecute = nameof(PodeSalvarModelo))]
    private void SalvarModelo()
    {
        try
        {
            var salvo = _modelos.Salvar(EmEdicao(), _edArquivoNovo);
            Editando = false;
            Recarregar(salvo.Id);
            Resultado = $"Modelo “{salvo.Nome}” salvo. O app guarda uma cópia; o seu arquivo não é alterado.";
            ResultadoComFalha = false;
        }
        catch (Exception ex) when (ex is PlanilhaInvalidaException or IOException or UnauthorizedAccessException)
        {
            _dialogos.Avisar("O modelo não foi salvo", ex.Message);
        }
    }

    [RelayCommand]
    private void CancelarEdicao()
    {
        Editando = false;
        SalvarModeloCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void RemoverModelo()
    {
        if (Selecionado is not { } item
            || !_dialogos.Confirmar("Tirar o modelo do app", $"Tirar “{item.Nome}” da lista de modelos? Os arquivos já gerados e a sua planilha original não são apagados.", "Tirar da lista"))
        {
            return;
        }

        _rascunhos.Remover(ChaveRascunho(item.Modelo));
        _modelos.Remover(item.Modelo);
        Recarregar(null);
        Resultado = $"“{item.Nome}” saiu da lista de modelos.";
        ResultadoComFalha = false;
    }
}
