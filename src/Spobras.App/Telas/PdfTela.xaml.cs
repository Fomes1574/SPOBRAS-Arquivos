using System.Windows;
using System.Windows.Controls;
using Spobras.App.ViewModels;

namespace Spobras.App.Telas;

public partial class PdfTela : UserControl
{
    public PdfTela()
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
        if (DataContext is PdfVm vm && e.Data.GetData(DataFormats.FileDrop) is string[] caminhos)
        {
            vm.Adicionar(caminhos);
        }
    }

    private void AoEscolherIntervalos(object sender, RoutedEventArgs e)
    {
        if (DataContext is PdfVm vm)
        {
            vm.SepararCadaPagina = false;
        }
    }
}
