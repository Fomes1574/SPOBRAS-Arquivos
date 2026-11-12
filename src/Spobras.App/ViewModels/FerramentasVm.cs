using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Spobras.App.Servicos;
using Spobras.Core;
using Spobras.Core.Abstracoes;
using Spobras.Core.Ferramentas;
using Spobras.Core.Nomes;
using Spobras.Core.Operacoes;
using Spobras.Core.Tarefas;
using Spobras.Documentos.Ferramentas;

namespace Spobras.App.ViewModels;

public enum FerramentaExtra
{
    Buscar,
    Juntar,
    Duplicados,
    ExcelPdf,
}

public sealed record AchadoVm(AchadoBusca Achado)
{
    public string Nome => Path.GetFileName(Achado.Caminho);
    public string Pasta => Path.GetDirectoryName(Achado.Caminho) ?? "";
    public string Trecho => Achado.Trecho;
}

public sealed record GrupoVm(GrupoDuplicados Grupo)
{
    public string Titulo => $"{Formato.Plural(Grupo.Arquivos.Count, "arquivo igual", "arquivos iguais")} · {Tamanho.Formatar(Grupo.Tamanho)} cada";
    public string Original => Grupo.Arquivos[0];
    public IReadOnlyList<string> Copias => [.. Grupo.Arquivos.Skip(1)];
}

/// <summary>
/// Funcoes complementares (especificacao 4.9): busca dentro dos arquivos, juntar planilhas, arquivos
/// repetidos e Excel para PDF em lote. Nenhuma altera ou apaga os arquivos de origem.
/// </summary>
public sealed partial class FerramentasVm : TelaVm
{
    public const string Modulo = "ferramentas";
    public const string PastaCopias = "Cópias repetidas";

    private readonly IDialogos _dialogos;
    private readonly ServicoOperacoes _operacoes;
    private readonly ServicoPreferencias _preferencias;
    private readonly IIndiceTexto _indice;
    private readonly IExportadorPdf _exportador;
    private readonly IRelogio _relogio;
    private PreviaJuntar? _previa;
    private string? _ultimoResultado;
    private Lote? _ultimoLote;

    public FerramentasVm(
        IDialogos dialogos, ServicoOperacoes operacoes, ServicoPreferencias preferencias, IIndiceTexto indice, IExportadorPdf exportador, IRelogio relogio)
    {
        _dialogos = dialogos;
        _operacoes = operacoes;
        _preferencias = preferencias;
        _indice = indice;
        _exportador = exportador;
        _relogio = relogio;
        _pastaDestino = preferencias.DestinoDe(Modulo);
        RestaurarPastas();
        Atualizar();
    }

    public override string Titulo => "Ferramentas";

    public ObservableCollection<string> Pastas { get; } = [];
    public ObservableCollection<string> Arquivos { get; } = [];
    public ObservableCollection<AchadoVm> Achados { get; } = [];
    public ObservableCollection<GrupoVm> Grupos { get; } = [];
    public ObservableCollection<string> LinhasPrevia { get; } = [];
    public ObservableCollection<AvisoVm> Avisos { get; } = [];

    [ObservableProperty]
    private FerramentaExtra _ferramenta = FerramentaExtra.Buscar;

    [ObservableProperty]
    private string _termo = "";

    [ObservableProperty]
    private string _pastaDestino;

    [ObservableProperty]
    private string _nomeSaida = "Planilhas juntas";

    [ObservableProperty]
    private string _textoBotao = "";

    [ObservableProperty]
    private string _resumo = "";

    [ObservableProperty]
    private bool _podeAbrirResultado;

    [ObservableProperty]
    private bool _podeDesfazer;

