using Spobras.Core.Relatorio;

namespace Spobras.Core.Tests;

public class OrdemDasFotosTests
{
    private sealed record F(string Nome, int L, int A);

    private static readonly F[] Lista = [new("1", 4, 3), new("2", 3, 4), new("3", 4, 3), new("4", 3, 4), new("5", 2, 2), new("6", 4, 3)];

    private static string Nomes(IEnumerable<F> f) => string.Concat(f.Select(x => x.Nome));

    [Fact]
    public void Marcadas_vao_juntas_para_o_fim_ou_para_o_inicio_sem_mudar_a_ordem_entre_elas()
    {
        var marcadas = new HashSet<int> { 1, 3 };
        Assert.Equal("135624", Nomes(OrdemDasFotos.Marcadas(Lista, marcadas.Contains, paraOFim: true)));
        Assert.Equal("241356", Nomes(OrdemDasFotos.Marcadas(Lista, marcadas.Contains, paraOFim: false)));
        Assert.Equal("123456", Nomes(OrdemDasFotos.Marcadas(Lista, _ => false, paraOFim: true)));
        Assert.Equal("123456", Nomes(OrdemDasFotos.Marcadas(Lista, _ => true, paraOFim: false)));
        Assert.Empty(OrdemDasFotos.Marcadas(Array.Empty<F>(), _ => true, true));
    }

    [Fact]
    public void Deitadas_primeiro_em_pe_no_fim_quadrada_com_as_deitadas_e_estavel()
    {
        var ordem = OrdemDasFotos.DeitadasPrimeiro(Lista, f => (f.L, f.A));
        Assert.Equal("135624", Nomes(ordem));
        Assert.Equal("135624", Nomes(OrdemDasFotos.DeitadasPrimeiro(ordem, f => (f.L, f.A)))); // de novo: nada muda
        Assert.Equal(0, OrdemDasFotos.Mudancas(ordem, OrdemDasFotos.DeitadasPrimeiro(ordem, f => (f.L, f.A))));
        Assert.Equal(5, OrdemDasFotos.Mudancas(Lista, ordem)); // so a primeira fica no lugar
    }

    [Fact]
    public void Muitas_fotos_aleatorias_nunca_perdem_nem_repetem_foto()
    {
        var sorte = new Random(7);
        for (int r = 0; r < 500; r++)
        {
            var fotos = Enumerable.Range(0, sorte.Next(0, 300)).Select(i => new F(i.ToString(), sorte.Next(1, 5), sorte.Next(1, 5))).ToArray();
            var marcadas = fotos.Select((_, i) => i).Where(_ => sorte.Next(3) == 0).ToHashSet();
            foreach (var nova in new[] { OrdemDasFotos.Marcadas(fotos, marcadas.Contains, true), OrdemDasFotos.Marcadas(fotos, marcadas.Contains, false), OrdemDasFotos.DeitadasPrimeiro(fotos, f => (f.L, f.A)) })
            {
                Assert.Equal(fotos.OrderBy(f => f.Nome), nova.OrderBy(f => f.Nome));
            }

            var fim = OrdemDasFotos.Marcadas(fotos, marcadas.Contains, true);
            Assert.Equal(marcadas.Order().Select(i => fotos[i]), fim.Skip(fotos.Length - marcadas.Count));
        }
    }
}
