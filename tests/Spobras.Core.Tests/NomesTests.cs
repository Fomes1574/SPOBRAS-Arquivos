using Spobras.Core.Nomes;

namespace Spobras.Core.Tests;

public class NomesTests
{
    [Fact]
    public void Ordem_natural_poe_2_antes_de_10()
    {
        string[] nomes = ["foto 10.jpg", "foto 2.jpg", "Foto 1.jpg", "foto 02b.jpg", "anexo.pdf", "foto 100.jpg"];

        Assert.Equal(
            ["anexo.pdf", "Foto 1.jpg", "foto 2.jpg", "foto 02b.jpg", "foto 10.jpg", "foto 100.jpg"],
            nomes.Order(OrdemNatural.Instancia));
    }

    [Theory]
    [InlineData("Relatório 01.pdf", null)]
    [InlineData("", "vazio")]
    [InlineData("a:b.pdf", "não aceita")]
    [InlineData("a?.pdf", "não aceita")]
    [InlineData("nome.", "terminar")]
    [InlineData("nome ", "terminar")]
    [InlineData("CON.txt", "reservado")]
    [InlineData("lpt1", "reservado")]
    public void Valida_nomes_do_Windows(string nome, string? trechoDoProblema)
    {
        string? problema = NomeArquivo.Problema(nome);

        if (trechoDoProblema is null)
        {
            Assert.Null(problema);
        }
        else
        {
            Assert.Contains(trechoDoProblema, problema);
        }
    }

    [Fact]
    public void Valida_tamanho_do_nome_e_do_caminho()
    {
        Assert.Contains("máximo", NomeArquivo.Problema(new string('a', 256)));
        Assert.Null(NomeArquivo.Problema(new string('a', 255)));
        Assert.Contains("caminho completo", NomeArquivo.Problema(new string('a', 100), @"C:\" + new string('p', 200)));
    }

    [Fact]
    public void Corrige_e_limpa_nomes()
    {
        Assert.Equal("Medição 10-11 - final", NomeArquivo.Corrigir("Medição 10/11 : final. "));
        Assert.Equal("_CON.txt", NomeArquivo.Corrigir("CON.txt"));
        Assert.Equal("Medicao no 3 - Sao Paulo", NomeArquivo.RemoverAcentos("Medição no 3 - São Paulo"));
        Assert.Equal("a b c", NomeArquivo.RemoverEspacosExcedentes("  a   b  c "));
    }

    [Fact]
    public void Nome_livre_usa_sufixo_2_3()
    {
        var ocupados = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "a.pdf", "A (2).pdf" };

        Assert.Equal("b.pdf", NomeArquivo.Livre("b.pdf", ocupados.Contains));
        Assert.Equal("a (3).pdf", NomeArquivo.Livre("a.pdf", ocupados.Contains));
    }

    [Fact]
    public void Formata_tamanhos_em_portugues()
    {
        Assert.Equal("950 bytes", Tamanho.Formatar(950));
        Assert.Equal("12,5 KB", Tamanho.Formatar(12_800));
        Assert.Equal("25 MB", Tamanho.Formatar(25L * 1024 * 1024));
    }
}
