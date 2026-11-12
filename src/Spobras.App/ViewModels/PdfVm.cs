using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Spobras.App.Servicos;
using Spobras.Core;
using Spobras.Core.Nomes;
using Spobras.Core.Pdf;
using Spobras.Core.Tarefas;
using Spobras.Documentos.Imagens;
using Spobras.Documentos.Pdf;

namespace Spobras.App.ViewModels;

public enum FerramentaPdf
{
    Juntar,
    Separar,
    FotosEmPdf,
    Reduzir,
    LogoENumeracao,
    ParaExcel,
}

public sealed record EntradaPdfVm(string Caminho, string Detalhe)
{
    public string Nome => Path.GetFileName(Caminho);
}

/// <summary>PDF: escolhe a ferramenta primeiro e ve so o painel dela (especificacao 7.2).</summary>
public sealed partial class PdfVm : TelaVm
{
    public const string Modulo = "pdf";

    private readonly IDialogos _dialogos;
    private readonly ServicoOperacoes _operacoes;
    private readonly ServicoPreferencias _preferencias;
    private readonly Func<string?> _logo;
    private string? _ultimoResultado;

    public static IReadOnlyList<KeyValuePair<PosicaoCarimbo, string>> Posicoes { get; } =
    [
        new(PosicaoCarimbo.RodapeDireita, "Rodapé, à direita"),
        new(PosicaoCarimbo.RodapeCentro, "Rodapé, no centro"),
        new(PosicaoCarimbo.RodapeEsquerda, "Rodapé, à esquerda"),
        new(PosicaoCarimbo.TopoDireita, "Topo, à direita"),
        new(PosicaoCarimbo.TopoEsquerda, "Topo, à esquerda"),
    ];

    private readonly IOcr? _ocr;

    public PdfVm(IDialogos dialogos, ServicoOperacoes operacoes, ServicoPreferencias preferencias, Func<string?>? logo = null, IOcr? ocr = null)
    {
        _ocr = ocr;
        _logo = logo ?? (() => null);
        _dialogos = dialogos;
        _operacoes = operacoes;
        _preferencias = preferencias;
        _pastaDestino = preferencias.DestinoDe(Modulo);
        Atualizar();
    }

    public override string Titulo => "PDF";

    public ObservableCollection<EntradaPdfVm> Entradas { get; } = [];
    public ObservableCollection<AvisoVm> Avisos { get; } = [];

    [ObservableProperty]
    private FerramentaPdf _ferramenta = FerramentaPdf.Juntar;

    [ObservableProperty]
    private string _nomeSaida = "";

    [ObservableProperty]
    private bool _separarCadaPagina = true;

    [ObservableProperty]
    private string _intervalos = "";

    [ObservableProperty]
    private QualidadeFotos _qualidade = QualidadeFotos.Media;

    [ObservableProperty]
    private string _pastaDestino;

    [ObservableProperty]
    private string _textoBotao = "";

    [ObservableProperty]
    private string _tituloEntradas = "";

    [ObservableProperty]
    private string _textoVazio = "";

    [ObservableProperty]
    private string _textoAdicionar = "";

    [ObservableProperty]
    private bool _podeAbrirResultado;

    [ObservableProperty]
    private bool _numerar = true;

    [ObservableProperty]
    private bool _comLogo;

    [ObservableProperty]
    private PosicaoCarimbo _posicaoNumero = PosicaoCarimbo.RodapeDireita;

    [ObservableProperty]
    private PosicaoCarimbo _posicaoLogo = PosicaoCarimbo.TopoEsquerda;

    [ObservableProperty]
    private string _aPartirDa = "1";

    public bool EhCarimbo => Ferramenta == FerramentaPdf.LogoENumeracao;
    public bool EhParaExcel => Ferramenta == FerramentaPdf.ParaExcel;
    public bool TemLogo => _logo() is not null;

    public bool EhJuntar => Ferramenta == FerramentaPdf.Juntar;
    public bool EhSeparar => Ferramenta == FerramentaPdf.Separar;
    public bool EhFotos => Ferramenta == FerramentaPdf.FotosEmPdf;
    public bool EhReduzir => Ferramenta == FerramentaPdf.Reduzir;
    public bool PedeNome => Ferramenta is FerramentaPdf.Juntar or FerramentaPdf.FotosEmPdf;
    public bool PodeOrdenar => Ferramenta is FerramentaPdf.Juntar or FerramentaPdf.FotosEmPdf;
    public bool TemEntradas => Entradas.Count > 0;
    public bool PorIntervalos => !SepararCadaPagina;

