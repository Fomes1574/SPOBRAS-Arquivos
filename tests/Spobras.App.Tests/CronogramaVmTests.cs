using Spobras.App.ViewModels;
using Spobras.Core.Base;
using Spobras.Documentos.Cronograma;
using Spobras.Documentos.Planilhas;
using Spobras.Testes;

namespace Spobras.App.Tests;

public class CronogramaVmTests : ComApp
{
    private static void Preencher(LinhaCronogramaVm l, string nome, string valor, string inicio = "out/26", string fim = "jun/27", string dotacao = "3362")
    {
        l.Portaria = "3";
        l.Lote = "12";
        l.Dotacao = dotacao;
        l.Nome = nome;
        l.Valor = valor;
        l.Inicio = inicio;
        l.Fim = fim;
    }

    private async Task<CronogramaVm> ComBase()
    {
        await Fontes.DefinirAsync(Spobras.Core.Base.Fontes.Dotacoes, GravarDotacoes());
        return NovoCronograma();
    }

    [Fact]
    public async Task Linhas_conferidas_calculo_na_tela_e_geracao_sem_modelo()
    {
        var vm = await ComBase();
        Assert.False(vm.GerarCommand.CanExecute(null));
        vm.NovaLinhaCommand.Execute(null);
        vm.NovaLinhaCommand.Execute(null);
        Preencher(vm.Linhas[0], "EMEF Um", "1.000.000,00");
        Preencher(vm.Linhas[1], "CEI Dois", "abc", dotacao: "9999");

        Assert.True(vm.Linhas[1].Impedida);
        Assert.Contains("O valor \"abc\" não foi entendido", vm.Linhas[1].Avisos);
        Assert.Contains("A dotação 9999 não está na base de dotações", vm.Linhas[1].Avisos);
        Assert.False(vm.GerarCommand.CanExecute(null));

        vm.Linhas[1].Valor = "500.000,01";
        vm.Linhas[1].Dotacao = "2878";
        Assert.False(vm.Linhas[1].Impedida);
        Assert.True(vm.GerarCommand.CanExecute(null));

        // A tela mostra o que vai para a planilha: remuneracao, meses, anos e dotacoes.
        Assert.Contains("Remuneração R$ 100.000,00 · total R$ 1.100.000,00 · 9 meses · dotação 16.10.12.365.3025.3362.4490.5100.00", vm.Linhas[0].Calculo.Replace(' ', ' '));
        Assert.Equal("2 obras · total solicitado R$ 1.650.000,02", vm.Resumo.Replace(' ', ' '));
        Assert.Equal(9, vm.Meses.Count);
        Assert.Equal(["Total 2026", "Total 2027"], vm.Anos.Select(a => a.Rotulo));
        Assert.Contains(vm.Dotacoes, d => d.Rotulo == "16.10.12.365.3025.3362.4490.3900.00" && d.Detalhe == "Remuneração");
        Assert.Contains(vm.Dotacoes, d => d.Rotulo == "16.10.12.365.3010.2878.3390.3900.00"); // 2878: a mesma para obra e remuneracao

        await vm.GerarCommand.ExecuteAsync(null);

        string arquivo = Em("Downloads", "Cronograma - Portaria Nº 03 - Lote 12.xlsx");
        Assert.True(File.Exists(arquivo));
        Assert.False(vm.ResultadoComFalha);
        Assert.StartsWith("Pronto! “Cronograma - Portaria Nº 03 - Lote 12.xlsx” criado, total solicitado R$ 1.650.000,02", vm.Resultado.Replace(' ', ' '));
        Assert.True(vm.GerarCommand.CanExecute(null)); // pode gerar de novo quando quiser

        using var leitor = new LeitorXlsx(arquivo);
        var grade = leitor.LerAba(GeradorCronograma.NomeDaAba);
        Assert.Equal("EMEF Um", grade[5][3]);
        Assert.Equal(1_650_000.02m, Convert.ToDecimal(grade[7][7]));
        Assert.Equal("Gerou “Cronograma - Portaria Nº 03 - Lote 12.xlsx”", Operacoes.Ultimos()[0].Descricao);

        await vm.GerarCommand.ExecuteAsync(null);
        Assert.True(File.Exists(Em("Downloads", "Cronograma - Portaria Nº 03 - Lote 12 (2).xlsx"))); // nao sobrescreve
    }

