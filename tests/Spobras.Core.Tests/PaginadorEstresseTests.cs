using Spobras.Core.Relatorio;

namespace Spobras.Core.Tests;

/// <summary>
/// Milhares de relatorios aleatorios (legendas e descricoes de qualquer tamanho, inclusive acima do limite,
/// cabecalhos enormes, todas as quantidades por pagina, com e sem assinaturas). Nunca quebra, nunca perde foto
/// ou palavra, e tudo que o modelo promete vale em todas as paginas.
/// </summary>
public class PaginadorEstresseTests
{
    private static readonly string[] Palavras = ["piso", "EXECUÇÃO", "da", "sala", "cobertura", "telhas", "pintura", "acessibilidade", "rampa", "e", "muro", "WWWWMMMM", "SuperPalavraSemEspaçoNenhumQueNãoCabeNaLinha"];

    private static string Texto(Random sorte, int maximo)
    {
        int alvo = sorte.Next(4) == 0 ? 0 : sorte.Next(maximo + 1);
        var partes = new List<string>();
        int tamanho = 0;
        while (tamanho < alvo)
        {
            string p = Palavras[sorte.Next(Palavras.Length)];
            if (sorte.Next(40) == 0) p += "\n";
            partes.Add(p);
            tamanho += p.Length + 1;
        }

        string t = string.Join(" ", partes).Trim();
        return t.Length > maximo ? t[..maximo].Trim() : t;
    }

    private static string SemEspacos(string t) => string.Concat(t.Where(c => !char.IsWhiteSpace(c)));

    [Fact]
    public void Relatorios_aleatorios_nunca_quebram_nem_perdem_texto_ou_foto()
    {
        var sorte = new Random(2026);
        int impedidos = 0, gerados = 0;
        for (int rodada = 0; rodada < 1500; rodada++)
        {
            int n = sorte.Next(1, 30);
            bool passarDoLimite = sorte.Next(8) == 0;
            var opcoes = new OpcoesRelatorio
            {
                FotosPorPagina = OpcoesRelatorio.Capacidades[sorte.Next(OpcoesRelatorio.Capacidades.Count)],
                Modo = sorte.Next(2) == 0 ? ModoLegenda.LegendaCurta : ModoLegenda.DescricaoServico,
                Assinaturas = sorte.Next(3) == 0,
                AssinaturaFiscal = Texto(sorte, 80),
            };
            var dados = new DadosRelatorio
            {
                Unidade = Texto(sorte, 200),
                Objeto = Texto(sorte, sorte.Next(5) == 0 ? 4000 : 300),
                Descricao = Texto(sorte, passarDoLimite ? 600 : 300),
                Contratada = Texto(sorte, 120),
                Contrato = "044/EX/2026",
                Lote = Texto(sorte, 60),
                Portaria = Texto(sorte, 60),
            };
            var fotos = Enumerable.Range(1, n).Select(i => new FotoRelatorio($"f{i}.jpg", sorte.Next(1, 6000), sorte.Next(1, 6000))
            {
                Legenda = Texto(sorte, passarDoLimite && sorte.Next(3) == 0 ? 400 : OpcoesRelatorio.MaximoDaLegenda),
                Descricao = Texto(sorte, passarDoLimite && sorte.Next(3) == 0 ? 900 : OpcoesRelatorio.MaximoDaDescricao),
                Rotacao = sorte.Next(4) * 90,
            }).ToList();

            PlanoRelatorio plano;
            try
            {
                plano = PaginadorRelatorio.Paginar(fotos, opcoes, dados);
            }
            catch (Exception ex)
            {
                Assert.Fail($"rodada {rodada}: {ex.GetType().Name} {ex.Message}");
                return;
            }

            bool acimaDoLimite = fotos.Any(f => f.Legenda.Length > OpcoesRelatorio.MaximoDaLegenda
                                                || (opcoes.Modo == ModoLegenda.DescricaoServico && f.Descricao.Length > OpcoesRelatorio.MaximoDaDescricao))
                                 || dados.Descricao.Length > OpcoesRelatorio.MaximoDaDescricaoGeral;
            if (!plano.PodeGerar)
            {
                impedidos++;
                Assert.Empty(plano.Paginas);
                Assert.True(acimaDoLimite || plano.Avisos.Any(a => a.Mensagem.Contains("cabeçalho")), $"rodada {rodada}: impediu sem motivo: " + string.Join(" ", plano.Avisos.Select(a => a.Mensagem)));
                continue;
            }

            gerados++;
            Assert.False(acimaDoLimite, $"rodada {rodada}: gerou com texto acima do limite");

            // cada foto exatamente uma vez, na ordem, e a capacidade respeitada
            Assert.Equal(Enumerable.Range(1, n), plano.Paginas.SelectMany(p => p.Fotos).Select(f => f.Numero));
            Assert.All(plano.Paginas, p => Assert.InRange(p.Fotos.Count, 1, opcoes.Capacidade));
            Assert.Equal(opcoes.Assinaturas, plano.Paginas[^1].Assinaturas is not null);
            RelatorioTests.ConferirPlano(plano, $"rodada {rodada}:");

            // nenhuma letra se perde: o que foi escrito e o que vai para a pagina
            foreach (var f in plano.Paginas.SelectMany(p => p.Fotos))
            {
                Assert.Equal(SemEspacos(f.Foto.Legenda), SemEspacos(string.Concat(f.LinhasDaLegenda)));
                Assert.Equal(opcoes.Modo == ModoLegenda.DescricaoServico ? SemEspacos(f.Foto.Descricao) : "", SemEspacos(string.Concat(f.LinhasDaDescricao)));
            }

            // o desenho de cada pagina tambem fica dentro da folha
            foreach (var p in plano.Paginas)
            {
                foreach (var e in DesenhoRelatorio.Pagina(plano, p, temLogo: true))
                {
                    var a = e switch { RetanguloDesenho r => r.Area, TextoDesenho t => t.Area, ImagemDesenho i => i.Area, LinhaDesenho l => new Area(Math.Min(l.X1, l.X2), Math.Min(l.Y1, l.Y2), Math.Abs(l.X2 - l.X1), Math.Abs(l.Y2 - l.Y1)), _ => default };
                    Assert.True(a.X >= GradeRelatorio.MargemLateral - 0.01 && a.Direita <= GradeRelatorio.LarguraDaFolha - GradeRelatorio.MargemLateral + 0.01, $"rodada {rodada}: peça sai pela lateral: {e}");
                    Assert.True(a.Base <= GradeRelatorio.AlturaDaFolha - GradeRelatorio.MargemDeBaixo + 0.01, $"rodada {rodada}: peça passa da margem de baixo");
                }
            }
        }

        Assert.InRange(impedidos, 1, 1400); // houve textos acima do limite e cabecalhos enormes, mas nem todos
        Assert.True(gerados > 1000);
    }
}
