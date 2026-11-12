using Spobras.App.Servicos;
using Spobras.App.ViewModels;
using Spobras.Core.Organizar;
using Spobras.Testes;

namespace Spobras.App.Tests;

/// <summary>O app montado como no Windows (sem janela): os modulos conversam entre si.</summary>
public class ComposicaoTests : IDisposable
{
    private readonly string _raiz = Directory.CreateTempSubdirectory("spobras-comp-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_raiz, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task Organizador_usa_a_base_lida_e_a_base_leva_a_busca()
    {
        string trabalho = Path.Combine(_raiz, "Downloads");
        Directory.CreateDirectory(trabalho);
        using var app = new Composicao(Path.Combine(_raiz, "dados"), trabalho, new DialogosFalsos(), new InterfaceDireta(), "teste", eventosDasFontes: false)
        {
            VigiarFontes = false,
        };
        string mestra = Path.Combine(_raiz, "Base", Spobras.Core.Base.Fontes.NomeArquivoMestra);
        Directory.CreateDirectory(Path.GetDirectoryName(mestra)!);
        XlsxSintetico.Gravar(BaseSintetica.Mestra(), mestra);
        await app.Fontes.DefinirAsync(Spobras.Core.Base.Fontes.Mestra, mestra);

        string arquivo = Path.Combine(trabalho, "Medição 004-EX-2025.xlsx");
        File.WriteAllText(arquivo, "x");
        var regra = new Regra { Nome = "Por obra", Destino = Path.Combine(_raiz, "Obras", "{contrato}", "{escola}") };
        var s = app.Novidades.Planejador.Planejar([new ArquivoParaOrganizar(arquivo)], [regra], []);
        Assert.Equal(Path.Combine(_raiz, "Obras", "004-EX-2025", "CEU Delta", "Medição 004-EX-2025.xlsx"), s[0].CaminhoDestino);

        app.Principal.Navegar(BaseVm.Modulo);
        var tela = (BaseVm)app.Principal.PaginaAtual!;
        tela.Selecionada = tela.Obras.First(o => o.Contrato == "004/EX/2025");
        tela.BuscarNosArquivosCommand.Execute("contrato");
        var busca = Assert.IsType<FerramentasVm>(app.Principal.PaginaAtual);
        Assert.Equal("004/EX/2025", busca.Termo);
        Assert.True(busca.EhBuscar);
    }
}

public class ComposicaoProtótipoTests : IDisposable
{
    private readonly string _raiz = Directory.CreateTempSubdirectory("spobras-prot-").FullName;
    private readonly DialogosFalsos _dialogos = new();

