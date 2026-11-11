using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Media;
using Spobras.Core.Ambiente;
using Spobras.Dados.Ambiente;

namespace Spobras.TesteAmbiente;

public partial class MainWindow : Window
{
    private static readonly string[] Titulos =
    [
        "O programa abre neste PC",
        "Versão e arquitetura do Windows; escala do monitor",
        VerificacaoExcel.TituloLocalizar,
        VerificacaoExcel.TituloAutomacao,
        VerificacaoArquivos.Titulo,
        VerificacaoOneDrive.Titulo,
        VerificacaoSqlite.Titulo,
        "Mostrar o ícone na bandeja e uma notificação",
        VerificacaoVigia.Titulo,
    ];

    private readonly ObservableCollection<ItemTela> _itens = [];
    private readonly MascaraCaminhos _mascara = new(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.UserName);

    private string? _modelo;
    private string? _caminhoRelatorio;

    public MainWindow()
    {
        InitializeComponent();
        ListaItens.ItemsSource = _itens;
        ReiniciarLista();
    }

    private void ReiniciarLista()
    {
        _itens.Clear();
        for (int i = 0; i < Titulos.Length; i++)
        {
            _itens.Add(ItemTela.Aguardando(i + 1, Titulos[i]));
        }
    }

    private void EscolherModelo_Click(object sender, RoutedEventArgs e)
    {
        var dialogo = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Escolha o modelo do cronograma",
            Filter = "Planilhas do Excel|*.xlsx;*.xlsm;*.xls;*.xltx;*.xltm|Todos os arquivos|*.*",
            CheckFileExists = true,
        };
        if (dialogo.ShowDialog(this) == true)
        {
            _modelo = dialogo.FileName;
            TextoModelo.Text = Path.GetFileName(_modelo) + " (o teste usa uma cópia; o original não é alterado)";
            BotaoLimparModelo.Visibility = Visibility.Visible;
        }
    }

    private void LimparModelo_Click(object sender, RoutedEventArgs e)
    {
        _modelo = null;
        TextoModelo.Text = "Nenhum arquivo escolhido. Se escolher, o teste abre uma cópia dele; o original não é alterado.";
        BotaoLimparModelo.Visibility = Visibility.Collapsed;
    }

    private async void Rodar_Click(object sender, RoutedEventArgs e)
    {
        BotaoRodar.IsEnabled = false;
        BotaoModelo.IsEnabled = false;
        BotaoLimparModelo.IsEnabled = false;
        PainelRelatorio.Visibility = Visibility.Collapsed;
        ReiniciarLista();

        var resultados = new List<ResultadoVerificacao>();
        try
        {
            string downloads = Pastas.Downloads();
            string? modelo = _modelo;

            await RodarItem(resultados, 1, () => Task.FromResult(VerificarPrograma()));
            await RodarItem(resultados, 2, () => Task.FromResult(VerificarWindows()));
            await RodarItem(resultados, 3, () => Task.Run(VerificacaoExcel.Localizar));
            await RodarItem(resultados, 4, () => VerificacaoExcel.AutomatizarAsync(downloads, modelo));
            await RodarItem(resultados, 5, () => Task.Run(() => VerificacaoArquivos.Executar(downloads)));
            await RodarItem(resultados, 6, VerificacaoOneDrive.ExecutarAsync);
            await RodarItem(resultados, 7, () => Task.Run(() => VerificacaoSqlite.Executar(Pastas.DadosDoApp())));
            await RodarItem(resultados, 8, VerificarBandejaAsync);
            await RodarItem(resultados, 9, () => Task.Run(() => VerificacaoVigia.ExecutarAsync(downloads, TimeSpan.FromSeconds(15))));

            SalvarRelatorio(resultados, downloads);
        }
        finally
        {
            TextoAndamento.Text = "";
            BotaoRodar.Content = "Rodar de novo";
            BotaoRodar.IsEnabled = true;
            BotaoModelo.IsEnabled = true;
            BotaoLimparModelo.IsEnabled = true;
        }
    }

    private async Task RodarItem(List<ResultadoVerificacao> resultados, int numero, Func<Task<ResultadoVerificacao>> verificar)
    {
        string titulo = Titulos[numero - 1];
        _itens[numero - 1] = ItemTela.Testando(numero, titulo);
        TextoAndamento.Text = $"Testando o item {numero} de {Titulos.Length}…";

        ResultadoVerificacao resultado;
        try
        {
            resultado = await verificar();
        }
        catch (Exception ex)
        {
            // Qualquer erro inesperado vira "falhou" com o motivo, para aparecer no relatorio.
            resultado = ResultadoVerificacao.Falhou(numero, titulo, $"Erro inesperado: {ex.GetType().Name}: {ex.Message}");
        }

        resultados.Add(resultado);
        _itens[numero - 1] = ItemTela.De(resultado, _mascara);
    }

    private static ResultadoVerificacao VerificarPrograma() => ResultadoVerificacao.Passou(1, Titulos[0],
        $"Versão do teste: {VersaoDoPrograma()}",
        $"Executável: {Environment.ProcessPath}",
        $"Processo: {(Environment.Is64BitProcess ? "64 bits" : "32 bits")}, {RuntimeInformation.FrameworkDescription} (embutido, sem instalação)");

    private ResultadoVerificacao VerificarWindows()
    {
        var detalhes = new List<string>
        {
            $"Windows: {RuntimeInformation.OSDescription} (compilação {Environment.OSVersion.Version.Build})",
            $"Arquitetura do Windows: {RuntimeInformation.OSArchitecture}",
        };

        var dpi = VisualTreeHelper.GetDpi(this);
        detalhes.Add($"Escala do monitor desta janela: {dpi.DpiScaleX * 100:0}%");

        var telas = System.Windows.Forms.Screen.AllScreens;
        for (int i = 0; i < telas.Length; i++)
        {
            var area = telas[i].Bounds;
            detalhes.Add($"Monitor {i + 1}: {area.Width} x {area.Height}{(telas[i].Primary ? " (principal)" : "")}");
        }

        return ResultadoVerificacao.Passou(2, Titulos[1], [.. detalhes]);
    }

    private async Task<ResultadoVerificacao> VerificarBandejaAsync()
    {
        const string titulo = "Arquivos SPOBRAS";
        using var icone = new System.Windows.Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Information,
            Text = "Arquivos SPOBRAS - Teste de Ambiente",
            Visible = true,
        };
        icone.ShowBalloonTip(8000, titulo, "Esta é a notificação do Teste de Ambiente.", System.Windows.Forms.ToolTipIcon.Info);

        // Da tempo de o Windows mostrar o icone e a notificacao antes da pergunta.
        await Task.Delay(TimeSpan.FromSeconds(3));

        var iconeVisto = MessageBox.Show(this,
            "Um ícone azul de informação (i) apareceu na bandeja, perto do relógio?\n\n" +
            "Se não estiver visível, confira também a setinha \"^\" de ícones ocultos.",
            "Item 8: ícone na bandeja", MessageBoxButton.YesNo, MessageBoxImage.Question);
        var notificacaoVista = MessageBox.Show(this,
            "Apareceu uma notificação com o título \"Arquivos SPOBRAS\"?",
            "Item 8: notificação", MessageBoxButton.YesNo, MessageBoxImage.Question);

        icone.Visible = false;

        string linhaIcone = $"Ícone na bandeja visto pelo usuário: {(iconeVisto == MessageBoxResult.Yes ? "sim" : "NÃO")}";
        string linhaNotificacao = $"Notificação vista pelo usuário: {(notificacaoVista == MessageBoxResult.Yes ? "sim" : "NÃO")}";

        if (iconeVisto != MessageBoxResult.Yes)
        {
            return ResultadoVerificacao.Falhou(8, Titulos[7], linhaIcone, linhaNotificacao);
        }

        return notificacaoVista == MessageBoxResult.Yes
            ? ResultadoVerificacao.Passou(8, Titulos[7], linhaIcone, linhaNotificacao)
            : ResultadoVerificacao.Falhou(8, Titulos[7], linhaIcone, linhaNotificacao,
                "As notificações podem estar desligadas no Windows (Configurações > Sistema > Notificações) ou em \"Não incomodar\".");
    }

    private void SalvarRelatorio(List<ResultadoVerificacao> resultados, string downloads)
    {
        string texto = RelatorioAmbiente.Gerar(resultados, DateTimeOffset.Now, VersaoDoPrograma(), _mascara);
        int falhas = resultados.Count(r => r.Estado == EstadoVerificacao.Falhou);
        string resumo = falhas == 0 ? "Teste concluído sem falhas." : $"Teste concluído com {falhas} falha(s).";

        try
        {
            _caminhoRelatorio = Path.Combine(downloads, RelatorioAmbiente.NomeArquivo);
            File.WriteAllText(_caminhoRelatorio, texto, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            TextoRelatorio.Text = $"{resumo} Relatório salvo em Downloads: {RelatorioAmbiente.NomeArquivo}. Envie esse arquivo de volta.";
            BotaoAbrirRelatorio.Content = "Mostrar o relatório na pasta";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Sem conseguir gravar em Downloads, o relatorio vai para a area de transferencia.
            _caminhoRelatorio = null;
            Clipboard.SetText(texto);
            TextoRelatorio.Text = $"{resumo} Não foi possível salvar o relatório em Downloads ({ex.Message}). " +
                                  "O texto do relatório foi copiado: cole (Ctrl+V) em uma mensagem e envie.";
            BotaoAbrirRelatorio.Content = "Copiar de novo";
        }

        PainelRelatorio.Tag = texto;
        PainelRelatorio.Visibility = Visibility.Visible;
    }

    private void AbrirRelatorio_Click(object sender, RoutedEventArgs e)
    {
        if (_caminhoRelatorio is null)
        {
            Clipboard.SetText((string)PainelRelatorio.Tag);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_caminhoRelatorio}\"") { UseShellExecute = false })?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            MessageBox.Show(this, $"Não foi possível abrir a pasta: {ex.Message}", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static string VersaoDoPrograma()
    {
        string? versao = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return versao ?? "desconhecida";
    }
}
