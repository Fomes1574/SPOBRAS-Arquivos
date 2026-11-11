using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;
using Spobras.App.Servicos;
using Spobras.App.Windows;

namespace Spobras.App;

public partial class App : Application
{
    private const string NomeInstancia = "ArquivosSPOBRAS-instancia-unica";
    private const string NomeSinal = "ArquivosSPOBRAS-mostrar-janela";

    private Mutex? _instancia;
    private EventWaitHandle? _sinalMostrar;
    private Composicao? _composicao;
    private Bandeja? _bandeja;
    private MainWindow? _janela;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Uma instancia so: abrir de novo traz a janela que ja existe.
        _instancia = new Mutex(initiallyOwned: true, NomeInstancia, out bool primeira);
        if (!primeira)
        {
            if (EventWaitHandle.TryOpenExisting(NomeSinal, out var sinal))
            {
                sinal.Set();
                sinal.Dispose();
            }

            Shutdown();
            return;
        }

        var ptBr = CultureInfo.GetCultureInfo("pt-BR");
        CultureInfo.DefaultThreadCurrentCulture = ptBr;
        CultureInfo.DefaultThreadCurrentUICulture = ptBr;
        FrameworkElement.LanguageProperty.OverrideMetadata(
            typeof(FrameworkElement), new FrameworkPropertyMetadata(XmlLanguage.GetLanguage("pt-BR")));

        string pastaDados = PastasWindows.DadosDoApp();
        DispatcherUnhandledException += (_, erro) =>
        {
            RegistrarErro(pastaDados, erro.Exception);
            erro.Handled = true;
            MessageBox.Show(
                "Aconteceu um erro inesperado e a ação não foi concluída. Nenhum arquivo foi apagado.\n\n" + erro.Exception.Message +
                "\n\nO detalhe ficou gravado na pasta de dados do app (Configurações > Abrir pasta de dados).",
                "Arquivos SPOBRAS", MessageBoxButton.OK, MessageBoxImage.Warning);
        };

        try
        {
            var dialogos = new DialogosWpf(() => _janela);
            _composicao = new Composicao(pastaDados, PastasWindows.Downloads(), dialogos, new InterfaceWpf(Dispatcher), Versao(), new ExportadorPdfExcel());
        }
        catch (Exception ex)
        {
            RegistrarErro(pastaDados, ex);
            MessageBox.Show("O app não conseguiu abrir os seus dados:\n\n" + ex.Message, "Arquivos SPOBRAS", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        _janela = new MainWindow(_composicao.Principal)
        {
            ManterNaBandeja = () => _composicao.Preferencias.Atual.ModoAutomatico && _bandeja is not null,
        };
        _janela.Closed += (_, _) => Sair();
        _janela.IsVisibleChanged += (_, _) => AvisarQueContinuaNaBandeja();

        _bandeja = new Bandeja(MostrarJanela, Sair);
        _composicao.Novidades.Chegaram += n => Dispatcher.BeginInvoke(() =>
        {
            _bandeja.NomeDaPasta = Path.GetFileName(_composicao.Preferencias.Atual.PastaTrabalho.TrimEnd('\\', '/'));
            _bandeja.Chegaram(n);
        });

        _sinalMostrar = new EventWaitHandle(false, EventResetMode.AutoReset, NomeSinal);
        var sinalMostrar = _sinalMostrar;
        new Thread(() =>
        {
            try
            {
                while (sinalMostrar.WaitOne())
                {
                    Dispatcher.BeginInvoke(() => MostrarJanela(null));
                }
            }
            catch (ObjectDisposedException)
            {
                // O app esta fechando.
            }
        })
        { IsBackground = true, Name = "Sinal de segunda instancia" }.Start();

        SugerirPastaDasPlanilhasBase(_composicao);
        _composicao.Iniciar();
        _janela.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _bandeja?.Dispose();
        _composicao?.Dispose();
        _sinalMostrar?.Dispose();
        _instancia?.Dispose();
        base.OnExit(e);
    }

    /// <summary>
    /// Primeira execucao: se a pasta das planilhas base ainda nao foi escolhida, procura a planilha mestra pelo
    /// nome nas pastas sincronizadas e sugere a pasta onde ela esta. Pode ser trocada em Configuracoes.
    /// </summary>
    private static void SugerirPastaDasPlanilhasBase(Composicao composicao)
    {
        if (!string.IsNullOrWhiteSpace(composicao.Preferencias.Atual.PastaPlanilhasBase))
        {
            return;
        }

        try
        {
            string? pasta = PastasWindows.Sincronizadas()
                .FirstOrDefault(p => File.Exists(Path.Combine(p, Spobras.Core.Base.Fontes.NomeArquivoMestra)));
            if (pasta is not null)
            {
                composicao.Preferencias.Alterar(p => p with { PastaPlanilhasBase = pasta });
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Pasta fora do ar: fica para o usuario escolher.
        }
    }

    private void MostrarJanela(string? pagina)
    {
        if (_janela is null)
        {
            return;
        }

        if (pagina is not null)
        {
            _composicao?.Principal.Navegar(pagina);
        }

        _janela.Show();
        if (_janela.WindowState == WindowState.Minimized)
        {
            _janela.WindowState = WindowState.Normal;
        }

        _janela.Activate();
    }

    private void AvisarQueContinuaNaBandeja()
    {
        if (_janela is { IsVisible: false } && _composicao is not null && !_composicao.Preferencias.Atual.AvisouBandeja)
        {
            _composicao.Preferencias.Alterar(p => p with { AvisouBandeja = true });
            _bandeja?.Avisar("O Arquivos SPOBRAS continua aqui",
                "O modo automático está ligado. Para sair de vez, clique neste ícone com o botão direito e escolha Sair.");
        }
    }

    private void Sair()
    {
        if (_janela is not null)
        {
            _janela.ManterNaBandeja = null;
        }

        Shutdown();
    }

    private static string Versao() =>
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? "desconhecida";

    /// <summary>Registro local de erros: mensagem e pilha, sem conteudo de documentos.</summary>
    private static void RegistrarErro(string pastaDados, Exception erro)
    {
        try
        {
            string pasta = Path.Combine(pastaDados, "registro");
            Directory.CreateDirectory(pasta);
            File.AppendAllText(Path.Combine(pasta, "erros.txt"),
                $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}  versão {Versao()}\r\n{erro}\r\n\r\n");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Sem conseguir gravar o registro, o aviso na tela ja informou o usuario.
        }
    }
}
