using Spobras.App.Servicos;
using Spobras.App.ViewModels;
using Spobras.Core.Abstracoes;
using Spobras.Core.Tarefas;
using Spobras.Dados;
using Spobras.Dados.Configuracao;
using Spobras.Dados.Historico;

namespace Spobras.App.Tests;

public sealed class RelogioFixo : IRelogio
{
    public DateTimeOffset Agora { get; set; } = new(2026, 11, 11, 10, 0, 0, TimeSpan.FromHours(-3));
}

/// <summary>Respostas combinadas de antemao para as janelas que a tela abriria.</summary>
public sealed class DialogosFalsos : IDialogos
{
    public Queue<string?> Pastas { get; } = new();
    public Queue<IReadOnlyList<string>> Arquivos { get; } = new();
    public Queue<bool> Confirmacoes { get; } = new();
    public Queue<string?> Textos { get; } = new();
    public string? AreaDeTransferencia { get; set; }
    public List<string> AvisosMostrados { get; } = [];
    public List<string> ConfirmacoesPedidas { get; } = [];
    public List<string> Abertos { get; } = [];

    public string? EscolherPasta(string titulo, string? pastaInicial = null) => Pastas.Count > 0 ? Pastas.Dequeue() : null;
    public IReadOnlyList<string> EscolherPastas(string titulo, string? pastaInicial = null) => Arquivos.Count > 0 ? Arquivos.Dequeue() : [];
    public IReadOnlyList<string> EscolherArquivos(string titulo, string? filtro = null, string? pastaInicial = null) => Arquivos.Count > 0 ? Arquivos.Dequeue() : [];

    public bool Confirmar(string titulo, string mensagem, string botaoSim = "Sim", string botaoNao = "Cancelar")
    {
        ConfirmacoesPedidas.Add(mensagem);
        return Confirmacoes.Count == 0 || Confirmacoes.Dequeue();
    }

    public void Avisar(string titulo, string mensagem) => AvisosMostrados.Add(mensagem);
    public string? PedirTexto(string titulo, string mensagem, string valorInicial = "") => Textos.Count > 0 ? Textos.Dequeue() : null;
    public string? LerAreaDeTransferencia() => AreaDeTransferencia;
    public void MostrarNaPasta(string caminho) => Abertos.Add(caminho);
    public void AbrirPasta(string pasta) => Abertos.Add(pasta);
}

/// <summary>Faz de conta que e o Excel exportando PDF: grava um arquivo no lugar pedido.</summary>
public sealed class ExportadorFalso : IExportadorPdf
{
    public bool Disponivel { get; set; } = true;
    public bool Falhar { get; set; }
    public List<(string Planilha, string Pdf)> Chamadas { get; } = [];

    public void Exportar(string planilha, string pdf)
    {
        Chamadas.Add((planilha, pdf));
        if (Falhar)
        {
            throw new InvalidOperationException("O Excel não respondeu.");
        }

        File.WriteAllText(pdf, "%PDF de teste");
    }
}

/// <summary>Faz de conta que e o Excel recalculando o cronograma: devolve os totais combinados no teste.</summary>
public sealed class VerificadorFalso : IVerificadorCronograma
{
    public bool Disponivel { get; set; } = true;
    public Func<string, ConferenciaCronograma>? Resposta { get; set; }
    public List<(string Caminho, string Aba, string? Pdf)> Chamadas { get; } = [];

    public ConferenciaCronograma Conferir(string caminho, string aba, string? pdf)
    {
        Chamadas.Add((caminho, aba, pdf));
        if (pdf is not null)
        {
            File.WriteAllText(pdf, "%PDF de teste");
        }

        return (Resposta?.Invoke(caminho) ?? new ConferenciaCronograma(null, null, null, "RESUMO OK", 0, null)) with { Pdf = pdf };
    }
}

/// <summary>O app inteiro sem tela: disco real numa pasta temporaria, banco SQLite real, janelas falsas.</summary>
public abstract class ComApp : IDisposable
{
    protected string Raiz { get; } = Directory.CreateTempSubdirectory("spobras-app-").FullName;
    protected string Trabalho => Em("Downloads");
    protected RelogioFixo Relogio { get; } = new();
    protected DialogosFalsos Dialogos { get; } = new();
    protected ArquivosDisco Disco { get; } = new();
    protected BancoApp Banco { get; }
    protected ServicoPreferencias Preferencias { get; }
    protected ServicoOperacoes Operacoes { get; }
    protected ServicoNovidades Novidades { get; }
    protected PerfilRepositorio Perfis { get; }

    protected ComApp()
    {
        Directory.CreateDirectory(Trabalho);
        Banco = new BancoApp(Em("dados", "app.db"));
        Preferencias = new ServicoPreferencias(new ConfigRepositorio(Banco), Trabalho);
        // Nos testes o modo automatico fica desligado para nao criar vigias de pasta de verdade.
        Preferencias.Alterar(p => p with { ModoAutomatico = false });
        Operacoes = new ServicoOperacoes(Disco, new HistoricoSqlite(Banco), Relogio, new FilaTarefas());
        Novidades = new ServicoNovidades(Disco, Relogio, Preferencias, new RegraRepositorio(Banco), Em("dados"));
        Perfis = new PerfilRepositorio(Banco);
    }

