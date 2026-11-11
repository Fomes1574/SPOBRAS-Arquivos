using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Spobras.App.Servicos;
using Spobras.App.ViewModels;
using Spobras.Core.Organizar;
using Spobras.Core.Renomear;

namespace Spobras.App.Telas.Tests;

/// <summary>
/// Abre a janela de verdade (so no Windows), passa por todas as telas com dados de exemplo,
/// falha se houver erro de XAML ou de ligacao de dados e salva uma imagem de cada tela.
/// </summary>
public class TelasDeVerdadeTests
{
    [Fact]
    public void Todas_as_telas_abrem_sem_erro_e_viram_imagem()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "As telas WPF só abrem no Windows.");

        Exception? erro = null;
        var thread = new Thread(() =>
        {
            try
            {
                Percorrer();
            }
            catch (Exception ex)
            {
                erro = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (erro is not null)
        {
            Assert.Fail(erro.ToString());
        }
    }

    private static void Percorrer()
    {
        string raiz = Directory.CreateTempSubdirectory("spobras-telas-").FullName;
        string saida = PastaDeSaida();
        Directory.CreateDirectory(saida);
        var errosDeLigacao = new ColetorDeErros();
        PresentationTraceSources.Refresh();
        PresentationTraceSources.DataBindingSource.Listeners.Add(errosDeLigacao);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;

        try
        {
            string trabalho = Path.Combine(raiz, "Downloads");
            Directory.CreateDirectory(trabalho);
            foreach (string nome in new[]
                     {
                         "medicao out final (2).xlsx", "IMG-20261110-WA0007.jpg", "Doc 10-11.pdf", "cronograma novo lote.xlsx",
                         "arquivo.zip", "scan_0012.pdf", "Novo documento 3.pdf", "foto 2.jpg", "foto 10.jpg",
                     })
            {
                File.WriteAllText(Path.Combine(trabalho, nome), new string('x', 2000));
            }

            foreach (string pasta in new[] { "Pautas", "Empenhos", "Fotos da escola" })
            {
                Directory.CreateDirectory(Path.Combine(trabalho, pasta));
                File.WriteAllText(Path.Combine(trabalho, pasta, "documento 1.pdf"), new string('y', 5000));
                File.WriteAllText(Path.Combine(trabalho, pasta, "documento 2.pdf"), new string('z', 9000));
            }

            var app = new Spobras.App.App();
            app.InitializeComponent();

            using var composicao = new Composicao(Path.Combine(raiz, "dados"), trabalho, new DialogosMudos(), new InterfaceDireta(), "0.0.0-teste");
            composicao.Preferencias.Alterar(p => p with { ModoAutomatico = false });
            composicao.Novidades.GravarRegras(
            [
                new Regra { Nome = "Medições", NomeContem = ["medicao", "medição"], Extensoes = ["xlsx"], Destino = Path.Combine(raiz, "Medições"), Prioridade = 10 },
                new Regra { Nome = "Fotos de obra", Extensoes = ["jpg", "jpeg"], Destino = Path.Combine(raiz, "Fotos de obra") },
                new Regra { Nome = "Cronogramas", NomeContem = ["cronograma"], Destino = Path.Combine(raiz, "Cronogramas"), Prioridade = 10 },
            ]);
            composicao.Iniciar();
            var principal = composicao.Principal;

            var janela = new Spobras.App.MainWindow(principal)
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = 0,
                Top = 0,
                Width = 1360,
                Height = 900,
                ShowInTaskbar = false,
            };
            janela.Show();

            // Inicio vazio
            Fotografar(janela, saida, "01-inicio-vazio");

            // Renomear: vazio, com previa, com impedimento e modo complemento
            principal.Navegar(RenomearVm.Modulo);
            var renomear = (RenomearVm)principal.PaginaAtual!;
            Fotografar(janela, saida, "02-renomear-vazio");
            renomear.Adicionar(Directory.GetFiles(trabalho, "*.pdf").Concat(Directory.GetFiles(trabalho, "*.jpg")));
            renomear.NomeBase = "Relatório";
            Fotografar(janela, saida, "03-renomear-previa");
            renomear.Linhas[1].NomeNovo = "Anexo 1/2";
            renomear.ContinuarNumeracao = true;
            Fotografar(janela, saida, "04-renomear-impedido-e-avisos");
            renomear.Modo = ModoRenomear.NomeComplemento;
            renomear.Linhas[0].Complemento = "Lote 12";
            Fotografar(janela, saida, "05-renomear-complemento");
            renomear.Modo = ModoRenomear.NomeNumero;
            renomear.ContinuarNumeracao = false;
            renomear.RenomearCommand.ExecuteAsync(null);
            Esperar(() => !renomear.Ocupado && renomear.Resultado.Length > 0);
            Fotografar(janela, saida, "06-renomear-resultado");

            // Organizar: sugestoes, voce decide e regras abertas
            principal.Navegar(OrganizarVm.Modulo);
            var organizar = (OrganizarVm)principal.PaginaAtual!;
            Fotografar(janela, saida, "07-organizar-sem-novidades");
            organizar.ProcurarDeNovoCommand.Execute(null);
            Fotografar(janela, saida, "08-organizar-sugestoes");
            organizar.RegrasAbertas = true;
            Fotografar(janela, saida, "09-organizar-regras");
            organizar.RegrasAbertas = false;
            organizar.MoverSugeridosCommand.ExecuteAsync(null);
            Esperar(() => !organizar.Ocupado && organizar.Resultado.Length > 0);
            Fotografar(janela, saida, "10-organizar-resultado");

            // Compactar: tres modos
            principal.Navegar(CompactarVm.Modulo);
            var compactar = (CompactarVm)principal.PaginaAtual!;
            Fotografar(janela, saida, "11-compactar-vazio");
            compactar.Adicionar(Directory.GetDirectories(trabalho).Append(Path.Combine(trabalho, "arquivo.zip")));
            Fotografar(janela, saida, "12-compactar-por-pasta");
            compactar.Modo = ModoCompactar.PorLimite;
            compactar.LimiteMb = "0,02";
            Fotografar(janela, saida, "13-compactar-por-limite");
            compactar.Modo = ModoCompactar.UmPorPasta;
            compactar.CriarCommand.ExecuteAsync(null);
            Esperar(() => !compactar.Ocupado && compactar.Resultado.Length > 0);
            Fotografar(janela, saida, "14-compactar-resultado");

            // Historico, configuracoes e inicio com acoes
            principal.Navegar("historico");
            var historico = (HistoricoVm)principal.PaginaAtual!;
            historico.Selecionado = historico.Lotes.LastOrDefault();
            Fotografar(janela, saida, "15-historico");
            principal.Navegar("configuracoes");
            Fotografar(janela, saida, "16-configuracoes");
            principal.Navegar("inicio");
            Fotografar(janela, saida, "17-inicio-com-acoes");

            // Janela estreita: o menu se recolhe
            janela.Width = 900;
            janela.Height = 700;
            principal.Navegar(RenomearVm.Modulo);
            renomear.Adicionar(Directory.GetFiles(trabalho).Take(4));
            Fotografar(janela, saida, "18-estreita-renomear");
            principal.Navegar(OrganizarVm.Modulo);
            organizar.ProcurarDeNovoCommand.Execute(null);
            Fotografar(janela, saida, "19-estreita-organizar");

            // Dialogo de confirmacao no estilo do app
            var dialogo = new Spobras.App.Windows.JanelaDialogo("Desfazer", "2 arquivos voltam ao nome anterior.\n1 arquivo fica como está: O arquivo foi alterado depois da ação.", "Desfazer os possíveis", "Cancelar", null);
            dialogo.WindowStartupLocation = WindowStartupLocation.Manual;
            dialogo.Show();
            Fotografar(dialogo, saida, "20-dialogo");
            dialogo.Close();

            janela.ManterNaBandeja = null;
            janela.Close();
            Bombear();

            Assert.True(errosDeLigacao.Mensagens.Count == 0,
                "Erros de ligação de dados nas telas:\n" + string.Join("\n", errosDeLigacao.Mensagens.Distinct().Take(20)));
            Assert.True(Directory.GetFiles(saida, "*.png").Length >= 20);
        }
        finally
        {
            PresentationTraceSources.DataBindingSource.Listeners.Remove(errosDeLigacao);
            try
            {
                Directory.Delete(raiz, recursive: true);
            }
            catch (IOException)
            {
                // Pasta temporaria; o sistema limpa depois.
            }
        }
    }

    private static void Fotografar(Window janela, string pasta, string nome)
    {
        Bombear();
        janela.UpdateLayout();
        Bombear();

        var conteudo = (FrameworkElement)janela.Content;
        int largura = Math.Max(1, (int)Math.Ceiling(conteudo.ActualWidth));
        int altura = Math.Max(1, (int)Math.Ceiling(conteudo.ActualHeight));
        var fundo = new DrawingVisual();
        using (var contexto = fundo.RenderOpen())
        {
            contexto.DrawRectangle(janela.Background ?? Brushes.White, null, new Rect(0, 0, largura, altura));
            contexto.DrawRectangle(new VisualBrush(conteudo) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null, new Rect(0, 0, largura, altura));
        }

        var imagem = new RenderTargetBitmap(largura, altura, 96, 96, PixelFormats.Pbgra32);
        imagem.Render(fundo);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(imagem));
        using var arquivo = File.Create(Path.Combine(pasta, nome + ".png"));
        png.Save(arquivo);
    }

    /// <summary>Deixa a tela processar ligacoes, layout e desenho pendentes.</summary>
    private static void Bombear()
    {
        var quadro = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => quadro.Continue = false));
        Dispatcher.PushFrame(quadro);
    }

    private static void Esperar(Func<bool> condicao)
    {
        var limite = Stopwatch.StartNew();
        while (!condicao() && limite.Elapsed < TimeSpan.FromSeconds(30))
        {
            Bombear();
            Thread.Sleep(20);
        }

        Assert.True(condicao(), "A ação não terminou em 30 segundos.");
    }

    private static string PastaDeSaida()
    {
        if (Environment.GetEnvironmentVariable("SPOBRAS_TELAS") is { Length: > 0 } pasta)
        {
            return pasta;
        }

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ArquivosSpobras.sln")))
        {
            dir = dir.Parent;
        }

        return Path.Combine(dir?.FullName ?? Path.GetTempPath(), "artifacts", "telas");
    }

    private sealed class ColetorDeErros : TraceListener
    {
        public List<string> Mensagens { get; } = [];

        public override void Write(string? message)
        {
        }

        public override void WriteLine(string? message)
        {
            if (!string.IsNullOrWhiteSpace(message))
            {
                Mensagens.Add(message);
            }
        }
    }

    /// <summary>Nenhuma janela do Windows e aberta no teste: toda pergunta e respondida com "sim".</summary>
    private sealed class DialogosMudos : IDialogos
    {
        public string? EscolherPasta(string titulo, string? pastaInicial = null) => null;
        public IReadOnlyList<string> EscolherPastas(string titulo, string? pastaInicial = null) => [];
        public IReadOnlyList<string> EscolherArquivos(string titulo, string? filtro = null, string? pastaInicial = null) => [];
        public bool Confirmar(string titulo, string mensagem, string botaoSim = "Sim", string botaoNao = "Cancelar") => true;

        public void Avisar(string titulo, string mensagem)
        {
        }

        public string? PedirTexto(string titulo, string mensagem, string valorInicial = "") => null;
        public string? LerAreaDeTransferencia() => null;

        public void MostrarNaPasta(string caminho)
        {
        }

        public void AbrirPasta(string pasta)
        {
        }
    }
}
