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
using Spobras.Documentos.Modelos;
using Spobras.Documentos.Planilhas;

namespace Spobras.App.ViewModels;

public sealed record LinhaRascunho(
    string Portaria, string Lote, string Dotacao, string Nome, string Escopo, string Valor, string Inicio, string Fim, OrigemInicio Origem, DateOnly? DataOs);

public sealed record RascunhoCronograma(List<LinhaRascunho> Linhas, bool AjustarInicio, bool GerarPdf, string NomeCronograma = "", string Remuneracao = "10");

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

    public OrigemInicio Origem { get; set; }
    public DateOnly? DataOsDaBase { get; set; }

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (!_carregando && e.PropertyName is not (nameof(Avisos) or nameof(Impedida) or nameof(Numero)))
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

    /// <summary>So o mes importa: inicio e fim vao para a planilha como dia 1 do mes (o modelo mostra "nov-26").</summary>
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

/// <summary>Cronograma padrao: linhas de obras, conferencia e geracao a partir do modelo oficial.</summary>
public sealed partial class CronogramaVm : TelaVm
{
    public const string Modulo = "cronograma";
    private const string ChaveRascunho = "cronograma-atual";

    private readonly IDialogos _dialogos;
    private readonly ServicoOperacoes _operacoes;
    private readonly ServicoPreferencias _preferencias;
    private readonly ServicoModelos _modelos;
    private readonly RascunhoRepositorio _rascunhos;
    private readonly IVerificadorCronograma _verificador;
    private readonly IRelogio _relogio;
    private readonly Func<IReadOnlyDictionary<string, string>> _apelidos;
    private ModeloCronogramaLido? _modelo;
    private bool _restaurando;
    private string? _ultimoResultado;

    public CronogramaVm(
        IDialogos dialogos, ServicoOperacoes operacoes, ServicoPreferencias preferencias, ServicoModelos modelos, RascunhoRepositorio rascunhos,
        IVerificadorCronograma verificador, IRelogio relogio, Func<IReadOnlyDictionary<string, string>>? apelidos = null)
    {
        _dialogos = dialogos;
        _operacoes = operacoes;
        _preferencias = preferencias;
        _modelos = modelos;
        _rascunhos = rascunhos;
        _verificador = verificador;
        _relogio = relogio;
        _apelidos = apelidos ?? (() => new Dictionary<string, string>());
        _pastaDestino = preferencias.DestinoDe(Modulo);
        CarregarModelo();
        Restaurar();
        Conferir();
    }

    public override string Titulo => "Cronograma padrão";

    public ObservableCollection<LinhaCronogramaVm> Linhas { get; } = [];
    public ObservableCollection<AvisoVm> Avisos { get; } = [];
    public ObservableCollection<string> Abas { get; } = [];
    public ObservableCollection<string> Dotacoes { get; } = [];

    [ObservableProperty]
    private string _estadoDoModelo = "";

    [ObservableProperty]
    private bool _temModelo;

    [ObservableProperty]
    private string? _aba;


    [ObservableProperty]
    private bool _gerarPdf;

    /// <summary>Nome do arquivo escolhido pelo usuario; vazio = nome sugerido por portaria e lote.</summary>
    [ObservableProperty]
    private string _nomeCronograma = "";

    /// <summary>Percentual de remuneracao. Padrao 10%, o do modelo.</summary>
    [ObservableProperty]
    private string _remuneracao = "10";

    partial void OnRemuneracaoChanged(string value) => Conferir();

    /// <summary>0,10 para "10"; null se o texto nao for um percentual valido (0 a 100).</summary>
    public decimal? PercentualRemuneracao =>
        decimal.TryParse(Remuneracao.Replace("%", "").Trim(), System.Globalization.NumberStyles.Number, Formato.PtBr, out decimal p) && p >= 0 && p <= 100
            ? Math.Round(p / 100m, 4)
            : null;

    partial void OnNomeCronogramaChanged(string value) => Conferir();

    [ObservableProperty]
    private string _pastaDestino;

    [ObservableProperty]
    private string _resumo = "";

    [ObservableProperty]
    private string _textoBotao = "Gerar cronograma";

    [ObservableProperty]
    private bool _podeAbrirResultado;

