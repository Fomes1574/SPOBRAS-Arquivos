using Spobras.App.ViewModels;
using Spobras.Core.Base;

namespace Spobras.App.Tests;

/// <summary>Configuracoes da base no computador (mexe nas regras de status, que sao globais: roda sozinha).</summary>
[Collection(nameof(ConfigBaseTests))]
[CollectionDefinition(nameof(ConfigBaseTests), DisableParallelization = true)]
public class ConfigBaseTests : ComApp
{
    private ConfigBaseVm Nova() => new(Base, Fontes, Preferencias, Dialogos, c => new Spobras.Documentos.Planilhas.LeitorXlsx(c));

    private static Spobras.Testes.PlanilhaFalsa ComColunaRenomeada()
    {
        var p = Spobras.Testes.BaseSintetica.Mestra();
        var t = p.TabelasPorNome[Spobras.Dados.Base.ImportadorMestra.TabelaObras];
        p.Tabela(t.Nome, [.. t.Colunas.Select(c => c == "INÍCIO EXECUÇÃO" ? "DATA INÍCIO OBRA" : c)], [.. t.Linhas]);
        return p;
    }

    [Fact]
    public async Task Coluna_renomeada_aparece_escolhe_a_nova_testa_salva_e_a_base_volta_a_ler()
    {
        await Fontes.DefinirAsync(Spobras.Core.Base.Fontes.Mestra, GravarMestra());
        GravarMestra(ComColunaRenomeada());
        await Fontes.RelerAsync(Spobras.Core.Base.Fontes.Mestra);
        Assert.NotEqual(EstadoFonte.Atualizada, Base.Fonte(Spobras.Core.Base.Fontes.Mestra)!.Estado); // leitura recusada, base anterior vale

        var vm = Nova();
        vm.AoEntrar();
        Assert.True(vm.FaltaAlgo);
        var dataOs = vm.Campos.Single(c => c.Rotulo == "Data da OS");
        Assert.True(dataOs.Falta);
        Assert.Contains("DATA INÍCIO OBRA", dataOs.Opcoes);
        Assert.False(vm.SalvarCommand.CanExecute(null));

        dataOs.Coluna = "DATA INÍCIO OBRA";
        Assert.True(vm.Mudou);
        Assert.False(vm.FaltaAlgo);
        Assert.False(vm.SalvarCommand.CanExecute(null)); // precisa testar antes
        vm.TestarCommand.Execute(null);
        Assert.True(vm.TesteOk);
        Assert.Contains("5 obras", vm.ResultadoTeste);
        Assert.Equal(3, vm.Amostra.Count);
        await vm.SalvarCommand.ExecuteAsync(null);

        Assert.False(Preferencias.Atual.Mapeamento.EPadrao);
        Assert.Equal(EstadoFonte.Atualizada, Base.Fonte(Spobras.Core.Base.Fontes.Mestra)!.Estado);
        Assert.Contains(Base.Buscar(new FiltroObras()).Obras, o => o.DataOs == new DateOnly(2026, 1, 10));
    }

    [Fact]
    public async Task Regra_de_status_vale_na_hora_e_voltar_ao_padrao_desfaz_tudo()
    {
        try
        {
            await Fontes.DefinirAsync(Spobras.Core.Base.Fontes.Mestra, GravarMestra());
            var vm = Nova();
            vm.AoEntrar();
            var vigente = vm.Status.First(s => s.Status == "VIGENTE");
            Assert.Equal(0, vigente.Situacao);
            vigente.Situacao = 2;
            Assert.Equal(SituacaoContrato.Ignorado, MedicoesEmTramite.Classificar("VIGENTE"));
            Assert.Equal(SituacaoContrato.Ignorado, Preferencias.Atual.RegrasStatus["VIGENTE"]);
            vigente.Situacao = 0; // voltar a escolha padrao tira a regra
            Assert.Empty(Preferencias.Atual.RegrasStatus);

            vigente.Situacao = 1;
            Dialogos.Confirmacoes.Enqueue(true);
            await vm.VoltarAoPadraoCommand.ExecuteAsync(null);
            Assert.Empty(Preferencias.Atual.RegrasStatus);
            Assert.Equal(SituacaoContrato.Andamento, MedicoesEmTramite.Classificar("VIGENTE"));
            Assert.NotEmpty(vm.Leituras);
        }
        finally
        {
            MedicoesEmTramite.DefinirRegras(null);
        }
    }
}
