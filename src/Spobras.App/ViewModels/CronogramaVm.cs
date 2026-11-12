using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Spobras.App.Servicos;
using Spobras.Core;
using Spobras.Core.Abstracoes;
using Spobras.Core.Base;
using Spobras.Core.Cronograma;
using Spobras.Core.Nomes;
using Spobras.Core.Tarefas;
using Spobras.Dados.Configuracao;
using Spobras.Documentos.Cronograma;
using Spobras.Documentos.Modelos;
using Spobras.Documentos.Planilhas;

namespace Spobras.App.ViewModels;

public sealed record LinhaRascunho(
    string Portaria, string Lote, string Dotacao, string Nome, string Escopo, string Valor, string Inicio, string Fim, OrigemInicio Origem, DateOnly? DataOs);

public sealed record AjusteRascunho(string Mes, bool EmReais, string Quantia, int Para);

public sealed record RascunhoCronograma(
    List<LinhaRascunho> Linhas, bool AjustarInicio, bool GerarPdf, string NomeCronograma = "", string Remuneracao = "10", List<AjusteRascunho>? Ajustes = null);

/// <summary>Um valor ou percentual fixado num mes; os outros meses dividem o resto.</summary>
public sealed partial class AjusteCronogramaVm(Action aoMudar) : ObservableObject
{
    private int _para;

    [ObservableProperty]
    private string _mes = "";

    /// <summary>0 = percentual do total da obra; 1 = valor em reais.</summary>
    [ObservableProperty]
    private int _tipo;

    [ObservableProperty]
    private string _quantia = "";

    [ObservableProperty]
    private string _aviso = "";

    /// <summary>0 = todas as obras; n = obra n da lista. A lista de opcoes e refeita quando as obras mudam: -1 e ignorado.</summary>
    public int Para
    {
        get => _para;
        set
        {
            if (value >= 0 && SetProperty(ref _para, value))
            {
                aoMudar();
            }
        }
    }

    partial void OnMesChanged(string value) => aoMudar();

    partial void OnTipoChanged(int value) => aoMudar();

    partial void OnQuantiaChanged(string value) => aoMudar();

    public void Avisar() => OnPropertyChanged(nameof(Para));

    /// <summary>O ajuste como o calculo entende, ou o motivo de nao valer.</summary>
    public (AjusteMensal? Ajuste, string Problema) Ler()
    {
        if (Mes.Trim().Length == 0 && Quantia.Trim().Length == 0)
        {
            return (null, "");
        }

        if (ColagemCronograma.MesDe(Mes) is not { } mes)
        {
            return (null, "Mês não entendido. Use nov/26 ou 11/2026.");
        }

        decimal? quantia = Tipo == 1
            ? Normalizacao.LerReais(Quantia)
            : decimal.TryParse(Quantia.Replace("%", "").Trim(), System.Globalization.NumberStyles.Number, Formato.PtBr, out decimal p) ? p : null;
        if (quantia is not { } q || q < 0 || (Tipo == 0 && q > 100))
        {
            return (null, Tipo == 1 ? "Valor não entendido. Use o formato 1.000,00." : "Percentual de 0 a 100, como 5 ou 7,5.");
        }

        return (new AjusteMensal(mes, Tipo == 1 ? TipoAjuste.Valor : TipoAjuste.Percentual, q, Para == 0 ? null : Para - 1), "");
    }
}

/// <summary>Uma linha do resumo do calculo (mes, ano ou dotacao).</summary>
public sealed record ItemCronograma(string Rotulo, string Valor, string Detalhe = "", bool Destaque = false);

public sealed partial class LinhaCronogramaVm(Action aoMudar) : ObservableObject
{
    private bool _carregando;

    [ObservableProperty]
    private string _portaria = "";

    [ObservableProperty]
    private string _lote = "";

    [ObservableProperty]
    private string _dotacao = "";

    [ObservableProperty]
    private string _nome = "";

    [ObservableProperty]
    private string _escopo = "";

