using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Spobras.Core;

namespace Spobras.App.Telas;

/// <summary>Campos de data: barras automaticas enquanto digita e ano completado ao sair (Mascaras.Data="True").</summary>
public static class Mascaras
{
    public static readonly DependencyProperty DataProperty =
        DependencyProperty.RegisterAttached("Data", typeof(bool), typeof(Mascaras), new PropertyMetadata(false, AoMudarData));

    public static bool GetData(DependencyObject d) => (bool)d.GetValue(DataProperty);

    public static void SetData(DependencyObject d, bool valor) => d.SetValue(DataProperty, valor);

    private static void AoMudarData(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox caixa)
        {
            return;
        }

        caixa.TextChanged -= AoDigitar;
        caixa.LostFocus -= AoSair;
        caixa.PreviewTextInput -= AoTeclar;
        if (e.NewValue is true)
        {
            caixa.TextChanged += AoDigitar;
            caixa.LostFocus += AoSair;
            caixa.PreviewTextInput += AoTeclar;
            caixa.MaxLength = 10;
            if (caixa.ToolTip is null)
            {
                caixa.ToolTip = "Digite só os números: 21052026 vira 21/05/2026";
            }
        }
    }

    private static void AoTeclar(object sender, TextCompositionEventArgs e) =>
        e.Handled = e.Text.Any(c => !char.IsAsciiDigit(c) && c != '/');

    private static void AoDigitar(object sender, TextChangedEventArgs e)
    {
        var caixa = (TextBox)sender;
        bool noFim = caixa.CaretIndex == caixa.Text.Length;
        string formatado = MascaraData.Formatar(caixa.Text);
        if (formatado != caixa.Text && noFim)
        {
            caixa.Text = formatado;
            caixa.CaretIndex = formatado.Length;
        }
    }

    private static void AoSair(object sender, RoutedEventArgs e)
    {
        var caixa = (TextBox)sender;
        string completo = MascaraData.Completar(caixa.Text, DateOnly.FromDateTime(DateTime.Today));
        if (completo != caixa.Text)
        {
            caixa.Text = completo;
        }
    }
}
