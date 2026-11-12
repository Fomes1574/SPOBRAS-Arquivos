using Spobras.Core.Base;
using Spobras.Documentos.Ferramentas;
using Spobras.Documentos.Planilhas;
using Spobras.Testes;

namespace Spobras.Documentos.Tests;

public class ConferenciaPadraoTests : IDisposable
{
    private const string P3_2021 = "PORTARIA EXEMPLO SME/SIURB Nº 03 (2021)";
    private const string P3_2025 = "PORTARIA EXEMPLO SME/SIURB Nº 03 (2025)";
    private const string P6 = "PORTARIA EXEMPLO SME/SIURB Nº 06 (2022)";
    private static readonly PortariaBase[] Portarias = [new(1, P3_2021, 3, 2021), new(2, P3_2025, 3, 2025), new(3, P6, 6, 2022)];
    private static readonly string[] Dotacoes = ["16.10.12.365.3010.2878.4490.5100.00"];
    private readonly string _pasta = Directory.CreateTempSubdirectory("spobras-padrao-").FullName;

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    [Fact]
    public void Propoe_so_o_inequivoco_e_marca_o_ambiguo_para_conferir()
    {
        object?[][] grade =
        [
            ["Portaria", "Dotação", "Obs"],
            ["3 (2025)", "16.10.12.365.3010.2878.4490.5100 00", "Portaria nº 6"],
            ["Nº 03", "3.362.44905100", "texto qualquer 3"],
            [P3_2021, "16.10.12.365.3010.9999.4490.5100.00", "Portaria 9 (2020)"],
            ["6", null, 3m],
        ];

        var achados = ConferenciaPadrao.Conferir("Plan1", grade, Portarias, Dotacoes);

        AchadoPadrao Em(string celula) => achados.Single(a => a.Celula == celula);
        Assert.Equal(P3_2025, Em("A2").Proposta);
        Assert.Equal("16.10.12.365.3010.2878.4490.5100.00", Em("B2").Proposta);
        Assert.Equal(P6, Em("C2").Proposta);
        Assert.Null(Em("A3").Proposta);
        Assert.Contains("na Base há 2021 e 2025", Em("A3").Motivo);
        Assert.Contains("formato abreviado", Em("B3").Motivo);
        Assert.Null(Em("B4").Proposta);
        Assert.Contains("não está na Base de dotações", Em("B4").Motivo);
        Assert.Contains("não está na Base de dados", Em("C4").Motivo);
        Assert.Equal(P6, Em("A5").Proposta);
        Assert.DoesNotContain(achados, a => a.Celula is "A4" or "C3"); // ja padronizada; texto sem portaria
        Assert.Equal(8, achados.Count);
    }

    [Fact]
    public void Copia_corrigida_nao_altera_o_original()
    {
        string arquivo = Path.Combine(_pasta, "medicoes.xlsx");
        XlsxSintetico.Gravar(new PlanilhaFalsa { AbasPorNome = { ["Plan1"] = [["Portaria", "Dotação"], ["Nº 06", "16.10.12.365.3010.2878.4490.5100 00"]] } }, arquivo);
        var antes = File.ReadAllBytes(arquivo);

        var achados = ConferirPlanilha.Conferir(arquivo, Portarias, Dotacoes);
        Assert.Equal(2, achados.Count(a => a.Corrigivel));
        string saida = Path.Combine(_pasta, "medicoes (padronizada).xlsx");
        var nao = ConferirPlanilha.GravarCopiaCorrigida(arquivo, achados, saida);

        Assert.Empty(nao);
        Assert.Equal(antes, File.ReadAllBytes(arquivo));
        using var leitor = new LeitorXlsx(saida);
        var grade = leitor.LerAba("Plan1");
        Assert.Equal(P6, grade[1][0]);
        Assert.Equal("16.10.12.365.3010.2878.4490.5100.00", grade[1][1]);
        Assert.Throws<IOException>(() => ConferirPlanilha.GravarCopiaCorrigida(arquivo, achados, saida));
    }
}
