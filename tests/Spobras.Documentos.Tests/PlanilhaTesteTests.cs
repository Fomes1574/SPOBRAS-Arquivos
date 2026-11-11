using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using Spobras.Documentos.Ambiente;

namespace Spobras.Documentos.Tests;

public class PlanilhaTesteTests : IDisposable
{
    private readonly string _pasta = Directory.CreateTempSubdirectory("spobras-doc-").FullName;

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    [Fact]
    public void Cria_xlsx_valido_com_entrada_e_formulas()
    {
        string caminho = Path.Combine(_pasta, "teste.xlsx");

        PlanilhaTeste.Criar(caminho);

        using (var doc = SpreadsheetDocument.Open(caminho, false))
        {
            var erros = new OpenXmlValidator().Validate(doc, TestContext.Current.CancellationToken).Select(e => e.Description).ToList();
            Assert.Empty(erros);
        }

        Assert.Equal(PlanilhaTeste.ValorInicialEntrada, PlanilhaTeste.LerNumero(caminho, PlanilhaTeste.CelulaEntrada));
        Assert.Equal("SUM(B2:B4)", PlanilhaTeste.LerFormula(caminho, PlanilhaTeste.CelulaTotal));
        Assert.Equal("B5*2", PlanilhaTeste.LerFormula(caminho, PlanilhaTeste.CelulaDobro));
        // Formula ainda sem valor calculado: quem calcula e o Excel.
        Assert.Null(PlanilhaTeste.LerNumero(caminho, PlanilhaTeste.CelulaTotal));
    }

    [Fact]
    public void Total_esperado_soma_a_entrada_com_as_parcelas_fixas()
    {
        Assert.Equal(400m, PlanilhaTeste.TotalEsperado(100m));
        Assert.Equal(1000m, PlanilhaTeste.TotalEsperado(700m));
    }
}
