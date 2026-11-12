using System.ComponentModel;
using System.Windows;
using Spobras.App.ViewModels;

namespace Spobras.App;

public partial class MainWindow : Window
{
    private const double LarguraMenu = 232;
    private const double LarguraEstreita = 1180;
    private readonly PrincipalVm _vm;

    public MainWindow(PrincipalVm vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        vm.PropertyChanged += AoMudarVm;
        MarcarItemAtivo();
        SourceInitialized += (_, _) => CaberNaTela();
    }

    /// <summary>
    /// A janela sempre cabe no monitor onde abre (o do mouse): num monitor menor que o tamanho padrao (ex.: 1600x900),
    /// a barra de titulo ficaria fora da tela e nao daria para arrastar nem maximizar. Nesse caso abre
    /// maximizada; senao, centralizada dentro da area util (sem a barra de tarefas).
    /// </summary>
    private void CaberNaTela()
    {
        // Monitor onde esta o mouse: e onde a pessoa clicou para abrir o app.
        var area = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position).WorkingArea;
        var origem = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? System.Windows.Media.Matrix.Identity;
        var canto = origem.Transform(new Point(area.Left, area.Top));
        var tamanho = origem.Transform(new Point(area.Width, area.Height));
        if (Width > tamanho.X - 40 || Height > tamanho.Y - 40)
        {
            Left = canto.X;
            Top = canto.Y;
            Width = Math.Min(Width, tamanho.X);
            Height = Math.Min(Height, tamanho.Y);
            WindowState = WindowState.Maximized;
            return;
        }

        Left = canto.X + ((tamanho.X - Width) / 2);
        Top = canto.Y + ((tamanho.Y - Height) / 2);
    }

    /// <summary>True quando fechar a janela deve so esconde-la (o app continua na bandeja).</summary>
    public Func<bool>? ManterNaBandeja { get; set; }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (ManterNaBandeja?.Invoke() == true)
        {
            e.Cancel = true;
            Hide();
        }

        base.OnClosing(e);
    }

    private void AoMudarVm(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PrincipalVm.IdAtual))
        {
            MarcarItemAtivo();
        }
    }

    private void MarcarItemAtivo()
    {
        ItemInicio.Tag = _vm.IdAtual == "inicio" ? "ativo" : null;
        ItemHistorico.Tag = _vm.IdAtual == "historico" ? "ativo" : null;
        ItemConfiguracoes.Tag = _vm.IdAtual == "configuracoes" ? "ativo" : null;
    }

    private void AoClicarNovidades(object sender, RoutedEventArgs e)
    {
        _vm.Navegar(OrganizarVm.Modulo);
        RecolherSeEstreita();
    }

    private void AoNavegar(object sender, RoutedEventArgs e) => RecolherSeEstreita();

    // Janela estreita: o menu se recolhe para nao espremer tabelas e campos.
    private void AoMudarTamanho(object sender, SizeChangedEventArgs e)
    {
        bool estreita = ActualWidth < LarguraEstreita;
        BotaoMenu.Visibility = estreita ? Visibility.Visible : Visibility.Collapsed;
        MostrarMenu(!estreita);
    }

    private void AoClicarMenu(object sender, RoutedEventArgs e) => MostrarMenu(Menu.Visibility != Visibility.Visible);

    private void RecolherSeEstreita()
    {
        if (ActualWidth < LarguraEstreita)
        {
            MostrarMenu(false);
        }
    }

    private void MostrarMenu(bool mostrar)
    {
        // Recolhido, o menu sai tambem da ordem do Tab e dos leitores de tela.
        Menu.Visibility = mostrar ? Visibility.Visible : Visibility.Collapsed;
        ColunaMenu.Width = new GridLength(mostrar ? LarguraMenu : 0);
    }
}
