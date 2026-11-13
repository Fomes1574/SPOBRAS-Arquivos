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
    public async Task Relatorio_pela_escola_pede_o_contrato_quando_ha_varios_e_avisa_quando_nao_combinam()
    {
        using var app = await AppComBase();
        app.Principal.Navegar(RelatorioVm.Modulo);
        var relatorio = (RelatorioVm)app.Principal.PaginaAtual!;

        // Escola com um contrato so: completa tudo.
        relatorio.Unidade = "ceu delta";
        Assert.Equal(("CEU Delta", "004/EX/2025"), (relatorio.Unidade, relatorio.Contrato));

        // Escola em dois contratos: lista os contratos para escolher, sem chutar.
        relatorio.Contrato = "";
        relatorio.Unidade = "EMEF Alfa";
        Assert.True(relatorio.TemContratosParaEscolher);
        Assert.Equal(["001/EX/2024", "003/EX/2025"], relatorio.ContratosDaEscola.Select(o => o.Obra.Contrato).Order());
        Assert.Contains("está em 2 contratos", relatorio.Resultado);
        relatorio.EscolherObraCommand.Execute(relatorio.ContratosDaEscola.Single(o => o.Obra.Contrato == "003/EX/2025").Obra);
        Assert.Equal(("EMEF Alfa", "003/EX/2025", "LOTE 07"), (relatorio.Unidade, relatorio.Contrato, relatorio.Lote));
        Assert.False(relatorio.TemContratosParaEscolher);

        // Contrato com duas escolas e a escola ja digitada e uma delas: usa essa, sem perguntar.
        relatorio.Contrato = "001/EX/2024";
        Assert.False(relatorio.TemEscolasParaEscolher);
        Assert.Equal(("EMEF Alfa", "001/EX/2024"), (relatorio.Unidade, relatorio.Contrato));

        // Escola que existe, mas nao neste contrato: avisa e nao troca nada.
        relatorio.Unidade = "EMEI Gama";
        Assert.True(relatorio.ResultadoComFalha);
        Assert.Contains("O contrato 001/EX/2024 não tem a escola “EMEI Gama”", relatorio.Resultado);
        Assert.Equal("001/EX/2024", relatorio.Contrato);

        relatorio.Contrato = "004/EX/2025";
        Assert.Contains("A escola “EMEI Gama” não aparece no contrato 004/EX/2025", relatorio.Resultado);
        Assert.Equal("EMEI Gama", relatorio.Unidade);
    }

    [Fact]
    public async Task Relatorio_atualizar_dados_traz_de_novo_o_que_vem_da_base_e_diz_o_que_mudou()
    {
        using var app = await AppComBase();
        app.Principal.Navegar(RelatorioVm.Modulo);
        var relatorio = (RelatorioVm)app.Principal.PaginaAtual!;
        Assert.Empty(relatorio.DataTexto);
        relatorio.Contrato = "004/EX/2025";
        Assert.Equal("7910.2024/0000001-0", relatorio.ProcessoSei);

        relatorio.Lote = "";
        relatorio.ProcessoSei = "";
        relatorio.Portaria = "outra";
        relatorio.Responsavel = "Fiscal Fulano";
        relatorio.Descricao = "Minha descrição";
        relatorio.AtualizarDadosCommand.Execute(null);

        Assert.StartsWith("Atualizado pela Base de dados: ", relatorio.Resultado);
        Assert.Contains("Portaria", relatorio.Resultado);
        Assert.Contains("Processo SEI", relatorio.Resultado);
        Assert.Equal(("Fiscal Fulano", "Minha descrição", "7910.2024/0000001-0"),
            (relatorio.Responsavel, relatorio.Descricao, relatorio.ProcessoSei));
        relatorio.AtualizarDadosCommand.Execute(null);
        Assert.Equal("Os dados já estavam iguais aos da Base de dados.", relatorio.Resultado);

        // Contrato com duas escolas e sem escola escrita: pede a escola e nao troca nada.
        relatorio.Contrato = "001/EX/2024";
        relatorio.EscolherObraCommand.Execute(relatorio.EscolasDoContrato[0]);
        relatorio.Unidade = "";
        relatorio.AtualizarDadosCommand.Execute(null);
        Assert.True(relatorio.ResultadoComFalha);
        Assert.Contains("tem 2 escolas", relatorio.Resultado);
    }

    [Fact]
    public async Task Cronograma_busca_pelo_nome_mostra_os_contratos_e_sugere_escolas_do_mesmo_lote()
    {
        using var app = await AppComBase();
        app.Principal.Navegar(CronogramaVm.Modulo);
        var cronograma = (CronogramaVm)app.Principal.PaginaAtual!;
        cronograma.NovaLinhaCommand.Execute(null);
        var linha = cronograma.Linhas[0];

        // EMEF Alfa esta em dois contratos: os dois aparecem, com lote, portaria e dotacao.
        linha.Valor = "100000";
        linha.Nome = "emef alfa";
        Assert.True(linha.TemSugestoes);
        Assert.Equal(["001/EX/2024", "003/EX/2025"], linha.Sugestoes.Select(o => o.Obra.Contrato).Order());
        var escolhida = linha.Sugestoes.Single(o => o.Obra.Contrato == "003/EX/2025");
        Assert.Contains("LOTE 07", escolhida.Texto);

        cronograma.EscolherSugestaoCommand.Execute(escolhida);
        Assert.False(linha.TemSugestoes);
        Assert.Equal(("EMEF Alfa", "07", "Nº 03 (2025)", "100000"), (linha.Nome, linha.Lote, linha.Portaria, linha.Valor)); // o valor digitado fica

        // O lote 07 so tem a EMEF Alfa: nada a sugerir. No lote 01 estao as outras escolas.
        Assert.False(cronograma.TemSugestoesDoLote);
        linha.Lote = "01";
        Assert.True(cronograma.TemSugestoesDoLote);
        Assert.DoesNotContain(cronograma.SugestoesDoLote, o => o.Obra.Empreendimento == "EMEF Alfa"); // ja esta no cronograma
        var sugerida = cronograma.SugestoesDoLote[0];
        cronograma.AdicionarDoLoteCommand.Execute(sugerida);
        Assert.Equal(2, cronograma.Linhas.Count);
        Assert.Equal(sugerida.Obra.Empreendimento, cronograma.Linhas[1].Nome);
    }

    [Fact]
    public async Task Base_filtra_por_lote_mostra_os_totais_do_lote_e_a_ficha_em_blocos()
    {
        using var app = await AppComBase();
        app.Principal.Navegar(BaseVm.Modulo);
        var b = (BaseVm)app.Principal.PaginaAtual!;
        Assert.Equal(["LOTE 01", "LOTE 07"], b.Lotes);
        Assert.False(b.TemLote);

        b.Lote = ["LOTE 01"];
        Assert.True(b.TemLote);
        Assert.Equal(4, b.Obras.Count); // a EMEF Alfa do lote 07 fica de fora
        Assert.StartsWith("LOTE 01 · 4 obras", b.TituloDoLote);
        Assert.Equal(["Valor original", "Aditado", "Contratado c/ aditamento", "Pago", "Em trâmite (2)", "Saldo a pagar"], b.TotaisDoLote.Select(t => t.Rotulo));
        Assert.Equal("R$ 105,50", b.TotaisDoLote.Single(t => t.Rotulo.StartsWith("Em trâmite")).Valor.Replace('\u00A0', ' '));
        decimal pago = b.Obras.Sum(o => o.Obra.ValorPago ?? 0m);
        Assert.Equal(FormatoBase.Reais(pago), b.TotaisDoLote.Single(t => t.Rotulo == "Pago").Valor);
        Assert.Contains("LOTE 01", b.FiltrosAtivos);

        // Ficha em blocos; o Ampliar e os filtros recolhidos so mudam a tela.
        b.Selecionada = b.Obras[0];
        Assert.Equal(["Valores", "Prazo", "Identificação", "Orçamento", "Escopo"], b.Selecionada.Grupos.Select(g => g.Titulo));
        b.AlternarAmpliarCommand.Execute(null);
        b.AlternarFiltrosCommand.Execute(null);
        Assert.True(b.FichaAmpliada && b.FiltrosRecolhidos);

        Assert.Equal(0.6, b.ProporcaoDaFicha);
        b.GuardarProporcaoDaFicha(0.95);
        Assert.Equal(0.8, b.ProporcaoDaFicha); // limite para a lista nao sumir

        b.LimparCommand.Execute("lote");
        Assert.False(b.TemLote);
        Assert.Equal(5, b.Obras.Count);
    }

    [Fact]
    public async Task Base_volta_para_lista_ao_limpar_filtros_ou_perder_a_obra()
    {
        using var app = await AppComBase();
        app.Principal.Navegar(BaseVm.Modulo);
        var tela = (BaseVm)app.Principal.PaginaAtual!;
        tela.Lote = ["LOTE 01"];
        tela.Selecionada = tela.Obras[0];
        tela.AlternarAmpliarCommand.Execute(null);
        Assert.True(tela.FichaAmpliada);
        tela.LimparFiltrosCommand.Execute(null);
        Assert.False(tela.FichaAmpliada);

        tela.Selecionada = tela.Obras[0];
        tela.AlternarAmpliarCommand.Execute(null);
        tela.Texto = "obra inexistente";
        Assert.Null(tela.Selecionada);
        Assert.False(tela.FichaAmpliada);
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
        Assert.Equal(("EMEF Alfa", "R$ 1.500,50", "nov/26", "Nº 03"), (linha.Nome, linha.Valor.Replace('\u00A0', ' '), linha.Inicio, linha.Portaria)); // valor ja em reais
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
