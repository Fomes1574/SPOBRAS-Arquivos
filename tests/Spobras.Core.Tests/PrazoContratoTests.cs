using Spobras.Core.Base;

namespace Spobras.Core.Tests;

public class PrazoContratoTests
{
    private static readonly DateOnly Hoje = new(2026, 11, 16);

    [Theory]
    [InlineData(91, "#1E7B34", "Vence em")]
    [InlineData(90, "#8A4300", "Vence em")]
    [InlineData(1, "#8A4300", "Vence em 1 dia")]
    [InlineData(0, "#8A4300", "Vence hoje")]
    [InlineData(-1, "#A50D26", "Venceu há 1 dia")]
    public void Prazo_usa_limites_em_dias_e_meses_de_calendario(int dias, string cor, string texto)
    {
        var obra = new ObraLida { DataOs = Hoje.AddMonths(-2), Termino = Hoje.AddDays(dias), StatusContrato = "STATUS NOVO" };
        var selo = PrazoContrato.Prazo(obra, Hoje);
        Assert.Equal(cor, selo.Cor);
        Assert.StartsWith(texto, selo.Texto);
        Assert.Equal("Vence em 1 mês e 1 dia", PrazoContrato.Prazo(obra with { Termino = Hoje.AddMonths(1).AddDays(1) }, Hoje).Texto);
    }

    [Theory]
    [InlineData("ENCERRADO / TRD")]
    [InlineData("CONCLUÍDO")]
    [InlineData("TRP ASSINADO")]
    [InlineData("RESCINDIDO")]
    [InlineData("RESCISÃO EM TRÂMITE")]
    [InlineData("EM ENCERRAMENTO")]
    [InlineData("TRD EM TRAMITAÇÃO")]
    public void Encerramento_mostra_so_a_data(string status)
    {
        var obra = new ObraLida { DataOs = Hoje.AddMonths(-2), Termino = Hoje.AddDays(-1), StatusContrato = status };
        Assert.Equal(SeloContrato.Cinza("Encerrado em 15/11/2026"), PrazoContrato.Prazo(obra, Hoje));
    }

    [Theory]
    [InlineData("VIGENTE")]
    [InlineData("VENCIDO")]
    [InlineData("SUSPENSO")]
    [InlineData("EM TRÂMITE - ADITIVO")]
    public void Status_em_andamento_contam_prazo(string status)
    {
        var obra = new ObraLida { DataOs = Hoje.AddMonths(-2), Termino = Hoje.AddDays(1), StatusContrato = status };
        Assert.Equal("Vence em 1 dia", PrazoContrato.Prazo(obra, Hoje).Texto);
        Assert.Equal("OS não emitida", PrazoContrato.Prazo(obra with { DataOs = null }, Hoje).Texto);
    }

    [Fact]
    public void Percentual_usa_o_contratado_e_nao_deixa_saldo_residual_negativo()
    {
        var valores = new ValoresResumo(900, 100, 1000, 0, 0, 600, 450, 400, 2);
        Assert.Equal(60, valores.Percentual(valores.Pago));
        Assert.Equal(45, valores.Percentual(valores.Tramite));
        Assert.Equal(0, valores.SemMedicao);
        Assert.Equal(0, (valores with { Contratado = 0 }).Percentual(valores.Pago));
    }
}
