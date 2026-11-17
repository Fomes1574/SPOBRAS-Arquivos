using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Spobras.Dados.Base;
using Spobras.App.ViewModels;

namespace Spobras.App.Telas;

public partial class RelatorioTela : UserControl
{
    public RelatorioTela()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is RelatorioVm antigo) antigo.PropertyChanged -= AoMudarVm;
            if (e.NewValue is RelatorioVm novo) novo.PropertyChanged += AoMudarVm;
        };
    }

    /// <summary>A faixa vermelha do Desfazer encolhe no tempo que ele fica na tela (recomeca a cada remocao).</summary>
    private void AoMudarVm(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(RelatorioVm.TextoDesfazer) || sender is not RelatorioVm { TemDesfazer: true } vm) return;
        if (TempoDesfazer.RenderTransform is ScaleTransform escala)
        {
            escala.BeginAnimation(ScaleTransform.ScaleXProperty,
                new System.Windows.Media.Animation.DoubleAnimation(1, 0, new Duration(vm.TempoDoDesfazer)));
        }
    }

    private (Popup Popup, ListBox Lista) Sugestoes(TextBox campo) => campo == SugestaoUnidade
        ? (PopupUnidade, ListaUnidade) : (PopupContrato, ListaContrato);

    // Enquanto a escolha preenche o campo, a mudanca de texto nao e digitacao: a lista nao reabre.
    private bool _aplicandoSugestao;

    private void AoDigitarSugestao(object sender, TextChangedEventArgs e)
    {
        if (_aplicandoSugestao || sender is not TextBox { IsKeyboardFocused: true } campo || DataContext is not RelatorioVm vm) return;
        var (popup, lista) = Sugestoes(campo);
        var buscar = campo == SugestaoUnidade ? vm.BuscarSugestoesEscola : vm.BuscarSugestoesContrato;
        var opcoes = buscar?.Invoke(campo.Text) ?? [];
        lista.ItemsSource = opcoes;
        popup.IsOpen = opcoes.Count > 0;
    }

    private void TeclasSugestao(object sender, KeyEventArgs e)
    {
        var (popup, lista) = Sugestoes((TextBox)sender);
        if (!popup.IsOpen) return;
        if (e.Key is Key.Down or Key.Up)
        {
            lista.SelectedIndex = Math.Clamp(lista.SelectedIndex + (e.Key == Key.Down ? 1 : -1), 0, lista.Items.Count - 1);
            lista.ScrollIntoView(lista.SelectedItem);
        }
        else if (e.Key == Key.Escape) popup.IsOpen = false;
        else if (e.Key == Key.Enter)
        {
            if (lista.SelectedIndex < 0) lista.SelectedIndex = 0;
            AplicarSugestao(lista);
        }
        else return;
        e.Handled = true;
    }

    private void ClicarSugestao(object sender, MouseButtonEventArgs e)
    {
        var lista = (ListBox)sender;
        if (ItemsControl.ContainerFromElement(lista, e.OriginalSource as DependencyObject) is ListBoxItem item)
        {
            lista.SelectedItem = item.DataContext;
            AplicarSugestao(lista);
        }
    }

    private void AplicarSugestao(ListBox lista)
    {
        if (lista.SelectedItem is not ProcuraObras.Sugestao sugestao || DataContext is not RelatorioVm vm) return;
        bool escola = lista == ListaUnidade;
        var campo = escola ? SugestaoUnidade : SugestaoContrato;
        Sugestoes(campo).Popup.IsOpen = false;
        _aplicandoSugestao = true;
        try
        {
            vm.EscolherSugestao(sugestao.Valor, escola);
            campo.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
            (escola ? SugestaoContrato : SugestaoUnidade).GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
        }
        finally
        {
            _aplicandoSugestao = false;
        }

        campo.CaretIndex = campo.Text.Length;
    }

    private void ApagarCampo(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: TextBox campo } || DataContext is not RelatorioVm vm) return;
        PopupUnidade.IsOpen = PopupContrato.IsOpen = false;
        vm.ApagarCampo(() =>
        {
            campo.SetCurrentValue(TextBox.TextProperty, "");
            campo.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        });
    }

    private const string FormatoFoto = "Spobras.FotoRelatorio";
    private Point _origemFoto;
    private ListBoxItem? _fotoArrastada;
    private ListBoxItem? _destinoFoto;

    private void IniciarArrasteFoto(object sender, MouseButtonEventArgs e)
    {
        _fotoArrastada = null;
        var origem = e.OriginalSource as DependencyObject;
        for (var elemento = origem; elemento is Visual && elemento != ListaFotos; elemento = VisualTreeHelper.GetParent(elemento))
        {
            if (elemento is ButtonBase or TextBoxBase or ScrollBar) return;
            if (elemento is ListBoxItem item)
            {
                _fotoArrastada = item;
                _origemFoto = e.GetPosition(ListaFotos);
                return;
            }
        }
    }

    private void ArrastarFoto(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _fotoArrastada?.DataContext is not FotoVm foto) return;
        var deslocamento = e.GetPosition(ListaFotos) - _origemFoto;
        if (Math.Abs(deslocamento.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(deslocamento.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var item = _fotoArrastada;
        _fotoArrastada = null;
        DragDrop.DoDragDrop(item, new DataObject(FormatoFoto, foto), DragDropEffects.Move);
        LimparDestinoFoto();
    }

    private void MarcarDestinoFoto(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(FormatoFoto)) return;
        e.Handled = true;
        var alvo = ItemsControl.ContainerFromElement(ListaFotos, e.OriginalSource as DependencyObject) as ListBoxItem;
        if (alvo != _destinoFoto)
        {
            LimparDestinoFoto();
            _destinoFoto = alvo;
        }
        e.Effects = alvo is null ? DragDropEffects.None : DragDropEffects.Move;
        if (alvo is not null)
        {
            alvo.BorderBrush = Brushes.SteelBlue;
            alvo.BorderThickness = e.GetPosition(alvo).Y < alvo.ActualHeight / 2
                ? new Thickness(0, 2, 0, 0) : new Thickness(0, 0, 0, 2);
        }
    }

    private void SoltarFoto(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(FormatoFoto)) return;
        e.Handled = true;
        if (e.Data.GetData(FormatoFoto) is FotoVm foto && DataContext is RelatorioVm vm &&
            ItemsControl.ContainerFromElement(ListaFotos, e.OriginalSource as DependencyObject) is ListBoxItem alvo &&
            alvo.DataContext is FotoVm destino)
        {
            int de = vm.Fotos.IndexOf(foto);
            int inserir = vm.Fotos.IndexOf(destino) + (e.GetPosition(alvo).Y >= alvo.ActualHeight / 2 ? 1 : 0);
            vm.MoverFoto(de, inserir > de ? inserir - 1 : inserir);
        }
        LimparDestinoFoto();
    }

    private void SairArrasteFoto(object sender, DragEventArgs e) => LimparDestinoFoto();

    private void LimparDestinoFoto()
    {
        _destinoFoto?.ClearValue(Border.BorderBrushProperty);
        _destinoFoto?.ClearValue(Border.BorderThicknessProperty);
        _destinoFoto = null;
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
