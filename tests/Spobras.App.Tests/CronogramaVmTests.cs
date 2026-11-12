using Spobras.App.Servicos;
using Spobras.App.ViewModels;
using Spobras.Core.Base;
using Spobras.Documentos.Planilhas;
using Spobras.Testes;

namespace Spobras.App.Tests;

public class CronogramaVmTests : ComApp
{
    private string Modelo()
    {
        string caminho = Em("Base", "Cronograma modelo.xlsx");
        XlsxSintetico.GravarModeloCronograma(caminho);
        return caminho;
    }

    private CronogramaVm ComModelo()
    {
        Modelos.DefinirModeloCronograma(Modelo());
        return NovoCronograma();
    }

    private static void Preencher(LinhaCronogramaVm l, string nome, string valor, string inicio = "01/11/2026", string fim = "28/02/2027", string dotacao = "3362")
    {
        l.Portaria = "3";
        l.Lote = "12";
        l.Dotacao = dotacao;
        l.Nome = nome;
        l.Valor = valor;
        l.Inicio = inicio;
        l.Fim = fim;
    }

    [Fact]
    public void Sem_modelo_explica_e_cadastrar_guarda_uma_copia_sem_alterar_o_original()
    {
        var vm = NovoCronograma();
        Assert.False(vm.TemModelo);
        Assert.Contains("Ainda não há modelo", vm.EstadoDoModelo);
        Assert.False(vm.GerarCommand.CanExecute(null));

        Dialogos.Arquivos.Enqueue([Criar(Path.Combine("Base", "nao-e-modelo.xlsx"), "x")]);
        vm.EscolherModeloCommand.Execute(null);
        Assert.False(vm.TemModelo);
        Assert.NotEmpty(Dialogos.AvisosMostrados);

        string original = Modelo();
        var bytes = File.ReadAllBytes(original);
        Dialogos.Arquivos.Enqueue([original]);
        vm.EscolherModeloCommand.Execute(null);

        Assert.True(vm.TemModelo);
        Assert.Equal(["Cronograma Um", "Cronograma Dois"], vm.Abas);
        Assert.Equal("Cronograma Dois", vm.Aba);
        Assert.Equal("Dotações do modelo: 2878, 3362", vm.TextoDotacoes);
        Assert.Equal(bytes, File.ReadAllBytes(original));
        Assert.StartsWith(Em("dados", "modelos"), Modelos.ModeloCronograma);
    }

    [Fact]
    public async Task T12_T13_linhas_conferidas_geracao_e_conferencia_pelo_excel()
    {
        var vm = ComModelo();
        vm.NovaLinhaCommand.Execute(null);
        vm.NovaLinhaCommand.Execute(null);
        Preencher(vm.Linhas[0], "EMEF Um", "100.000,00");
        Preencher(vm.Linhas[1], "CEI Dois", "abc", dotacao: "9999");

        Assert.True(vm.Linhas[1].Impedida);
        Assert.Contains("O valor \"abc\" não foi entendido", vm.Linhas[1].Avisos);
        Assert.Contains("A dotação 9999 não existe na aba Dotacoes do modelo", vm.Linhas[1].Avisos);
        Assert.False(vm.GerarCommand.CanExecute(null));

        vm.Linhas[1].Valor = "50.000,55";
        vm.Linhas[1].Dotacao = "2878";
        Assert.False(vm.Linhas[1].Impedida);
        Assert.Equal("2 obras · valor para obras R$ 150.000,55", vm.Resumo.Replace(' ', ' '));
        Assert.True(vm.GerarCommand.CanExecute(null));

        Verificador.Resposta = _ => new ConferenciaCronograma(150000.55m, 165000.61m, 165000.61m, "RESUMO OK", 0, null);
        vm.GerarPdf = true;
        await vm.GerarCommand.ExecuteAsync(null);

        string xlsx = Em("Downloads", "Cronograma - Portaria 3 - Lote 12.xlsx");
        Assert.True(File.Exists(xlsx));
        Assert.True(File.Exists(Path.ChangeExtension(xlsx, ".pdf")));
        Assert.False(vm.ResultadoComFalha);
        Assert.Contains("gerado e conferido no Excel (total R$ 165.000,61), com PDF.", vm.Resultado.Replace(' ', ' '));
        Assert.Equal((xlsx, "Cronograma Dois"), (Verificador.Chamadas[0].Caminho, Verificador.Chamadas[0].Aba));
        using (var leitor = new LeitorXlsx(xlsx))
        {
            var grade = leitor.LerAba("Cronograma Dois");
            Assert.Equal("EMEF Um", grade[2][4]);
            Assert.Equal(50000.55m, grade[3][6]);
        }

        Assert.StartsWith("Gerou “Cronograma - Portaria 3 - Lote 12.xlsx”", Operacoes.Ultimos()[0].Descricao);
    }

    [Fact]
    public async Task Conferencia_que_nao_bate_e_avisada_e_sem_excel_fica_claro_que_nao_foi_conferido()
    {
        var vm = ComModelo();
        vm.NovaLinhaCommand.Execute(null);
        Preencher(vm.Linhas[0], "EMEF Um", "1000");

        Verificador.Resposta = _ => new ConferenciaCronograma(1000m, 1100m, 825m, "VERIFICAR RESUMO", 2, null);
        await vm.GerarCommand.ExecuteAsync(null);
        Assert.True(vm.ResultadoComFalha);
        Assert.Contains("CONFIRA", vm.Resultado);
        Assert.Contains("a soma dos meses", vm.Resultado);
        Assert.Contains("2 células com erro", vm.Resultado);
        Assert.Contains("VERIFICAR RESUMO", vm.Resultado);

        Verificador.Disponivel = false;
        vm.GerarPdf = true;
        Assert.Contains(vm.Avisos, a => a.Texto.Contains("Será gerado só o arquivo Excel"));
        await vm.GerarCommand.ExecuteAsync(null);
        Assert.False(vm.ResultadoComFalha);
        Assert.Contains("O Excel não foi encontrado para conferir os totais", vm.Resultado);
        Assert.True(File.Exists(Em("Downloads", "Cronograma - EMEF Um (2).xlsx"))); // nao sobrescreveu o primeiro
    }

