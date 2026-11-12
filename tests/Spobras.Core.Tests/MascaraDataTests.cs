namespace Spobras.Core.Tests;

public class MascaraDataTests
{
    [Theory]
    [InlineData("2", "2")]
    [InlineData("21", "21")]
    [InlineData("210", "21/0")]
    [InlineData("2105", "21/05")]
    [InlineData("21052", "21/05/2")]
    [InlineData("21052026", "21/05/2026")]
    [InlineData("210520261", "21/05/2026")]  // numero a mais e ignorado
    [InlineData("21/05/2026", "21/05/2026")]
    [InlineData("21/05", "21/05")]
    [InlineData("21//05", "21/05")]
    [InlineData("210/5", "21/05")]          // barra em lugar estranho: refaz pelos numeros
    [InlineData("", "")]
    [InlineData("hoje", "hoje")]
    public void Poe_as_barras_enquanto_digita(string digitado, string esperado) =>
        Assert.Equal(esperado, MascaraData.Formatar(digitado));

    [Theory]
    [InlineData("2105", "21/05/2026")]
    [InlineData("21/05", "21/05/2026")]
    [InlineData("21/05/26", "21/05/2026")]
    [InlineData("1/5/2026", "01/05/2026")]
    [InlineData("21052025", "21/05/2025")]
    [InlineData("31/02/2026", "31/02/2026")] // nao existe: fica como esta e a tela avisa
    [InlineData("abc", "abc")]
    [InlineData("", "")]
    public void Ao_sair_completa_o_ano(string digitado, string esperado) =>
        Assert.Equal(esperado, MascaraData.Completar(digitado, new DateOnly(2026, 11, 12)));
}