    public bool EhBuscar => Ferramenta == FerramentaExtra.Buscar;
    public bool EhJuntar => Ferramenta == FerramentaExtra.Juntar;
    public bool EhDuplicados => Ferramenta == FerramentaExtra.Duplicados;
    public bool EhExcelPdf => Ferramenta == FerramentaExtra.ExcelPdf;
    public bool UsaPastas => Ferramenta is FerramentaExtra.Buscar or FerramentaExtra.Duplicados;
    public bool UsaArquivos => !UsaPastas;
    public bool TemAchados => Achados.Count > 0;
    public bool TemGrupos => Grupos.Count > 0;
    public bool TemEntradas => UsaPastas ? Pastas.Count > 0 : Arquivos.Count > 0;

    partial void OnFerramentaChanged(FerramentaExtra value)
    {
        Arquivos.Clear();
        _previa = null;
        LinhasPrevia.Clear();
        Resultado = "";
        PodeAbrirResultado = false;
        foreach (string nome in new[] { nameof(EhBuscar), nameof(EhJuntar), nameof(EhDuplicados), nameof(EhExcelPdf), nameof(UsaPastas), nameof(UsaArquivos) })
        {
            OnPropertyChanged(nome);
        }

        Atualizar();
    }

    partial void OnTermoChanged(string value) => Atualizar();

    partial void OnNomeSaidaChanged(string value) => Atualizar();

    private void RestaurarPastas()
    {
        Pastas.Clear();
        foreach (string pasta in new[] { _preferencias.Atual.PastaTrabalho }.Where(p => p.Length > 0 && Directory.Exists(p)))
        {
            Pastas.Add(pasta);
        }
    }

    /// <summary>Abre a busca ja com o termo (por exemplo, o contrato da obra escolhida na Base de dados).</summary>
    public void BuscarPor(string termo)
    {
        Ferramenta = FerramentaExtra.Buscar;
        Termo = termo;
        Achados.Clear();
        OnPropertyChanged(nameof(TemAchados));
        Resultado = Pastas.Count == 0 ? "Escolha as pastas onde procurar." : $"Clique em Procurar para buscar “{termo}” nas pastas da lista.";
        ResultadoComFalha = false;
    }

    [RelayCommand]
    private void AdicionarPasta()
    {
        foreach (string pasta in _dialogos.EscolherPastas("Pastas onde procurar", Pastas.LastOrDefault() ?? _preferencias.Atual.PastaTrabalho))
        {
            if (!Pastas.Contains(pasta, StringComparer.OrdinalIgnoreCase))
            {
                Pastas.Add(pasta);
            }
        }

        Atualizar();
    }

    [RelayCommand]
    private void RemoverPasta(string pasta)
    {
        Pastas.Remove(pasta);
        Atualizar();
    }

    [RelayCommand]
    private void AdicionarArquivos() =>
        Adicionar(_dialogos.EscolherArquivos(EhJuntar ? "Planilhas para juntar" : "Planilhas para transformar em PDF", "Planilhas do Excel|*.xlsx;*.xlsm", _preferencias.Atual.PastaTrabalho));

    public void Adicionar(IEnumerable<string> caminhos)
    {
        if (UsaPastas)
        {
            foreach (string pasta in caminhos.Where(Directory.Exists).Where(p => !Pastas.Contains(p, StringComparer.OrdinalIgnoreCase)))
            {
                Pastas.Add(pasta);
            }

            Atualizar();
            return;
        }

        var ignorados = new List<string>();
        foreach (string caminho in caminhos.SelectMany(c => Directory.Exists(c) ? Directory.EnumerateFiles(c).Order(OrdemNatural.Instancia) : (IEnumerable<string>)[c]))
        {
            string ext = Path.GetExtension(caminho);
            if (!ext.Equals(".xlsx", StringComparison.OrdinalIgnoreCase) && !ext.Equals(".xlsm", StringComparison.OrdinalIgnoreCase))
            {
                ignorados.Add(Path.GetFileName(caminho));
                continue;
            }

            if (!Arquivos.Contains(caminho, StringComparer.OrdinalIgnoreCase))
            {
                Arquivos.Add(caminho);
            }
        }

        _previa = EhJuntar && Arquivos.Count > 0 ? JuntarPlanilhas.Analisar([.. Arquivos]) : null;
        Atualizar();
        if (ignorados.Count > 0)
        {
            Avisos.Add(new AvisoVm(Gravidade.Informacao, $"Ficaram de fora (não são planilhas .xlsx ou .xlsm): {string.Join(", ", ignorados.Take(5))}{(ignorados.Count > 5 ? "…" : "")}."));
        }
    }

