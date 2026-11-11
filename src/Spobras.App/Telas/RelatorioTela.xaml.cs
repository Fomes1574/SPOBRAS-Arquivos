using System.Windows;
using System.Windows.Controls;
using Spobras.App.ViewModels;

namespace Spobras.App.Telas;

public partial class RelatorioTela : UserControl
{
    public RelatorioTela()
    {
        InitializeComponent();
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
