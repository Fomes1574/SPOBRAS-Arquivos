using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Spobras.Core;

namespace Spobras.App.Telas;

/// <summary>
/// Campos de data: barras automaticas enquanto digita e ano completado ao sair (Mascaras.Data="True").
/// Campos de dinheiro: enquanto digita, 50000 aparece como 50.000; ao sair, vira R$ 50.000,00 (Mascaras.Dinheiro="True").
/// </summary>
public static class Mascaras
{
    public static readonly DependencyProperty DinheiroProperty =
        DependencyProperty.RegisterAttached("Dinheiro", typeof(bool), typeof(Mascaras), new PropertyMetadata(false, AoMudarDinheiro));

    public static bool GetDinheiro(DependencyObject d) => (bool)d.GetValue(DinheiroProperty);

    public static void SetDinheiro(DependencyObject d, bool valor) => d.SetValue(DinheiroProperty, valor);

    private static void AoMudarDinheiro(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox caixa)
        {
            return;
        }

        caixa.LostFocus -= AoSairDoDinheiro;
        caixa.TextChanged -= AoDigitarDinheiro;
        if (e.NewValue is true)
        {
            caixa.LostFocus += AoSairDoDinheiro;
            caixa.TextChanged += AoDigitarDinheiro;
            AoSairDoDinheiro(caixa, new RoutedEventArgs());
        }
    }

    private static bool _formatando;

    /// <summary>Enquanto digita: 2500000 aparece como 2.500.000 (o cursor fica onde a pessoa estava).</summary>
    private static void AoDigitarDinheiro(object sender, TextChangedEventArgs e)
    {
        var caixa = (TextBox)sender;
        if (_formatando || !caixa.IsKeyboardFocusWithin || !GetDinheiro(caixa))
        {
            return;
        }

        var (texto, cursor) = Spobras.Core.Base.DinheiroAoDigitar.Formatar(caixa.Text, caixa.CaretIndex);
        if (texto == caixa.Text)
        {
            return;
        }

        _formatando = true;
        try
        {
            caixa.Text = texto;
            caixa.CaretIndex = cursor;
        }
        finally
        {
            _formatando = false;
        }
    }

    private static void AoSairDoDinheiro(object sender, RoutedEventArgs e)
    {
        var caixa = (TextBox)sender;
        if (GetDinheiro(caixa) && Spobras.Core.Base.Normalizacao.LerReais(caixa.Text) is { } valor)
        {
            string formatado = Reais(valor);
            if (formatado != caixa.Text)
            {
                caixa.Text = formatado;
            }
        }
    }

    /// <summary>R$ 50.000,00 (a mesma conta que o app usa para mostrar dinheiro).</summary>
    public static string Reais(decimal valor) => valor.ToString("C2", Spobras.App.ViewModels.Formato.PtBr);

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