    public bool ExcelDisponivel => _verificador.Disponivel;
    public string TextoDotacoes => Dotacoes.Count == 0 ? "" : "Dotações do modelo: " + string.Join(", ", Dotacoes);

    public override void AoEntrar()
    {
        CarregarModelo();
        Conferir();
    }


    partial void OnGerarPdfChanged(bool value) => Conferir();

    [RelayCommand]
    private void EscolherModelo()
    {
        string? arquivo = _dialogos.EscolherArquivos("Escolha o modelo do cronograma", "Planilhas do Excel|*.xlsx;*.xlsm", _preferencias.Atual.PastaPlanilhasBase).FirstOrDefault();
        if (arquivo is null)
        {
            return;
        }

        try
        {
            _modelos.DefinirModeloCronograma(arquivo);
            Resultado = "Modelo cadastrado. O app guarda uma cópia; o seu arquivo não é alterado.";
            ResultadoComFalha = false;
        }
        catch (Exception ex) when (ex is PlanilhaInvalidaException or IOException or UnauthorizedAccessException)
        {
            _dialogos.Avisar("Modelo do cronograma", ex.Message);
        }

        CarregarModelo();
        Conferir();
    }

    [RelayCommand]
    private void NovaLinha()
    {
        Linhas.Add(new LinhaCronogramaVm(Conferir));
        Conferir();
    }

    [RelayCommand]
    private void RemoverLinha(LinhaCronogramaVm linha)
    {
        Linhas.Remove(linha);
        Conferir();
    }