    [Fact]
    public void T32_inicio_vem_da_os_da_base_previsto_fica_identificado_e_divergencia_e_mostrada()
    {
        var vm = ComModelo();
        Base.GravarApelido(BaseSintetica.P3De2021, "Portaria 3");
        var comOs = new ObraLida { Contrato = "001/EX/2024", Empreendimento = "EMEF Alfa", Portaria = BaseSintetica.P3De2021, Lote = "LOTE 05", Atividade = "3362", DataOs = new DateOnly(2026, 11, 10), Escopo = "Reforma" };
        var semOs = new ObraLida { Contrato = "002/EX/2024", Empreendimento = "EMEI Gama", Portaria = "PORTARIA EXEMPLO Nº 06 (2022)", Lote = "LOTE 07" };

        vm.AdicionarDaBase(comOs);
        vm.AdicionarDaBase(semOs);

        var a = vm.Linhas[0];
        Assert.Equal(("3", "05", "3362", "EMEF Alfa", "10/11/2026", ""), (a.Portaria, a.Lote, a.Dotacao, a.Nome, a.Inicio, a.Valor)); // apelido do usuario; valor em branco
        Assert.Contains("Início = Data da OS da base", a.Avisos);
        Assert.Contains("vai para a planilha como 01/11/2026", a.Avisos); // ajuste visivel
        Assert.Equal("6", vm.Linhas[1].Portaria); // sem apelido: so o numero lido do texto
        Assert.Contains("sem Data da OS", vm.Resultado);

        vm.Linhas[1].Inicio = "02/2027";
        vm.Linhas[1].Fim = "05/2027";
        Assert.Contains("Início previsto", vm.Linhas[1].Avisos);

        // O usuario troca a data que veio da OS: a divergencia aparece e a base nao muda.
        a.Inicio = "20/12/2026";
        Assert.Contains("é diferente da Data da OS da base", a.Avisos);

        vm.AjustarInicio = false;
        Assert.Contains("o modelo não distribui valor em 12/2026", a.Avisos);
    }

    [Fact]
    public async Task T12_dezesseis_obras_geram_dois_arquivos_e_colagem_do_excel()
    {
        var vm = ComModelo();
        Dialogos.AreaDeTransferencia = string.Join("\r\n", Enumerable.Range(1, 16).Select(n => $"3\t12\t3362\tEscola {n}\tReforma\t1.000,00\t11/2026\t02/2027"));
        vm.ColarDoExcelCommand.Execute(null);

        Assert.Equal(16, vm.Linhas.Count);
        Assert.Equal("16 linhas coladas.", vm.Resultado);
        Assert.Equal("Gerar 2 cronogramas", vm.TextoBotao);
        Assert.Contains(vm.Avisos, a => a.Conferir && a.Texto.Contains("nenhuma obra fica de fora"));

        Verificador.Disponivel = false;
        await vm.GerarCommand.ExecuteAsync(null);

        Assert.Equal(["Cronograma - Portaria 3 - Lote 12 - parte 1 de 2.xlsx", "Cronograma - Portaria 3 - Lote 12 - parte 2 de 2.xlsx"], Nomes("Downloads"));
        using var leitor = new LeitorXlsx(Em("Downloads", "Cronograma - Portaria 3 - Lote 12 - parte 2 de 2.xlsx"));
        Assert.Equal("Escola 16", leitor.LerAba("Cronograma Dois")[2][4]);
        Assert.Equal("Gerou 2 arquivos de cronograma em Downloads", Operacoes.Ultimos()[0].Descricao);

        vm.NomeCronograma = "Lote 12: novembro";
        await vm.GerarCommand.ExecuteAsync(null);
        Assert.Contains("Lote 12- novembro - parte 1 de 2.xlsx", Nomes("Downloads"));
        Assert.Equal("Lote 12: novembro", NovoCronograma().NomeCronograma); // fica no rascunho
    }

    [Fact]
    public void Rascunho_do_cronograma_volta_ao_reabrir_e_limpar_pede_confirmacao()
    {
        var vm = ComModelo();
        vm.NovaLinhaCommand.Execute(null);
        Preencher(vm.Linhas[0], "EMEF Um", "1.000,00");
        vm.AjustarInicio = false;

        var reaberto = NovoCronograma();
        Assert.Equal(("EMEF Um", "1.000,00", "01/11/2026"), (reaberto.Linhas[0].Nome, reaberto.Linhas[0].Valor, reaberto.Linhas[0].Inicio));
        Assert.False(reaberto.AjustarInicio);

        Dialogos.Confirmacoes.Enqueue(false);
        reaberto.LimparCommand.Execute(null);
        Assert.Single(reaberto.Linhas);
        Dialogos.Confirmacoes.Enqueue(true);
        reaberto.LimparCommand.Execute(null);
        Assert.Empty(NovoCronograma().Linhas);
    }
}