    [Fact]
    public void Ajuste_por_percentual_em_meses_redistribui_o_resto_e_vale_para_uma_ou_todas_as_obras()
    {
        var vm = NovoCronograma();
        vm.NovaLinhaCommand.Execute(null);
        vm.NovaLinhaCommand.Execute(null);
        Preencher(vm.Linhas[0], "EMEF Um", "1.000.000,00", dotacao: "");
        Preencher(vm.Linhas[1], "CEI Dois", "2.000.000,00", dotacao: "");

        Assert.Equal(["Todas as obras", "1 - EMEF Um", "2 - CEI Dois"], vm.OpcoesDeObra);
        vm.NovoAjusteCommand.Execute(null);
        vm.Ajustes[0].Mes = "out/26";
        vm.Ajustes[0].Quantia = "5";
        vm.NovoAjusteCommand.Execute(null);
        vm.Ajustes[1].Mes = "nov/26";
        vm.Ajustes[1].Tipo = 1;
        vm.Ajustes[1].Quantia = "10.000,00";
        vm.Ajustes[1].Para = 2; // so a CEI Dois

        // out/26: 5% de 1.100.000 + 5% de 2.200.000; nov/26: CEI Dois fixa 10 mil e EMEF Um divide o resto.
        Assert.Equal("R$ 165.000,00", vm.Meses[0].Valor.Replace(' ', ' '));
        Assert.True(vm.Meses[0].Destaque);
        Assert.Equal("R$ 140.625,00", vm.Meses[1].Valor.Replace(' ', ' ')); // 1.045.000/8 + 10.000

        vm.Ajustes[2 - 1].Mes = "13/26";
        Assert.Equal("Escolha o mês na lista.", vm.Ajustes[1].Aviso);
        Assert.False(vm.GerarCommand.CanExecute(null));

        // Tirar a obra 1 leva o ajuste que era da obra 2 para a nova posicao.
        vm.Ajustes[1].Mes = "nov/26";
        vm.RemoverLinhaCommand.Execute(vm.Linhas[0]);
        Assert.Equal(1, vm.Ajustes[1].Para);

        // Volta igual ao reabrir.
        var reaberto = NovoCronograma();
        Assert.Equal(2, reaberto.Ajustes.Count);
        Assert.Equal(("nov/26", 1, "10.000,00", 1), (reaberto.Ajustes[1].Mes, reaberto.Ajustes[1].Tipo, reaberto.Ajustes[1].Quantia, reaberto.Ajustes[1].Para));
    }

    [Fact]
    public void Inicio_vem_da_os_da_base_previsto_fica_identificado_e_divergencia_e_mostrada()
    {
        var vm = NovoCronograma();
        var comOs = new ObraLida { Contrato = "001/EX/2024", Empreendimento = "EMEF Alfa", Portaria = BaseSintetica.P3De2021, Lote = "LOTE 05", Atividade = "3362", DataOs = new DateOnly(2026, 11, 10), Escopo = "Reforma" };
        var semOs = new ObraLida { Contrato = "002/EX/2024", Empreendimento = "EMEI Gama", Portaria = "PORTARIA EXEMPLO Nº 06 (2022)", Lote = "LOTE 07" };

        vm.AdicionarDaBase(comOs);
        vm.AdicionarDaBase(semOs);

        var a = vm.Linhas[0];
        Assert.Equal(("Nº 03 (2021)", "05", "3362", "EMEF Alfa", "nov/26", ""), (a.Portaria, a.Lote, a.Dotacao, a.Nome, a.Inicio, a.Valor));
        Assert.Equal("Nº 06 (2022)", vm.Linhas[1].Portaria);
        Assert.Contains("sem Data da OS", vm.Resultado);
        Assert.True(vm.ResultadoInformativo);

        vm.Linhas[1].Inicio = "02/2027";
        vm.Linhas[1].Fim = "mai/27";
        Assert.Contains("Início previsto", vm.Linhas[1].Avisos);

        a.Inicio = "20/11/2026"; // outro dia do mesmo mes nao muda nada
        Assert.DoesNotContain("diferente", a.Avisos);

        a.Inicio = "dez/26";
        Assert.Contains("O início (dez/26) é diferente do mês da Data da OS da base (nov/26)", a.Avisos);
    }

