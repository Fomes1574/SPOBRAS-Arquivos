using System.IO.Compression;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using Spobras.Core.Cronograma;
using Spobras.Documentos.Cronograma;
using Spobras.Documentos.Planilhas;

namespace Spobras.Documentos.Tests;

public class GeradorCronogramaTests : IDisposable
{
    private readonly string _pasta = Directory.CreateTempSubdirectory("spobras-crono-").FullName;
    private static readonly XNamespace S = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    private static CronogramaCalculado Exemplo()
    {
        var obras = new[]
        {
            new ObraCronograma { Portaria = "Nº 06 (2022)", Lote = "900", Dotacao = "16.10.12.365.3010.3360.4490.5100.00", Nome = "CEI Exemplo Beta", Escopo = "Reforma", Valor = 1_000_000m, Inicio = new(2026, 10, 1), Fim = new(2027, 6, 1) },
            new ObraCronograma { Portaria = "Nº 06 (2022)", Lote = "900", Dotacao = "16.10.12.365.3010.3360.4490.5100.00", Nome = "EMEI Exemplo Gama", Escopo = "Reforma", Valor = 500_000.01m, Inicio = new(2026, 11, 1), Fim = new(2028, 1, 1) },
        };
        return CalculoCronograma.Calcular(obras, 0.10m, [new(new DateOnly(2026, 10, 1), TipoAjuste.Percentual, 5)]);
    }

    [Fact]
    public void Gera_planilha_valida_com_meses_anos_totais_e_tabela_por_dotacao()
    {
        var c = Exemplo();
        string caminho = Path.Combine(_pasta, "cronograma.xlsx");
        GeradorCronograma.Gerar(c, caminho, GeradorCronograma.TituloSugerido(c.Obras.Select(o => o.Obra).ToList()));

        using (var doc = SpreadsheetDocument.Open(caminho, false))
        {
            Assert.Empty(new OpenXmlValidator().Validate(doc).Select(e => e.Description));
        }

        using var leitor = new LeitorXlsx(caminho);
        var grade = leitor.LerAba(GeradorCronograma.NomeDaAba);
        Assert.Equal("Cronograma SPOBRAS - Portaria Nº 06 (2022) / Lote 900 - 2 unidades", grade[1][3]);

        var titulos = grade[4].Select(v => v?.ToString()).ToList();
        titulos = titulos.Take(titulos.FindLastIndex(t => t is not null) + 1).ToList();
        Assert.Equal("Valores de Remuneração (10%)", titulos[6]);
        Assert.Contains("Total 2026", titulos);
        Assert.Contains("Total 2027", titulos);
        Assert.Contains("Total 2028", titulos);
        Assert.Equal("Total solicitado", titulos[^1]);
        Assert.Equal(16, c.Meses.Count); // out/26 a jan/28

        // Linha de TOTAL: a soma gravada fecha com o calculo.
        var total = grade[7];
        Assert.Equal("TOTAL", total[0]);
        Assert.Equal(c.Total, Convert.ToDecimal(total[7]));
        Assert.Equal(c.Total, Convert.ToDecimal(total[titulos.Count - 1]));
        Assert.Equal(c.DoAno(2027), Convert.ToDecimal(total[titulos.IndexOf("Total 2027")]));

        // Tabela por dotacao: obra e remuneracao somadas das duas obras.
        var linhas = grade.Where(l => l.Length > 5 && l[3] is string tipo && tipo.StartsWith("Valor para")).ToList();
        Assert.Equal(2, linhas.Count);
        Assert.Equal("16.10.12.365.3010.3360.4490.3900.00", linhas[1][4]);
        Assert.Equal(c.TotalRemuneracao, Convert.ToDecimal(linhas[1][5]));

        // Somas como formulas (continuam certas se mexerem num mes), com o resultado ja gravado.
        using var zip = ZipFile.OpenRead(caminho);
        using var fluxo = zip.GetEntry("xl/worksheets/sheet1.xml")!.Open();
        var formulas = XDocument.Load(fluxo).Descendants(S + "f").Select(f => f.Value).ToList();
        Assert.Contains("F6+G6", formulas);
        Assert.Contains("SUM(F6:F7)", formulas);
        Assert.Contains("G6+G7", formulas);
    }

    [Fact]
    public void Nao_sobrescreve_e_nao_gera_com_pendencias()
    {
        var c = Exemplo();
        string caminho = Path.Combine(_pasta, "a.xlsx");
        File.WriteAllText(caminho, "meu arquivo");
        Assert.Throws<IOException>(() => GeradorCronograma.Gerar(c, caminho, "t"));
        Assert.Equal("meu arquivo", File.ReadAllText(caminho));

        var ruim = CalculoCronograma.Calcular([new ObraCronograma { Nome = "Sem valor" }], 0.10m, []);
        Assert.Throws<InvalidOperationException>(() => GeradorCronograma.Gerar(ruim, Path.Combine(_pasta, "b.xlsx"), "t"));
        Assert.False(File.Exists(Path.Combine(_pasta, "b.xlsx")));
    }
}
