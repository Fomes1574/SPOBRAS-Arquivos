using System.Security.Cryptography;
using SkiaSharp;
using Spobras.Core.Relatorio;
using Spobras.Documentos.Imagens;
using Spobras.Documentos.Relatorio;

namespace Spobras.Documentos.Tests;

/// <summary>Carimbo gravado na copia da foto (nunca na original) e a fonte reduzida do relatorio.</summary>
public sealed class CarimboTests : IDisposable
{
    private readonly string _pasta = Directory.CreateTempSubdirectory("spobras-carimbo-").FullName;

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    private static readonly CarimboFoto Carimbo = CarimboFoto.Montar(new AjusteCarimbo { Ligado = true }, "EMEF Escola Exemplo", "Rua Exemplo, 123 – Vila Exemplo", new DateTime(2026, 11, 18, 14, 32, 0))!;

    /// <summary>Foto clara e lisa: o carimbo escuro com letra branca aparece bem na comparacao de pixels.</summary>
    private string Foto(string nome, int largura, int altura)
    {
        string caminho = Path.Combine(_pasta, nome);
        using var b = new SKBitmap(largura, altura);
        using (var c = new SKCanvas(b))
        {
            c.Clear(new SKColor(200, 210, 220));
        }

        using var img = SKImage.FromBitmap(b);
        using var dados = img.Encode(SKEncodedImageFormat.Jpeg, 95);
        File.WriteAllBytes(caminho, dados.ToArray());
        return caminho;
    }

    private static SKBitmap Ler(byte[] jpeg) => SKBitmap.Decode(jpeg);

    [Fact]
    public void Grava_o_carimbo_so_na_copia_dentro_da_area_calculada()
    {
        string original = Foto("f.jpg", 1600, 1200);
        byte[] antes = SHA256.HashData(File.ReadAllBytes(original));
        using var sem = Ler(Fotos.Preparar(original, 1600, 90));
        using var com = Ler(Fotos.Preparar(original, 1600, 90, 0, Carimbo));
        Assert.Equal(antes, SHA256.HashData(File.ReadAllBytes(original))); // a original nunca muda

        var g = GeometriaCarimbo.Calcular(1600, 1200, Carimbo)!;
        int brancos = 0, escuros = 0, foraIgual = 0, fora = 0;
        for (int y = 0; y < com.Height; y += 3)
        {
            for (int x = 0; x < com.Width; x += 3)
            {
                var p = com.GetPixel(x, y);
                bool dentro = x > g.X + 2 && x < g.X + g.Largura - 2 && y > g.Y + 2 && y < g.Y + g.Altura - 2;
                bool longe = x < g.X - 8 || x > g.X + g.Largura + 8 || y < g.Y - 8 || y > g.Y + g.Altura + 8;
                if (dentro)
                {
                    if (p.Red > 235 && p.Green > 235 && p.Blue > 235) brancos++;
                    if (p.Red < 120) escuros++;
                }
                else if (longe)
                {
                    fora++;
                    var q = sem.GetPixel(x, y);
                    if (Math.Abs(p.Red - q.Red) <= 6 && Math.Abs(p.Green - q.Green) <= 6 && Math.Abs(p.Blue - q.Blue) <= 6) foraIgual++;
                }
            }
        }

        Assert.True(escuros > 200, $"faixa escura nao apareceu ({escuros})");
        Assert.True(brancos > 30, $"texto branco nao apareceu ({brancos})");
        Assert.True(foraIgual > fora * 0.99, $"o carimbo mexeu fora da faixa ({foraIgual} de {fora})");
    }

    [Theory]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public void Com_giro_o_carimbo_fica_no_canto_da_foto_ja_girada(int giro)
    {
        string original = Foto($"g{giro}.jpg", 1600, 1200);
        using var com = Ler(Fotos.Preparar(original, 1600, 90, giro, Carimbo));
        int l = giro % 180 == 0 ? 1600 : 1200, a = giro % 180 == 0 ? 1200 : 1600;
        Assert.Equal((l, a), (com.Width, com.Height));
        var g = GeometriaCarimbo.Calcular(l, a, Carimbo)!; // canto inferior esquerdo da foto como ela sai
        var centro = com.GetPixel((int)(g.X + g.Raio + 2), (int)(g.Y + g.Altura - g.Raio - 2));
        Assert.True(centro.Red < 120, $"faixa fora do lugar com giro {giro}");
    }

    [Fact]
    public void Cache_e_geracao_separam_copia_com_e_sem_carimbo()
    {
        string original = Foto("c.jpg", 1600, 1200);
        Assert.NotEqual(PreparoDeFotos.NomeNoCache(original, 0, 1600, 82), PreparoDeFotos.NomeNoCache(original, 0, 1600, 82, Carimbo));
        Assert.EndsWith($"-0-1600-82c{Carimbo.Assinatura}.jpg", PreparoDeFotos.NomeNoCache(original, 0, 1600, 82, Carimbo));
        var preparo = new PreparoDeFotos(Path.Combine(_pasta, "cache"));
        var sem = new FotoRelatorio(original, 1600, 1200);
        var com = sem with { Carimbo = Carimbo };
        byte[] a = preparo.Obter(sem, 1600, 82), b = preparo.Obter(com, 1600, 82);
        Assert.NotEqual(a, b);
        Assert.Equal(2, Directory.GetFiles(Path.Combine(_pasta, "cache")).Length);
        Assert.Equal(b, new PreparoDeFotos().Obter(com, 1600, 82)); // em memoria, o mesmo resultado
    }

    [Fact]
    public void Excel_e_PDF_saem_com_a_foto_carimbada()
    {
        string original = Foto("r.jpg", 1600, 1200);
        var foto = new FotoRelatorio(original, 1600, 1200) { Legenda = "Fachada", Carimbo = Carimbo };
        var plano = PaginadorRelatorio.Paginar([foto], new OpcoesRelatorio { FotosPorPagina = 1 }, new DadosRelatorio { Unidade = "EMEF Escola Exemplo" });
        string xlsx = Path.Combine(_pasta, "r.xlsx"), pdf = Path.Combine(_pasta, "r.pdf");
        GeradorRelatorio.Gerar(plano, xlsx, null);
        GeradorRelatorioPdf.Gerar(plano, pdf, null);
        byte[] carimbada = Fotos.Preparar(original, plano.Opcoes.LadoMaximoFoto, plano.Opcoes.QualidadeFoto, 0, Carimbo);
        using var zip = System.IO.Compression.ZipFile.OpenRead(xlsx);
        var midia = zip.Entries.Where(e => e.FullName.StartsWith("xl/media/", StringComparison.Ordinal)).ToList();
        Assert.Contains(midia, e =>
        {
            using var s = e.Open();
            using var m = new MemoryStream();
            s.CopyTo(m);
            return m.ToArray().AsSpan().SequenceEqual(carimbada);
        });
        Assert.True(new FileInfo(pdf).Length > 10_000);
    }

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
