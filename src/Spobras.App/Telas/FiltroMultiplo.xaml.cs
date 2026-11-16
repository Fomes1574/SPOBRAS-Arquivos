using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using Spobras.Core.Base;

namespace Spobras.App.Telas;

public partial class FiltroMultiplo : UserControl
{
    /// <summary>Uma opcao da lista. Marcar so muda esta caixinha: a lista nao e recriada e a rolagem fica onde estava.</summary>
    private sealed class Opcao(string texto, string titulo, string? detalhe, bool marcada) : System.ComponentModel.INotifyPropertyChanged
    {
        private bool _marcada = marcada;

        public string Texto { get; } = texto;
        public string Titulo { get; } = titulo;
        public string? Detalhe { get; } = detalhe;

        public bool Marcada
        {
            get => _marcada;
            set
            {
                if (_marcada != value)
                {
                    _marcada = value;
                    PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Marcada)));
                }
            }
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    }

    private List<Opcao> _lista = [];

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

    private bool _atualizacaoPendente;

    // A lista de opcoes e trocada item a item (Clear + Add): junta tudo numa atualizacao so.
    private void ItensMudaram(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_atualizacaoPendente)
            return;
        _atualizacaoPendente = true;
        Dispatcher.BeginInvoke(() =>
        {
            _atualizacaoPendente = false;
            Atualizar();
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private readonly Dictionary<string, string> _chaves = [];

    private string Chave(string texto)
    {
        if (!_chaves.TryGetValue(texto, out var chave))
            _chaves[texto] = chave = Normalizacao.Busca(texto);
        return chave;
    }

    private IEnumerable<Opcao> Visiveis()
    {
        string busca = Normalizacao.Busca(Pesquisa.Text);
        foreach (var item in Itens ?? Array.Empty<string>())
        {
            string valor = item is OpcaoContratoResumo contrato ? contrato.Contrato : (string)item;
            string titulo = item is OpcaoContratoResumo c ? c.Titulo : valor;
            string? detalhe = (item as OpcaoContratoResumo)?.Detalhe;
            if (busca.Length == 0 || Chave(titulo + " " + detalhe).Contains(busca, StringComparison.Ordinal))
                yield return new Opcao(valor, titulo, detalhe, Selecionados.Contains(valor, StringComparer.OrdinalIgnoreCase));
        }
    }

    private void Atualizar()
    {
        SetValue(ResumoProperty, Selecionados.Count switch
        {
            0 => Rotulo is "Portaria" or "Escola" or "Empresa" or "Dotação" ? "Todas ▾" : "Todos ▾",
            1 => Selecionados[0],
            _ => $"{Selecionados[0]} + {Selecionados.Count - 1}",
        });
        if (LimparSelecao is not null)
        {
            LimparSelecao.Visibility = Selecionados.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            ResumoTexto.FontWeight = Selecionados.Count == 0 ? FontWeights.Normal : FontWeights.Bold;
        }
        SetValue(LimparNomeProperty, $"Limpar {Rotulo}");
        if (Opcoes is null || Abrir?.IsChecked != true)
            return;

        var visiveis = Visiveis().ToList();
        if (visiveis.Select(o => (o.Texto, o.Titulo, o.Detalhe)).SequenceEqual(_lista.Select(o => (o.Texto, o.Titulo, o.Detalhe))))
        {
            // Mesmas opcoes: so as marcas mudam (a rolagem fica onde o usuario deixou).
            foreach (var o in _lista)
                o.Marcada = Selecionados.Contains(o.Texto, StringComparer.OrdinalIgnoreCase);
            return;
        }

        _lista = visiveis;
        Opcoes.ItemsSource = _lista;
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
        SetCurrentValue(SelecionadosProperty, Selecionados.Union(Visiveis().Select(o => o.Texto), StringComparer.OrdinalIgnoreCase).ToArray());

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
