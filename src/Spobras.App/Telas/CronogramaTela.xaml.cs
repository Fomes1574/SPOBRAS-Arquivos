using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Spobras.App.ViewModels;

namespace Spobras.App.Telas;

public partial class CronogramaTela : UserControl
{
    public CronogramaTela()
    {
        InitializeComponent();
        DataContextChanged += AoMudarContexto;
    }

    private void AoMudarContexto(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is CronogramaVm antigo) antigo.PropertyChanged -= AoMudarVm;
        if (e.NewValue is CronogramaVm vm) vm.PropertyChanged += AoMudarVm;
    }

    /// <summary>Ao abrir "Adicionar contrato", o cursor ja vai para o campo da busca.</summary>
    private void AoMudarVm(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CronogramaVm.BuscandoContrato) && sender is CronogramaVm { BuscandoContrato: true })
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () => CampoBuscaContrato.Focus());
        }
    }
}