    partial void OnFerramentaChanged(FerramentaPdf value)
    {
        // Cada ferramenta tem a sua lista: nao misturar PDFs de uma com fotos de outra.
        Entradas.Clear();
        NomeSaida = "";
        Resultado = "";
        foreach (string nome in new[] { nameof(EhJuntar), nameof(EhSeparar), nameof(EhFotos), nameof(EhReduzir), nameof(PedeNome), nameof(PodeOrdenar), nameof(EhCarimbo), nameof(EhParaExcel), nameof(TemLogo) })
        {
            OnPropertyChanged(nome);
        }

        Atualizar();
    }

    partial void OnSepararCadaPaginaChanged(bool value)
    {
        OnPropertyChanged(nameof(PorIntervalos));
        Atualizar();
    }

    partial void OnIntervalosChanged(string value) => Atualizar();

    partial void OnNomeSaidaChanged(string value) => Atualizar();

    partial void OnNumerarChanged(bool value) => Atualizar();

    partial void OnComLogoChanged(bool value) => Atualizar();

    partial void OnAPartirDaChanged(string value) => Atualizar();

    public override void AoEntrar()
    {
        OnPropertyChanged(nameof(TemLogo)); // o logo pode ter sido cadastrado em Configuracoes
        if (ComLogo && !TemLogo)
        {
            ComLogo = false;
        }

        Atualizar();
    }

    private int? PaginaInicial() => int.TryParse(APartirDa.Trim(), out int n) && n >= 1 ? n : null;

    [RelayCommand]
    private void AdicionarArquivos()
    {
        string filtro = EhFotos ? "Fotos|*.jpg;*.jpeg;*.png;*.bmp;*.webp;*.gif"
            : EhParaExcel ? "PDF ou imagem escaneada|*.pdf;*.jpg;*.jpeg;*.png;*.bmp;*.tif;*.tiff" : "Arquivos PDF|*.pdf";
        Adicionar(_dialogos.EscolherArquivos(EhFotos ? "Escolha as fotos" : "Escolha os PDFs", filtro, _preferencias.Atual.PastaTrabalho));
    }

