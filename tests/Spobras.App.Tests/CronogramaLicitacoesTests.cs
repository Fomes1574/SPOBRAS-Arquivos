using Spobras.App.ViewModels;
using Spobras.Core.Base;
using Spobras.Core.Cronograma;
using Spobras.Dados.Configuracao;

namespace Spobras.App.Tests;

/// <summary>Cronograma com obras sem contrato: grupos por base + lote + modalidade, valor sem 10% em dobro, Desfazer e rascunho.</summary>
public class CronogramaLicitacoesTests : ComApp
{
    private async Task<CronogramaVm> ComBases()
    {
        await Fontes.DefinirAsync(Spobras.Core.Base.Fontes.Mestra, GravarMestra());
        await Fontes.DefinirAsync(Spobras.Core.Base.Fontes.Dotacoes, GravarDotacoes());
        var vm = NovoCronograma();
        vm.BuscarContratos = t => Spobras.Dados.Base.ProcuraObras.ContratosComDetalhes(Base, t);
        vm.ObrasDoContrato = c => Spobras.Dados.Base.ProcuraObras.DoContrato(Base, c);
        vm.BuscarPrevistas = t => GrupoPrevisto.Montar(Base.Previstas(BaseDeObras.EmLicitacao).Concat(Base.Previstas(BaseDeObras.ALicitar)), t);
        return vm;
    }

    [Fact]
    public async Task Busca_mostra_contratos_e_grupos_sem_contrato_cada_um_com_o_selo_da_sua_base()
    {
        var vm = await ComBases();
        Assert.False(vm.BuscandoContrato);
        vm.AlternarBuscaContratoCommand.Execute(null);
        Assert.True(vm.BuscandoContrato);

        vm.PesquisaContrato = "0";
        Assert.Empty(vm.ItensBusca);
        Assert.Equal("Continue digitando…", vm.AvisoBusca);

        vm.PesquisaContrato = "lote 01";
        Assert.Contains(vm.ItensBusca, i => i.Base == BaseDeObras.EmAndamento);
        var lic = Assert.Single(vm.ItensBusca, i => i.Base == BaseDeObras.EmLicitacao);
        Assert.Equal("LOTE 01 · Manutenção CEU", lic.Titulo);
        Assert.Equal("Em licitação", lic.Selo);
        Assert.True(vm.ItensBusca.IndexOf(lic) > vm.ItensBusca.IndexOf(vm.ItensBusca.First(i => i.Base == BaseDeObras.EmAndamento)));

        vm.PesquisaContrato = "nada parecido";
        Assert.False(vm.TemItensBusca);
        Assert.Equal("Nenhum contrato, lote ou modalidade com esse texto.", vm.AvisoBusca);
    }

    [Fact]
    public async Task Grupo_em_licitacao_entra_com_selo_valor_antes_dos_10_porcento_e_sem_datas()
    {
        var vm = await ComBases();
        vm.PesquisaContrato = "64";
        var grupo = Assert.Single(vm.ItensBusca, i => i.Base == BaseDeObras.EmLicitacao);
        vm.AdicionarItemBuscaCommand.Execute(grupo);
        Assert.False(vm.BuscandoContrato);
        Assert.Equal("", vm.PesquisaContrato);
        Assert.Equal("2 escolas adicionadas. Obra sem contrato: informe o início e o fim.", vm.Resultado);
        Assert.True(vm.PodeDesfazerInclusao);

        var alfa = vm.Linhas.Single(l => l.Nome == "EMEF Alfa");
        Assert.Equal(BaseDeObras.EmLicitacao, alfa.Base);
        Assert.Equal("Em licitação", alfa.Selo);
        Assert.Equal("64", alfa.Lote);
        Assert.Equal("Reforma e Manutenção", alfa.Escopo);
        Assert.Equal(2_000_000m, Normalizacao.LerReais(alfa.Valor)); // "Valor do Contrato com ADM": antes dos 10%
        Assert.Equal("", alfa.Inicio);
        Assert.Contains("Faltam o início e o fim", alfa.Avisos);

        // Com as datas, o total do cronograma é o valor da coluna "Recursos" da planilha: 10% uma vez só.
        foreach (var l in vm.Linhas)
        {
            l.Inicio = "jan/27";
            l.Fim = "jun/27";
        }

        var zeta = vm.Linhas.Single(l => l.Nome == "CEI Zeta");
        Assert.Contains("total R$ 2.200.000,00", alfa.Calculo.Replace(' ', ' '));
        Assert.Contains("total R$ 660.000,00", zeta.Calculo.Replace(' ', ' '));
        Assert.Equal("Nº 03 (2025)", zeta.Portaria); // a aba diz "3 (2025)"

        // Repetir não duplica; Desfazer tira só as que acabaram de entrar.
        vm.PesquisaContrato = "64";
        vm.AdicionarItemBuscaCommand.Execute(vm.ItensBusca.Single(i => i.Base == BaseDeObras.EmLicitacao));
        Assert.Equal("Nenhuma escola nova: 2 já estavam no cronograma.", vm.Resultado);
        Assert.Equal(2, vm.Linhas.Count);
        Assert.False(vm.PodeDesfazerInclusao);

        vm.NovaLinhaCommand.Execute(null);
        vm.PesquisaContrato = "skate";
        vm.AdicionarItemBuscaCommand.Execute(Assert.Single(vm.ItensBusca));
        Assert.Equal(4, vm.Linhas.Count);
        Assert.Equal(BaseDeObras.ALicitar, vm.Linhas[^1].Base); // EMEF Alfa de novo, mas da base A licitar: outra obra
        vm.DesfazerInclusaoCommand.Execute(null);
        Assert.Equal(3, vm.Linhas.Count);
        Assert.DoesNotContain(vm.Linhas, l => l.Base == BaseDeObras.ALicitar);
    }

