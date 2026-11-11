using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Spobras.App.Servicos;

namespace Spobras.App.Windows;

internal static class PastasWindows
{
    private static readonly Guid IdDownloads = new("374DE290-123F-4565-9164-39C4925E467B");

    /// <summary>Pasta Downloads do usuario (mesmo se ele a mudou de lugar).</summary>
    public static string Downloads()
    {
        if (SHGetKnownFolderPath(IdDownloads, 0, 0, out nint ponteiro) == 0)
        {
            try
            {
                string? caminho = Marshal.PtrToStringUni(ponteiro);
                if (!string.IsNullOrEmpty(caminho))
                {
                    return caminho;
                }
            }
            finally
            {
                Marshal.FreeCoTaskMem(ponteiro);
            }
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }

    public static string DadosDoApp() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ArquivosSPOBRAS");

    /// <summary>
    /// Pastas sincronizadas pelo OneDrive neste usuario: o OneDrive pessoal/corporativo e as bibliotecas do
    /// SharePoint (pontos de montagem). Vem do proprio Windows; nenhum caminho fica fixo no codigo.
    /// </summary>
    public static IReadOnlyList<string> Sincronizadas()
    {
        var pastas = new List<string>();
        void Somar(string? caminho)
        {
            if (!string.IsNullOrWhiteSpace(caminho) && Directory.Exists(caminho) && !pastas.Contains(caminho.TrimEnd('\\'), StringComparer.OrdinalIgnoreCase))
            {
                pastas.Add(caminho.TrimEnd('\\'));
            }
        }

        try
        {
            using var provedores = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\SyncEngines\Providers\OneDrive");
            foreach (string nome in provedores?.GetSubKeyNames() ?? [])
            {
                using var provedor = provedores!.OpenSubKey(nome);
                Somar(provedor?.GetValue("MountPoint") as string);
            }

            using var contas = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\OneDrive\Accounts");
            foreach (string nome in contas?.GetSubKeyNames() ?? [])
            {
                using var conta = contas!.OpenSubKey(nome);
                Somar(conta?.GetValue("UserFolder") as string);
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            // Sem acesso ao registro: o usuario escolhe a pasta em Configuracoes.
        }

        foreach (string variavel in new[] { "OneDriveCommercial", "OneDrive", "OneDriveConsumer" })
        {
            Somar(Environment.GetEnvironmentVariable(variavel));
        }

        return pastas;
    }

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath(
        [MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint dwFlags, nint hToken, out nint ppszPath);
}

public sealed class InterfaceWpf(System.Windows.Threading.Dispatcher despachante) : IInterface
{
    public void Executar(Action acao)
    {
        if (despachante.CheckAccess())
        {
            acao();
        }
        else
        {
            despachante.BeginInvoke(acao);
        }
    }
}

/// <summary>Janela pequena de confirmacao, aviso ou pergunta de texto, no estilo do app.</summary>
public sealed class JanelaDialogo : Window
{
    private readonly TextBox? _campo;

    public JanelaDialogo(string titulo, string mensagem, string? botaoSim, string? botaoNao, string? textoInicial)
    {
        Title = titulo;
        SizeToContent = SizeToContent.WidthAndHeight;
        MinWidth = 420;
        MaxWidth = 620;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (Brush)Application.Current.FindResource("Branco");
        FontFamily = (FontFamily)Application.Current.FindResource("Fonte");
        FontSize = 15;

        var painel = new StackPanel { Margin = new Thickness(24) };
        painel.Children.Add(new TextBlock { Text = mensagem, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)Application.Current.FindResource("Preto") });

        if (textoInicial is not null)
        {
            _campo = new TextBox { Text = textoInicial, Margin = new Thickness(0, 12, 0, 0) };
            _campo.SelectAll();
            painel.Children.Add(_campo);
        }

        var botoes = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        if (botaoNao is not null)
        {
            var nao = new Button { Content = botaoNao, IsCancel = true, Style = (Style)Application.Current.FindResource("BotaoSecundario"), Margin = new Thickness(0, 0, 8, 0) };
            botoes.Children.Add(nao);
        }

        var sim = new Button { Content = botaoSim ?? "OK", IsDefault = true, Style = (Style)Application.Current.FindResource(botaoNao is null ? "BotaoSecundario" : "BotaoPrimario") };
        sim.Click += (_, _) => DialogResult = true;
        botoes.Children.Add(sim);
        painel.Children.Add(botoes);
        Content = painel;

        Loaded += (_, _) =>
        {
            if (_campo is not null)
            {
                Keyboard.Focus(_campo);
            }
            else
            {
                sim.Focus();
            }
        };
    }

    public string Texto => _campo?.Text ?? "";
}

public sealed class DialogosWpf(Func<Window?> dona) : IDialogos
{
    public string? EscolherPasta(string titulo, string? pastaInicial = null)
    {
        var dialogo = new Microsoft.Win32.OpenFolderDialog { Title = titulo, Multiselect = false };
        DefinirPastaInicial(dialogo, pastaInicial);
        return dialogo.ShowDialog(dona()) == true ? dialogo.FolderName : null;
    }

    public IReadOnlyList<string> EscolherPastas(string titulo, string? pastaInicial = null)
    {
        var dialogo = new Microsoft.Win32.OpenFolderDialog { Title = titulo, Multiselect = true };
        DefinirPastaInicial(dialogo, pastaInicial);
        return dialogo.ShowDialog(dona()) == true ? dialogo.FolderNames : [];
    }

    public IReadOnlyList<string> EscolherArquivos(string titulo, string? filtro = null, string? pastaInicial = null)
    {
        var dialogo = new Microsoft.Win32.OpenFileDialog
        {
            Title = titulo,
            Multiselect = true,
            CheckFileExists = true,
            Filter = filtro ?? "Todos os arquivos|*.*",
        };
        if (!string.IsNullOrWhiteSpace(pastaInicial) && Directory.Exists(pastaInicial))
        {
            dialogo.InitialDirectory = pastaInicial;
        }

        return dialogo.ShowDialog(dona()) == true ? dialogo.FileNames : [];
    }

    public bool Confirmar(string titulo, string mensagem, string botaoSim = "Sim", string botaoNao = "Cancelar") =>
        Mostrar(new JanelaDialogo(titulo, mensagem, botaoSim, botaoNao, null)) == true;

    public void Avisar(string titulo, string mensagem) => Mostrar(new JanelaDialogo(titulo, mensagem, "OK", null, null));

    public string? PedirTexto(string titulo, string mensagem, string valorInicial = "")
    {
        var janela = new JanelaDialogo(titulo, mensagem, "Salvar", "Cancelar", valorInicial);
        return Mostrar(janela) == true ? janela.Texto : null;
    }

    public string? LerAreaDeTransferencia()
    {
        try
        {
            return Clipboard.ContainsText() ? Clipboard.GetText() : null;
        }
        catch (COMException)
        {
            return null; // area de transferencia ocupada por outro programa
        }
    }

    public void MostrarNaPasta(string caminho)
    {
        if (File.Exists(caminho))
        {
            Iniciar("explorer.exe", $"/select,\"{caminho}\"");
        }
        else if (Directory.Exists(caminho))
        {
            AbrirPasta(caminho);
        }
        else if (Path.GetDirectoryName(caminho) is { } pasta && Directory.Exists(pasta))
        {
            AbrirPasta(pasta);
        }
        else
        {
            Avisar("Abrir pasta", "O arquivo não está mais nesse lugar.");
        }
    }

    public void AbrirPasta(string pasta)
    {
        if (Directory.Exists(pasta))
        {
            Iniciar("explorer.exe", $"\"{pasta}\"");
        }
        else
        {
            Avisar("Abrir pasta", "A pasta não foi encontrada.");
        }
    }

    private bool? Mostrar(JanelaDialogo janela)
    {
        if (dona() is { IsVisible: true } janelaDona)
        {
            janela.Owner = janelaDona;
        }
        else
        {
            janela.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        return janela.ShowDialog();
    }

    private static void DefinirPastaInicial(Microsoft.Win32.OpenFolderDialog dialogo, string? pasta)
    {
        if (!string.IsNullOrWhiteSpace(pasta) && Directory.Exists(pasta))
        {
            dialogo.InitialDirectory = pasta;
        }
    }

    private void Iniciar(string programa, string argumentos)
    {
        try
        {
            Process.Start(new ProcessStartInfo(programa, argumentos) { UseShellExecute = false })?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Avisar("Abrir pasta", $"Não foi possível abrir o Explorador: {ex.Message}");
        }
    }
}