    [ObservableProperty]
    private string _valor = "";

    [ObservableProperty]
    private string _inicio = "";

    [ObservableProperty]
    private string _fim = "";

    [ObservableProperty]
    private string _avisos = "";

    [ObservableProperty]
    private bool _impedida;

    [ObservableProperty]
    private string _numero = "";

    /// <summary>Remuneracao, total e meses, para conferir antes de gerar.</summary>
    [ObservableProperty]
    private string _calculo = "";

    public OrigemInicio Origem { get; set; }
    public DateOnly? DataOsDaBase { get; set; }

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (!_carregando && e.PropertyName is not (nameof(Avisos) or nameof(Impedida) or nameof(Numero) or nameof(Calculo)))
        {
            if (e.PropertyName == nameof(Inicio) && Origem == OrigemInicio.OrdemDeServico)
            {
                Origem = OrigemInicio.Informado; // o usuario trocou a data que veio da OS
            }

            aoMudar();
        }
    }

    public void Carregar(Action acao)
    {
        _carregando = true;
        acao();
        _carregando = false;
    }

    public bool ValorInvalido => Valor.Trim().Length > 0 && Normalizacao.LerReais(Valor) is null;
    public bool InicioInvalido => Inicio.Trim().Length > 0 && ColagemCronograma.MesDe(Inicio) is null;
    public bool FimInvalido => Fim.Trim().Length > 0 && ColagemCronograma.MesDe(Fim) is null;

    /// <summary>So o mes importa: inicio e fim vao para a planilha como dia 1 do mes (aparece "nov/26").</summary>
    public ObraCronograma ParaObra() => new()
    {
        Portaria = ColagemCronograma.Portaria(Portaria),
        Lote = Lote.Trim(),
        Dotacao = Dotacao.Trim(),
        Nome = Nome.Trim(),
        Escopo = Escopo.Trim(),
        Valor = Normalizacao.LerReais(Valor),
        Inicio = ColagemCronograma.MesDe(Inicio),
        Fim = ColagemCronograma.MesDe(Fim),
        OrigemDoInicio = Origem,
        DataOsDaBase = DataOsDaBase,
    };
}

/// <summary>
/// Cronograma feito pelo app, sem modelo: as obras, a remuneracao, os ajustes por mes e, ao gerar, a planilha
/// com meses, totais por ano, total solicitado e tabela por dotacao completa.
/// </summary>
public sealed partial class CronogramaVm : TelaVm
{
    public const string Modulo = "cronograma";
    private const string ChaveRascunho = "cronograma-atual";
    private const string TodasAsObras = "Todas as obras";

    private readonly IDialogos _dialogos;
    private readonly ServicoOperacoes _operacoes;
    private readonly ServicoPreferencias _preferencias;
    private readonly ServicoModelos _modelos;
    private readonly RascunhoRepositorio _rascunhos;
    private readonly IExportadorPdf _exportador;
    private readonly IRelogio _relogio;
    private readonly Func<IReadOnlyList<VinculoBase>> _vinculos;
    private CronogramaCalculado? _calculo;
    private bool _restaurando;
    private string? _ultimoResultado;

    public CronogramaVm(
        IDialogos dialogos, ServicoOperacoes operacoes, ServicoPreferencias preferencias, ServicoModelos modelos, RascunhoRepositorio rascunhos,
        IExportadorPdf exportador, IRelogio relogio, Func<IReadOnlyList<VinculoBase>>? vinculos = null)
    {
        _dialogos = dialogos;
        _operacoes = operacoes;
        _preferencias = preferencias;
        _modelos = modelos;
        _rascunhos = rascunhos;
        _exportador = exportador;
        _relogio = relogio;
        _vinculos = vinculos ?? (() => []);
        _pastaDestino = preferencias.DestinoDe(Modulo);
        Restaurar();
        Conferir();
    }

    public override string Titulo => "Cronograma padrão";