    [Fact]
    public async Task Rascunho_guarda_a_base_e_rascunho_antigo_com_distribuicao_geral_continua_igual()
    {
        var vm = await ComBases();
        vm.PesquisaContrato = "teta";
        vm.AdicionarItemBuscaCommand.Execute(Assert.Single(vm.ItensBusca));
        var reaberto = NovoCronograma();
        Assert.Equal(BaseDeObras.ALicitar, Assert.Single(reaberto.Linhas).Base);
        Assert.Equal("A licitar", reaberto.Linhas[0].Selo);

        // Versão anterior: linha 0 = "como o cronograma" e a distribuição geral era curva S.
        new RascunhoRepositorio(Banco).Gravar("cronograma-atual", new RascunhoCronograma(
            [new LinhaRascunho("3", "12", "3362", "EMEF Antiga", "Reforma", "1.000,00", "out/26", "mar/27", OrigemInicio.Informado, null, 0),
             new LinhaRascunho("3", "12", "3362", "EMEF Linear", "Reforma", "1.000,00", "out/26", "mar/27", OrigemInicio.Informado, null, 1)],
            true, false, Distribuicao: 1), Relogio.Agora);
        var antigo = NovoCronograma();
        Assert.Equal(2, antigo.Linhas[0].Distribuicao); // curva S, como era
        Assert.Equal(1, antigo.Linhas[1].Distribuicao); // escolha da linha fica
        Assert.Equal(BaseDeObras.EmAndamento, antigo.Linhas[0].Base);
    }

    [Fact]
    public async Task Usar_no_cronograma_da_Base_leva_a_obra_sem_contrato_pela_composicao()
    {
        string trabalho = Em("Downloads");
        Directory.CreateDirectory(trabalho);
        using var app = new Servicos.Composicao(Em("dados2"), trabalho, Dialogos, new Servicos.InterfaceDireta(), "teste", eventosDasFontes: false) { VigiarFontes = false };
        await app.Fontes.DefinirAsync(Spobras.Core.Base.Fontes.Mestra, GravarMestra());
        app.Principal.Navegar(BaseVm.Modulo);
        var tela = (BaseVm)app.Principal.PaginaAtual!;
        tela.BaseAtual = BaseDeObras.ALicitar;
        tela.ALicitar.Selecionada = tela.ALicitar.Obras.Single(o => o.Nome == "EMEI Gama");
        Assert.True(tela.ALicitar.PodeUsarNoCronograma);
        tela.ALicitar.UsarNoCronogramaCommand.Execute(null);
        var cronograma = Assert.IsType<CronogramaVm>(app.Principal.PaginaAtual);
        var linha = Assert.Single(cronograma.Linhas);
        Assert.Equal(BaseDeObras.ALicitar, linha.Base);
        Assert.Equal("PREVISÃO DE MUROS – EMEF'S – PORTARIA 03", linha.Lote);
        Assert.Equal(800_000m, Normalizacao.LerReais(linha.Valor));
        Assert.Contains("já", RepetirEMensagem(tela, cronograma, app));
    }

    private static string RepetirEMensagem(BaseVm tela, CronogramaVm cronograma, Servicos.Composicao app)
    {
        app.Principal.Navegar(BaseVm.Modulo);
        tela.ALicitar.UsarNoCronogramaCommand.Execute(null);
        Assert.Single(cronograma.Linhas);
        return cronograma.Resultado;
    }
}