    [RelayCommand]
    private void RemoverArquivo(string caminho)
    {
        Arquivos.Remove(caminho);
        _previa = EhJuntar && Arquivos.Count > 0 ? JuntarPlanilhas.Analisar([.. Arquivos]) : null;
        Atualizar();
    }

    [RelayCommand]
    private void EscolherDestino()
    {
        if (_dialogos.EscolherPasta("Salvar em", PastaDestino) is { } pasta)
        {
            PastaDestino = pasta;
            _preferencias.LembrarDestino(Modulo, pasta);
        }
    }

    [RelayCommand]
    private void Abrir(string caminho) => _dialogos.MostrarNaPasta(caminho);

    [RelayCommand]
    private void AbrirResultado()
    {
        if (_ultimoResultado is not null)
        {
            _dialogos.MostrarNaPasta(_ultimoResultado);
        }
    }

    private void Atualizar()
    {
        Avisos.Clear();
        LinhasPrevia.Clear();
        OnPropertyChanged(nameof(TemEntradas));
        TextoBotao = Ferramenta switch
        {
            FerramentaExtra.Buscar => "Procurar",
            FerramentaExtra.Juntar => Arquivos.Count > 1 ? $"Juntar {Arquivos.Count} planilhas" : "Juntar planilhas",
            FerramentaExtra.Duplicados => "Procurar repetidos",
            _ => Arquivos.Count > 1 ? $"Gerar {Arquivos.Count} PDFs" : "Gerar o PDF",
        };

        switch (Ferramenta)
        {
            case FerramentaExtra.Buscar:
                Resumo = $"Procura no nome e dentro de planilhas, PDFs com texto, Word e .txt/.csv. O que já foi lido fica guardado e só é lido de novo se o arquivo mudar.";
                if (Termo.Trim().Length is > 0 and < 3)
                {
                    Avisos.Add(new AvisoVm(Gravidade.ImpedeGerar, "Digite pelo menos 3 letras ou números."));
                }

                break;
            case FerramentaExtra.Duplicados:
                Resumo = "Compara o conteúdo, não o nome. Nada é apagado: você pode separar as cópias numa pasta, com Desfazer.";
                break;
            case FerramentaExtra.Juntar:
                Resumo = "Casa as colunas pelo título e marca de qual arquivo veio cada linha. Nada é somado nem recalculado.";
                if (_previa is not null)
                {
                    foreach (var origem in _previa.Origens)
                    {
                        string nome = Path.GetFileName(origem.Arquivo);
                        if (!origem.Ok)
                        {
                            Avisos.Add(new AvisoVm(Gravidade.PrecisaConferir, $"{nome}: não entra ({origem.Problema})."));
                            continue;
                        }

                        var faltam = _previa.Faltando(origem);
                        LinhasPrevia.Add($"{nome} · {origem.Onde} · {Formato.Plural(origem.Linhas.Count, "linha", "linhas")}"
                                         + (faltam.Count > 0 ? $" · sem: {string.Join(", ", faltam)}" : ""));
                    }

                    if (_previa.Colunas.Count > 0)
                    {
                        Avisos.Add(new AvisoVm(Gravidade.Informacao, $"Colunas da planilha nova: {string.Join(", ", _previa.Colunas)}. Total: {Formato.Plural(_previa.TotalLinhas, "linha", "linhas")}."));
                    }

                    if (_previa.Origens.Count(o => o.Ok) > 1 && _previa.Origens.Where(o => o.Ok).Any(o => _previa.Faltando(o).Count > 0))
                    {
                        Avisos.Add(new AvisoVm(Gravidade.PrecisaConferir, "Nem todos os arquivos têm as mesmas colunas: as que faltam ficam em branco. Confira se os títulos estão escritos do mesmo jeito."));
                    }
                }

                if (NomeArquivo.Problema(NomeSaida.Trim() + ".xlsx") is { } problema)
                {
                    Avisos.Add(new AvisoVm(Gravidade.ImpedeGerar, "Nome do arquivo: " + problema));
                }

                break;
            default:
                Resumo = "Usa o Excel deste computador, como na impressão. Cada planilha vira um PDF com o mesmo nome; a planilha não é alterada.";
                if (!_exportador.Disponivel)
                {
                    Avisos.Add(new AvisoVm(Gravidade.ImpedeGerar, "O Excel não foi encontrado neste computador. Esta função precisa dele."));
                }

                break;
        }

        ExecutarCommand.NotifyCanExecuteChanged();
    }

