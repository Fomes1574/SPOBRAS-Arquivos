using Spobras.Core.Base;
using Spobras.Core.Cronograma;

namespace Spobras.Core.Tests;

public class CalculoCronogramaTests
{
    private static readonly DateOnly Out26 = new(2026, 10, 1), Nov26 = new(2026, 11, 1), Dez26 = new(2026, 12, 1), Jun27 = new(2027, 6, 1);

    private static ObraCronograma Obra(string nome, decimal valor, DateOnly inicio, DateOnly fim, string dotacao = "") =>
        new() { Nome = nome, Valor = valor, Inicio = inicio, Fim = fim, Dotacao = dotacao, Portaria = "Nº 06 (2022)", Lote = "900" };

    [Fact]
    public void Remuneracao_e_parcelas_arredondam_para_cima_e_a_sobra_fica_no_ultimo_mes()
    {
        var c = CalculoCronograma.Calcular([Obra("EMEF Alfa", 1000.01m, Out26, Dez26)], 0.10m, []);
        var o = Assert.Single(c.Obras);

        Assert.Equal(100.01m, o.Remuneracao); // 100,001 vira 100,01
        Assert.Equal(1100.02m, o.Total);
        Assert.Equal(366.68m, o.DoMes(Out26)); // 366,6733 para cima
        Assert.Equal(366.68m, o.DoMes(Nov26));
        Assert.Equal(366.66m, o.DoMes(Dez26)); // o que sobra
        Assert.Equal(o.Total, o.Meses.Values.Sum());
    }

    [Fact]
    public void Percentuais_nos_primeiros_meses_e_o_resto_igual_como_no_cronograma_de_referencia()
    {
        AjusteMensal[] ajustes =
        [
            new(Out26, TipoAjuste.Percentual, 5),
            new(Nov26, TipoAjuste.Percentual, 7),
            new(Dez26, TipoAjuste.Percentual, 8),
        ];
        var c = CalculoCronograma.Calcular(
            [Obra("CEI Beta", 1_000_000m, Out26, Jun27), Obra("EMEI Gama", 2_000_000m, Out26, Jun27)], 0.10m, ajustes);

        var beta = c.Obras[0];
        Assert.Equal(1_100_000m, beta.Total);
        Assert.Equal(55_000m, beta.DoMes(Out26));
        Assert.Equal(77_000m, beta.DoMes(Nov26));
        Assert.Equal(88_000m, beta.DoMes(Dez26));
        // 80% em 6 meses: 146.666,666... para cima nos 5 primeiros, sobra no ultimo.
        Assert.Equal(146_666.67m, beta.DoMes(new DateOnly(2027, 1, 1)));
        Assert.Equal(146_666.65m, beta.DoMes(Jun27));
        Assert.Equal(beta.Total, beta.Meses.Values.Sum());

        Assert.Equal([2026, 2027], c.Anos);
        Assert.Equal(220_000m + 440_000m, c.DoAno(2026));
        Assert.Equal(c.Total, c.DoAno(2026) + c.DoAno(2027));
        Assert.Equal(9, c.Meses.Count);
        Assert.True(c.PodeGerar);
    }

    [Fact]
    public void Valor_num_mes_para_uma_obra_e_para_todas_reparte_o_resto()
    {
        var obras = new[] { Obra("A", 1000m, Out26, Dez26), Obra("B", 3000m, Nov26, Dez26) };
        var c = CalculoCronograma.Calcular(obras, 0m,
            [new(Out26, TipoAjuste.Valor, 100m, Obra: 0), new(Dez26, TipoAjuste.Valor, 400m)]);

        // Dezembro: R$ 400 para todas, na proporcao dos totais (1000 e 3000).
        Assert.Equal(100m, c.Obras[0].DoMes(Out26));
        Assert.Equal(100m, c.Obras[0].DoMes(Dez26));
        Assert.Equal(800m, c.Obras[0].DoMes(Nov26));
        Assert.Equal(300m, c.Obras[1].DoMes(Dez26));
        Assert.Equal(2700m, c.Obras[1].DoMes(Nov26));
        Assert.Equal(400m, c.DoMes(Dez26));
    }

