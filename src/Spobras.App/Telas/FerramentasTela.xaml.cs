using System.Windows;
using System.Windows.Controls;
using Spobras.App.ViewModels;

namespace Spobras.App.Telas;

public partial class FerramentasTela : UserControl
{
    public FerramentasTela()
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
        if (DataContext is FerramentasVm vm && e.Data.GetData(DataFormats.FileDrop) is string[] caminhos)
        {
            vm.Adicionar(caminhos);
        }
    }
}