    private bool PodeExecutar() =>
        !Ocupado && !Avisos.Any(a => a.Impede) && Ferramenta switch
        {
            FerramentaExtra.Buscar => Pastas.Count > 0 && Termo.Trim().Length >= 3,
            FerramentaExtra.Duplicados => Pastas.Count > 0,
            FerramentaExtra.Juntar => _previa is not null && _previa.TotalLinhas > 0,
            _ => Arquivos.Count > 0,
        };

    [RelayCommand(CanExecute = nameof(PodeExecutar))]
    private async Task ExecutarAsync()
    {
        Ocupado = true;
        ExecutarCommand.NotifyCanExecuteChanged();
        try
        {
            switch (Ferramenta)
            {
                case FerramentaExtra.Buscar:
                    await BuscarAsync();
                    break;
                case FerramentaExtra.Duplicados:
                    await ProcurarRepetidosAsync();
                    break;
                case FerramentaExtra.Juntar:
                    await JuntarAsync();
                    break;
                default:
                    await ExcelPdfAsync();
                    break;
            }
        }
        finally
        {
            Ocupado = false;
            ExecutarCommand.NotifyCanExecuteChanged();
        }
    }

    private async Task BuscarAsync()
    {
        string termo = Termo.Trim();
        var pastas = Pastas.ToList();
        ResultadoBusca? r = null;
        var tarefa = _operacoes.Fila.Enfileirar($"Buscar “{termo}”", (progresso, cancelamento) =>
        {
            r = BuscaArquivos.Buscar(pastas, termo, _indice, new Progresso<(int, string)>(p => progresso.Report(new ProgressoTarefa(p.Item1, 0, p.Item2))), cancelamento);
            return Task.FromResult<string?>(Formato.Plural(r.Achados.Count, "arquivo encontrado", "arquivos encontrados"));
        });
        await FilaTarefas.Aguardar(tarefa);

        Achados.Clear();
        foreach (var achado in r?.Achados ?? [])
        {
            Achados.Add(new AchadoVm(achado));
        }

        OnPropertyChanged(nameof(TemAchados));
        if (r is null)
        {
            Resultado = "A busca não terminou.";
            ResultadoComFalha = true;
            return;
        }

        ResultadoComFalha = false;
        Resultado = (r.Achados.Count == 0 ? $"Nenhum arquivo com “{termo}”" : $"“{termo}” em {Formato.Plural(r.Achados.Count, "arquivo", "arquivos")}")
                    + $" ({Formato.Plural(r.Arquivos, "arquivo olhado", "arquivos olhados")}"
                    + (r.SemLeitura > 0 ? $"; {r.SemLeitura} sem texto para ler, como PDF digitalizado" : "") + ")."
                    + (r.Interrompida ? $" A busca parou em {BuscaArquivos.LimiteArquivos:N0} arquivos ou foi cancelada: escolha pastas menores." : "");
    }