    public ObservableCollection<LinhaCronogramaVm> Linhas { get; } = [];
    public ObservableCollection<AjusteCronogramaVm> Ajustes { get; } = [];
    public ObservableCollection<AvisoVm> Avisos { get; } = [];

    /// <summary>"Todas as obras" e as obras da lista, para escolher a quem vale cada ajuste.</summary>
    public ObservableCollection<string> OpcoesDeObra { get; } = [TodasAsObras];

    public ObservableCollection<ItemCronograma> Meses { get; } = [];
    public ObservableCollection<ItemCronograma> Anos { get; } = [];
    public ObservableCollection<ItemCronograma> Dotacoes { get; } = [];

    public IReadOnlyList<string> TiposDeAjuste { get; } = ["% do total", "R$"];

    [ObservableProperty]
    private bool _gerarPdf;

    /// <summary>Nome do arquivo escolhido pelo usuario; vazio = nome sugerido por portaria e lote.</summary>
    [ObservableProperty]
    private string _nomeCronograma = "";

    /// <summary>Percentual de remuneracao sobre o valor da obra. Padrao 10%.</summary>
    [ObservableProperty]
    private string _remuneracao = "10";

    [ObservableProperty]
    private string _pastaDestino;

    [ObservableProperty]
    private string _resumo = "";

    [ObservableProperty]
    private string _totais = "";

    [ObservableProperty]
    private bool _temCalculo;

    [ObservableProperty]
    private bool _podeAbrirResultado;

    public string TextoBotao => "Gerar cronograma";

    public bool PdfDisponivel => _exportador.Disponivel;

    partial void OnRemuneracaoChanged(string value) => Conferir();

    partial void OnNomeCronogramaChanged(string value) => Conferir();

    partial void OnGerarPdfChanged(bool value) => Conferir();

    /// <summary>0,10 para "10"; null se o texto nao for um percentual valido (0 a 100).</summary>
    public decimal? PercentualRemuneracao =>
        decimal.TryParse(Remuneracao.Replace("%", "").Trim(), System.Globalization.NumberStyles.Number, Formato.PtBr, out decimal p) && p >= 0 && p <= 100
            ? Math.Round(p / 100m, 4)
            : null;

    public override void AoEntrar() => Conferir();

    [RelayCommand]
    private void NovaLinha()
    {
        Linhas.Add(new LinhaCronogramaVm(Conferir));
        Conferir();
    }

    [RelayCommand]
    private void RemoverLinha(LinhaCronogramaVm linha)
    {
        int indice = Linhas.IndexOf(linha) + 1;
        Linhas.Remove(linha);

        // Ajuste que era so desta obra sai junto; os das obras de baixo acompanham a nova posicao.
        foreach (var ajuste in Ajustes.ToList())
        {
            if (ajuste.Para == indice)
            {
                Ajustes.Remove(ajuste);
            }
            else if (ajuste.Para > indice)
            {
                ajuste.Para--;
            }
        }

        Conferir();
    }

    [RelayCommand]
    private void NovoAjuste()
    {
        Ajustes.Add(new AjusteCronogramaVm(Conferir));
        Conferir();
    }

    [RelayCommand]
    private void RemoverAjuste(AjusteCronogramaVm ajuste)
    {
        Ajustes.Remove(ajuste);
        Conferir();
    }

    [RelayCommand]
    private void Limpar()
    {
        if (Linhas.Count + Ajustes.Count == 0
            || _dialogos.Confirmar("Começar outro cronograma", "Limpar as obras e os ajustes deste rascunho? Os cronogramas já gerados não são apagados.", "Limpar"))
        {
            Linhas.Clear();
            Ajustes.Clear();
            Resultado = "";
            Conferir();
        }
    }