    [RelayCommand]
    private void Limpar()
    {
        if (Linhas.Count == 0 || _dialogos.Confirmar("Começar outro cronograma", "Limpar as linhas deste rascunho? Os cronogramas já gerados não são apagados.", "Limpar"))
        {
            Linhas.Clear();
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

    /// <summary>Cola linhas copiadas do Excel, nas colunas do modelo.</summary>
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
        // Portaria como a planilha mestra identifica (com o ano), no formato do cronograma: "Nº 03 (2021)".
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
        Resultado = $"{obra.Empreendimento} entrou no cronograma" + (obra.DataOs is null ? " sem Data da OS: informe o início previsto." : " com o início no mês da Data da OS.") + " Informe o valor e o fim.";
        ResultadoComFalha = false;
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

    private bool PodeGerar() => _modelo is not null && Aba is not null && !Ocupado && PercentualRemuneracao is not null && Validar().PodeGerar && !Linhas.Any(l => l.ValorInvalido || l.InicioInvalido || l.FimInvalido);

    [RelayCommand(CanExecute = nameof(PodeGerar))]
    private async Task GerarAsync()
    {
        string modelo = _modelos.ModeloCronograma!;
        string aba = Aba!;
        string extensao = Path.GetExtension(modelo);
        string destino = PastaDestino;
        var grupos = ValidadorCronograma.Dividir(Obras());
        bool pdf = GerarPdf && _verificador.Disponivel;
        string nomeEscolhido = NomeArquivo.Corrigir(NomeCronograma.Trim()).Trim();
        decimal percentual = PercentualRemuneracao ?? ModeloCronograma.PadraoRemuneracao;
        var criados = new List<string>();
        var notas = new List<string>();
        bool falha = false;

        Ocupado = true;
        GerarCommand.NotifyCanExecuteChanged();
        try
        {
            var tarefa = _operacoes.Fila.Enfileirar($"Cronograma: {Formato.Plural(grupos.Count, "arquivo", "arquivos")}", (progresso, cancelamento) =>
            {
                for (int g = 0; g < grupos.Count && !cancelamento.IsCancellationRequested; g++)
                {
                    progresso.Report(new ProgressoTarefa(g, grupos.Count, _verificador.Disponivel ? "Aguardando o Excel calcular" : null));
                    string nome = nomeEscolhido.Length == 0
                        ? ModeloCronograma.NomeSugerido(grupos[g], g, grupos.Count)
                        : grupos.Count == 1 ? nomeEscolhido : $"{nomeEscolhido} - parte {g + 1} de {grupos.Count}";
                    string saida = Path.Combine(destino, NomeArquivo.Livre(nome + extensao, n => File.Exists(Path.Combine(destino, n))));
                    try
                    {
                        ModeloCronograma.Gerar(modelo, aba, grupos[g], saida, percentual);
                        criados.Add(saida);
                        notas.Add(ConferirNoExcel(saida, aba, grupos[g], pdf, criados, ref falha));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlanilhaInvalidaException or EscritaBloqueadaException)
                    {
                        falha = true;
                        notas.Add($"{nome}: não foi gerado. {ex.Message}");
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

        if (criados.Count > 0)
        {
            var planilhas = criados.Where(c => !c.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)).ToList();
            string comPdf = planilhas.Count < criados.Count ? " (com PDF)" : "";
            _operacoes.RegistrarCriados(
                planilhas.Count == 1
                    ? $"Gerou “{Path.GetFileName(planilhas[0])}”{comPdf}"
                    : $"Gerou {planilhas.Count} arquivos de cronograma em {Path.GetFileName(destino.TrimEnd('\\', '/'))}{comPdf}",
                criados);
            _ultimoResultado = criados[0];
            PodeAbrirResultado = true;
        }

        ResultadoComFalha = falha;
        Resultado = string.Join(" ", notas);
    }

    [RelayCommand]
    private void AbrirResultado()
    {
        if (_ultimoResultado is not null)
        {
            _dialogos.MostrarNaPasta(_ultimoResultado);
        }
    }

    /// <summary>Recalcula no Excel e compara com o que foi digitado. Sem Excel, diz que a conferencia nao foi feita.</summary>
    private string ConferirNoExcel(string saida, string aba, IReadOnlyList<ObraCronograma> obras, bool pdf, List<string> criados, ref bool falha)
    {
        string nome = Path.GetFileName(saida);
        if (!_verificador.Disponivel)
        {
            return $"{nome}: gerado. O Excel não foi encontrado para conferir os totais; eles serão calculados quando você abrir o arquivo.";
        }

        try
        {
            string? caminhoPdf = pdf ? Path.ChangeExtension(saida, ".pdf") : null;
            var c = _verificador.Conferir(saida, aba, caminhoPdf);
            if (c.Pdf is not null)
            {
                criados.Add(c.Pdf);
            }

            var problemas = new List<string>();
            decimal esperado = ValidadorCronograma.TotalParaObras(obras);
            if (c.TotalParaObras is not { } total || Math.Abs(total - esperado) > 0.01m)
            {
                problemas.Add($"o total para obras no Excel ({FormatoBase.Reais(c.TotalParaObras)}) é diferente da soma digitada ({FormatoBase.Reais(esperado)})");
            }

            if (c.TotalGeral is { } geral && c.SomaDosMeses is { } meses && Math.Abs(geral - meses) > 0.01m)
            {
                problemas.Add($"a soma dos meses ({FormatoBase.Reais(meses)}) é diferente do total ({FormatoBase.Reais(geral)}): alguma obra tem parte fora dos meses distribuídos");
            }

            if (c.Erros > 0)
            {
                problemas.Add($"{Formato.Plural(c.Erros, "célula com erro", "células com erro")} de cálculo");
            }

            if (c.Resumo.Length > 0 && !c.Resumo.Equals("RESUMO OK", StringComparison.OrdinalIgnoreCase))
            {
                problemas.Add($"o Resumo pedido do modelo indica \"{c.Resumo}\"");
            }

            if (problemas.Count > 0)
            {
                falha = true;
                return $"{nome}: gerado, mas CONFIRA: {string.Join("; ", problemas)}.";
            }

            return $"{nome}: gerado e conferido no Excel (total {FormatoBase.Reais(c.TotalGeral)})" + (c.Pdf is not null ? ", com PDF." : ".");
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or System.Runtime.InteropServices.COMException or TimeoutException)
        {
            falha = true;
            return $"{nome}: gerado, mas o Excel não conseguiu conferir ({ex.Message}). Abra o arquivo e confira os totais.";
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

    private List<ObraCronograma> Obras() => [.. Linhas.Select(l => l.ParaObra())];

    private CronogramaValidado Validar() => ValidadorCronograma.Validar(Obras(), [.. Dotacoes]);

    private void CarregarModelo()
    {
        _modelo = null;
        string? aba = Aba;
        Abas.Clear();
        Dotacoes.Clear();
        if (_modelos.ModeloCronograma is not { } caminho)
        {
            EstadoDoModelo = "Ainda não há modelo de cronograma. Escolha o arquivo do modelo (o app guarda uma cópia e usa sempre ela).";
            TemModelo = false;
        }
        else
        {
            try
            {
                _modelo = ModeloCronograma.Analisar(caminho);
                foreach (string a in _modelo.AbasDeCronograma)
                {
                    Abas.Add(a);
                }

                foreach (string d in _modelo.Dotacoes)
                {
                    Dotacoes.Add(d);
                }

                Aba = aba is not null && Abas.Contains(aba) ? aba : _modelo.AbaPrincipal;
                EstadoDoModelo = $"Modelo cadastrado, com {Formato.Plural(Abas.Count, "aba de cronograma", "abas de cronograma")}. As fórmulas do modelo fazem todos os cálculos.";
                TemModelo = true;
            }
            catch (PlanilhaInvalidaException ex)
            {
                EstadoDoModelo = "O modelo cadastrado não pôde ser usado: " + ex.Message;
                TemModelo = false;
            }
        }

        OnPropertyChanged(nameof(TextoDotacoes));
    }

    private void Conferir()
    {
        if (_restaurando)
        {
            return;
        }

        Avisos.Clear();
        var obras = Obras();
        var validado = ValidadorCronograma.Validar(obras, [.. Dotacoes]);
        for (int i = 0; i < Linhas.Count; i++)
        {
            var linha = Linhas[i];
            var avisos = validado.Linhas.Where(a => a.Linha == i + 1).Select(a => a.Aviso).ToList();
            var textos = avisos.Select(a => a.Mensagem).ToList();
            if (linha.ValorInvalido)
            {
                textos.Insert(0, $"O valor \"{linha.Valor}\" não foi entendido. Use o formato 1.000,00.");
            }

            if (linha.InicioInvalido || linha.FimInvalido)
            {
                textos.Insert(0, "Mês não entendido. Use nov/26 ou 11/2026.");
            }

            linha.Numero = (i + 1).ToString();
            linha.Avisos = string.Join(" ", textos);
            linha.Impedida = avisos.Any(a => a.Gravidade == Gravidade.ImpedeGerar) || linha.ValorInvalido || linha.InicioInvalido || linha.FimInvalido;
        }

        foreach (var aviso in validado.Gerais.Where(a => Linhas.Count > 0 || a.Gravidade != Gravidade.ImpedeGerar))
        {
            Avisos.Add(new AvisoVm(aviso));
        }

        if (PercentualRemuneracao is not { } pct)
        {
            Avisos.Add(new AvisoVm(Gravidade.ImpedeGerar, "Remuneração: use um percentual de 0 a 100, como 10 ou 12,5."));
        }
        else if (pct != ModeloCronograma.PadraoRemuneracao)
        {
            Avisos.Add(new AvisoVm(Gravidade.PrecisaConferir, $"Remuneração de {(pct * 100).ToString("0.##", Formato.PtBr)}% (o padrão é 10%). O cronograma gerado usa esse percentual."));
        }

        if (GerarPdf && !_verificador.Disponivel)
        {
            Avisos.Add(new AvisoVm(Gravidade.PrecisaConferir, "O PDF é feito pelo Excel, que não foi encontrado neste computador. Será gerado só o arquivo Excel."));
        }

        int validas = obras.Count(o => !o.Vazia);
        int grupos = ValidadorCronograma.Dividir(obras).Count;
        Resumo = validas == 0
            ? "Adicione as obras: linha por linha, colando do Excel ou a partir da Base de dados."
            : $"{Formato.Plural(validas, "obra", "obras")} · valor para obras {FormatoBase.Reais(ValidadorCronograma.TotalParaObras(obras))}"
              + (grupos > 1 ? $" · {grupos} cronogramas" : "");
        TextoBotao = grupos > 1 ? $"Gerar {grupos} cronogramas" : "Gerar cronograma";
        GerarCommand.NotifyCanExecuteChanged();
        _rascunhos.Gravar(ChaveRascunho, new RascunhoCronograma(
            [.. Linhas.Select(l => new LinhaRascunho(l.Portaria, l.Lote, l.Dotacao, l.Nome, l.Escopo, l.Valor, l.Inicio, l.Fim, l.Origem, l.DataOsDaBase))],
            true, GerarPdf, NomeCronograma, Remuneracao), _relogio.Agora);
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

        _restaurando = false;
    }
}
