using Spobras.Core.Base;

namespace Spobras.Core.Tests;

public class MedicoesEmTramiteTests
{
    private static ObraLida Obra(string status = "VIGENTE") => new()
    {
        Contrato = "044/EX/2026", Empreendimento = "EMEI Exemplo Alfa", StatusContrato = status,
        ValorEmpreendimento = 3_500_000m, ValorAditado = 296_523.69m,
    };

    private static MedicaoLida M(string numero, string status, decimal considerado, decimal? reajuste = null, string escola = "EMEI Exemplo Alfa", string? statusContrato = "VIGENTE") =>
        new("044/EX/2026", numero, escola, new DateOnly(2026, 1, 1).AddMonths(int.Parse(numero)), null, null, considerado + 1, considerado, null, status, null, null, reajuste, statusContrato);

    [Theory]
    [InlineData("VIGENTE", SituacaoContrato.Andamento)]
    [InlineData("Vencido", SituacaoContrato.Andamento)]
    [InlineData("SUSPENSO", SituacaoContrato.Andamento)]
    [InlineData("EM TRÂMITE - ADITIVO", SituacaoContrato.Andamento)]
    [InlineData("em tramite - aditivo", SituacaoContrato.Andamento)]
    [InlineData("NÃO INICIADO", SituacaoContrato.Andamento)]
    [InlineData("RESCISÃO EM TRÂMITE", SituacaoContrato.Andamento)]
    [InlineData("CONCLUÍDO", SituacaoContrato.Concluido)]
    [InlineData("ENCERRADO / TRD", SituacaoContrato.Concluido)]
    [InlineData("TRP ASSINADO", SituacaoContrato.Concluido)]
    [InlineData("TRD EM TRAMITAÇÃO", SituacaoContrato.Concluido)]
    [InlineData("EM ENCERRAMENTO", SituacaoContrato.Concluido)]
    [InlineData("RESCINDIDO", SituacaoContrato.Concluido)]
    [InlineData("SOB GESTÃO DA SIURB", SituacaoContrato.Ignorado)]
    [InlineData("STATUS NOVO QUALQUER", SituacaoContrato.Desconhecido)]
    [InlineData("", SituacaoContrato.Desconhecido)]
    public void Classifica_o_status_do_contrato_como_combinado(string status, SituacaoContrato esperado) =>
        Assert.Equal(esperado, MedicoesEmTramite.Classificar(status));

    [Fact]
    public void Em_tramite_e_tudo_que_nao_foi_pago_da_escola_com_considerado_mais_reajuste_da_mais_recente()
    {
        MedicaoLida[] medicoes =
        [
            M("1", "PAGAMENTO EFETUADO", 100m),
            M("2", "LIBERADO PARA FATURAMENTO", 200m),
            M("3", "CADASTRADO PARA FATURAMENTO", 300m),
            M("4", "DEVOLUÇÃO FISCAL", 400m, reajuste: 10m),
            M("5", "ANÁLISE GEC", 500m, reajuste: 25.5m),
            M("6", "LIBERADO PARA PAGAMENTO", 999m, escola: "CEI Outra Escola"), // outra escola do mesmo contrato
            M("7", "ANÁLISE FISCAL", 50m, statusContrato: "SOB GESTÃO DA SIURB"), // nao aparece
        ];

        var tramite = MedicoesEmTramite.DaObra(Obra(), medicoes);

        Assert.Equal(["5", "4", "3", "2"], tramite.Select(m => m.Numero));
        Assert.Equal(525.5m, tramite[0].Valor);
        Assert.Equal(410m, tramite[1].Valor);
        Assert.Equal(1435.5m, tramite.Sum(m => m.Valor));
    }

    [Fact]
    public void Contrato_concluido_ou_ignorado_nao_mostra_medicoes_e_desconhecido_vale_como_andamento()
    {
        MedicaoLida[] medicoes = [M("2", "ANÁLISE GEC", 200m)];
        Assert.Empty(MedicoesEmTramite.DaObra(Obra("EM ENCERRAMENTO"), medicoes));
        Assert.Empty(MedicoesEmTramite.DaObra(Obra("SOB GESTÃO DA SIURB"), medicoes));
        Assert.Single(MedicoesEmTramite.DaObra(Obra("STATUS NOVO"), medicoes));
        Assert.Single(MedicoesEmTramite.DaObra(Obra("RESCISÃO EM TRÂMITE"), medicoes));

        // Sem status na tabela de contratos: vale o status que vem nas medicoes da obra.
        Assert.Equal(SituacaoContrato.Concluido, MedicoesEmTramite.Situacao(Obra("") with { StatusContrato = null }, [M("1", "ANÁLISE GEC", 1m, statusContrato: "TRP ASSINADO")]));
    }

    [Fact]
    public void Percentual_do_aditado_sobre_o_valor_original()
    {
        Assert.Equal(8.5m, MedicoesEmTramite.PercentualAditado(Obra()));
        Assert.Null(MedicoesEmTramite.PercentualAditado(Obra() with { ValorEmpreendimento = 0m }));
    }
}