    /// <summary>Le as obras de uma planilha sua (primeira aba), achando as colunas pelos titulos.</summary>
    [RelayCommand]
    private void ImportarPlanilha()
    {
        if (_dialogos.EscolherArquivos("Planilha com as obras", "Planilhas do Excel|*.xlsx;*.xlsm", _preferencias.Atual.PastaTrabalho).FirstOrDefault() is not { } arquivo)
        {
            return;
        }

        try
        {
            using var leitor = new LeitorXlsx(arquivo);
            var (texto, problema) = ImportacaoCronograma.ParaColagem(leitor.LerAba(leitor.NomesDasAbas()[0]));
            if (texto is null)
            {
                _dialogos.Avisar("Importar de uma planilha", problema!);
                return;
            }

            Incluir(texto);
            Resultado = $"De “{Path.GetFileName(arquivo)}”: " + Resultado;
        }
        catch (Exception ex) when (ex is PlanilhaInvalidaException or IOException or UnauthorizedAccessException)
        {
            _dialogos.Avisar("Importar de uma planilha", ex.Message);
        }
    }

    /// <summary>Cola linhas copiadas do Excel: Portaria, Lote, Dotação, Nome da Obra, Breve escopo, Valor, Início, Fim.</summary>
    [RelayCommand]
    private void ColarDoExcel()
    {
        string? texto = _dialogos.LerAreaDeTransferencia();
        if (string.IsNullOrWhiteSpace(texto))
        {
            _dialogos.Avisar("Colar do Excel", "Não há nada copiado. No Excel, selecione as linhas (Portaria, Lote, Dotação, Nome da Obra, Breve escopo, Valor, Início, Fim), copie e tente de novo.");
            return;
        }

        Incluir(texto);
    }

    private void Incluir(string texto)
    {
        var lida = ColagemCronograma.Ler(texto);
        foreach (var obra in lida.Obras)
        {
            Adicionar(obra);
        }

        Conferir();
        foreach (string aviso in lida.Avisos)
        {
            Avisos.Add(new AvisoVm(Gravidade.PrecisaConferir, "Colagem: " + aviso));
        }

        Resultado = $"{Formato.Plural(lida.Obras.Count, "linha colada", "linhas coladas")}" + (lida.Avisos.Count > 0 ? $"; {Formato.Plural(lida.Avisos.Count, "aviso", "avisos")} abaixo." : ".");
        ResultadoComFalha = false;
    }

    /// <summary>
    /// Traz uma obra da Base de dados. O inicio vem da Data da OS (identificada como tal); o valor fica em
    /// branco, porque a base tem varios valores e so o usuario sabe qual vale para este cronograma.
    /// </summary>
    public void AdicionarDaBase(ObraLida obra)
    {
        string portaria = ColagemCronograma.Portaria(obra.Portaria ?? "");
        string lote = obra.Lote ?? "";
        lote = lote.StartsWith("LOTE ", StringComparison.OrdinalIgnoreCase) ? lote["LOTE ".Length..] : lote;

        Adicionar(new ObraCronograma
        {
            Portaria = portaria,
            Lote = lote,
            Dotacao = obra.Atividade ?? "",
            Nome = obra.Empreendimento,
            Escopo = obra.Escopo ?? "",
            Inicio = obra.DataOs is { } os ? new DateOnly(os.Year, os.Month, 1) : null,
            OrigemDoInicio = obra.DataOs is null ? OrigemInicio.Previsto : OrigemInicio.OrdemDeServico,
            DataOsDaBase = obra.DataOs,
        });
        Conferir();
        Orientar($"{obra.Empreendimento} entrou no cronograma" + (obra.DataOs is null ? " sem Data da OS: informe o início previsto." : " com o início no mês da Data da OS.") + " Informe o valor e o fim.");
    }

    [RelayCommand]
    private void EscolherDestino()
    {
        if (_dialogos.EscolherPasta("Salvar o cronograma em", PastaDestino) is { } pasta)
        {
            PastaDestino = pasta;
            _preferencias.LembrarDestino(Modulo, pasta);
        }
    }

    private bool PodeGerar() => !Ocupado && _calculo is { PodeGerar: true } && PercentualRemuneracao is not null
        && !Linhas.Any(l => l.ValorInvalido || l.InicioInvalido || l.FimInvalido) && Ajustes.All(a => a.Aviso.Length == 0);

