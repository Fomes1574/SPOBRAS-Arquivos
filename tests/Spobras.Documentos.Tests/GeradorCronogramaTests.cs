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
        Assert.Equal("Cronograma SPOBRAS - Portaria Nº 06 (2022) / Lote 900 - 2 unidades", grade[1][0]);

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

        // Estilo do modelo do usuario: cabecalho azul-marinho com letra branca, valores em formato contabil, sem frase extra.
        using var zip2 = ZipFile.OpenRead(caminho);
        using var estilos = zip2.GetEntry("xl/styles.xml")!.Open();
        string css = new StreamReader(estilos).ReadToEnd();
        Assert.Contains("FF002060", css);
        Assert.Contains("FFFFFFFF", css);
        Assert.Contains("&quot;R$&quot;", css);
        Assert.DoesNotContain(grade.SelectMany(l => l), v => v is string t && t.StartsWith("Remuneração de"));
    }

    [Fact]
    public void Titulo_automatico_como_no_modelo_de_formulas()
    {
        ObraCronograma O(string nome, string portaria, string lote) => new() { Nome = nome, Portaria = portaria, Lote = lote, Valor = 1, Inicio = new(2026, 1, 1), Fim = new(2026, 2, 1) };
        Assert.Equal("Cronograma SPOBRAS - EMEF Exemplo", GeradorCronograma.TituloSugerido([O("EMEF Exemplo", "Nº 03 (2021)", "12")]));
        Assert.Equal("Cronograma SPOBRAS - Portaria Nº 03 (2021) / Lote 12 - 2 unidades",
            GeradorCronograma.TituloSugerido([O("A", "Nº 03 (2021)", "12"), O("B", "Nº 03 (2021)", "12")]));
        Assert.Equal("Cronograma SPOBRAS - Lote 12 - 2 unidades", GeradorCronograma.TituloSugerido([O("A", "Nº 03 (2021)", "12"), O("B", "Nº 02 (2025)", "12")]));
        Assert.Equal("Cronograma SPOBRAS - 2 unidades", GeradorCronograma.TituloSugerido([O("A", "Nº 03 (2021)", "12"), O("B", "Nº 02 (2025)", "13")]));
        Assert.Equal("Cronograma SPOBRAS", GeradorCronograma.TituloSugerido([]));
    }

    [Fact]
    public void Lote_comprido_ganha_coluna_mais_larga_e_letra_menor_e_as_linhas_ficam_com_a_mesma_altura()
    {
        Assert.Equal(10, GeradorCronograma.LarguraDoLote(["161", "64", "CEI SAFRA"[..8]]));
        double larga = GeradorCronograma.LarguraDoLote(["CEI R. DA SAFRA", "CONSTRUÇÃO CENTRO EDUCACIONAL – CEI, EMEI E EMEF EXEMPLO “REDONDINHOS”"]);
        Assert.InRange(larga, 16, 26);

        var obras = new[]
        {
            new ObraCronograma { Portaria = "Nº 03 (2021)", Lote = "CEI R. DA SAFRA", Dotacao = "16.10.12.365.3010.3359.4490.5100.00", Nome = "CEI - Profa. Exemplo (CEI Rua Exemplo x Rua Exemplo)", Escopo = "Construção", Valor = 2_500_000m, Inicio = new(2026, 11, 1), Fim = new(2027, 5, 1) },
            new ObraCronograma { Portaria = "Nº 02 (2025)", Lote = "CONSTRUÇÃO CENTRO EDUCACIONAL – CEI, EMEI E EMEF EXEMPLO “REDONDINHOS”", Dotacao = "16.10.12.361.3010.3365.4490.5100.00", Nome = "EMEF Exemplo - “Redondinhos”", Escopo = "Construção", Valor = 5_000_000m, Inicio = new(2026, 4, 1), Fim = new(2027, 7, 1) },
        };
        var c = CalculoCronograma.Calcular(obras, 0.10m, []);
        string caminho = Path.Combine(_pasta, "lotes.xlsx");
        GeradorCronograma.Gerar(c, caminho, GeradorCronograma.TituloSugerido(obras));
        if (Environment.GetEnvironmentVariable("SPOBRAS_SAIDA_VISUAL") is { Length: > 0 } saida) File.Copy(caminho, Path.Combine(saida, "cronograma-lotes.xlsx"), true);

        using (var doc = SpreadsheetDocument.Open(caminho, false))
        {
            Assert.Empty(new OpenXmlValidator().Validate(doc).Select(e => e.Description));
        }

        using var zip = ZipFile.OpenRead(caminho);
        using var fluxo = zip.GetEntry("xl/worksheets/sheet1.xml")!.Open();
        var folha = XDocument.Load(fluxo);
        var coluna = folha.Descendants(S + "col").Single(c => (string?)c.Attribute("min") == "2");
        Assert.InRange(double.Parse((string)coluna.Attribute("width")!, System.Globalization.CultureInfo.InvariantCulture), 16, 26);
        var alturas = folha.Descendants(S + "row").Where(r => (string?)r.Attribute("r") is "6" or "7").Select(r => (string?)r.Attribute("ht")).ToList();
        Assert.Equal(2, alturas.Count);
        Assert.Equal(alturas[0], alturas[1]); // mesma altura nas duas obras
        double altura = double.Parse(alturas[0]!, System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(altura, 30, 90);

        using var leitor = new LeitorXlsx(caminho);
        var grade = leitor.LerAba(GeradorCronograma.NomeDaAba);
        Assert.Equal("Cronograma SPOBRAS - 2 unidades", grade[1][0]);
        Assert.Equal("CONSTRUÇÃO CENTRO EDUCACIONAL – CEI, EMEI E EMEF EXEMPLO “REDONDINHOS”", grade[6][1]); // o nome do lote fica inteiro
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
