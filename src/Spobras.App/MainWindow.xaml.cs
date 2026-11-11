using System.ComponentModel;
using System.Windows;
using Spobras.App.ViewModels;

namespace Spobras.App;

public partial class MainWindow : Window
{
    private const double LarguraMenu = 232;
    private const double LarguraEstreita = 1000;
    private readonly PrincipalVm _vm;

    public MainWindow(PrincipalVm vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        vm.PropertyChanged += AoMudarVm;
        MarcarItemAtivo();
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
        ColunaMenu.Width = new GridLength(estreita ? 0 : LarguraMenu);
    }

    private void AoClicarMenu(object sender, RoutedEventArgs e) =>
        ColunaMenu.Width = new GridLength(ColunaMenu.Width.Value > 0 ? 0 : LarguraMenu);

    private void RecolherSeEstreita()
    {
        if (ActualWidth < LarguraEstreita)
        {
            ColunaMenu.Width = new GridLength(0);
        }
    }
}
