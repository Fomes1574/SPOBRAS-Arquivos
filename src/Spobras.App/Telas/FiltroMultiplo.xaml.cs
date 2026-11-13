using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using Spobras.Core.Base;

namespace Spobras.App.Telas;

public partial class FiltroMultiplo : UserControl
{
    private sealed record Opcao(string Texto, bool Marcada);

    public static readonly DependencyProperty RotuloProperty = DependencyProperty.Register(
        nameof(Rotulo), typeof(string), typeof(FiltroMultiplo), new PropertyMetadata("", AoMudar));
    public static readonly DependencyProperty PluralProperty = DependencyProperty.Register(
        nameof(Plural), typeof(string), typeof(FiltroMultiplo), new PropertyMetadata("", AoMudar));
    public static readonly DependencyProperty ItensProperty = DependencyProperty.Register(
        nameof(Itens), typeof(IEnumerable), typeof(FiltroMultiplo), new PropertyMetadata(null, AoMudarItens));
    public static readonly DependencyProperty SelecionadosProperty = DependencyProperty.Register(
        nameof(Selecionados), typeof(IReadOnlyList<string>), typeof(FiltroMultiplo),
        new FrameworkPropertyMetadata(Array.Empty<string>(), FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, AoMudar));
    public static readonly DependencyProperty ResumoProperty = DependencyProperty.Register(
        nameof(Resumo), typeof(string), typeof(FiltroMultiplo), new PropertyMetadata(""));
    public static readonly DependencyProperty LimparNomeProperty = DependencyProperty.Register(
        nameof(LimparNome), typeof(string), typeof(FiltroMultiplo), new PropertyMetadata("Limpar filtro"));

    public string Rotulo { get => (string)GetValue(RotuloProperty); set => SetValue(RotuloProperty, value); }
    public string Plural { get => (string)GetValue(PluralProperty); set => SetValue(PluralProperty, value); }
    public IEnumerable? Itens { get => (IEnumerable?)GetValue(ItensProperty); set => SetValue(ItensProperty, value); }
    public IReadOnlyList<string> Selecionados { get => (IReadOnlyList<string>)GetValue(SelecionadosProperty); set => SetValue(SelecionadosProperty, value); }
    public string Resumo => (string)GetValue(ResumoProperty);
    public string LimparNome => (string)GetValue(LimparNomeProperty);

    public FiltroMultiplo() => InitializeComponent();

    private static void AoMudar(DependencyObject obj, DependencyPropertyChangedEventArgs e) =>
        ((FiltroMultiplo)obj).Atualizar();

    private static void AoMudarItens(DependencyObject obj, DependencyPropertyChangedEventArgs e)
    {
        var filtro = (FiltroMultiplo)obj;
        if (e.OldValue is INotifyCollectionChanged anterior)
            CollectionChangedEventManager.RemoveHandler(anterior, filtro.ItensMudaram);
        if (e.NewValue is INotifyCollectionChanged novo)
            CollectionChangedEventManager.AddHandler(novo, filtro.ItensMudaram);
        filtro.Atualizar();
    }

    private void ItensMudaram(object? sender, NotifyCollectionChangedEventArgs e) => Atualizar();

    private IEnumerable<string> Visiveis()
    {
        string busca = Normalizacao.Busca(Pesquisa.Text);
        return (Itens?.Cast<string>() ?? []).Where(s => Normalizacao.Busca(s).Contains(busca, StringComparison.Ordinal));
    }

    private void Atualizar()
    {
        SetValue(ResumoProperty, Selecionados.Count switch
        {
            0 => Rotulo,
            1 => Selecionados[0],
            _ => $"{Selecionados.Count} {Plural}",
        });
        SetValue(LimparNomeProperty, $"Limpar {Rotulo}");
        if (Opcoes is not null && Abrir?.IsChecked == true)
            Opcoes.ItemsSource = Visiveis().Select(o => new Opcao(o, Selecionados.Contains(o, StringComparer.OrdinalIgnoreCase))).ToList();
    }

    private void AoAbrir(object? sender, EventArgs e)
    {
        Atualizar();
        Pesquisa.Focus();
    }

    private void AoPesquisar(object sender, TextChangedEventArgs e) => Atualizar();

    private void Limpar(object sender, RoutedEventArgs e)
    {
        Pesquisa.Clear();
        SetCurrentValue(SelecionadosProperty, Array.Empty<string>());
    }

    private void MarcarVisiveis(object sender, RoutedEventArgs e) =>
        SetCurrentValue(SelecionadosProperty, Selecionados.Union(Visiveis(), StringComparer.OrdinalIgnoreCase).ToArray());

    private void Alternar(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { DataContext: Opcao opcao })
        {
            var novos = opcao.Marcada
                ? Selecionados.Where(o => !string.Equals(o, opcao.Texto, StringComparison.OrdinalIgnoreCase)).ToArray()
                : [.. Selecionados, opcao.Texto];
            SetCurrentValue(SelecionadosProperty, novos);
        }
    }
}
