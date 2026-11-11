using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Spobras.App.Telas;

/// <summary>Liga um RadioButton a um valor de enum: marcado quando o valor e igual ao parametro.</summary>
public sealed class EnumIgual : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not null && parameter is not null
        && string.Equals(value.ToString(), parameter.ToString(), StringComparison.Ordinal);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true && parameter is not null ? Enum.Parse(targetType, parameter.ToString()!) : Binding.DoNothing;
}

/// <summary>Visivel quando o valor e false (ou texto vazio, ou zero).</summary>
public sealed class VisivelSeNao : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Vazio(value) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;

    internal static bool Vazio(object? value) => value switch
    {
        null => true,
        bool b => !b,
        string s => s.Length == 0,
        int i => i == 0,
        _ => false,
    };
}

/// <summary>Visivel quando ha valor (true, texto nao vazio, numero diferente de zero).</summary>
public sealed class VisivelSe : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        VisivelSeNao.Vazio(value) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>True quando o id do item e o id da pagina atual (item ativo do menu).</summary>
public sealed class Iguais : IMultiValueConverter
{
    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values.Length == 2 && values[0] is string a && values[1] is string b && a == b;

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