    public void Adicionar(IEnumerable<string> caminhos)
    {
        var existentes = new HashSet<string>(Entradas.Select(e => e.Caminho), StringComparer.OrdinalIgnoreCase);
        var problemas = new List<string>();
        foreach (string caminho in caminhos.SelectMany(c => Directory.Exists(c) ? Directory.EnumerateFiles(c).Order(OrdemNatural.Instancia) : (IEnumerable<string>)[c]))
        {
            if (existentes.Contains(caminho))
            {
                continue;
            }

            try
            {
                if (EhFotos)
                {
                    if (!Fotos.Suportada(caminho))
                    {
                        if (!Directory.Exists(Path.GetDirectoryName(caminho)) || caminhos.Contains(caminho))
                        {
                            problemas.Add($"{Path.GetFileName(caminho)}: {Fotos.MotivoNaoSuportada(caminho)}");
                        }

                        continue;
                    }

                    var info = Fotos.Ler(caminho);
                    Entradas.Add(new EntradaPdfVm(caminho, $"{info.Largura} x {info.Altura}"));
                }
                else if (EhParaExcel && Path.GetExtension(caminho).ToLowerInvariant() is ".jpg" or ".jpeg" or ".png" or ".bmp" or ".tif" or ".tiff")
                {
                    Entradas.Add(new EntradaPdfVm(caminho, $"Imagem · {Tamanho.Formatar(new FileInfo(caminho).Length)} · lida pelo OCR"));
                }
                else
                {
                    if (!Path.GetExtension(caminho).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
                    {
                        if (caminhos.Contains(caminho))
                        {
                            problemas.Add($"{Path.GetFileName(caminho)}: não é um PDF.");
                        }

                        continue;
                    }

                    var info = FerramentasPdf.Ler(caminho);
                    string detalhe = $"{Formato.Plural(info.Paginas, "página", "páginas")} · {Tamanho.Formatar(new FileInfo(caminho).Length)}"
                                     + (info.Assinado ? " · assinado digitalmente" : "");
                    if (EhSeparar)
                    {
                        Entradas.Clear(); // separar trabalha com um PDF de cada vez
                    }

                    Entradas.Add(new EntradaPdfVm(caminho, detalhe));
                }

                existentes.Add(caminho);
            }
            catch (Exception ex) when (ex is PdfInvalidoException or FotoInvalidaException)
            {
                problemas.Add(ex is FotoInvalidaException ? $"{Path.GetFileName(caminho)}: {ex.Message}" : ex.Message);
            }
        }

        if (string.IsNullOrWhiteSpace(NomeSaida) && Entradas.Count > 0 && PedeNome)
        {
            NomeSaida = EhJuntar ? Path.GetFileNameWithoutExtension(Entradas[0].Nome) + " (junto)" : "Fotos";
        }

        Resultado = "";
        Atualizar();
        foreach (string problema in problemas)
        {
            Avisos.Add(new AvisoVm(Gravidade.PrecisaConferir, problema));
        }
    }

    [RelayCommand]
    private void Remover(EntradaPdfVm entrada)
    {
        Entradas.Remove(entrada);
        Atualizar();
    }

    [RelayCommand]
    private void Subir(EntradaPdfVm entrada) => Mover(entrada, -1);

    [RelayCommand]
    private void Descer(EntradaPdfVm entrada) => Mover(entrada, +1);

    [RelayCommand]
    private void Limpar()
    {
        Entradas.Clear();
        NomeSaida = "";
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

    private bool PodeExecutar() => Entradas.Count > 0 && !Ocupado && !Avisos.Any(a => a.Impede);

    [RelayCommand(CanExecute = nameof(PodeExecutar))]
    private async Task ExecutarAsync()
    {
        var entradas = Entradas.Select(e => e.Caminho).ToList();
        string destino = PastaDestino;
        var ferramenta = Ferramenta;

        if (ferramenta is FerramentaPdf.Reduzir or FerramentaPdf.LogoENumeracao && Entradas.Any(e => e.Detalhe.Contains("assinado")) &&
            !_dialogos.Confirmar("PDF assinado digitalmente",
                "Há PDF com assinatura digital na lista. A cópia alterada deixa de ter a assinatura válida; o original não é alterado.\n\nContinuar mesmo assim?", "Alterar a cópia"))
        {
            return;
        }

        var carimbo = new OpcoesCarimbo
        {
            Numerar = Numerar,
            PosicaoNumero = PosicaoNumero,
            Logo = ComLogo ? _logo() : null,
            PosicaoLogo = PosicaoLogo,
            APartirDa = PaginaInicial() ?? 1,
        };

        string Livre(string nome) => Path.Combine(destino, NomeArquivo.Livre(nome, n => File.Exists(Path.Combine(destino, n))));
        var (lado, qualidade) = Qualidade switch { QualidadeFotos.Alta => (2000, 85), QualidadeFotos.Media => (1400, 70), _ => (1000, 55) };
        var intervalos = ferramenta == FerramentaPdf.Separar ? IntervalosDoPdf() : null;
        string nome = NomeSaida.Trim();
        var criados = new List<string>();
        var notas = new List<string>();
        string? erro = null;

        Ocupado = true;
        ExecutarCommand.NotifyCanExecuteChanged();
        try
        {
            var tarefa = _operacoes.Fila.Enfileirar($"PDF: {TextoBotao}", (progresso, cancelamento) =>
            {
                try
                {
                    switch (ferramenta)
                    {
                        case FerramentaPdf.Juntar:
                            string junto = Livre(nome + ".pdf");
                            FerramentasPdf.Juntar(entradas, junto, cancelamento);
                            criados.Add(junto);
                            break;
                        case FerramentaPdf.Separar:
                            criados.AddRange(FerramentasPdf.Separar(entradas[0], intervalos!, destino, cancelamento));
                            break;
                        case FerramentaPdf.FotosEmPdf:
                            string fotos = Livre(nome + ".pdf");
                            FerramentasPdf.FotosEmPdf(entradas, fotos, lado, qualidade,
                                new Progresso(n => progresso.Report(new ProgressoTarefa(n, entradas.Count, null))), cancelamento);
                            criados.Add(fotos);
                            break;
                        case FerramentaPdf.LogoENumeracao:
                        case FerramentaPdf.ParaExcel:
                            for (int i = 0; i < entradas.Count; i++)
                            {
                                cancelamento.ThrowIfCancellationRequested();
                                progresso.Report(new ProgressoTarefa(i, entradas.Count, Path.GetFileName(entradas[i])));
                                string original = Path.GetFileName(entradas[i]);
                                try
                                {
                                    if (ferramenta == FerramentaPdf.LogoENumeracao)
                                    {
                                        string saida = Livre(Path.GetFileNameWithoutExtension(entradas[i]) + (carimbo.Logo is null ? " (numerado).pdf" : " (com logo).pdf"));
                                        int paginas = CarimboPdf.Carimbar(entradas[i], saida, carimbo);
                                        criados.Add(saida);
                                        notas.Add($"{original}: {Formato.Plural(paginas, "página marcada", "páginas marcadas")}");
                                    }
                                    else
                                    {
                                        string saida = Livre(Path.GetFileNameWithoutExtension(entradas[i]) + ".xlsx");
                                        var lido = TextoPdf.ParaExcel(entradas[i], saida, cancelamento, _ocr);
                                        criados.Add(saida);
                                        notas.Add($"{original} virou “{Path.GetFileName(saida)}” ({Formato.Plural(lido.Linhas.Count, "linha", "linhas")}"
                                                  + (lido.Tabela is { } t && t.Larguras.Count > 0 ? $", {Formato.Plural(t.Larguras.Count, "coluna", "colunas")})" : ")")
                                                  + (lido.Tabela is { PaginasPorAlinhamento.Count: > 0 } sem ? $"; {Formato.Plural(sem.PaginasPorAlinhamento.Count, "página sem linhas de tabela foi lida", "páginas sem linhas de tabela foram lidas")} pelo alinhamento, confira" : "")
                                                  + (lido.LidasPorOcr.Count > 0 ? $"; {Formato.Plural(lido.LidasPorOcr.Count, "página lida", "páginas lidas")} pelo OCR, confira com atenção" : "")
                                                  + (lido.PaginasSemTexto.Count > 0 ? $"; {Formato.Plural(lido.PaginasSemTexto.Count, "página sem texto ficou", "páginas sem texto ficaram")} de fora" : ""));
                                    }
                                }
                                catch (Exception ex) when (ex is PdfInvalidoException or IOException or UnauthorizedAccessException)
                                {
                                    erro = "Alguns arquivos não foram feitos.";
                                    notas.Add($"{original}: {ex.Message}");
                                }
                            }

                            break;
                        default:
                            for (int i = 0; i < entradas.Count; i++)
                            {
                                cancelamento.ThrowIfCancellationRequested();
                                progresso.Report(new ProgressoTarefa(i, entradas.Count, Path.GetFileName(entradas[i])));
                                var r = FerramentasPdf.Reduzir(entradas[i], Livre(Path.GetFileNameWithoutExtension(entradas[i]) + " (reduzido).pdf"), lado, qualidade, cancelamento);
                                if (r.Reduziu)
                                {
                                    criados.Add(r.Caminho!);
                                    notas.Add($"{Path.GetFileName(entradas[i])}: de {Tamanho.Formatar(r.TamanhoOriginal)} para {Tamanho.Formatar(r.TamanhoNovo)}");
                                }
                                else
                                {
                                    notas.Add($"{Path.GetFileName(entradas[i])}: já está otimizado; não ficou menor e nenhuma cópia foi criada");
                                }
                            }

                            break;
                    }
                }
                catch (OperationCanceledException)
                {
                    erro = "A tarefa foi cancelada. Nenhum arquivo incompleto ficou na pasta.";
                }
                catch (Exception ex) when (ex is PdfInvalidoException or FotoInvalidaException or IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    erro = ex.Message;
                }

                return Task.FromResult<string?>(erro ?? Formato.Plural(criados.Count, "arquivo criado", "arquivos criados"));
            });
            await FilaTarefas.Aguardar(tarefa);
        }
        finally
        {
            Ocupado = false;
            ExecutarCommand.NotifyCanExecuteChanged();
        }

        if (criados.Count > 0)
        {
            _operacoes.RegistrarCriados(
                criados.Count == 1 ? $"Gerou “{Path.GetFileName(criados[0])}”" : $"Gerou {criados.Count} PDFs em {Path.GetFileName(destino.TrimEnd('\\', '/'))}", criados);
            _ultimoResultado = criados[0];
            PodeAbrirResultado = true;
        }

        ResultadoComFalha = erro is not null;
        Resultado = (erro is null && criados.Count > 0 ? "Pronto! " : "") + (ferramenta is FerramentaPdf.Reduzir or FerramentaPdf.LogoENumeracao or FerramentaPdf.ParaExcel && notas.Count > 0
                        ? string.Join("; ", notas) + "."
                        : $"{Formato.Plural(criados.Count, "arquivo criado", "arquivos criados")}" + (criados.Count == 1 ? $": {Path.GetFileName(criados[0])}." : "."))
                    + (erro is not null ? " " + erro : "");

        if (erro is null)
        {
            Entradas.Clear();
            NomeSaida = "";
        }

        Atualizar();
    }

    [RelayCommand]
    private void AbrirResultado()
    {
        if (_ultimoResultado is not null)
        {
            _dialogos.MostrarNaPasta(_ultimoResultado);
        }
    }

    private IReadOnlyList<IntervaloPdf>? IntervalosDoPdf()
    {
        if (Entradas.Count == 0)
        {
            return null;
        }

        int paginas = FerramentasPdf.Ler(Entradas[0].Caminho).Paginas;
        return SepararCadaPagina ? IntervalosPdf.CadaPagina(paginas) : IntervalosPdf.Interpretar(Intervalos, paginas).Intervalos;
    }

    private void Mover(EntradaPdfVm entrada, int passo)
    {
        int de = Entradas.IndexOf(entrada), para = de + passo;
        if (de >= 0 && para >= 0 && para < Entradas.Count)
        {
            Entradas.Move(de, para);
        }
    }

    private void Atualizar()
    {
        Avisos.Clear();
        OnPropertyChanged(nameof(TemEntradas));
        int n = Entradas.Count;
        (TituloEntradas, TextoVazio, TextoAdicionar) = Ferramenta switch
        {
            FerramentaPdf.Juntar => ("PDFs na ordem em que vão ficar", "Arraste os PDFs para cá", "Adicionar PDFs"),
            FerramentaPdf.Separar => ("PDF que vai ser separado", "Arraste um PDF para cá", "Escolher o PDF"),
            FerramentaPdf.FotosEmPdf => ("Fotos na ordem das páginas", "Arraste as fotos para cá", "Adicionar fotos"),
            FerramentaPdf.LogoENumeracao => ("PDFs que vão receber logo ou número", "Arraste os PDFs para cá", "Adicionar PDFs"),
            FerramentaPdf.ParaExcel => ("PDFs e imagens para passar para o Excel", "Arraste PDFs ou fotos de documentos para cá", "Adicionar arquivos"),
            _ => ("PDFs para reduzir", "Arraste os PDFs para cá", "Adicionar PDFs"),
        };

        TextoBotao = Ferramenta switch
        {
            FerramentaPdf.Juntar => n > 1 ? $"Juntar {n} PDFs" : "Juntar PDFs",
            FerramentaPdf.Separar => "Separar o PDF",
            FerramentaPdf.FotosEmPdf => n > 0 ? $"Criar PDF com {Formato.Plural(n, "foto", "fotos")}" : "Criar PDF com as fotos",
            FerramentaPdf.LogoENumeracao => n > 1 ? $"Marcar {n} PDFs" : "Marcar o PDF",
            FerramentaPdf.ParaExcel => n > 1 ? $"Gerar {n} planilhas" : "Gerar a planilha",
            _ => n > 0 ? $"Reduzir {Formato.Plural(n, "PDF", "PDFs")}" : "Reduzir PDFs",
        };

        if (n > 0)
        {
            if (Ferramenta == FerramentaPdf.Juntar && n < 2)
            {
                Avisos.Add(new AvisoVm(Gravidade.ImpedeGerar, "Para juntar, escolha pelo menos dois PDFs."));
            }

            if (PedeNome && NomeArquivo.Problema(NomeSaida.Trim() + ".pdf") is { } problema)
            {
                Avisos.Add(new AvisoVm(Gravidade.ImpedeGerar, "Nome do arquivo: " + problema));
            }

            if (Ferramenta == FerramentaPdf.Separar && !SepararCadaPagina)
            {
                try
                {
                    var lidos = IntervalosPdf.Interpretar(Intervalos, FerramentasPdf.Ler(Entradas[0].Caminho).Paginas);
                    if (!lidos.Valido)
                    {
                        Avisos.Add(new AvisoVm(Gravidade.ImpedeGerar, lidos.Erro!));
                    }
                    else
                    {
                        Avisos.Add(new AvisoVm(Gravidade.Informacao, $"Vão sair {Formato.Plural(lidos.Intervalos.Count, "arquivo", "arquivos")}: {string.Join(", ", lidos.Intervalos.Select(i => "p " + i))}."));
                    }
                }
                catch (PdfInvalidoException ex)
                {
                    Avisos.Add(new AvisoVm(Gravidade.ImpedeGerar, ex.Message));
                }
            }

            if (Ferramenta == FerramentaPdf.LogoENumeracao)
            {
                if (!Numerar && !ComLogo)
                {
                    Avisos.Add(new AvisoVm(Gravidade.ImpedeGerar, "Escolha numerar as páginas, colocar o logo ou os dois."));
                }
                else if (PaginaInicial() is not { } inicio)
                {
                    Avisos.Add(new AvisoVm(Gravidade.ImpedeGerar, "Em “A partir da página”, use um número a partir de 1."));
                }
                else
                {
                    foreach (var entrada in Entradas)
                    {
                        try
                        {
                            var (primeira, ultima, total) = CarimboPdf.Atingidas(FerramentasPdf.Ler(entrada.Caminho).Paginas, new OpcoesCarimbo { APartirDa = inicio });
                            Avisos.Add(total == 0
                                ? new AvisoVm(Gravidade.ImpedeGerar, $"{entrada.Nome}: não tem a página {inicio}.")
                                : new AvisoVm(Gravidade.Informacao, $"{entrada.Nome}: páginas {primeira} a {ultima} ({total}) recebem a marca; a cópia sai com outro nome."));
                        }
                        catch (PdfInvalidoException ex)
                        {
                            Avisos.Add(new AvisoVm(Gravidade.ImpedeGerar, ex.Message));
                        }
                    }

                    Avisos.Add(new AvisoVm(Gravidade.PrecisaConferir,
                        "A marca vai na margem, pequena, por cima do que houver ali. Confira a cópia antes de enviar. Documento de outro órgão continua sendo dele: o logo não o torna emitido pela SPOBRAS."));
                }
            }

            if (Ferramenta == FerramentaPdf.ParaExcel)
            {
                Avisos.Add(new AvisoVm(Gravidade.Informacao,
                    "Uma planilha por arquivo, com a página e a linha de origem de cada dado. PDF gerado por sistema é lido direto; PDF digitalizado e foto são lidos pelo OCR do Windows."));
                if (_ocr is { Disponivel: true } ocr)
                {
                    Avisos.Add(new AvisoVm(Gravidade.PrecisaConferir, $"O OCR (idioma: {ocr.Idioma}) pode trocar letras e números parecidos. Confira valores e datas das páginas lidas por OCR antes de usar."));
                }
                else
                {
                    Avisos.Add(new AvisoVm(Gravidade.PrecisaConferir, "O OCR do Windows não está disponível neste computador: páginas digitalizadas e fotos ficam de fora."));
                }
            }

            if (Ferramenta == FerramentaPdf.Reduzir)
            {
                Avisos.Add(new AvisoVm(Gravidade.Informacao,
                    "A redução recomprime as fotos de dentro do PDF (com alguma perda). PDF só de texto ou já otimizado quase não diminui; nesse caso o app avisa e não cria cópia."));
            }
        }

        ExecutarCommand.NotifyCanExecuteChanged();
    }

    private sealed class Progresso(Action<int> aoMudar) : IProgress<int>
    {
        public void Report(int value) => aoMudar(value);
    }
}
