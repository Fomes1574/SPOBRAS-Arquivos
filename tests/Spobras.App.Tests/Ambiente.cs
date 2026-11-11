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
        Directory.Delete(Raiz, recursive: true);
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
}
