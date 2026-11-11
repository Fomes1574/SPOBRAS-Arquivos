using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Spobras.App.ViewModels;

namespace Spobras.App.Telas;

public partial class RenomearTela : UserControl
{
    public RenomearTela()
    {
        InitializeComponent();
    }

    private void AoArrastar(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void AoSoltar(object sender, DragEventArgs e)
    {
        if (DataContext is RenomearVm vm && e.Data.GetData(DataFormats.FileDrop) is string[] caminhos)
        {
            vm.Adicionar(caminhos);
        }
    }

    // Enter confirma o nome digitado na previa (sem precisar sair do campo).
    private void AoTeclarNoNome(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox campo)
        {
            campo.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            e.Handled = true;
        }
    }
}
