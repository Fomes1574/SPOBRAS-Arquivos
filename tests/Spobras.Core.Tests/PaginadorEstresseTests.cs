using Spobras.Core.Relatorio;

namespace Spobras.Core.Tests;

/// <summary>
/// Milhares de relatorios aleatorios (legendas de 0 a 5.000 caracteres, descricoes, cabecalhos enormes, todas as
/// quantidades por pagina, com e sem assinaturas). Antes, legenda longa ou cabecalho grande derrubavam a geracao.
/// </summary>
public class PaginadorEstresseTests
{
    private static readonly string[] Palavras = ["piso", "execução", "da", "sala", "cobertura", "telhas", "pintura", "acessibilidade", "rampa", "e", "muro", "SuperPalavraSemEspaçoNenhumQueNãoCabeNaLinha"];

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

        return string.Join(" ", partes).Trim();
    }

    private static IEnumerable<string> PalavrasDe(string t) => t.Split([' ', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries);

    [Fact]
    public void Relatorios_aleatorios_nunca_quebram_nem_perdem_texto_ou_foto()
    {
        var sorte = new Random(2026);
        int comPendencia = 0;
        for (int rodada = 0; rodada < 1500; rodada++)
        {
            int n = sorte.Next(1, 25);
            var opcoes = new OpcoesRelatorio
            {
                FotosPorPagina = OpcoesRelatorio.Capacidades[sorte.Next(OpcoesRelatorio.Capacidades.Count)],
                Modo = sorte.Next(2) == 0 ? ModoLegenda.LegendaCurta : ModoLegenda.DescricaoServico,
                Assinaturas = sorte.Next(3) == 0,
            };
            var dados = new DadosRelatorio
            {
                Unidade = Texto(sorte, 200),
                Objeto = Texto(sorte, sorte.Next(5) == 0 ? 4000 : 300),
                Descricao = Texto(sorte, sorte.Next(5) == 0 ? 6000 : 400),
                Contratada = Texto(sorte, 120),
                Contrato = "044/EX/2026",
            };
            var fotos = Enumerable.Range(1, n).Select(i => new FotoRelatorio($"f{i}.jpg", sorte.Next(2) == 0 ? 4000 : 3000, sorte.Next(2) == 0 ? 3000 : 4000)
            {
                Legenda = Texto(sorte, sorte.Next(6) == 0 ? 5000 : 300),
                Descricao = Texto(sorte, sorte.Next(6) == 0 ? 5000 : 600),
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

            if (plano.Avisos.Any(a => a.Gravidade == Gravidade.ImpedeGerar))
            {
                comPendencia++;
                Assert.Empty(plano.Paginas);
                Assert.Contains(plano.Avisos, a => a.Mensagem.Contains("cabeçalho"));
                continue;
            }

            int corpo = plano.Cabecalho.LinhasDoCorpo;

            // cada foto exatamente uma vez, na ordem
            Assert.Equal(Enumerable.Range(1, n), plano.Paginas.SelectMany(p => p.Fotos).Select(f => f.Numero));

            foreach (var pagina in plano.Paginas)
            {
                foreach (var f in pagina.Fotos)
                {
                    Assert.True(f.LinhasDaFoto >= 5, $"rodada {rodada}: foto pequena demais");
                    Assert.True(f.LinhaInicial + f.LinhasDaFoto + f.LinhasDoTexto <= corpo, $"rodada {rodada}: foto {f.Numero} passou da página");
                    double largura = f.Espaco.Largura;
                    int precisa = (f.Titulo.Length > 0 ? PaginadorRelatorio.LinhasDe(f.Titulo, largura) : 0)
                                  + (f.Descricao.Length > 0 ? PaginadorRelatorio.LinhasDe(f.Descricao, largura) : 0);
                    Assert.True(precisa <= f.LinhasDoTexto, $"rodada {rodada}: texto da foto {f.Numero} não cabe ({precisa} > {f.LinhasDoTexto})");
                }

                foreach (var c in pagina.Continuacoes)
                {
                    Assert.True(c.LinhaInicial + c.Linhas <= corpo, $"rodada {rodada}: continuação passou da página");
                    Assert.True(PaginadorRelatorio.LinhasDe(c.Texto, GradeRelatorio.Largura) + 1 <= c.Linhas, $"rodada {rodada}: continuação cortada");
                }

                if (pagina.Assinaturas is { } a)
                {
                    Assert.True(a.LinhaInicial >= 0 && a.LinhaInicial + a.Linhas <= corpo);
                    Assert.All(pagina.Fotos, f => Assert.True(f.LinhaInicial + f.LinhasDaFoto + f.LinhasDoTexto <= a.LinhaInicial, $"rodada {rodada}: foto embaixo das assinaturas"));
                }
            }

            // nenhuma palavra se perde: tudo que foi escrito aparece embaixo da foto ou na continuacao
            var escritas = fotos.SelectMany(f => PalavrasDe(f.Legenda).Concat(opcoes.Modo == ModoLegenda.DescricaoServico ? PalavrasDe(f.Descricao) : [])).ToList();
            var mostradas = plano.Paginas.SelectMany(p => p.Fotos).SelectMany(f => PalavrasDe(f.Titulo.Replace(" (continua na próxima página)", "")).Concat(PalavrasDe(f.Descricao.Replace(" (continua na próxima página)", ""))))
                .Concat(plano.Paginas.SelectMany(p => p.Continuacoes).SelectMany(c => PalavrasDe(c.Texto))).ToList();
            Assert.Equal(escritas.Order(StringComparer.Ordinal), mostradas.Order(StringComparer.Ordinal));
        }

        Assert.InRange(comPendencia, 1, 1499); // houve cabecalhos enormes, mas nem todos
    }
}
