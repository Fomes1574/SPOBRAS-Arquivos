using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Spobras.App.ViewModels;

namespace Spobras.App.Telas;

public partial class BaseTela : UserControl
{
    public BaseTela()
    {
        InitializeComponent();
        DataContextChanged += AoMudarContexto;
    }

    private BaseVm? Vm => DataContext as BaseVm;

    private void AoMudarContexto(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is BaseVm antigo)
        {
            antigo.PropertyChanged -= AoMudarVm;
        }

        if (e.NewValue is BaseVm vm)
        {
            vm.PropertyChanged += AoMudarVm;
            AplicarLarguras();
        }
    }

    private void AoMudarVm(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BaseVm.FichaAmpliada))
        {
            AplicarLarguras();
        }
    }

    /// <summary>Lista e ficha na proporcao que o usuario deixou; com "Ampliar", a ficha ocupa tudo.</summary>
    private void AplicarLarguras()
    {
        if (Vm is not { } vm)
        {
            return;
        }

        bool ampliada = vm.FichaAmpliada;
        PainelLista.Visibility = ampliada ? Visibility.Collapsed : Visibility.Visible;
        Divisoria.Visibility = PainelLista.Visibility;
        ColunaLista.MinWidth = ampliada ? 0 : 440;
        ColunaLista.Width = ampliada ? new GridLength(0) : new GridLength(1 - vm.ProporcaoDaFicha, GridUnitType.Star);
        ColunaDivisoria.Width = new GridLength(ampliada ? 0 : 16);
        ColunaFicha.Width = new GridLength(ampliada ? 1 : vm.ProporcaoDaFicha, GridUnitType.Star);
    }

    private void AoSoltarDivisoria(object sender, DragCompletedEventArgs e)
    {
        double lista = ColunaLista.ActualWidth, ficha = ColunaFicha.ActualWidth;
        if (Vm is { } vm && lista + ficha > 0)
        {
            vm.GuardarProporcaoDaFicha(ficha / (lista + ficha));
            AplicarLarguras();
        }
    }
}