    private async Task ProcurarRepetidosAsync()
    {
        var pastas = Pastas.ToList();
        IReadOnlyList<GrupoDuplicados> grupos = [];
        string? erro = null;
        var tarefa = _operacoes.Fila.Enfileirar("Procurar arquivos repetidos", (progresso, cancelamento) =>
        {
            try
            {
                grupos = Duplicados.Procurar(pastas, new Progresso<int>(n => progresso.Report(new ProgressoTarefa(n, 0, null))), cancelamento);
            }
            catch (OperationCanceledException)
            {
                erro = "Cancelado.";
            }

            return Task.FromResult<string?>(Formato.Plural(grupos.Count, "grupo de repetidos", "grupos de repetidos"));
        });
        await FilaTarefas.Aguardar(tarefa);

        Grupos.Clear();
        foreach (var grupo in grupos.Where(g => !g.Arquivos.Skip(1).All(EstaNaPastaDeCopias)))
        {
            Grupos.Add(new GrupoVm(grupo));
        }

        OnPropertyChanged(nameof(TemGrupos));
        ResultadoComFalha = erro is not null;
        long sobra = Grupos.Sum(g => g.Grupo.Sobra);
        Resultado = erro ?? (Grupos.Count == 0
            ? "Nenhum arquivo repetido nessas pastas."
            : $"{Formato.Plural(Grupos.Count, "grupo", "grupos")} de arquivos iguais; as cópias ocupam {Tamanho.Formatar(sobra)}. O primeiro de cada grupo (o mais antigo) fica onde está.");
    }

    private static bool EstaNaPastaDeCopias(string caminho) =>
        Path.GetFileName(Path.GetDirectoryName(caminho) ?? "").Equals(PastaCopias, StringComparison.OrdinalIgnoreCase);

    /// <summary>Move as copias (nao o mais antigo) para "Copias repetidas" na pasta de cada uma. Com previa e Desfazer.</summary>
    [RelayCommand]
    private async Task SepararCopiasAsync()
    {
        var copias = Grupos.SelectMany(g => g.Copias).Where(c => !EstaNaPastaDeCopias(c)).ToList();
        if (copias.Count == 0)
        {
            return;
        }

        if (!_dialogos.Confirmar("Separar as cópias",
                $"{Formato.Plural(copias.Count, "cópia vai", "cópias vão")} para uma pasta “{PastaCopias}” dentro da pasta onde cada uma está. "
                + "O arquivo mais antigo de cada grupo fica onde está. Nada é apagado e dá para desfazer pelo Histórico.\n\nSeparar agora?", "Separar"))
        {
            return;
        }

        var reservados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var itens = new List<ItemLote>();
        foreach (string copia in copias)
        {
            string pasta = Path.Combine(Path.GetDirectoryName(copia)!, PastaCopias);
            string nome = NomeArquivo.Livre(Path.GetFileName(copia), n => File.Exists(Path.Combine(pasta, n)) || reservados.Contains(Path.Combine(pasta, n)));
            string destino = Path.Combine(pasta, nome);
            reservados.Add(destino);
            itens.Add(new ItemLote { Ordem = itens.Count, Origem = copia, Destino = destino });
        }

        var lote = new Lote { Tipo = TipoLote.Mover, Descricao = $"Separou {Formato.Plural(itens.Count, "cópia repetida", "cópias repetidas")}", CriadoEm = _relogio.Agora, Itens = itens };
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
        PodeDesfazer = lote.Contar(EstadoItem.Concluido) > 0;
        int falhas = lote.Contar(EstadoItem.Falhou);
        ResultadoComFalha = falhas > 0;
        Resultado = $"{Formato.Plural(lote.Contar(EstadoItem.Concluido), "cópia separada", "cópias separadas")} em “{PastaCopias}”."
                    + (falhas > 0 ? $" {Formato.Plural(falhas, "falhou", "falharam")}: {lote.Itens.First(i => i.Estado == EstadoItem.Falhou).Mensagem}" : "");
        Grupos.Clear();
        OnPropertyChanged(nameof(TemGrupos));
    }

