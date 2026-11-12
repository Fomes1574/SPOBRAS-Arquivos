using Spobras.App.ViewModels;
using Spobras.Documentos.Planilhas;
using Spobras.Testes;

namespace Spobras.App.Tests;

public class FerramentasVmTests : ComApp
{
    [Fact]
    public async Task Busca_comeca_na_pasta_de_trabalho_pede_tres_letras_e_mostra_trecho()
    {
        Criar(Path.Combine("Downloads", "anotacoes.txt"), "Conversa com a fiscal do contrato 004/EX/2025 sobre a medição");
        Criar(Path.Combine("Downloads", "outro.txt"), "nada a ver");
        var vm = NovoFerramentas();
        Assert.Equal([Trabalho], vm.Pastas);

        vm.Termo = "04";
        Assert.Contains(vm.Avisos, a => a.Impede && a.Texto.Contains("3 letras"));
        Assert.False(vm.ExecutarCommand.CanExecute(null));

        vm.BuscarPor("004/EX/2025");
        Assert.Contains("Clique em Procurar", vm.Resultado);
        await vm.ExecutarCommand.ExecuteAsync(null);

        var achado = Assert.Single(vm.Achados);
        Assert.Equal("anotacoes.txt", achado.Nome);
        Assert.Contains("contrato 004/EX/2025", achado.Trecho);
        Assert.StartsWith("“004/EX/2025” em 1 arquivo (2 arquivos olhados)", vm.Resultado);
        vm.AbrirCommand.Execute(achado.Achado.Caminho);
        Assert.Equal([achado.Achado.Caminho], Dialogos.Abertos);
    }

    [Fact]
    public async Task Repetidos_separa_as_copias_com_desfazer_e_nao_apaga_nada()
    {
        string original = Criar(Path.Combine("Downloads", "relatorio.pdf"), "igual");
        File.SetLastWriteTimeUtc(original, new DateTime(2026, 1, 1));
        string copia = Criar(Path.Combine("Downloads", "Sub", "relatorio (1).pdf"), "igual");
        var vm = NovoFerramentas();
        vm.Ferramenta = FerramentaExtra.Duplicados;
        await vm.ExecutarCommand.ExecuteAsync(null);

        var grupo = Assert.Single(vm.Grupos);
        Assert.Equal(original, grupo.Original);
        Assert.Equal([copia], grupo.Copias);

        Dialogos.Confirmacoes.Enqueue(false);
        await vm.SepararCopiasCommand.ExecuteAsync(null);
        Assert.True(File.Exists(copia));

        Dialogos.Confirmacoes.Enqueue(true);
        await vm.SepararCopiasCommand.ExecuteAsync(null);
        string movida = Path.Combine(Trabalho, "Sub", FerramentasVm.PastaCopias, "relatorio (1).pdf");
        Assert.True(File.Exists(movida));
        Assert.True(File.Exists(original));
        Assert.Equal("1 cópia separada em “Cópias repetidas”.", vm.Resultado);
        Assert.True(vm.PodeDesfazer);

        // Procurar de novo nao mostra o que ja foi separado
        await vm.ExecutarCommand.ExecuteAsync(null);
        Assert.Empty(vm.Grupos);

        await vm.DesfazerCommand.ExecuteAsync(null);
        Assert.True(File.Exists(copia));
        Assert.False(File.Exists(movida));
    }

    [Fact]
    public async Task Juntar_mostra_previa_e_gera_planilha_nova()
    {
        string a = Em("Downloads", "jan.xlsx"), b = Em("Downloads", "fev.xlsx");
        XlsxSintetico.Gravar(new PlanilhaFalsa { AbasPorNome = { ["Jan"] = [["Escola", "Valor"], ["EMEF Alfa", 10m]] } }, a);
        XlsxSintetico.Gravar(new PlanilhaFalsa { AbasPorNome = { ["Fev"] = [["Escola", "Valor", "Obs"], ["CEI Beta", 5m, "ok"]] } }, b);
        var vm = NovoFerramentas();
        vm.Ferramenta = FerramentaExtra.Juntar;
        Assert.False(vm.ExecutarCommand.CanExecute(null));

        vm.Adicionar([a, b, Criar(Path.Combine("Downloads", "nota.txt"))]);
        Assert.Equal(["jan.xlsx · aba Jan · 1 linha · sem: Obs", "fev.xlsx · aba Fev · 1 linha"], vm.LinhasPrevia);
        Assert.Contains(vm.Avisos, x => x.Texto.Contains("Ficaram de fora") && x.Texto.Contains("nota.txt"));
        Assert.Contains(vm.Avisos, x => x.Conferir && x.Texto.Contains("Nem todos os arquivos têm as mesmas colunas"));
        Assert.Equal("Juntar 2 planilhas", vm.TextoBotao);

        await vm.ExecutarCommand.ExecuteAsync(null);

        Assert.False(vm.ResultadoComFalha, vm.Resultado);
        using var leitor = new LeitorXlsx(Em("Downloads", "Planilhas juntas.xlsx"));
        Assert.Equal(3, leitor.LerAba("Dados juntos").Count);
        Assert.Equal("Juntou 2 planilhas em “Planilhas juntas.xlsx”", Operacoes.Ultimos()[0].Descricao);
        Assert.Empty(vm.Arquivos);
    }

    [Fact]
    public async Task Excel_para_pdf_usa_o_excel_e_sem_excel_explica()
    {
        string a = Em("Downloads", "planilha.xlsx");
        XlsxSintetico.Gravar(new PlanilhaFalsa { AbasPorNome = { ["A"] = [["x"]] } }, a);
        var vm = NovoFerramentas();
        vm.Ferramenta = FerramentaExtra.ExcelPdf;
        vm.Adicionar([a]);
        await vm.ExecutarCommand.ExecuteAsync(null);
        Assert.Equal("1 PDF criado.", vm.Resultado);
        Assert.True(File.Exists(Em("Downloads", "planilha.pdf")));

        Exportador.Disponivel = false;
        var semExcel = NovoFerramentas();
        semExcel.Ferramenta = FerramentaExtra.ExcelPdf;
        semExcel.Adicionar([a]);
        Assert.Contains(semExcel.Avisos, x => x.Impede && x.Texto.Contains("Excel não foi encontrado"));
        Assert.False(semExcel.ExecutarCommand.CanExecute(null));
    }
}
