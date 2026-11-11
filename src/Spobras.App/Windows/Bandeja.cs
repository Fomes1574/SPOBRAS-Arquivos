using System.Windows.Threading;
using Spobras.App.ViewModels;

namespace Spobras.App.Windows;

/// <summary>
/// Icone perto do relogio. Avisa das novidades de forma agrupada (uma notificacao para varios arquivos)
/// e so abre o app quando o usuario clica.
/// </summary>
public sealed class Bandeja : IDisposable
{
    private readonly System.Windows.Forms.NotifyIcon _icone;
    private readonly DispatcherTimer _agrupador;
    private readonly Action<string?> _abrir;
    private int _acumulados;
    private string? _paginaDoAviso;

    public Bandeja(Action<string?> abrir, Action sair)
    {
        _abrir = abrir;
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Abrir o Arquivos SPOBRAS", null, (_, _) => abrir(null));
        menu.Items.Add("Ver arquivos novos", null, (_, _) => abrir(OrganizarVm.Modulo));
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Sair", null, (_, _) => sair());

        _icone = new System.Windows.Forms.NotifyIcon
        {
            Icon = IconeDoApp(),
            Text = "Arquivos SPOBRAS",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icone.DoubleClick += (_, _) => abrir(null);
        _icone.BalloonTipClicked += (_, _) => abrir(_paginaDoAviso);

        // Varios arquivos chegando juntos viram um aviso so.
        _agrupador = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _agrupador.Tick += (_, _) => MostrarAgrupado();
    }

    public string NomeDaPasta { get; set; } = "";

    /// <summary>Chamar na thread da tela.</summary>
    public void Chegaram(int quantidade)
    {
        _acumulados += quantidade;
        _agrupador.Stop();
        _agrupador.Start();
    }

    public void Avisar(string titulo, string texto, string? paginaAoClicar = null)
    {
        _paginaDoAviso = paginaAoClicar;
        _icone.ShowBalloonTip(8000, titulo, texto, System.Windows.Forms.ToolTipIcon.Info);
    }

    public void Dispose()
    {
        _agrupador.Stop();
        _icone.Visible = false;
        _icone.Dispose();
    }

    private void MostrarAgrupado()
    {
        _agrupador.Stop();
        int n = _acumulados;
        _acumulados = 0;
        if (n <= 0)
        {
            return;
        }

        Avisar(
            $"{Formato.Plural(n, "arquivo novo", "arquivos novos")} em {NomeDaPasta}",
            "Clique para ver as sugestões. Nada é movido sem a sua confirmação.",
            OrganizarVm.Modulo);
    }

    private static System.Drawing.Icon IconeDoApp()
    {
        try
        {
            if (Environment.ProcessPath is { } exe && System.Drawing.Icon.ExtractAssociatedIcon(exe) is { } icone)
            {
                return icone;
            }
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            // Usa o icone padrao abaixo.
        }

        return System.Drawing.SystemIcons.Application;
    }
}