    [RelayCommand]
    private async Task DesfazerAsync()
    {
        if (_ultimoLote is null)
        {
            return;
        }

        var plano = _operacoes.PlanejarDesfazer(_ultimoLote);
        await _operacoes.DesfazerAsync(plano);
        PodeDesfazer = false;
        Resultado = "As cópias voltaram para onde estavam.";
        ResultadoComFalha = false;
    }

    private async Task JuntarAsync()
    {
        var previa = _previa!;
        string destino = PastaDestino;
        string saida = Path.Combine(destino, NomeArquivo.Livre(NomeSaida.Trim() + ".xlsx", n => File.Exists(Path.Combine(destino, n))));
        string? erro = null;
        var tarefa = _operacoes.Fila.Enfileirar("Juntar planilhas", (_, _) =>
        {
            try
            {
                JuntarPlanilhas.Gravar(previa, saida);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                erro = ex.Message;
            }

            return Task.FromResult<string?>(erro ?? Path.GetFileName(saida));
        });
        await FilaTarefas.Aguardar(tarefa);

        ResultadoComFalha = erro is not null;
        if (erro is not null)
        {
            Resultado = $"A planilha não foi gerada: {erro}";
            return;
        }

        _operacoes.RegistrarCriados($"Juntou {Formato.Plural(previa.Origens.Count(o => o.Ok), "planilha", "planilhas")} em “{Path.GetFileName(saida)}”", [saida]);
        _ultimoResultado = saida;
        PodeAbrirResultado = true;
        Resultado = $"“{Path.GetFileName(saida)}” gerado com {Formato.Plural(previa.TotalLinhas, "linha", "linhas")}. Os arquivos de origem não foram alterados.";
        Arquivos.Clear();
        _previa = null;
        Atualizar();
    }

    private async Task ExcelPdfAsync()
    {
        var arquivos = Arquivos.ToList();
        string destino = PastaDestino;
        var criados = new List<string>();
        var falhas = new List<string>();
        var tarefa = _operacoes.Fila.Enfileirar($"Excel para PDF: {Formato.Plural(arquivos.Count, "planilha", "planilhas")}", (progresso, cancelamento) =>
        {
            for (int i = 0; i < arquivos.Count && !cancelamento.IsCancellationRequested; i++)
            {
                progresso.Report(new ProgressoTarefa(i, arquivos.Count, Path.GetFileName(arquivos[i])));
                string pdf = Path.Combine(destino, NomeArquivo.Livre(Path.GetFileNameWithoutExtension(arquivos[i]) + ".pdf", n => File.Exists(Path.Combine(destino, n))));
                try
                {
                    Directory.CreateDirectory(destino);
                    _exportador.Exportar(arquivos[i], pdf);
                    criados.Add(pdf);
                }
                catch (Exception ex)
                {
                    falhas.Add($"{Path.GetFileName(arquivos[i])}: {ex.Message}");
                }
            }

            return Task.FromResult<string?>(Formato.Plural(criados.Count, "PDF criado", "PDFs criados"));
        });
        await FilaTarefas.Aguardar(tarefa);

        if (criados.Count > 0)
        {
            _operacoes.RegistrarCriados(criados.Count == 1 ? $"Gerou “{Path.GetFileName(criados[0])}”" : $"Gerou {criados.Count} PDFs de planilhas", criados);
            _ultimoResultado = criados[0];
            PodeAbrirResultado = true;
        }

        ResultadoComFalha = falhas.Count > 0;
        Resultado = $"{Formato.Plural(criados.Count, "PDF criado", "PDFs criados")}." + (falhas.Count > 0 ? " Não foram: " + string.Join("; ", falhas) : "");
        if (falhas.Count == 0)
        {
            Arquivos.Clear();
        }

        Atualizar();
    }

    private sealed class Progresso<T>(Action<T> aoMudar) : IProgress<T>
    {
        public void Report(T value) => aoMudar(value);
    }
}
