using SkiaSharp;
using Spobras.Core.Relatorio;

namespace Spobras.Documentos.Tests;

/// <summary>A fonte do relatorio reduzida (so as letras aceitas, com outro nome, como a licenca pede).</summary>
public sealed class FonteDoRelatorioTests
{
    [Fact]
    public void Fonte_reduzida_tem_todas_as_letras_aceitas_e_as_mesmas_larguras_da_conta()
    {
        foreach (bool negrito in new[] { false, true })
        {
            byte[] dados = (byte[])typeof(Spobras.Documentos.Pdf.FontesPdf).GetMethod("FonteDoRelatorio", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .Invoke(null, [negrito])!;
            Assert.True(dados.Length < 120_000, $"fonte com {dados.Length / 1024} KB: a reduzida deveria ser bem menor");
            using var face = SKTypeface.FromData(SKData.CreateCopy(dados));
            Assert.Equal("SPOBRAS Relatorio", face.FamilyName);
            using var fonte = new SKFont(face, 1000);
            var letras = Enumerable.Range(0x20, 0x5F).Concat(Enumerable.Range(0xA0, 0x60)).Select(c => (char)c).Concat("–—‘’“”…•€ŒœŠšŽžŸƒ").Where(c => c != '­');
            foreach (char c in letras)
            {
                Assert.True(face.ContainsGlyph(c), $"sem desenho para '{c}' (U+{(int)c:X4})");
                float largura = fonte.MeasureText(c.ToString());
                Assert.InRange(TextoMedido.Milesimos(c, negrito) - largura, -0.01, 1.0); // a conta arredonda para cima
            }
        }
    }
}
