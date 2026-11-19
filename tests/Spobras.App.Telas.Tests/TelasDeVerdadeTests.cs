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
        Relatorio.Clear();
        Problemas.Clear();
        // Erro fora da thread da tela derruba o processo: deixa o motivo gravado para o CI mostrar.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            File.WriteAllText(Path.Combine(saida, "resumo.txt"), "ERRO NAO TRATADO:\n" + e.ExceptionObject);
        var errosDeLigacao = new ColetorDeErros();
        PresentationTraceSources.Refresh();
        PresentationTraceSources.DataBindingSource.Listeners.Add(errosDeLigacao);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;

        // Como no app de verdade: o que vem depois de um "await" volta para a thread da tela.
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));

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

            var dialogos = new DialogosMudos();
            using var composicao = new Composicao(Path.Combine(raiz, "dados"), trabalho, dialogos, new Spobras.App.Windows.InterfaceWpf(Dispatcher.CurrentDispatcher), "0.0.0-teste", eventosDasFontes: false)
            {
                VigiarFontes = false,
            };
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

            // Monitor do CI menor que a janela: o app abre maximizado (CaberNaTela). As medidas do teste
            // valem para o tamanho pedido, entao a janela volta ao normal com 1360x900.
            janela.WindowState = WindowState.Normal;
            janela.Left = 0;
            janela.Top = 0;
            janela.Width = 1360;
            janela.Height = 900;

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

            // Relatorio fotografico: tres etapas, com logo, fotos deitadas e em pe
            string logo = CriarFoto(Path.Combine(raiz, "logo.png"), 195, 138, System.Windows.Media.Colors.Firebrick);
            composicao.Modelos.DefinirLogo(logo);
            var fotos = Enumerable.Range(1, 9).Select(n => CriarFoto(Path.Combine(trabalho, "Fotos", $"foto {n}.png"), n % 4 == 0 ? 600 : 800, n % 4 == 0 ? 800 : 600,
                n % 2 == 0 ? System.Windows.Media.Colors.SteelBlue : System.Windows.Media.Colors.Peru)).ToList();
            principal.Navegar(RelatorioVm.Modulo);
            var relatorio = (RelatorioVm)principal.PaginaAtual!;
            Fotografar(janela, saida, "21-relatorio-dados-vazio");
            relatorio.Unidade = "EMEF Exemplo de Escola";
            relatorio.Contratada = "Construtora Exemplo Ltda";
            relatorio.DataTexto = "10/11/2026";
            relatorio.Contrato = "000/EXEMPLO/2026";
            relatorio.Lote = "12";
            Fotografar(janela, saida, "22-relatorio-dados");
            relatorio.Etapa = 1;
            Fotografar(janela, saida, "23-relatorio-fotos-vazio");
            relatorio.Adicionar([.. fotos, Path.Combine(trabalho, "arquivo.zip")]);
            relatorio.Fotos[0].Legenda = "Execução de alvenaria de vedação no bloco administrativo, pavimento térreo";
            relatorio.Fotos[1].Legenda = "Fachada principal";
            Esperar(() => relatorio.Fotos.All(f => f.Miniatura is not null));
            Fotografar(janela, saida, "24-relatorio-fotos");
            // Tela 2 nova: modo com descricao, fotos marcadas, mesmo texto e o Desfazer
            relatorio.Modo = Spobras.Core.Relatorio.ModoLegenda.DescricaoServico;
            relatorio.Fotos[0].Descricao = "Concreto usinado fck 30 MPa lançado nos blocos B1 a B4, com adensamento mecânico e cura úmida por sete dias.";
            relatorio.Fotos[1].Selecionada = true;
            relatorio.Fotos[2].Selecionada = true;
            relatorio.LegendaEmLote = "Armação do bloco de fundação";
            relatorio.DescricaoEmLote = "Aço CA-50, cobrimento conferido e espaçadores posicionados antes da concretagem.";
            Fotografar(janela, saida, "24b-relatorio-fotos-descricao-marcadas");
            relatorio.DesmarcarTodasCommand.Execute(null);
            relatorio.TempoDoDesfazer = TimeSpan.FromMinutes(5);
            relatorio.RemoverCommand.Execute(relatorio.Fotos[^1]);
            Fotografar(janela, saida, "24c-relatorio-desfazer");
            relatorio.DesfazerCommand.Execute(null);
            relatorio.Modo = Spobras.Core.Relatorio.ModoLegenda.LegendaCurta;
            Fotografar(janela, saida, "24d-relatorio-descricoes-guardadas");
            relatorio.Etapa = 2;
            Fotografar(janela, saida, "25-relatorio-conferencia");
            relatorio.Modo = Spobras.Core.Relatorio.ModoLegenda.DescricaoServico;
            relatorio.Fotos[0].Descricao = "Serviço de impermeabilização da laje de cobertura com manta asfáltica, incluindo regularização da base e proteção mecânica.";
            relatorio.FotosPorPagina = 4;
            relatorio.Assinaturas = true;
            relatorio.AssinaturaFiscal = "Fulano de Tal";
            relatorio.DataTexto = "31/02/2026";
            Fotografar(janela, saida, "26-relatorio-descricao-e-avisos");
            relatorio.DataTexto = "10/11/2026";
            relatorio.GerarCommand.ExecuteAsync(null);
            Esperar(() => !relatorio.Ocupado && relatorio.Resultado.Length > 0);
            Assert.False(relatorio.ResultadoComFalha, relatorio.Resultado);
            Fotografar(janela, saida, "27-relatorio-gerado");
            relatorio.Etapa = 1;
            Fotografar(janela, saida, "28-relatorio-fotos-com-descricao");

            // PDF: as quatro ferramentas
            principal.Navegar(PdfVm.Modulo);
            var pdf = (PdfVm)principal.PaginaAtual!;
            Fotografar(janela, saida, "29-pdf-juntar-vazio");
            pdf.Ferramenta = FerramentaPdf.FotosEmPdf;
            pdf.Adicionar(fotos.Take(3));
            Fotografar(janela, saida, "30-pdf-fotos");
            pdf.ExecutarCommand.ExecuteAsync(null);
            Esperar(() => !pdf.Ocupado && pdf.Resultado.Length > 0);
            Assert.False(pdf.ResultadoComFalha, pdf.Resultado);
            Fotografar(janela, saida, "31-pdf-resultado");
            pdf.Ferramenta = FerramentaPdf.Separar;
            pdf.Adicionar(Directory.GetFiles(trabalho, "Fotos*.pdf"));
            pdf.SepararCadaPagina = false;
            pdf.Intervalos = "1-2, 9";
            Fotografar(janela, saida, "32-pdf-separar-intervalo-invalido");
            pdf.Ferramenta = FerramentaPdf.Reduzir;
            pdf.Adicionar(Directory.GetFiles(trabalho, "Fotos*.pdf"));
            Fotografar(janela, saida, "33-pdf-reduzir");
            pdf.Ferramenta = FerramentaPdf.LogoENumeracao;
            pdf.Adicionar(Directory.GetFiles(trabalho, "Fotos*.pdf"));
            pdf.APartirDa = "2";
            Fotografar(janela, saida, "47-pdf-logo-e-numeracao");
            pdf.Ferramenta = FerramentaPdf.ParaExcel;
            pdf.Adicionar(Directory.GetFiles(trabalho, "Fotos*.pdf"));
            Fotografar(janela, saida, "48-pdf-para-excel");

            // Cronograma: vazio, com linhas (uma impedida) e ajustes por mes
            principal.Navegar(CronogramaVm.Modulo);
            var cronograma = (CronogramaVm)principal.PaginaAtual!;
            Fotografar(janela, saida, "41-cronograma-vazio");
            string modeloCronograma = Path.Combine(raiz, "Base", "Cronograma modelo.xlsx");
            Spobras.Testes.XlsxSintetico.GravarModeloCronograma(modeloCronograma);
            for (int n = 1; n <= 3; n++)
            {
                cronograma.NovaLinhaCommand.Execute(null);
                var linha = cronograma.Linhas[n - 1];
                linha.Portaria = "3";
                linha.Lote = "12";
                linha.Dotacao = "16.10.12.365.3025.3362.4490.5100.00";
                linha.Nome = n == 2 ? "EMEF Professora Fulana de Tal da Silva Sauro - Unidade II" : $"Escola Exemplo {n}";
                linha.Escopo = "Reforma geral";
                linha.Valor = n == 3 ? "abc" : "1.250.000,00";
                linha.Inicio = "out/26";
                linha.Fim = "jun/27";
            }

            cronograma.NomeCronograma = "Cronograma de novembro";
            Fotografar(janela, saida, "42-cronograma-linhas-e-avisos");
            cronograma.Linhas[2].Valor = "980.000,00";
            foreach (var (mes, pct) in new[] { ("out/26", "5"), ("nov/26", "7"), ("dez/26", "8") })
            {
                cronograma.NovoAjusteCommand.Execute(null);
                cronograma.Ajustes[^1].Mes = mes;
                cronograma.Ajustes[^1].Quantia = pct;
            }

            cronograma.NovoAjusteCommand.Execute(null);
            cronograma.Ajustes[^1].Mes = "jan/27";
            cronograma.Ajustes[^1].Tipo = 1;
            cronograma.Ajustes[^1].Quantia = "50.000,00";
            cronograma.Ajustes[^1].Para = 2;
            Fotografar(janela, saida, "43-cronograma-ajustes-e-como-fica");

            // Ferramentas: busca com resultado, repetidos, juntar com previa, Excel para PDF
            principal.Navegar(FerramentasVm.Modulo);
            var ferramentas = (FerramentasVm)principal.PaginaAtual!;
            Fotografar(janela, saida, "49-ferramentas-buscar-vazio");
            ferramentas.Termo = "Fotos";
            ferramentas.ExecutarCommand.ExecuteAsync(null);
            Esperar(() => !ferramentas.Ocupado && ferramentas.Resultado.Length > 0);
            Fotografar(janela, saida, "50-ferramentas-buscar-resultado");
            ferramentas.Ferramenta = FerramentaExtra.Duplicados;
            ferramentas.ExecutarCommand.ExecuteAsync(null);
            Esperar(() => !ferramentas.Ocupado && ferramentas.Resultado.Length > 0);
            Fotografar(janela, saida, "51-ferramentas-repetidos");
            ferramentas.Ferramenta = FerramentaExtra.Juntar;
            ferramentas.Adicionar([modeloCronograma]);
            Fotografar(janela, saida, "52-ferramentas-juntar");
            ferramentas.Ferramenta = FerramentaExtra.ExcelPdf;
            Fotografar(janela, saida, "53-ferramentas-excel-pdf");
            ferramentas.Ferramenta = FerramentaExtra.Padrao;
            Fotografar(janela, saida, "54-ferramentas-conferir-padrao");

            // Base de dados e Fontes de dados, com planilhas sinteticas de verdade
            string mestra = Path.Combine(raiz, "Base", Spobras.Core.Base.Fontes.NomeArquivoMestra);
            string dotacoes = Path.Combine(raiz, "Base", Spobras.Core.Base.Fontes.NomeArquivoDotacoes);
            Spobras.Testes.XlsxSintetico.Gravar(Spobras.Testes.BaseSintetica.Mestra(), mestra);
            Spobras.Testes.XlsxSintetico.Gravar(Spobras.Testes.BaseSintetica.Dotacoes(), dotacoes);
            principal.Navegar(BaseVm.Modulo);
            var baseDados = (BaseVm)principal.PaginaAtual!;
            Fotografar(janela, saida, "34-base-vazia");
            principal.Navegar(FontesVm.Modulo);
            Fotografar(janela, saida, "35-fontes-sem-arquivo");
            var lendo = Task.WhenAll(
                composicao.Fontes.DefinirAsync(Spobras.Core.Base.Fontes.Mestra, mestra),
                composicao.Fontes.DefinirAsync(Spobras.Core.Base.Fontes.Dotacoes, dotacoes));
            Esperar(() => lendo.IsCompleted);
            Assert.Equal(5, composicao.Base.TotalDeObras());
            Fotografar(janela, saida, "36-fontes-lidas");
            principal.Navegar(BaseVm.Modulo);
            Fotografar(janela, saida, "37-base-todas");
            baseDados.Portaria = [Spobras.Testes.BaseSintetica.P3De2021];
            baseDados.Selecionada = baseDados.Obras.First();
            Fotografar(janela, saida, "38-base-filtro-e-ficha");
            Assert.Equal([Spobras.Testes.BaseSintetica.P3De2021], baseDados.Portaria); // a tela nao pode soltar o filtro ao trocar as opcoes
            Assert.Equal(3, baseDados.Obras.Count);
            baseDados.ValorDe = "abc";
            baseDados.Contrato = ["002/EX/2024"];
            Fotografar(janela, saida, "39-base-avisos");
            // Bases sem contrato: seletor, ficha da licitação sem vencedora, "também está em" e a licitar
            baseDados.BaseAtual = Spobras.Core.Base.BaseDeObras.EmLicitacao;
            baseDados.Licitacao.Selecionada = baseDados.Licitacao.Obras.Single(o => o.Nome == "EMEF Alfa");
            Assert.True(baseDados.Licitacao.TemTambemEm);
            Fotografar(janela, saida, "39a-base-em-licitacao");
            baseDados.BaseAtual = Spobras.Core.Base.BaseDeObras.ALicitar;
            baseDados.ALicitar.Selecionada = baseDados.ALicitar.Obras.First();
            Fotografar(janela, saida, "39b-base-a-licitar");
            // Cronograma: "Adicionar contrato" com resultados visiveis (contratos e grupos em licitacao) e o grupo incluido
            principal.Navegar(CronogramaVm.Modulo);
            var cronoBusca = (CronogramaVm)principal.PaginaAtual!;
            cronoBusca.LimparCommand.Execute(null);
            cronoBusca.AlternarBuscaContratoCommand.Execute(null);
            cronoBusca.PesquisaContrato = "lote 01";
            Esperar(() => cronoBusca.TemItensBusca);
            Fotografar(janela, saida, "43a-cronograma-adicionar-contrato");
            cronoBusca.AdicionarItemBuscaCommand.Execute(cronoBusca.ItensBusca.First(i => i.Base == Spobras.Core.Base.BaseDeObras.EmLicitacao));
            Fotografar(janela, saida, "43b-cronograma-obra-em-licitacao");
            principal.Navegar(BaseVm.Modulo);
            baseDados.BaseAtual = Spobras.Core.Base.BaseDeObras.EmAndamento;
            baseDados.LimparFiltrosCommand.Execute(null);
            baseDados.Selecionada = baseDados.Obras.First();
            Assert.False(baseDados.EditarObraCommand.CanExecute(null)); // a Base e so consulta

            // Lote escolhido (faixa de totais), filtros recolhidos e ficha ampliada
            baseDados.Contrato = ["001/EX/2024"];
            Bombear();
            var filtroContrato = Assert.IsType<Spobras.App.Telas.FiltroMultiplo>(EncontrarNaTela(janela, "FiltroContratos"));
            var limpar = Assert.IsType<System.Windows.Controls.Button>(filtroContrato.FindName("LimparSelecao"));
            Assert.True(limpar.IsVisible);
            var caixa = Assert.IsType<System.Windows.Controls.Primitives.ToggleButton>(filtroContrato.FindName("Abrir"));
            caixa.IsChecked = true;
            Bombear();
            var popup = Assert.IsType<System.Windows.Controls.Primitives.Popup>(filtroContrato.FindName("FolhaOpcoes"));
            Assert.True(popup.IsOpen);
            caixa.IsChecked = false;
            Bombear();
            Assert.Single(baseDados.ResumosContratos);
            Assert.Equal(3, baseDados.ResumosContratos[0].Escolas.Count);
            Assert.Contains(baseDados.ContratosDetalhados, c => c.Titulo.Contains("Lote 01"));
            var exportacao = baseDados.ExportarContratosPdfCommand.ExecuteAsync(null);
            Esperar(() => exportacao.IsCompleted);
            exportacao.GetAwaiter().GetResult();
            Assert.False(baseDados.ResultadoComFalha, baseDados.Resultado);
            baseDados.Selecionada = baseDados.Obras.First();
            Fotografar(janela, saida, "58-base-resumo-contrato");
            baseDados.AlternarFiltrosCommand.Execute(null);
            Bombear();
            var rolagemResumo = Assert.IsType<System.Windows.Controls.ScrollViewer>(EncontrarNaTela(janela, "RolagemResumoContratos"));
            rolagemResumo.ScrollToEnd();
            Fotografar(janela, saida, "58b-base-escolas-do-contrato");
            baseDados.AlternarAmpliarCommand.Execute(null);
            Fotografar(janela, saida, "59-base-ficha-ampliada");
            baseDados.LimparFiltrosCommand.Execute(null);
            Bombear();
            Assert.False(baseDados.FichaAmpliada);
            var lista = Assert.IsAssignableFrom<FrameworkElement>(EncontrarNaTela(janela, "PainelLista"));
            Assert.True(lista.IsVisible);
            baseDados.AlternarFiltrosCommand.Execute(null);
            Fotografar(janela, saida, "59b-base-lista-restaurada");

            // Ficha com medicoes em tramite (contrato em andamento) e lista em duas linhas
            baseDados.Selecionada = baseDados.Obras.First(o => o.Escola == "CEU Delta");
            Assert.Equal(2, baseDados.MedicoesEmTramiteDaObra.Count);
            Fotografar(janela, saida, "60-base-medicoes-em-tramite");
            // Configuracoes da base (engrenagem): colunas, regras de status e diagnostico
            baseDados.AbrirConfiguracoesCommand.Execute(null);
            var config = Assert.IsType<ConfigBaseVm>(principal.PaginaAtual);
            Assert.False(config.FaltaAlgo);
            Fotografar(janela, saida, "61-config-base");
            config.Aba = 1;
            Fotografar(janela, saida, "62-config-status");
            config.Aba = 2;
            Fotografar(janela, saida, "63-config-diagnostico");
            config.VoltarCommand.Execute(null);
            principal.Navegar(FontesVm.Modulo);
            var telaFontes = (FontesVm)principal.PaginaAtual!;
            Assert.False(telaFontes.EditarVinculoCommand.CanExecute(telaFontes.Vinculos.First()));
            principal.Navegar(BaseVm.Modulo);
            baseDados.Selecionada = baseDados.Obras.First();
            baseDados.UsarNoRelatorioCommand.Execute(null);
            Assert.IsType<RelatorioVm>(principal.PaginaAtual);
            Fotografar(janela, saida, "40-relatorio-com-dados-da-base");

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

            // O mesmo problema em varias telas aparece uma vez so, com a lista das telas.
            var agrupados = Problemas
                .Select(p => (Tela: p[..p.IndexOf(": ", StringComparison.Ordinal)], Texto: p[(p.IndexOf(": ", StringComparison.Ordinal) + 2)..]))
                .GroupBy(p => p.Texto[..p.Texto.LastIndexOf(" em ", StringComparison.Ordinal)])
                .ToList();
            Relatorio.AppendLine().AppendLine($"Problemas de layout: {agrupados.Count}");
            foreach (var grupo in agrupados.Take(60))
            {
                Relatorio.AppendLine($"  {grupo.Key} [{string.Join(",", grupo.Select(g => g.Tela[..2]).Distinct())}] ex.: {grupo.First().Texto[(grupo.Key.Length + 4)..]}");
            }

            Relatorio.AppendLine($"Erros de ligação: {errosDeLigacao.Mensagens.Distinct().Count()}");
            foreach (string mensagem in errosDeLigacao.Mensagens.Distinct().Take(30))
            {
                Relatorio.AppendLine("  " + mensagem);
            }

            File.WriteAllText(Path.Combine(saida, "relatorio.txt"), Relatorio.ToString());
            File.WriteAllText(Path.Combine(saida, "resumo.txt"),
                $"Telas fotografadas: {Directory.GetFiles(saida, "*.png").Length}\n" + Relatorio.ToString()[Relatorio.ToString().LastIndexOf("Problemas de layout", StringComparison.Ordinal)..]);

            Assert.True(Problemas.Count == 0, "Elementos cortados ou fora da janela:\n" + string.Join("\n", Problemas.Take(30)));
            Assert.True(errosDeLigacao.Mensagens.Count == 0,
                "Erros de ligação de dados nas telas:\n" + string.Join("\n", errosDeLigacao.Mensagens.Distinct().Take(20)));
            Assert.True(Directory.GetFiles(saida, "*.png").Length >= 40);
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

    /// <summary>Imagem de teste (PNG) com uma faixa mais escura, para dar para ver a orientacao.</summary>
    private static string CriarFoto(string caminho, int largura, int altura, Color cor)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(caminho)!);
        var desenho = new DrawingVisual();
        using (var contexto = desenho.RenderOpen())
        {
            contexto.DrawRectangle(new SolidColorBrush(cor), null, new Rect(0, 0, largura, altura));
            contexto.DrawRectangle(Brushes.Black, null, new Rect(0, altura * 0.8, largura, altura * 0.2));
            contexto.DrawEllipse(Brushes.White, null, new Point(largura / 2.0, altura / 2.5), largura / 6.0, largura / 6.0);
        }

        var imagem = new RenderTargetBitmap(largura, altura, 96, 96, PixelFormats.Pbgra32);
        imagem.Render(desenho);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(imagem));
        using var arquivo = File.Create(caminho);
        png.Save(arquivo);
        return caminho;
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
        using (var arquivo = File.Create(Path.Combine(pasta, nome + ".png")))
        {
            png.Save(arquivo);
        }

        Conferir(conteudo, nome);
    }

    private static FrameworkElement? EncontrarNaTela(DependencyObject elemento, string nome)
    {
        if (elemento is FrameworkElement visual && visual.Name == nome) return visual;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(elemento); i++)
        {
            var encontrado = EncontrarNaTela(VisualTreeHelper.GetChild(elemento, i), nome);
            if (encontrado is not null) return encontrado;
        }
        return null;
    }

    private static readonly System.Text.StringBuilder Relatorio = new();
    private static readonly List<string> Problemas = [];

    /// <summary>
    /// Confere o layout sem depender de olho: nada cortado pelo espaco disponivel e nenhum botao ou
    /// texto fora da janela. Tambem escreve um esboco da tela (textos e posicoes) no relatorio.
    /// </summary>
    private static void Conferir(FrameworkElement raiz, string nome)
    {
        var limite = new Rect(0, 0, raiz.ActualWidth + 0.5, raiz.ActualHeight + 0.5);
        Relatorio.AppendLine().AppendLine($"== {nome} ({raiz.ActualWidth:0} x {raiz.ActualHeight:0})");
        int linhasDoEsboco = 0;

        void Andar(DependencyObject pai, bool rolavel)
        {
            int filhos = VisualTreeHelper.GetChildrenCount(pai);
            for (int i = 0; i < filhos; i++)
            {
                var filho = VisualTreeHelper.GetChild(pai, i);
                if (filho is not FrameworkElement fe || !fe.IsVisible)
                {
                    continue;
                }

                bool filhoRolavel = rolavel || (fe is System.Windows.Controls.ScrollViewer sv && (sv.ScrollableHeight > 0 || sv.ScrollableWidth > 0));
                bool interessa = fe is System.Windows.Controls.TextBlock or System.Windows.Controls.Primitives.ButtonBase or System.Windows.Controls.TextBox;
                if (interessa && fe.ActualWidth > 0 && fe.ActualHeight > 0)
                {
                    var area = fe.TransformToAncestor(raiz).TransformBounds(new Rect(0, 0, fe.ActualWidth, fe.ActualHeight));
                    string texto = fe switch
                    {
                        System.Windows.Controls.TextBlock tb => tb.Text.Length > 0 ? tb.Text : string.Concat(tb.Inlines.OfType<System.Windows.Documents.Run>().Select(r => r.Text)),
                        System.Windows.Controls.TextBox cx => "[campo] " + cx.Text,
                        System.Windows.Controls.ContentControl cc => "[botão] " + (cc.Content as string ?? ""),
                        _ => "",
                    };
                    texto = texto.Length > 70 ? texto[..70] + "…" : texto;

                    if (!rolavel)
                    {
                        if (System.Windows.Controls.Primitives.LayoutInformation.GetLayoutClip(fe) is not null)
                        {
                            Problemas.Add($"{nome}: cortado pelo espaço disponível: \"{texto}\" em {area.X:0},{area.Y:0} ({area.Width:0}x{area.Height:0})");
                        }
                        else if (!limite.Contains(area))
                        {
                            Problemas.Add($"{nome}: fora da janela: \"{texto}\" em {area.X:0},{area.Y:0} ({area.Width:0}x{area.Height:0})");
                        }
                    }

                    if (fe is System.Windows.Controls.TextBlock && texto.Length > 0 && linhasDoEsboco < 45)
                    {
                        linhasDoEsboco++;
                        Relatorio.AppendLine($"  {area.X,5:0},{area.Y,4:0} {area.Width,5:0}x{area.Height,-3:0} {texto}");
                    }
                }

                Andar(filho, filhoRolavel);
            }
        }

        Andar(raiz, false);
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
        public Queue<IReadOnlyList<string>> Arquivos { get; } = new();
        public IReadOnlyList<string> EscolherArquivos(string titulo, string? filtro = null, string? pastaInicial = null) => Arquivos.Count > 0 ? Arquivos.Dequeue() : [];
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