    [RelayCommand(CanExecute = nameof(PodeGerar))]
    private async Task GerarAsync()
    {
        var calculo = _calculo!;
        var obras = calculo.Obras.Select(o => o.Obra).ToList();
        string destino = PastaDestino;
        string escolhido = NomeArquivo.Corrigir(NomeCronograma.Trim()).Trim();
        string nome = escolhido.Length > 0 ? escolhido : ModeloCronograma.NomeSugerido(obras);
        string saida = Path.Combine(destino, NomeArquivo.Livre(nome + ".xlsx", n => File.Exists(Path.Combine(destino, n))));
        string titulo = GeradorCronograma.TituloSugerido(obras);
        bool pdf = GerarPdf && _exportador.Disponivel;
        string? logo = _modelos.Logo;
        string? caminhoPdf = null;
        string? problemaPdf = null;
        string? erro = null;

        Ocupado = true;
        GerarCommand.NotifyCanExecuteChanged();
        try
        {
            var tarefa = _operacoes.Fila.Enfileirar($"Cronograma: {Path.GetFileName(saida)}", (progresso, cancelamento) =>
            {
                try
                {
                    GeradorCronograma.Gerar(calculo, saida, titulo, logo);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    erro = ex.Message;
                    return Task.FromResult<string?>(null);
                }

                if (pdf)
                {
                    progresso.Report(new ProgressoTarefa(1, 2, "Fazendo o PDF no Excel"));
                    string alvo = Path.ChangeExtension(saida, ".pdf");
                    try
                    {
                        _exportador.Exportar(saida, alvo);
                        caminhoPdf = alvo;
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
                    {
                        problemaPdf = ex.Message;
                    }
                }

                return Task.FromResult<string?>("1 arquivo criado");
            });
            await FilaTarefas.Aguardar(tarefa);
        }
        finally
        {
            Ocupado = false;
            GerarCommand.NotifyCanExecuteChanged();
        }

        if (erro is not null)
        {
            ResultadoComFalha = true;
            Resultado = "O cronograma não foi gerado. " + erro;
            return;
        }

        List<string> criados = caminhoPdf is null ? [saida] : [saida, caminhoPdf];
        _operacoes.RegistrarCriados($"Gerou “{Path.GetFileName(saida)}”" + (caminhoPdf is null ? "" : " (com PDF)"), criados);
        _ultimoResultado = saida;
        PodeAbrirResultado = true;
        ResultadoComFalha = problemaPdf is not null;
        Resultado = $"Pronto! “{Path.GetFileName(saida)}” criado, total solicitado {FormatoBase.Reais(calculo.Total)}"
                    + (caminhoPdf is not null ? ", com PDF." : ".")
                    + (problemaPdf is not null ? $" O PDF não saiu: {problemaPdf}" : "");
    }

    [RelayCommand]
    private void AbrirResultado()
    {
        if (_ultimoResultado is not null)
        {
            _dialogos.MostrarNaPasta(_ultimoResultado);
        }
    }

    private void Adicionar(ObraCronograma o)
    {
        var linha = new LinhaCronogramaVm(Conferir) { Origem = o.OrigemDoInicio, DataOsDaBase = o.DataOsDaBase };
        linha.Carregar(() =>
        {
            linha.Portaria = ColagemCronograma.Portaria(o.Portaria);
            linha.Lote = o.Lote;
            linha.Dotacao = o.Dotacao;
            linha.Nome = o.Nome;
            linha.Escopo = o.Escopo;
            linha.Valor = o.Valor?.ToString("N2", Formato.PtBr) ?? "";
            linha.Inicio = o.Inicio is { } i ? ColagemCronograma.Mes(i) : "";
            linha.Fim = o.Fim is { } f ? ColagemCronograma.Mes(f) : "";
        });
        Linhas.Add(linha);
    }

    private void Conferir()
    {
        if (_restaurando)
        {
            return;
        }

        AtualizarOpcoesDeObra();
        Avisos.Clear();
        var obras = Linhas.Select(l => l.ParaObra()).ToList();
        var ajustes = new List<AjusteMensal>();
        foreach (var a in Ajustes)
        {
            var (ajuste, problema) = a.Ler();
            a.Aviso = problema;
            if (ajuste is not null)
            {
                ajustes.Add(ajuste);
            }
        }

        var vinculos = _vinculos();
        _calculo = CalculoCronograma.Calcular(obras, PercentualRemuneracao ?? CalculoCronograma.PadraoRemuneracao, ajustes,
            o => CalculoCronograma.Resolver(o, vinculos));

        int k = 0;
        for (int i = 0; i < Linhas.Count; i++)
        {
            var linha = Linhas[i];
            var avisos = _calculo.AvisosDasObras.Where(a => a.Linha == i + 1).Select(a => a.Aviso).ToList();
            var textos = avisos.Select(a => a.Mensagem).ToList();
            if (linha.ValorInvalido)
            {
                textos.Insert(0, $"O valor \"{linha.Valor}\" não foi entendido. Use o formato 1.000,00.");
            }

            if (linha.InicioInvalido || linha.FimInvalido)
            {
                textos.Insert(0, "Mês não entendido. Use nov/26 ou 11/2026.");
            }

            linha.Numero = (i + 1).ToString(Formato.PtBr);
            linha.Avisos = string.Join(" ", textos);
            linha.Impedida = avisos.Any(a => a.Gravidade == Gravidade.ImpedeGerar) || linha.ValorInvalido || linha.InicioInvalido || linha.FimInvalido;

            // Remuneracao, total e meses de cada obra calculada (a lista calculada so tem as obras sem pendencia).
            var calculada = k < _calculo.Obras.Count && ReferenceEquals(_calculo.Obras[k].Obra, obras[i]) ? _calculo.Obras[k++] : null;
            linha.Calculo = calculada is null
                ? ""
                : $"Remuneração {FormatoBase.Reais(calculada.Remuneracao)} · total {FormatoBase.Reais(calculada.Total)} · "
                  + Formato.Plural(calculada.Meses.Count, "mês", "meses")
                  + (calculada.Dotacoes is { } d ? $" · dotação {d.Obra}" : "");
        }

        foreach (var aviso in _calculo.Avisos.Where(a => Linhas.Count > 0 || a.Gravidade != Gravidade.ImpedeGerar))
        {
            Avisos.Add(new AvisoVm(aviso));
        }

        if (PercentualRemuneracao is not { } pct)
        {
            Avisos.Add(new AvisoVm(Gravidade.ImpedeGerar, "Remuneração: use um percentual de 0 a 100, como 10 ou 12,5."));
        }
        else if (pct != CalculoCronograma.PadraoRemuneracao)
        {
            Avisos.Add(new AvisoVm(Gravidade.PrecisaConferir, $"Remuneração de {(pct * 100).ToString("0.##", Formato.PtBr)}% (o padrão é 10%)."));
        }

        if (GerarPdf && !_exportador.Disponivel)
        {
            Avisos.Add(new AvisoVm(Gravidade.PrecisaConferir, "O PDF é feito pelo Excel, que não foi encontrado neste computador. Será gerado só o arquivo Excel."));
        }

        MostrarCalculo(ajustes);
        GerarCommand.NotifyCanExecuteChanged();
        _rascunhos.Gravar(ChaveRascunho, new RascunhoCronograma(
            [.. Linhas.Select(l => new LinhaRascunho(l.Portaria, l.Lote, l.Dotacao, l.Nome, l.Escopo, l.Valor, l.Inicio, l.Fim, l.Origem, l.DataOsDaBase))],
            true, GerarPdf, NomeCronograma, Remuneracao,
            [.. Ajustes.Select(a => new AjusteRascunho(a.Mes, a.Tipo == 1, a.Quantia, a.Para))]), _relogio.Agora);
    }

    /// <summary>O que vai para a planilha: total de cada mes e de cada ano e a tabela por dotacao.</summary>
    private void MostrarCalculo(IReadOnlyList<AjusteMensal> ajustes)
    {
        var c = _calculo!;
        Meses.Clear();
        Anos.Clear();
        Dotacoes.Clear();
        int validas = Linhas.Count(l => !l.ParaObra().Vazia);
        TemCalculo = c.Obras.Count > 0;
        if (!TemCalculo)
        {
            Resumo = validas == 0 ? "Adicione as obras: linha por linha, colando do Excel ou a partir da Base de dados." : "Complete as obras marcadas em vermelho para ver o cronograma.";
            Totais = "";
            return;
        }

        var ajustados = ajustes.Select(a => a.Mes).ToHashSet();
        foreach (var mes in c.Meses)
        {
            Meses.Add(new ItemCronograma(ColagemCronograma.Mes(mes), FormatoBase.Reais(c.DoMes(mes)), ajustados.Contains(mes) ? "ajustado" : "", ajustados.Contains(mes)));
        }

        foreach (int ano in c.Anos)
        {
            Anos.Add(new ItemCronograma($"Total {ano}", FormatoBase.Reais(c.DoAno(ano))));
        }

        foreach (var d in c.Dotacoes)
        {
            Dotacoes.Add(new ItemCronograma(d.Dotacao, FormatoBase.Reais(d.Valor), d.DaRemuneracao ? "Remuneração" : "Obra"));
        }

        string pendentes = c.Obras.Count < validas ? $" ({validas - c.Obras.Count} com pendência, fora da conta)" : "";
        Resumo = $"{Formato.Plural(c.Obras.Count, "obra", "obras")}{pendentes} · total solicitado {FormatoBase.Reais(c.Total)}";
        Totais = $"Obras {FormatoBase.Reais(c.TotalObra)} + remuneração {FormatoBase.Reais(c.TotalRemuneracao)} = {FormatoBase.Reais(c.Total)}";
    }

    /// <summary>Refaz a lista "Todas as obras, 1 - Escola..." so quando ela muda, e devolve a escolha de cada ajuste.</summary>
    private void AtualizarOpcoesDeObra()
    {
        var novas = new List<string> { TodasAsObras };
        novas.AddRange(Linhas.Select((l, i) => $"{i + 1} - " + (l.Nome.Trim().Length > 0 ? l.Nome.Trim() : "(sem nome)")));
        if (novas.SequenceEqual(OpcoesDeObra))
        {
            return;
        }

        OpcoesDeObra.Clear();
        foreach (string o in novas)
        {
            OpcoesDeObra.Add(o);
        }

        foreach (var a in Ajustes)
        {
            a.Avisar();
        }
    }

    private void Restaurar()
    {
        if (_rascunhos.Ler<RascunhoCronograma>(ChaveRascunho) is not { } r)
        {
            return;
        }

        _restaurando = true;
        GerarPdf = r.GerarPdf;
        Remuneracao = string.IsNullOrWhiteSpace(r.Remuneracao) ? "10" : r.Remuneracao;
        NomeCronograma = r.NomeCronograma ?? "";
        foreach (var l in r.Linhas)
        {
            var linha = new LinhaCronogramaVm(Conferir) { Origem = l.Origem, DataOsDaBase = l.DataOs };
            linha.Carregar(() =>
            {
                linha.Portaria = l.Portaria;
                linha.Lote = l.Lote;
                linha.Dotacao = l.Dotacao;
                linha.Nome = l.Nome;
                linha.Escopo = l.Escopo;
                linha.Valor = l.Valor;
                linha.Inicio = l.Inicio;
                linha.Fim = l.Fim;
            });
            Linhas.Add(linha);
        }

        foreach (var a in r.Ajustes ?? [])
        {
            var ajuste = new AjusteCronogramaVm(Conferir) { Mes = a.Mes, Tipo = a.EmReais ? 1 : 0, Quantia = a.Quantia, Para = a.Para };
            Ajustes.Add(ajuste);
        }

        _restaurando = false;
    }
}