    public void Dispose()
    {
        try
        {
            Directory.Delete(_raiz, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private async Task<Composicao> AppComBase()
    {
        string trabalho = Path.Combine(_raiz, "Downloads");
        Directory.CreateDirectory(trabalho);
        var app = new Composicao(Path.Combine(_raiz, "dados"), trabalho, _dialogos, new InterfaceDireta(), "teste", eventosDasFontes: false) { VigiarFontes = false };
        Directory.CreateDirectory(Path.Combine(_raiz, "Base"));
        string mestra = Path.Combine(_raiz, "Base", Spobras.Core.Base.Fontes.NomeArquivoMestra);
        string dotacoes = Path.Combine(_raiz, "Base", Spobras.Core.Base.Fontes.NomeArquivoDotacoes);
        XlsxSintetico.Gravar(BaseSintetica.Mestra(), mestra);
        XlsxSintetico.Gravar(BaseSintetica.Dotacoes(), dotacoes);
        await app.Fontes.DefinirAsync(Spobras.Core.Base.Fontes.Mestra, mestra);
        await app.Fontes.DefinirAsync(Spobras.Core.Base.Fontes.Dotacoes, dotacoes);
        return app;
    }

    [Fact]
    public async Task Relatorio_completa_pelo_contrato_e_pede_a_escola_quando_ha_varias()
    {
        using var app = await AppComBase();
        app.Principal.Navegar(RelatorioVm.Modulo);
        var relatorio = (RelatorioVm)app.Principal.PaginaAtual!;

        relatorio.Contrato = "004-ex-2025"; // hifen e minusculas
        Assert.Equal(("CEU Delta", "004/EX/2025"), (relatorio.Unidade, relatorio.Contrato));
        Assert.Equal("15/06/2025", relatorio.DataOsTexto);

        relatorio.Contrato = "001/EX/2024"; // duas escolas
        Assert.True(relatorio.TemEscolasParaEscolher);
        Assert.Equal(["CEI Beta", "EMEF Alfa"], relatorio.EscolasDoContrato.Select(o => o.Empreendimento).Order());
        relatorio.EscolherObraCommand.Execute(relatorio.EscolasDoContrato.Single(o => o.Empreendimento == "CEI Beta"));
        Assert.Equal("CEI Beta", relatorio.Unidade);
        Assert.False(relatorio.TemEscolasParaEscolher);

        relatorio.Contrato = "999/NAO/2020"; // fora da base: nada muda
        Assert.Equal("CEI Beta", relatorio.Unidade);
    }

    [Fact]
    public async Task Inicio_mostra_a_planilha_mestra_e_leva_a_base()
    {
        using var app = await AppComBase();
        var inicio = (InicioVm)app.Principal.PaginaAtual!;
        inicio.AoEntrar();
        Assert.True(inicio.TemQuadroMestra);
        Assert.Equal(Spobras.Core.Base.Fontes.NomeArquivoMestra, inicio.NomeMestra);
        Assert.StartsWith("Lida pela última vez em", inicio.SituacaoMestra);
        inicio.VerBaseCommand.Execute(null);
        Assert.IsType<BaseVm>(app.Principal.PaginaAtual);
    }

    [Fact]
    public async Task Conferir_planilha_pela_base_propoe_e_gera_copia()
    {
        using var app = await AppComBase();
        app.Principal.Navegar(BaseVm.Modulo);
        ((BaseVm)app.Principal.PaginaAtual!).ConferirPlanilhaCommand.Execute(null);
        var ferramentas = Assert.IsType<FerramentasVm>(app.Principal.PaginaAtual);
        Assert.True(ferramentas.EhPadrao);

        string planilha = Path.Combine(_raiz, "Downloads", "controle.xlsx");
        XlsxSintetico.Gravar(new PlanilhaFalsa { AbasPorNome = { ["Obras"] = [["Portaria", "Escola"], ["6", "CEU Delta"], ["3", "EMEF Alfa"]] } }, planilha);
        ferramentas.Adicionar([planilha]);
        await ferramentas.ExecutarCommand.ExecuteAsync(null);

        Assert.Equal(2, ferramentas.AchadosPadrao.Count);
        Assert.Equal(BaseSintetica.P6, ferramentas.AchadosPadrao[0].Proposta);
        Assert.Equal("conferir", ferramentas.AchadosPadrao[1].Proposta); // portaria 3 de 2021 ou 2025
        ferramentas.GerarCopiaCorrigidaCommand.Execute(null);
        Assert.True(File.Exists(Path.Combine(_raiz, "Downloads", "controle (padronizada).xlsx")));
        Assert.Contains("A planilha original não mudou", ferramentas.Resultado);
    }

    [Fact]
    public async Task Cronograma_importa_obras_de_uma_planilha_pelos_titulos()
    {
        using var app = await AppComBase();
        app.Principal.Navegar(CronogramaVm.Modulo);
        var cronograma = (CronogramaVm)app.Principal.PaginaAtual!;
        string planilha = Path.Combine(_raiz, "Downloads", "obras.xlsx");
        XlsxSintetico.Gravar(new PlanilhaFalsa
        {
            AbasPorNome =
            {
                ["Lista"] =
                [
                    ["Lista de obras", null, null, null],
                    ["Escola", "Valor", "Início", "Fim", "Portaria"],
                    ["EMEF Alfa", 1500.5m, new DateTime(2026, 11, 1), new DateTime(2027, 2, 28), "3"],
                ],
            },
        }, planilha);
        _dialogos.Arquivos.Enqueue([planilha]);
        cronograma.ImportarPlanilhaCommand.Execute(null);

        var linha = Assert.Single(cronograma.Linhas);
        Assert.Equal(("EMEF Alfa", "1.500,50", "nov/26", "Nº 03"), (linha.Nome, linha.Valor, linha.Inicio, linha.Portaria));
        Assert.StartsWith("De “obras.xlsx”: 1 linha colada", cronograma.Resultado);
    }
}

public class CopiasDeSegurancaTests : ComApp
{
    [Fact]
    public void Configuracoes_faz_copia_e_marca_a_volta_para_a_proxima_abertura()
    {
        var config = new ConfiguracoesVm(Preferencias, Novidades, Dialogos, Disco, Relogio, "0.1.0", Em("dados"), Modelos, Banco);
        Assert.True(config.TemBackups);
        Assert.StartsWith("Nenhuma cópia ainda", config.EstadoBackups);

        config.FazerBackupCommand.Execute(null);
        Assert.StartsWith("Cópia de segurança feita", config.Resultado);
        Assert.StartsWith("1 cópia guardada", config.EstadoBackups);

        Dialogos.Arquivos.Enqueue([Banco.Backups()[0].FullName]);
        Dialogos.Confirmacoes.Enqueue(true);
        config.RestaurarBackupCommand.Execute(null);
        Assert.True(config.RestauracaoPendente);
        Assert.Contains("abra de novo", config.Resultado);

        config.CancelarRestauracaoCommand.Execute(null);
        Assert.False(config.RestauracaoPendente);
        Assert.False(Banco.RestauracaoPendente);
    }
}