    public void Dispose()
    {
        Novidades.Dispose();
        _fontes?.Dispose();
        // Pode haver trabalho em segundo plano terminando de gravar (miniaturas): tenta de novo antes de desistir.
        for (int tentativa = 0; ; tentativa++)
        {
            try
            {
                Directory.Delete(Raiz, recursive: true);
                break;
            }
            catch (IOException) when (tentativa < 20)
            {
                Thread.Sleep(50);
            }
        }

        GC.SuppressFinalize(this);
    }

    protected string Em(params string[] partes) => Path.Combine([Raiz, .. partes]);

    protected string Criar(string relativo, string conteudo = "x")
    {
        string caminho = Em(relativo);
        Directory.CreateDirectory(Path.GetDirectoryName(caminho)!);
        File.WriteAllText(caminho, conteudo);
        return caminho;
    }

    protected string[] Nomes(params string[] pasta) =>
        [.. Directory.GetFiles(Em(pasta)).Select(c => Path.GetFileName(c)!).Order(StringComparer.Ordinal)];

    protected RenomearVm NovoRenomear() => new(Disco, Dialogos, Operacoes, Preferencias, Perfis, Relogio);
    protected OrganizarVm NovoOrganizar() => new(Disco, Dialogos, new InterfaceDireta(), Operacoes, Novidades, Preferencias, Relogio);
    protected CompactarVm NovoCompactar() => new(Disco, Dialogos, Operacoes, Preferencias);
    protected HistoricoVm NovoHistorico() => new(Operacoes, Dialogos, new InterfaceDireta());
    protected ServicoModelos Modelos => new(Em("dados"));
    protected ExportadorFalso Exportador { get; } = new();
    protected RelatorioVm NovoRelatorio() =>
        new(Dialogos, new InterfaceDireta(), Operacoes, Preferencias, Modelos, new RascunhoRepositorio(Banco), Exportador, Relogio);
    protected PdfVm NovoPdf() => new(Dialogos, Operacoes, Preferencias);
    protected VerificadorFalso Verificador { get; } = new();
    protected CronogramaVm NovoCronograma() =>
        new(Dialogos, Operacoes, Preferencias, Modelos, new RascunhoRepositorio(Banco), Verificador, Relogio, () => Base.Apelidos());

    private Spobras.Dados.Base.BaseRepositorio? _base;
    private ServicoFontes? _fontes;
    protected Spobras.Dados.Base.BaseRepositorio Base => _base ??= new(Banco);

    /// <summary>Fontes lendo planilhas .xlsx de verdade; sem vigia automatica (o teste manda conferir).</summary>
    protected ServicoFontes Fontes => _fontes ??= new ServicoFontes(Base, c => new Spobras.Documentos.Planilhas.LeitorXlsx(c), Relogio, Operacoes.Fila, Preferencias)
    {
        Quieto = TimeSpan.FromSeconds(4),
        UsarEventos = false,
    };

    protected string GravarMestra(Spobras.Testes.PlanilhaFalsa? planilha = null, string pasta = "Base")
    {
        string caminho = Em(pasta, Spobras.Core.Base.Fontes.NomeArquivoMestra);
        if (File.Exists(caminho))
        {
            File.Delete(caminho);
        }

        Spobras.Testes.XlsxSintetico.Gravar(planilha ?? Spobras.Testes.BaseSintetica.Mestra(), caminho);
        return caminho;
    }

    protected string GravarDotacoes(string pasta = "Base")
    {
        string caminho = Em(pasta, Spobras.Core.Base.Fontes.NomeArquivoDotacoes);
        Spobras.Testes.XlsxSintetico.Gravar(Spobras.Testes.BaseSintetica.Dotacoes(), caminho);
        return caminho;
    }

    /// <summary>Foto JPEG de verdade, pequena, para os testes.</summary>
    protected string CriarFoto(string relativo, int largura = 400, int altura = 300)
    {
        string caminho = Em(relativo);
        Directory.CreateDirectory(Path.GetDirectoryName(caminho)!);
        using var bitmap = new SkiaSharp.SKBitmap(largura, altura);
        bitmap.Erase(SkiaSharp.SKColors.SteelBlue);
        using var imagem = SkiaSharp.SKImage.FromBitmap(bitmap);
        File.WriteAllBytes(caminho, imagem.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 85).ToArray());
        return caminho;
    }

    protected static async Task<bool> Ate(Func<bool> condicao)
    {
        for (int i = 0; i < 200 && !condicao(); i++)
        {
            await Task.Delay(25);
        }

        return condicao();
    }
}