    [Fact]
    public void Ajustes_maiores_que_o_total_e_linhas_incompletas_impedem_gerar()
    {
        var c = CalculoCronograma.Calcular(
            [Obra("A", 1000m, Out26, Nov26), new ObraCronograma { Nome = "Sem valor", Inicio = Out26 }],
            0.10m, [new(Out26, TipoAjuste.Percentual, 80), new(Nov26, TipoAjuste.Percentual, 30)]);

        Assert.False(c.PodeGerar);
        Assert.Contains(c.AvisosDasObras, a => a.Linha == 1 && a.Aviso.Mensagem.Contains("mais que o total"));
        Assert.Contains(c.AvisosDasObras, a => a.Linha == 2 && a.Aviso.Mensagem.Contains("Falta o valor"));
        Assert.Contains(c.AvisosDasObras, a => a.Linha == 2 && a.Aviso.Mensagem.Contains("Falta o fim"));
    }

    [Fact]
    public void Tabela_por_dotacao_soma_as_iguais_e_separa_obra_de_remuneracao()
    {
        VinculoBase V(string completa, string portaria = "Portaria 6 (2022)") =>
            new(1, portaria, "001/SME/2022", null, completa, null, CalculoCronograma.Resumida(completa), null);
        VinculoBase[] vinculos =
        [
            V("16.10.12.365.3010.3360.4490.5100.00"),
            V("16.10.12.365.3025.3362.4490.5100.00"),
            V("16.10.12.361.3010.2830.4490.3900.00"),
            V("16.10.12.365.3010.3360.4490.5100.00", "Portaria 3 (2021)"),
        ];
        var obras = new[]
        {
            Obra("A", 1000m, Out26, Nov26, "3360"),
            Obra("B", 2000m, Out26, Nov26, "3362"),
            Obra("C", 500m, Out26, Nov26, "3360"),
            Obra("D", 300m, Out26, Nov26, "2830"),
            Obra("E", 100m, Out26, Nov26, "9999"),
        };

        var c = CalculoCronograma.Calcular(obras, 0.10m, [], o => CalculoCronograma.Resolver(o, vinculos));

        var l3360 = c.Dotacoes.Where(d => d.Dotacao.Contains(".3360.")).ToList();
        Assert.Equal(new[] { "16.10.12.365.3010.3360.4490.5100.00", "16.10.12.365.3010.3360.4490.3900.00" }, l3360.Select(d => d.Dotacao));
        Assert.Equal(1500m, l3360[0].Valor);
        Assert.Equal(150m, l3360[1].Valor);
        Assert.Equal([0, 2], l3360[0].Obras);

        // 2830 termina em 3900 e serve a obra e a remuneracao.
        var l2830 = c.Dotacoes.Where(d => d.Dotacao.Contains(".2830.")).ToList();
        Assert.All(l2830, d => Assert.Equal("16.10.12.361.3010.2830.4490.3900.00", d.Dotacao));
        Assert.Equal([CalculoCronograma.TipoObra, CalculoCronograma.TipoRemuneracao], l2830.Select(d => d.Tipo));

        Assert.Contains(c.AvisosDasObras, a => a.Linha == 5 && a.Aviso.Mensagem.Contains("9999"));
        Assert.Contains(c.Avisos, a => a.Mensagem.Contains("E"));
        Assert.Equal(c.TotalObra - 100m, c.Dotacoes.Where(d => !d.DaRemuneracao).Sum(d => d.Valor));
    }

    [Fact]
    public void Dotacao_completa_digitada_na_linha_vale_direto_e_resumida_vem_dela()
    {
        var (d, aviso) = CalculoCronograma.Resolver(Obra("A", 1, Out26, Out26, "16.10.12.365.3010.3366.4490.5100 00"), []);

        Assert.Null(aviso);
        Assert.Equal("16.10.12.365.3010.3366.4490.5100.00", d!.Obra);
        Assert.Equal("16.10.12.365.3010.3366.4490.3900.00", d.Remuneracao);
        Assert.Equal("3366", CalculoCronograma.Resumida(d.Obra));
    }
}
