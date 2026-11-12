using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media.Imaging;

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

/// <summary>
/// Caminho de arquivo -> imagem carregada inteira na memoria, sem manter o arquivo aberto
/// (assim o logo e as miniaturas podem ser trocados com a tela aberta). O parametro limita a largura.
/// </summary>
public sealed class CaminhoParaImagem : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string caminho || caminho.Length == 0 || !System.IO.File.Exists(caminho))
        {
            return null;
        }

        try
        {
            var imagem = new BitmapImage();
            imagem.BeginInit();
            imagem.CacheOption = BitmapCacheOption.OnLoad;
            imagem.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            if (parameter is string largura && int.TryParse(largura, out int pixels))
            {
                imagem.DecodePixelWidth = pixels;
            }

            imagem.UriSource = new Uri(caminho, UriKind.Absolute);
            imagem.EndInit();
            imagem.Freeze();
            return imagem;
        }
        catch (Exception ex) when (ex is System.IO.IOException or NotSupportedException or UnauthorizedAccessException or ArgumentException
                                       or System.Runtime.InteropServices.COMException)
        {
            return null; // arquivo ilegivel: a tela mostra o espaco vazio
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Inverte um bool (por exemplo, desabilitar a lista enquanto um cadastro esta aberto).</summary>
public sealed class Negar : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
}

/// <summary>Caminho completo vira so o nome do arquivo.</summary>
public sealed class SoNome : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is string s ? Path.GetFileName(s) : "";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}
