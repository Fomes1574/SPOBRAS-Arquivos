using Spobras.App.ViewModels;
using Spobras.Core.Base;
using Spobras.Documentos.Planilhas;
using Spobras.Testes;

namespace Spobras.App.Tests;

public class ModelosVmTests : ComApp
{
    private const string Aba = "Cronograma Dois";

    private string Planilha(string nome = "Ficha modelo.xlsx")
    {
        string caminho = Em("Base", nome);
        XlsxSintetico.GravarModeloCronograma(caminho);
        return caminho;
    }

    private static void Campo(CampoEdicaoVm c, string nome, string celula, string tipo = "Texto", string origem = "Digitado por mim", bool obrigatorio = false)
    {
        c.Nome = nome;
        c.Aba = Aba;
        c.Celula = celula;
        c.Tipo = tipo;
        c.Origem = origem;
        c.Obrigatorio = obrigatorio;
    }

    /// <summary>Cadastra "Ficha" com Escola (E3, da base), Valor (G3) e Início (J3, Data da OS).</summary>
    private ModelosVm ComModelo()
    {
        var vm = NovoModelos();
        Dialogos.Arquivos.Enqueue([Planilha()]);
        vm.NovoModeloCommand.Execute(null);
        vm.EdNome = "Ficha";
        vm.EdPadraoNome = "Ficha - {Escola}";
        Campo(vm.EdCampos[0], "Escola", "e3", origem: "Escola / obra", obrigatorio: true);
        vm.AdicionarCampoCommand.Execute(null);
        Campo(vm.EdCampos[1], "Valor", "G3", "Número");
        vm.AdicionarCampoCommand.Execute(null);
        Campo(vm.EdCampos[2], "Início", "J3", "Data", "Data da OS");
        Assert.Equal("", vm.EdProblemas);
        vm.SalvarModeloCommand.Execute(null);
        Assert.False(vm.Editando);
        return vm;
    }

    [Fact]
    public void Sem_modelos_a_tela_explica_e_cadastro_com_problema_nao_salva()
    {
        var vm = NovoModelos();
        Assert.True(vm.SemNada);
        Assert.False(vm.Usando);

        string original = Planilha();
        var bytes = File.ReadAllBytes(original);
        Dialogos.Arquivos.Enqueue([original]);
        vm.NovoModeloCommand.Execute(null);

        Assert.True(vm.Editando);
        Assert.False(vm.SemNada);
        Assert.Equal("Ficha modelo", vm.EdNome);
        Assert.Contains(Aba, vm.EdAbas);
        Assert.Equal(vm.EdAbas[0], vm.EdCampos[0].Aba);

        // Celula com formula e celula invalida bloqueiam o salvar
        Campo(vm.EdCampos[0], "Total", "G18", "Número");
        Assert.Contains("tem fórmula", vm.EdProblemas);
        Assert.False(vm.SalvarModeloCommand.CanExecute(null));
        vm.EdCampos[0].Celula = "linha 3";
        Assert.Contains("não é uma célula", vm.EdProblemas);

        vm.CancelarEdicaoCommand.Execute(null);
        Assert.Empty(ModelosExcel.Listar());
        Assert.Equal(bytes, File.ReadAllBytes(original));
    }

    [Fact]
    public async Task Cadastra_preenche_varios_arquivos_e_registra_no_historico()
    {
        var vm = ComModelo();
        Assert.True(vm.Usando);
        Assert.Equal("Ficha", vm.Selecionado!.Nome);
        Assert.Equal(["Escola", "Valor", "Início"], vm.Colunas);
        Assert.StartsWith("Modelo “Ficha” salvo.", vm.Resultado);
        Assert.False(vm.GerarCommand.CanExecute(null)); // sem linhas

        vm.NovaLinhaCommand.Execute(null);
        vm.NovaLinhaCommand.Execute(null);
        vm.Linhas[0].Celulas[0].Valor = "EMEF Alfa";
        vm.Linhas[0].Celulas[1].Valor = "1.250,50";
        vm.Linhas[0].Celulas[2].Valor = "10/11/2026";
        vm.Linhas[1].Celulas[1].Valor = "abc";

        Assert.True(vm.Linhas[1].Impedida);
        Assert.Contains("Falta “Escola”", vm.Linhas[1].Avisos);
        Assert.Contains("o número \"abc\" não foi entendido", vm.Linhas[1].Avisos);
        Assert.False(vm.GerarCommand.CanExecute(null));

        vm.Linhas[1].Celulas[0].Valor = "CEI Beta";
        vm.Linhas[1].Celulas[1].Valor = "";
        Assert.True(vm.GerarCommand.CanExecute(null));
        Assert.Equal("Gerar 2 arquivos", vm.TextoBotao);

        vm.GerarPdf = true;
        await vm.GerarCommand.ExecuteAsync(null);

        Assert.False(vm.ResultadoComFalha, vm.Resultado);
        Assert.Equal("2 arquivos gerados, com PDF.", vm.Resultado);
        Assert.Equal(["Ficha - CEI Beta.pdf", "Ficha - CEI Beta.xlsx", "Ficha - EMEF Alfa.pdf", "Ficha - EMEF Alfa.xlsx"], Nomes("Downloads"));
        using (var leitor = new LeitorXlsx(Em("Downloads", "Ficha - EMEF Alfa.xlsx")))
        {
            var grade = leitor.LerAba(Aba);
            Assert.Equal("EMEF Alfa", grade[2][4]);
            Assert.Equal(1250.5m, grade[2][6]);
        }

        Assert.Equal("Gerou 2 arquivos pelo modelo Ficha em Downloads (com PDF)", Operacoes.Ultimos()[0].Descricao);

        // Gerar de novo nao sobrescreve
        vm.GerarPdf = false;
        await vm.GerarCommand.ExecuteAsync(null);
        Assert.Contains("Ficha - EMEF Alfa (2).xlsx", Nomes("Downloads"));
    }

