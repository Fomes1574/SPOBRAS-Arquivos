using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Spobras.Dados.Base;
using Spobras.App.ViewModels;

namespace Spobras.App.Telas;

public partial class RelatorioTela : UserControl
{
    public RelatorioTela()
    {
        InitializeComponent();
    }

    private (Popup Popup, ListBox Lista) Sugestoes(TextBox campo) => campo == SugestaoUnidade
        ? (PopupUnidade, ListaUnidade) : (PopupContrato, ListaContrato);

    private void AoDigitarSugestao(object sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox { IsKeyboardFocused: true } campo || DataContext is not RelatorioVm vm) return;
        var (popup, lista) = Sugestoes(campo);
        var buscar = campo == SugestaoUnidade ? vm.BuscarSugestoesEscola : vm.BuscarSugestoesContrato;
        var opcoes = buscar?.Invoke(campo.Text) ?? [];
        lista.ItemsSource = opcoes;
        popup.IsOpen = opcoes.Count > 0;
    }

    private void TeclasSugestao(object sender, KeyEventArgs e)
    {
        var (popup, lista) = Sugestoes((TextBox)sender);
        if (!popup.IsOpen) return;
        if (e.Key is Key.Down or Key.Up)
        {
            lista.SelectedIndex = Math.Clamp(lista.SelectedIndex + (e.Key == Key.Down ? 1 : -1), 0, lista.Items.Count - 1);
            lista.ScrollIntoView(lista.SelectedItem);
        }
        else if (e.Key == Key.Escape) popup.IsOpen = false;
        else if (e.Key == Key.Enter)
        {
            if (lista.SelectedIndex < 0) lista.SelectedIndex = 0;
            AplicarSugestao(lista);
        }
        else return;
        e.Handled = true;
    }

    private void ClicarSugestao(object sender, MouseButtonEventArgs e)
    {
        var lista = (ListBox)sender;
        if (ItemsControl.ContainerFromElement(lista, e.OriginalSource as DependencyObject) is ListBoxItem item)
        {
            lista.SelectedItem = item.DataContext;
            AplicarSugestao(lista);
        }
    }

    private void AplicarSugestao(ListBox lista)
    {
        if (lista.SelectedItem is not ProcuraObras.Sugestao sugestao || DataContext is not RelatorioVm vm) return;
        bool escola = lista == ListaUnidade;
        var campo = escola ? SugestaoUnidade : SugestaoContrato;
        Sugestoes(campo).Popup.IsOpen = false;
        vm.EscolherSugestao(sugestao.Valor, escola);
        campo.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
    }

    private void AoArrastar(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    // Soltar fotos em qualquer etapa leva para a etapa das fotos.
    private void AoSoltar(object sender, DragEventArgs e)
    {
        if (DataContext is RelatorioVm vm && e.Data.GetData(DataFormats.FileDrop) is string[] caminhos)
        {
            vm.Adicionar(caminhos);
            vm.Etapa = 1;
        }
    }
}
