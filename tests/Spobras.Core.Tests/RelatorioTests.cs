using Spobras.Core.Relatorio;

namespace Spobras.Core.Tests;

public class RelatorioTests
{
    private static FotoRelatorio Horizontal(int n, string legenda = "", string descricao = "") =>
        new($"foto {n}.jpg", 4000, 3000) { Legenda = legenda, Descricao = descricao };

    private static List<FotoRelatorio> Fotos(int quantidade) => [.. Enumerable.Range(1, quantidade).Select(n => Horizontal(n, $"Serviço {n}"))];

    /// <summary>Como no modelo do Jardim Paulistano: as fotos nunca se encostam, nem na horizontal nem na vertical.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(8)]
    public void Fotos_nunca_se_encostam(int porPagina)
    {
        var fotos = Enumerable.Range(1, porPagina * 2)
            .Select(n => new FotoRelatorio($"f{n}.jpg", n % 3 == 0 ? 3000 : 4000, n % 3 == 0 ? 4000 : n % 5 == 0 ? 4000 : 3000) { Legenda = n % 2 == 0 ? "Serviço com legenda" : "" })
            .ToList();
        var plano = PaginadorRelatorio.Paginar(fotos, new OpcoesRelatorio { FotosPorPagina = porPagina });

        foreach (var pagina in plano.Paginas)
        {
            foreach (var a in pagina.Fotos)
            {
                // Dentro do proprio espaco, com folga em volta
                Assert.True(a.Imagem.X >= a.Espaco.X + (GradeRelatorio.Folga / 2) - 0.01 && a.Imagem.X + a.Imagem.Largura <= a.Espaco.X + a.Espaco.Largura - (GradeRelatorio.Folga / 2) + 0.01, $"foto {a.Numero} encosta na lateral");
                foreach (var b in pagina.Fotos.Where(b => b.Numero > a.Numero))
                {
                    double folgaX = Math.Max(b.Imagem.X - (a.Imagem.X + a.Imagem.Largura), a.Imagem.X - (b.Imagem.X + b.Imagem.Largura));
                    double folgaY = Math.Max(b.Imagem.Y - (a.Imagem.Y + a.Imagem.Altura), a.Imagem.Y - (b.Imagem.Y + b.Imagem.Altura));
                    Assert.True(Math.Max(folgaX, folgaY) >= GradeRelatorio.Folga - 0.01, $"fotos {a.Numero} e {b.Numero} estão a menos de {GradeRelatorio.Folga} pt");
                }
            }
        }
    }

    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(2, 1, 2)]
    [InlineData(4, 2, 2)]
    [InlineData(6, 2, 3)]
    [InlineData(8, 2, 4)]
    public void T10_capacidades_1_2_4_6_8_por_pagina(int porPagina, int colunas, int faixas)
    {
        var plano = PaginadorRelatorio.Paginar(Fotos(porPagina * 2 + 1), new OpcoesRelatorio { FotosPorPagina = porPagina });

        Assert.True(plano.PodeGerar);
        Assert.Equal(3, plano.Paginas.Count);
        Assert.Equal([porPagina, porPagina, 1], plano.Paginas.Select(p => p.Fotos.Count));
        Assert.All(plano.Paginas[0].Fotos, f => Assert.Equal(colunas, f.Colunas));
        Assert.Equal(faixas, plano.Paginas[0].Fotos.Select(f => f.LinhaInicial).Distinct().Count());
        Assert.Empty(plano.Avisos);
    }

    [Fact]
    public void T09_cem_fotos_ficam_em_ordem_e_numeradas()
    {
        var plano = PaginadorRelatorio.Paginar(Fotos(100), new OpcoesRelatorio());

        Assert.Equal(17, plano.Paginas.Count); // 16 paginas de 6 + 1 de 4
        var todas = plano.Paginas.SelectMany(p => p.Fotos).ToList();
        Assert.Equal(Enumerable.Range(1, 100), todas.Select(f => f.Numero));
        Assert.Equal("Foto 001 — Serviço 1", todas[0].Titulo);
        Assert.Equal("Foto 100 — Serviço 100", todas[99].Titulo);
        Assert.Equal(Enumerable.Range(1, 17), plano.Paginas.Select(p => p.Numero));
    }

    [Theory]
    [InlineData(4000, 3000)] // horizontal
    [InlineData(3000, 4000)] // vertical
    [InlineData(2000, 2000)] // quadrada
    [InlineData(6000, 1000)] // panoramica
    public void T09_foto_cabe_no_espaco_sem_distorcer_nem_cortar(int largura, int altura)
    {
        var foto = new FotoRelatorio("f.jpg", largura, altura);
        foreach (int porPagina in OpcoesRelatorio.Capacidades)
        {
            var naPagina = PaginadorRelatorio.Paginar([foto], new OpcoesRelatorio { FotosPorPagina = porPagina }).Paginas[0].Fotos[0];

            Assert.Equal((double)largura / altura, naPagina.Imagem.Largura / naPagina.Imagem.Altura, 3);
            Assert.True(naPagina.Imagem.X >= naPagina.Espaco.X && naPagina.Imagem.Direita <= naPagina.Espaco.Direita + 0.01);
            Assert.True(naPagina.Imagem.Y >= naPagina.Espaco.Y && naPagina.Imagem.Base <= naPagina.Texto.Y + 0.01); // nao invade o texto
        }
    }

    [Fact]
    public void Nada_passa_do_fim_da_pagina_e_as_colunas_nao_se_sobrepoem()
    {
        var fotos = Enumerable.Range(1, 30).Select(n => Horizontal(n, new string('x', n * 9), new string('y', n * 40))).ToList();
        foreach (int porPagina in OpcoesRelatorio.Capacidades)
        {
            var plano = PaginadorRelatorio.Paginar(fotos, new OpcoesRelatorio { FotosPorPagina = porPagina, Modo = ModoLegenda.DescricaoServico, Assinaturas = true });

            Assert.Equal(30, plano.TotalDeFotos);
            foreach (var pagina in plano.Paginas)
            {
                Assert.All(pagina.Fotos, f => Assert.True(f.Espaco.Base <= GradeRelatorio.AlturaDaPagina + 0.01, $"página {pagina.Numero} estourou"));
                Assert.All(pagina.Fotos, f => Assert.True(f.Espaco.Direita <= GradeRelatorio.Largura + 0.01));
                foreach (var faixa in pagina.Fotos.GroupBy(f => f.LinhaInicial).Where(g => g.Count() == 2))
                {
                    Assert.True(faixa.First().Espaco.Direita <= faixa.Last().Espaco.X + 0.01);
                }

                // Faixas empilhadas sem sobreposicao.
                var inicios = pagina.Fotos.Select(f => (f.LinhaInicial, Fim: f.LinhaInicial + f.LinhasDaFoto + f.LinhasDoTexto)).Distinct().OrderBy(f => f.LinhaInicial).ToList();
                for (int i = 1; i < inicios.Count; i++)
                {
                    Assert.True(inicios[i].LinhaInicial >= inicios[i - 1].Fim);
                }

                if (pagina.Assinaturas is { } assinaturas)
                {
                    Assert.All(pagina.Fotos, f => Assert.True(f.LinhaInicial + f.LinhasDaFoto + f.LinhasDoTexto <= assinaturas.LinhaInicial));
                }
            }

            Assert.NotNull(plano.Paginas[^1].Assinaturas);
        }
    }

    [Fact]
    public void T10_descricao_longa_tira_fotos_da_pagina_em_vez_de_diminuir_a_letra()
    {
        string longa = string.Join(" ", Enumerable.Repeat("Execução de alvenaria de vedação com blocos cerâmicos e chapisco.", 9));
        var fotos = Enumerable.Range(1, 6).Select(n => Horizontal(n, "Bloco A", longa)).ToList();

        var curta = PaginadorRelatorio.Paginar(fotos, new OpcoesRelatorio { Modo = ModoLegenda.LegendaCurta });
        var completa = PaginadorRelatorio.Paginar(fotos, new OpcoesRelatorio { Modo = ModoLegenda.DescricaoServico });

        Assert.Single(curta.Paginas); // no modo legenda curta a descricao nao entra
        Assert.All(curta.Paginas[0].Fotos, f => Assert.Equal("", f.Descricao));
        Assert.True(completa.Paginas.Count > 1);
        Assert.Contains(completa.Avisos, a => a.Gravidade == Gravidade.Informacao && a.Mensagem.Contains("menos de 6 fotos"));
        Assert.All(completa.Paginas.SelectMany(p => p.Fotos), f =>
        {
            Assert.Equal(longa, f.Descricao); // texto inteiro, nada escondido
            Assert.True(f.LinhasDoTexto >= PaginadorRelatorio.LinhasDe(longa, f.Espaco.Largura));
            Assert.True(f.LinhasDaFoto >= 5);
        });
    }

    [Fact]
    public void Descricao_maior_que_a_pagina_continua_na_seguinte_com_aviso()
    {
        string enorme = string.Join(" ", Enumerable.Repeat("palavra", 1500));
        var plano = PaginadorRelatorio.Paginar([Horizontal(1, "Geral", enorme), Horizontal(2)], new OpcoesRelatorio { FotosPorPagina = 1, Modo = ModoLegenda.DescricaoServico });

        Assert.Contains(plano.Avisos, a => a.Gravidade == Gravidade.PrecisaConferir && a.Mensagem.Contains("continuam na página seguinte"));
        Assert.EndsWith("(continua na próxima página)", plano.Paginas[0].Fotos[0].Descricao);
        var continuacao = Assert.Single(plano.Paginas.SelectMany(p => p.Continuacoes));
        Assert.Equal("Foto 01 — Geral (continuação)", continuacao.Titulo);
        string juntos = plano.Paginas[0].Fotos[0].Descricao.Replace(" (continua na próxima página)", "") + " " + continuacao.Texto;
        Assert.Equal(enorme, juntos); // nenhuma palavra se perde
        Assert.Equal(2, plano.TotalDeFotos);
    }

    [Fact]
    public void T10_assinaturas_no_pe_da_ultima_pagina_ou_em_pagina_propria()
    {
        var cabem = PaginadorRelatorio.Paginar(Fotos(4), new OpcoesRelatorio { Assinaturas = true });
        var naoCabem = PaginadorRelatorio.Paginar(Fotos(6), new OpcoesRelatorio { Assinaturas = true });
        var sem = PaginadorRelatorio.Paginar(Fotos(6), new OpcoesRelatorio());

        Assert.Single(cabem.Paginas);
        Assert.NotNull(cabem.Paginas[0].Assinaturas);
        Assert.Equal(2, naoCabem.Paginas.Count);
        Assert.Empty(naoCabem.Paginas[1].Fotos);
        Assert.NotNull(naoCabem.Paginas[1].Assinaturas);
        Assert.Contains(naoCabem.Avisos, a => a.Mensagem.Contains("página própria"));
        Assert.All(sem.Paginas, p => Assert.Null(p.Assinaturas));
    }

    [Fact]
    public void Legenda_vazia_mostra_so_o_numero_e_sem_fotos_impede()
    {
        var plano = PaginadorRelatorio.Paginar([Horizontal(1)], new OpcoesRelatorio());
        Assert.Equal("Foto 01", plano.Paginas[0].Fotos[0].Titulo);

        Assert.False(PaginadorRelatorio.Paginar([], new OpcoesRelatorio()).PodeGerar);
        Assert.False(PaginadorRelatorio.Paginar([Horizontal(1)], new OpcoesRelatorio { FotosPorPagina = 3 }).PodeGerar);
    }

    [Fact]
    public void Conta_de_linhas_erra_para_mais_nunca_para_menos()
    {
        Assert.Equal(1, PaginadorRelatorio.LinhasDe("curto", 400));
        Assert.Equal(2, PaginadorRelatorio.LinhasDe("linha um\nlinha dois", 400));
        Assert.True(PaginadorRelatorio.LinhasDe(new string('a', 500), 400) >= 500 / 55); // palavra sem espaco tambem quebra
        Assert.True(PaginadorRelatorio.LinhasDe(string.Join(" ", Enumerable.Repeat("alvenaria", 40)), 393) >= 7);
    }
}