    [Fact]
    public void Obra_da_base_preenche_os_campos_ligados_e_o_rascunho_volta()
    {
        var vm = ComModelo();
        var obra = new ObraLida { Contrato = "001/EX/2024", Empreendimento = "EMEF Alfa", DataOs = new DateOnly(2026, 11, 10) };
        vm.AdicionarDaBase(obra);

        var linha = Assert.Single(vm.Linhas);
        Assert.Equal(["EMEF Alfa", "", "10/11/2026"], linha.Celulas.Select(c => c.Valor));
        Assert.Contains("Confira os campos", vm.Resultado);

        var reaberto = NovoModelos();
        Assert.Equal(["EMEF Alfa", "", "10/11/2026"], Assert.Single(reaberto.Linhas).Celulas.Select(c => c.Valor));
    }

    [Fact]
    public void Colar_do_excel_ignora_titulos_e_avisa_colunas_a_mais()
    {
        var vm = ComModelo();
        Dialogos.AreaDeTransferencia = "Escola\tValor\tInício\r\nEMEF Um\t100\t01/12/2026\r\n\r\nEMEF Dois\t200\t02/12/2026\textra\r\n";
        vm.ColarDoExcelCommand.Execute(null);

        Assert.Equal(["EMEF Um", "EMEF Dois"], vm.Linhas.Select(l => l.Celulas[0].Valor));
        Assert.Equal("2 linhas coladas. Em 1 linha havia mais colunas do que campos; o que sobrou foi ignorado.", vm.Resultado);
    }

    [Fact]
    public void Modelo_que_mudou_pede_revisao_e_remover_nao_apaga_o_original()
    {
        var vm = ComModelo();
        var modelo = vm.Selecionado!.Modelo;

        // A copia interna troca de estrutura (simula outra versao colocada por fora)
        XlsxSintetico.Gravar(BaseSintetica.Dotacoes(), ModelosExcel.Caminho(modelo));
        var reaberto = NovoModelos();
        reaberto.NovaLinhaCommand.Execute(null);
        reaberto.Linhas[0].Celulas[0].Valor = "EMEF Alfa";
        Assert.Contains(reaberto.Avisos, a => a.Impede && a.Texto.Contains("mudou desde o cadastro"));
        Assert.False(reaberto.GerarCommand.CanExecute(null));

        // Editar e trocar o arquivo pela versao certa resolve
        reaberto.EditarModeloCommand.Execute(null);
        Dialogos.Arquivos.Enqueue([Planilha("Ficha v2.xlsx")]);
        reaberto.TrocarArquivoCommand.Execute(null);
        Assert.Equal("", reaberto.EdProblemas);
        reaberto.SalvarModeloCommand.Execute(null);
        Assert.Empty(reaberto.Avisos);
        Assert.True(reaberto.GerarCommand.CanExecute(null));

        Dialogos.Confirmacoes.Enqueue(true);
        reaberto.RemoverModeloCommand.Execute(null);
        Assert.Empty(ModelosExcel.Listar());
        Assert.True(reaberto.SemNada);
        Assert.True(File.Exists(Em("Base", "Ficha modelo.xlsx")));
        Assert.True(File.Exists(Em("Base", "Ficha v2.xlsx")));
        Assert.Empty(Directory.GetFiles(Em("dados", "modelos", "excel")));
    }

    [Fact]
    public async Task Outro_arquivo_com_a_mesma_estrutura_e_atualizado_numa_copia()
    {
        var vm = ComModelo();
        string existente = Planilha("Ficha já preenchida.xlsx");
        var bytes = File.ReadAllBytes(existente);

        Dialogos.Arquivos.Enqueue([Criar(Path.Combine("Base", "texto.xlsx"), "não é planilha")]);
        vm.EscolherOutroArquivoCommand.Execute(null);
        Assert.False(vm.UsaOutroArquivo);
        Assert.NotEmpty(Dialogos.AvisosMostrados);

        Dialogos.Arquivos.Enqueue([existente]);
        vm.EscolherOutroArquivoCommand.Execute(null);
        Assert.True(vm.UsaOutroArquivo);
        Assert.Contains("Ficha já preenchida.xlsx", vm.TextoArquivoBase);

        vm.NovaLinhaCommand.Execute(null);
        vm.Linhas[0].Celulas[0].Valor = "EMEF Alfa";
        await vm.GerarCommand.ExecuteAsync(null);

        Assert.Equal(bytes, File.ReadAllBytes(existente));
        Assert.True(File.Exists(Em("Downloads", "Ficha - EMEF Alfa.xlsx")));
        vm.VoltarAoModeloCommand.Execute(null);
        Assert.False(vm.UsaOutroArquivo);
    }
}
