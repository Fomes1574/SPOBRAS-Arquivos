using Spobras.Core.Base;

namespace Spobras.Core.Tests;

/// <summary>Valor da obra formatado enquanto digita (2.500.000 em vez de 2500000).</summary>
public class DinheiroAoDigitarTests
{
    [Theory]
    [InlineData("2500000", "2.500.000")]
    [InlineData("250000", "250.000")]
    [InlineData("1000", "1.000")]
    [InlineData("999", "999")]
    [InlineData("", "")]
    [InlineData("2500000,5", "2.500.000,5")]
    [InlineData("2500000,567", "2.500.000,56")]
    [InlineData("2.500.000,00", "2.500.000,00")]
    [InlineData("R$ 2.500.000,00", "2.500.000,00")]
    [InlineData("00012", "12")]
    [InlineData("0,5", "0,5")]
    [InlineData(",5", "0,5")]
    [InlineData("2.5000", "25.000")]
    [InlineData("12a34", "1.234")]
    [InlineData("1,2,3", "1,23")]
    public void Formata_ao_digitar(string digitado, string esperado) => Assert.Equal(esperado, DinheiroAoDigitar.Formatar(digitado, digitado.Length).Texto);

    [Fact]
    public void Cursor_fica_depois_do_digito_que_a_pessoa_acabou_de_digitar()
    {
        // digitou o 4o zero no fim: "2.5000" vira "25.000" e o cursor vai para o fim
        Assert.Equal(("25.000", 6), DinheiroAoDigitar.Formatar("2.5000", 6));
        // digitou "9" no meio de "2.500.000" (depois do "2"): "29.500.000", cursor depois do 9
        Assert.Equal(("29.500.000", 2), DinheiroAoDigitar.Formatar("29.500.000", 2));
        // apagou um digito no meio: "2.50.000" -> "250.000", cursor continua depois do "50"
        var (texto, cursor) = DinheiroAoDigitar.Formatar("2.50.000", 4);
        Assert.Equal("250.000", texto);
        Assert.Equal("250", texto[..cursor]);
        // digitou a virgula no fim
        Assert.Equal(("1.000,", 6), DinheiroAoDigitar.Formatar("1000,", 5));
        // digitou um centavo
        Assert.Equal(("1.000,5", 7), DinheiroAoDigitar.Formatar("1.000,5", 7));
    }

    [Fact]
    public void Texto_formatado_continua_sendo_lido_como_o_mesmo_valor()
    {
        foreach (string v in new[] { "2500000", "1234567,89", "0,05", "10" })
        {
            string f = DinheiroAoDigitar.Formatar(v, v.Length).Texto;
            Assert.Equal(Normalizacao.LerReais(v), Normalizacao.LerReais(f));
        }

        Assert.Equal(2_500_000m, Normalizacao.LerReais("2.500.000"));
    }
}

/// <summary>Estresse da máscara de dinheiro: milhares de digitações aleatórias, colagens e apagamentos.</summary>
public class DinheiroAoDigitarEstresseTests
{
    [Fact]
    public void Nunca_quebra_nunca_perde_digito_e_o_cursor_fica_dentro_do_texto()
    {
        var sorte = new Random(1574);
        const string teclas = "0123456789,.R$ a-";
        for (int rodada = 0; rodada < 20_000; rodada++)
        {
            int tamanho = sorte.Next(0, 18);
            string texto = new([.. Enumerable.Range(0, tamanho).Select(_ => teclas[sorte.Next(teclas.Length)])]);
            int cursor = sorte.Next(0, texto.Length + 1);
            var (novo, pos) = DinheiroAoDigitar.Formatar(texto, cursor);
            Assert.InRange(pos, 0, novo.Length);
            Assert.Equal(novo, DinheiroAoDigitar.Formatar(novo, pos).Texto); // formatar de novo não muda nada

            int virgula = texto.IndexOf(',');
            string inteiros = new([.. (virgula < 0 ? texto : texto[..virgula]).Where(char.IsAsciiDigit)]);
            string centavos = virgula < 0 ? "" : new string([.. texto[(virgula + 1)..].Where(char.IsAsciiDigit)]);
            if (inteiros.Length + centavos.Length == 0)
            {
                continue;
            }

            decimal esperado = (inteiros.TrimStart('0') is { Length: > 0 } i ? decimal.Parse(i, System.Globalization.CultureInfo.InvariantCulture) : 0m)
                + (centavos.Length == 0 ? 0m : decimal.Parse("0." + centavos[..Math.Min(2, centavos.Length)], System.Globalization.CultureInfo.InvariantCulture));
            Assert.Equal(esperado, Normalizacao.LerReais(novo.EndsWith(',') ? novo + "0" : novo));
        }
    }
}