    [Fact]
    public async Task Remuneracao_padrao_10_outro_percentual_colagem_pdf_e_nome_escolhido()
    {
        var vm = NovoCronograma();
        Assert.Equal("10", vm.Remuneracao);
        Dialogos.AreaDeTransferencia = string.Join("\r\n", Enumerable.Range(1, 16).Select(n => $"3\t12\t\tEscola {n}\tReforma\t1.000,00\t11/2026\t02/2027"));
        vm.ColarDoExcelCommand.Execute(null);
        Assert.Equal(16, vm.Linhas.Count);
        Assert.Equal("16 linhas coladas.", vm.Resultado);

        vm.Remuneracao = "abc";
        Assert.Contains(vm.Avisos, x => x.Impede && x.Texto.StartsWith("Remuneração:"));
        Assert.False(vm.GerarCommand.CanExecute(null));

        vm.Remuneracao = "12,5";
        Assert.Contains(vm.Avisos, x => x.Texto.StartsWith("Remuneração de 12,5%"));
        Assert.Contains("Remuneração R$ 125,00", vm.Linhas[0].Calculo.Replace(' ', ' '));

        vm.NomeCronograma = "Lote 12: novembro";
        vm.GerarPdf = true;
        await vm.GerarCommand.ExecuteAsync(null);

        Assert.Equal(["Lote 12- novembro.pdf", "Lote 12- novembro.xlsx"], Nomes("Downloads")); // 16 obras num arquivo so
        Assert.EndsWith(", com PDF.", vm.Resultado);
        using (var leitor = new LeitorXlsx(Em("Downloads", "Lote 12- novembro.xlsx")))
        {
            var grade = leitor.LerAba(GeradorCronograma.NomeDaAba);
            Assert.Equal("Valores de Remuneração (12,5%)", grade[4][6]);
            Assert.Equal("Escola 16", grade[20][3]);
        }

        var reaberto = NovoCronograma();
        Assert.Equal(("12,5", "Lote 12: novembro", true), (reaberto.Remuneracao, reaberto.NomeCronograma, reaberto.GerarPdf));

        Exportador.Falhar = true;
        await vm.GerarCommand.ExecuteAsync(null);
        Assert.True(vm.ResultadoComFalha);
        Assert.Contains("O PDF não saiu: O Excel não respondeu.", vm.Resultado);
    }

    [Fact]
    public void Limpar_pede_confirmacao()
    {
        var vm = NovoCronograma();
        vm.NovaLinhaCommand.Execute(null);
        Preencher(vm.Linhas[0], "EMEF Um", "1.000,00");
        vm.NovoAjusteCommand.Execute(null);

        var reaberto = NovoCronograma();
        Dialogos.Confirmacoes.Enqueue(false);
        reaberto.LimparCommand.Execute(null);
        Assert.Single(reaberto.Linhas);
        Dialogos.Confirmacoes.Enqueue(true);
        reaberto.LimparCommand.Execute(null);
        Assert.Empty(NovoCronograma().Linhas);
        Assert.Empty(NovoCronograma().Ajustes);
    }

    [Fact]
    public void Meses_do_ajuste_vem_do_cronograma_e_curva_s_vale_para_todas_ou_para_uma_obra()
    {
        var vm = NovoCronograma();
        vm.NovaLinhaCommand.Execute(null);
        vm.NovaLinhaCommand.Execute(null);
        Preencher(vm.Linhas[0], "EMEF Um", "50000", "out/26", "mar/27", dotacao: "");
        Preencher(vm.Linhas[1], "CEI Dois", "R$ 50.000,00", "out/26", "mar/27", dotacao: "");
        Assert.Equal(["out/26", "nov/26", "dez/26", "jan/27", "fev/27", "mar/27"], vm.MesesDoCronograma);

        vm.Remuneracao = "0";
        Assert.Equal("R$ 16.666,68", vm.Meses[0].Valor.Replace('\u00A0', ' ')); // linear: 2 x 8.333,34

        vm.Distribuicao = 1; // curva S para todas
        Assert.Equal("R$ 9.866,68", vm.Meses[0].Valor.Replace('\u00A0', ' ')); // 2 x 4.933,34
        Assert.Equal("R$ 22.351,56", vm.Meses[2].Valor.Replace('\u00A0', ' ')); // pico no meio

        vm.Linhas[1].Distribuicao = 1; // so a CEI Dois volta a linear
        Assert.Equal("R$ 13.266,68", vm.Meses[0].Valor.Replace('\u00A0', ' '));

        // Ajuste por cima da curva, escolhendo o mes na lista.
        vm.NovoAjusteCommand.Execute(null);
        vm.Ajustes[0].Mes = vm.MesesDoCronograma[0];
        vm.Ajustes[0].Quantia = "20";
        vm.Ajustes[0].Para = 1;
        Assert.Equal("R$ 18.333,34", vm.Meses[0].Valor.Replace('\u00A0', ' ')); // 10.000 + 8.333,34

        var reaberto = NovoCronograma();
        Assert.Equal((1, 1), (reaberto.Distribuicao, reaberto.Linhas[1].Distribuicao));
    }
}
