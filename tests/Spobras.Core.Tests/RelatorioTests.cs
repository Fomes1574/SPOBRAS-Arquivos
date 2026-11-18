using Spobras.Core.Relatorio;

namespace Spobras.Core.Tests;

/// <summary>Layout do relatorio fotografico aprovado em 17/11/2026 (modelo-relatorio.pdf e modelo-tela2-e-assinatura.pdf).</summary>
public class RelatorioTests
{
    private static FotoRelatorio Horizontal(int n, string legenda = "", string descricao = "") =>
        new($"foto {n}.jpg", 4000, 3000) { Legenda = legenda, Descricao = descricao };

    private static List<FotoRelatorio> Fotos(int quantidade) => [.. Enumerable.Range(1, quantidade).Select(n => Horizontal(n, $"Serviço {n}"))];

    /// <summary>Pior caso dos limites: 100 caracteres em maiusculas (a letra mais larga) e 300 de descricao.</summary>
    public const string LegendaNoLimite = "EXECUÇÃO DA ARMADURA DO BLOCO DE COROAMENTO B12 COM CONFERÊNCIA DE COBRIMENTO E ESPAÇADORES NA OBRA.";

    public static readonly string DescricaoNoLimite = string.Concat(Enumerable.Repeat("Execução completa da armadura com aço CA-50, conferência de bitolas e cobrimento mínimo. ", 5))[..300];

    private static readonly DadosRelatorio Completo = new()
    {
        Unidade = "CEU Exemplo Ômega – Construção de nova unidade", Contrato = "017/EX/2026", Lote = "CEU Ômega – Lote 003",
        Contratada = "Consórcio Exemplo (VPP/JZ)", DataOs = new DateOnly(2026, 4, 22), Portaria = "Portaria Conjunta nº 04 (2022)",
        Responsavel = "Fulano de Exemplo", ProcessoSei = "7810.2026/0000000-0", Data = new DateOnly(2026, 11, 17),
    };

    /// <summary>Confere tudo que o modelo promete numa pagina pronta.</summary>
    internal static void ConferirPagina(PlanoRelatorio plano, PaginaRelatorio pagina, string onde = "")
    {
        double esquerda = GradeRelatorio.MargemLateral, direita = GradeRelatorio.LarguraDaFolha - GradeRelatorio.MargemLateral;
        foreach (var f in pagina.Fotos)
        {
            // dentro da area util, acima do rodape
            Assert.True(f.Espaco.X >= esquerda - 0.01 && f.Espaco.Direita <= direita + 0.01, $"{onde} foto {f.Numero} sai pela lateral");
            Assert.True(f.Espaco.Y >= pagina.TopoDasFotos - 0.01, $"{onde} foto {f.Numero} invade o cabeçalho");
            Assert.True(f.Espaco.Base <= GradeRelatorio.FimDoConteudo + 0.01, $"{onde} foto {f.Numero} invade o rodapé");

            // quadro igual em todo o relatorio; foto inteira, sem distorcer, centralizada e dentro do quadro
            Assert.Equal(plano.AlturaDoQuadro, f.Quadro.Altura, 3);
            Assert.Equal(GradeRelatorio.LarguraDoQuadro(f.Colunas), f.Quadro.Largura, 3);
            double proporcao = (double)f.Foto.Largura / f.Foto.Altura;
            Assert.True(Math.Abs(f.Imagem.Largura / f.Imagem.Altura / proporcao - 1) < 0.001, $"{onde} foto {f.Numero} distorcida");
            Assert.True(f.Imagem.X >= f.Quadro.X && f.Imagem.Direita <= f.Quadro.Direita + 0.01 && f.Imagem.Y >= f.Quadro.Y && f.Imagem.Base <= f.Quadro.Base + 0.01, $"{onde} foto {f.Numero} sai do quadro");
            Assert.Equal(f.Quadro.X + f.Quadro.Largura / 2, f.Imagem.X + f.Imagem.Largura / 2, 1);
            Assert.Equal(f.Quadro.Y + f.Quadro.Altura / 2, f.Imagem.Y + f.Imagem.Altura / 2, 1);

            // legenda colada no quadro, cada linha cabe na largura, espaco de pelo menos 2 linhas
            Assert.Equal(f.Quadro.Base + GradeRelatorio.RespiroDaLegenda, f.Legenda.Y, 3);
            Assert.True(f.Legenda.Altura >= 2 * GradeRelatorio.LetraDaLegenda * GradeRelatorio.AlturaDaLinhaDaLegenda - 0.01);
            Assert.True(f.LinhasDaLegenda.Count * GradeRelatorio.LetraDaLegenda * GradeRelatorio.AlturaDaLinhaDaLegenda <= f.Legenda.Altura + 0.01);
            Assert.All(f.LinhasDaLegenda, l => Assert.True(TextoMedido.Largura(l, GradeRelatorio.LetraDaLegenda, true) <= f.Quadro.Largura + 0.01, $"{onde} legenda passa da largura: {l}"));
            Assert.All(f.LinhasDaDescricao, l => Assert.True(TextoMedido.Largura(l, GradeRelatorio.LetraDaDescricao, false) <= f.Quadro.Largura + 0.01));
            Assert.True(f.Legenda.Base <= f.Espaco.Base + 0.01 && f.Descricao.Base <= f.Espaco.Base + 0.01, $"{onde} texto da foto {f.Numero} sai do espaço");
        }

        // fotos da mesma linha na mesma altura; linhas sem encostar (4 mm entre elas)
        var faixas = pagina.Fotos.GroupBy(f => f.Faixa).Select(g => g.ToList()).ToList();
        foreach (var faixa in faixas)
        {
            Assert.Single(faixa.Select(f => Math.Round(f.Quadro.Y, 3)).Distinct());
            if (faixa.Count == 2)
            {
                Assert.True(faixa[1].Quadro.X - faixa[0].Quadro.Direita >= GradeRelatorio.EspacoEntreColunas - 0.01);
            }
        }

        for (int i = 1; i < faixas.Count; i++)
        {
            Assert.True(faixas[i][0].Quadro.Y >= faixas[i - 1].Max(f => f.Espaco.Base) + GradeRelatorio.EspacoEntreLinhas - 0.01, $"{onde} linhas de fotos encostam");
        }

        if (pagina.Assinaturas is { } a)
        {
            Assert.NotEmpty(pagina.Fotos); // nunca sozinha numa pagina
            Assert.True(a.Espaco.Y >= pagina.BaseDasFotos + GradeRelatorio.RespiroDasAssinaturas - 0.01, $"{onde} assinatura encosta nas fotos");
            Assert.True(a.Espaco.Base <= GradeRelatorio.FimDoConteudo + 0.01, $"{onde} assinatura invade o rodapé");
        }
    }

    internal static void ConferirPlano(PlanoRelatorio plano, string onde = "")
    {
        foreach (var p in plano.Paginas)
        {
            ConferirPagina(plano, p, onde);
            Assert.Equal(plano.Paginas[0].TopoDasFotos, p.TopoDasFotos, 3); // o mesmo cabecalho em todas as paginas
        }
    }

    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(2, 1, 2)]
    [InlineData(4, 2, 2)]
    [InlineData(6, 2, 3)]
    [InlineData(8, 2, 4)]
    public void Capacidades_1_2_4_6_8_por_pagina(int porPagina, int colunas, int faixas)
    {
        var plano = PaginadorRelatorio.Paginar(Fotos(porPagina * 2 + 1), new OpcoesRelatorio { FotosPorPagina = porPagina }, Completo);

        Assert.True(plano.PodeGerar);
        Assert.Equal([porPagina, porPagina, 1], plano.Paginas.Select(p => p.Fotos.Count));
        Assert.All(plano.Paginas[0].Fotos, f => Assert.Equal(colunas, f.Colunas));
        Assert.Equal(faixas, plano.Paginas[0].Fotos.Select(f => f.Faixa).Distinct().Count());
        Assert.Empty(plano.Avisos);
        ConferirPlano(plano);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(8)]
    public void Pior_caso_dos_limites_cabe_em_todas_as_capacidades_com_e_sem_assinatura(int porPagina)
    {
        foreach (var modo in new[] { ModoLegenda.LegendaCurta, ModoLegenda.DescricaoServico })
        {
            foreach (bool assinaturas in new[] { false, true })
            {
                foreach (int n in new[] { 1, porPagina, porPagina + 1, porPagina * 2, porPagina * 3 })
                {
                    var fotos = Enumerable.Range(1, n).Select(i => new FotoRelatorio($"f{i}.jpg", i % 3 == 0 ? 3000 : 4000, i % 3 == 0 ? 4000 : 3000) { Legenda = LegendaNoLimite, Descricao = DescricaoNoLimite }).ToList();
                    var opcoes = new OpcoesRelatorio { FotosPorPagina = porPagina, Modo = modo, Assinaturas = assinaturas, AssinaturaFiscal = "Fulano", AssinaturaContratada = "Beltrano" };
                    var plano = PaginadorRelatorio.Paginar(fotos, opcoes, Completo with { Descricao = DescricaoNoLimite });
                    string onde = $"{porPagina}/pág {modo} assin={assinaturas} n={n}:";
                    Assert.True(plano.PodeGerar, onde + string.Join(" ", plano.Avisos.Select(a => a.Mensagem)));
                    Assert.Equal(n, plano.TotalDeFotos);
                    Assert.True(plano.AlturaDoQuadro >= GradeRelatorio.MenorQuadro);
                    Assert.All(plano.Paginas, p => Assert.True(p.Fotos.Count <= opcoes.Capacidade));
                    Assert.True(plano.Paginas[0].Fotos.Count == Math.Min(n, opcoes.Capacidade) || assinaturas, onde + " a primeira página não encheu");
                    Assert.Equal(assinaturas, plano.Paginas[^1].Assinaturas is not null);
                    Assert.All(plano.Paginas.Take(plano.Paginas.Count - 1), p => Assert.Null(p.Assinaturas));
                    ConferirPlano(plano, onde);
                }
            }
        }
    }

    [Fact]
    public void Com_descricao_cabem_no_maximo_4_por_pagina()
    {
        Assert.Equal([1, 2, 4], OpcoesRelatorio.CapacidadesDoModo(ModoLegenda.DescricaoServico));
        Assert.Equal([1, 2, 4, 6, 8], OpcoesRelatorio.CapacidadesDoModo(ModoLegenda.LegendaCurta));
        foreach (int escolhida in new[] { 6, 8 })
        {
            var plano = PaginadorRelatorio.Paginar(Fotos(9), new OpcoesRelatorio { FotosPorPagina = escolhida, Modo = ModoLegenda.DescricaoServico });
            Assert.Equal([4, 4, 1], plano.Paginas.Select(p => p.Fotos.Count));
        }
    }

    [Fact]
    public void Cem_fotos_ficam_em_ordem_e_numeradas()
    {
        var plano = PaginadorRelatorio.Paginar(Fotos(100), new OpcoesRelatorio());

        Assert.Equal(17, plano.Paginas.Count); // 16 paginas de 6 + 1 de 4
        var todas = plano.Paginas.SelectMany(p => p.Fotos).ToList();
        Assert.Equal(Enumerable.Range(1, 100), todas.Select(f => f.Numero));
        Assert.Equal("Serviço 1", todas[0].Titulo); // so a legenda: sem "Foto 001" automatico
        Assert.Equal(["Serviço 100"], todas[99].LinhasDaLegenda);
        Assert.Equal(Enumerable.Range(1, 17), plano.Paginas.Select(p => p.Numero));
        ConferirPlano(plano);
    }

    [Theory]
    [InlineData(4000, 3000)] // horizontal
    [InlineData(3000, 4000)] // vertical
    [InlineData(2000, 2000)] // quadrada
    [InlineData(6000, 1000)] // panoramica
    [InlineData(1000, 6000)] // tira em pe
    public void Foto_cabe_no_quadro_sem_distorcer_nem_cortar(int largura, int altura)
    {
        foreach (int porPagina in OpcoesRelatorio.Capacidades)
        {
            var plano = PaginadorRelatorio.Paginar([new FotoRelatorio("f.jpg", largura, altura)], new OpcoesRelatorio { FotosPorPagina = porPagina });
            ConferirPlano(plano);
        }
    }

    [Fact]
    public void Legenda_e_descricao_acima_do_limite_impedem_com_mensagem_clara()
    {
        var longa = PaginadorRelatorio.Paginar([Horizontal(1, new string('a', 101))], new OpcoesRelatorio());
        Assert.False(longa.PodeGerar);
        Assert.Contains(longa.Avisos, a => a.Mensagem == "Foto 01: a legenda tem 101 caracteres; o limite é 100. Encurte para gerar.");

        // a descricao so conta no modo com descricao
        string desc = new('d', 301);
        Assert.True(PaginadorRelatorio.Paginar([Horizontal(1, "ok", desc)], new OpcoesRelatorio()).PodeGerar);
        var comDescricao = PaginadorRelatorio.Paginar([Horizontal(1, "ok", desc)], new OpcoesRelatorio { Modo = ModoLegenda.DescricaoServico });
        Assert.Contains(comDescricao.Avisos, a => a.Mensagem.Contains("descrição do serviço tem 301 caracteres"));

        var geral = PaginadorRelatorio.Paginar([Horizontal(1)], new OpcoesRelatorio(), new DadosRelatorio { Descricao = new string('g', 301) });
        Assert.Contains(geral.Avisos, a => a.Mensagem.Contains("descrição do relatório tem 301"));

        // exatamente no limite, gera
        Assert.True(PaginadorRelatorio.Paginar([Horizontal(1, new string('a', 100), new string('d', 300))], new OpcoesRelatorio { Modo = ModoLegenda.DescricaoServico }).PodeGerar);
    }

    [Fact]
    public void No_modo_so_legenda_a_descricao_guardada_nao_sai()
    {
        var plano = PaginadorRelatorio.Paginar([Horizontal(1, "Fachada", "texto guardado")], new OpcoesRelatorio());
        Assert.Empty(plano.Paginas[0].Fotos[0].LinhasDaDescricao);
    }

    [Fact]
    public void Nenhuma_palavra_se_perde_e_as_quebras_cabem_na_largura()
    {
        string desc = "Concretagem dos blocos B1 a B4 com concreto usinado fck 30 MPa,\nadensamento mecânico e cura úmida por sete dias.";
        var plano = PaginadorRelatorio.Paginar([Horizontal(1, "Concretagem de bloco de fundação com SuperPalavraQueNaoCabeEmLinhaNenhumaDoQuadroDeFotosDoRelatorio", desc)],
            new OpcoesRelatorio { Modo = ModoLegenda.DescricaoServico, FotosPorPagina = 6 });
        var f = plano.Paginas[0].Fotos[0];
        Assert.Equal(f.Titulo.Replace(" ", ""), string.Concat(f.LinhasDaLegenda).Replace(" ", "")); // a palavra enorme e partida, nada some
        Assert.Equal(desc.Replace("\n", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries), f.LinhasDaDescricao.SelectMany(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries)));
        Assert.DoesNotContain(f.LinhasDaDescricao, l => l.Contains('\n')); // a quebra de linha vira espaco: o texto sempre cabe
        ConferirPlano(plano);
    }

    [Fact]
    public void Assinatura_fica_na_pagina_da_ultima_foto_nunca_sozinha()
    {
        var opcoes = new OpcoesRelatorio { Assinaturas = true, AssinaturaFiscal = "Fulano", AssinaturaContratada = "Beltrano" };

        // cabe direto
        var cabem = PaginadorRelatorio.Paginar(Fotos(4), opcoes, Completo);
        Assert.Single(cabem.Paginas);
        Assert.NotNull(cabem.Paginas[0].Assinaturas);

        // 6 fotos numa pagina cheia (o caso que antes ia para uma terceira pagina vazia): as fotos diminuem um pouco
        var cheia = PaginadorRelatorio.Paginar(Fotos(6), opcoes, Completo);
        var semAssinatura = PaginadorRelatorio.Paginar(Fotos(6), new OpcoesRelatorio(), Completo);
        Assert.Single(cheia.Paginas);
        Assert.Equal(6, cheia.Paginas[0].Fotos.Count);
        Assert.True(cheia.AlturaDoQuadro < semAssinatura.AlturaDoQuadro && cheia.AlturaDoQuadro >= semAssinatura.AlturaDoQuadro * 0.75);
        Assert.Contains(cheia.Avisos, a => a.Mensagem.Contains("um pouco menores"));

        // 12 fotos (pedido de 18/11/2026): as paginas continuam cheias e todas as fotos diminuem igual, sem pagina com 2 fotos
        var duas = PaginadorRelatorio.Paginar(Fotos(12), opcoes, Completo);
        Assert.Equal([6, 6], duas.Paginas.Select(p => p.Fotos.Count));
        Assert.Equal(cheia.AlturaDoQuadro, duas.AlturaDoQuadro, 3);
        Assert.NotNull(duas.Paginas[1].Assinaturas);
        Assert.Single(duas.Paginas.SelectMany(p => p.Fotos).Select(f => Math.Round(f.Quadro.Altura, 3)).Distinct());
        Assert.DoesNotContain(duas.Avisos, a => a.Mensagem.Contains("última linha"));

        // 13 fotos: a ultima pagina tem 1 foto e sobra espaco, nada muda
        var treze = PaginadorRelatorio.Paginar(Fotos(13), opcoes, Completo);
        Assert.Equal(semAssinatura.AlturaDoQuadro, treze.AlturaDoQuadro, 3);
        Assert.Equal([6, 6, 1], treze.Paginas.Select(p => p.Fotos.Count));

        Assert.All(PaginadorRelatorio.Paginar(Fotos(6), new OpcoesRelatorio()).Paginas, p => Assert.Null(p.Assinaturas));
        ConferirPlano(cheia);
        ConferirPlano(duas);
    }

    [Fact]
    public void Cabecalho_completo_em_todas_as_paginas_com_rotulo_sem_fundo_e_dado_normal()
    {
        var plano = PaginadorRelatorio.Paginar(Fotos(13), new OpcoesRelatorio(), Completo);
        Assert.All(plano.Paginas, p => Assert.Equal(plano.Paginas[0].TopoDasFotos, p.TopoDasFotos, 3));
        foreach (var pagina in plano.Paginas)
        {
            var desenho = DesenhoRelatorio.Pagina(plano, pagina, temLogo: true);
            var textos = desenho.OfType<TextoDesenho>().ToList();
            Assert.Contains(textos, t => t.Linhas.SequenceEqual(["UNIDADE EDUCACIONAL"]));
            Assert.Contains(textos, t => t.Linhas.SequenceEqual(["017/EX/2026"]) && !t.Negrito); // o dado em letra normal
            Assert.Contains(textos, t => t.Linhas.SequenceEqual(["CONTRATO"]) && t.Negrito);      // o nome do campo em negrito
            Assert.Contains(textos, t => t.Linhas.SequenceEqual(["17 de novembro de 2026"]));
            Assert.DoesNotContain(desenho.OfType<RetanguloDesenho>(), r => r.Fundo is not null && r.Fundo != DesenhoRelatorio.FundoDoQuadro); // sem fundo cinza no cabecalho
        }

        Assert.Equal("CEU Exemplo Ômega – Construção de nova unidade · Relatório fotográfico de 17/11/2026", plano.Cabecalho.Rodape);
    }

    [Fact]
    public void Cabecalho_em_grade_dois_a_dois_e_campo_vazio_nao_aparece()
    {
        var campos = CabecalhoRelatorio.Campos(Completo);
        Assert.Equal(["UNIDADE EDUCACIONAL", "CONTRATO", "CONTRATADA", "PORTARIA", "PROCESSO SEI"], campos.Select(c => c.Rotulo));
        Assert.Equal(["LOTE", "DATA DA OS", "FISCAL"], campos.Where(c => c.Dupla).Select(c => c.Rotulo2!));
        Assert.False(campos[^1].Dupla); // sobrou um: linha inteira
        Assert.Empty(CabecalhoRelatorio.Campos(new DadosRelatorio()));
        var so = CabecalhoRelatorio.Campos(new DadosRelatorio { Contrato = "1", Objeto = "Reforma", Descricao = "Visita" });
        Assert.Equal(["CONTRATO", "OBJETO", "DESCRIÇÃO"], so.Select(c => c.Rotulo));
    }

    [Fact]
    public void Legenda_vazia_guarda_o_mesmo_espaco_e_sem_fotos_impede()
    {
        var plano = PaginadorRelatorio.Paginar([Horizontal(1), Horizontal(2, "Fachada")], new OpcoesRelatorio { FotosPorPagina = 4 });
        var a = plano.Paginas[0].Fotos[0];
        var b = plano.Paginas[0].Fotos[1];
        Assert.Empty(a.LinhasDaLegenda);
        Assert.Equal(b.Legenda.Altura, a.Legenda.Altura, 3);

        Assert.False(PaginadorRelatorio.Paginar([], new OpcoesRelatorio()).PodeGerar);
        Assert.False(PaginadorRelatorio.Paginar([Horizontal(1)], new OpcoesRelatorio { FotosPorPagina = 3 }).PodeGerar);
    }

    [Fact]
    public void Foto_sem_legenda_ou_sem_descricao_avisa_sem_impedir()
    {
        var fotos = new List<FotoRelatorio> { Horizontal(1, "Fachada", "Pintura"), Horizontal(2), Horizontal(3, "Pátio"), Horizontal(4) };
        var curta = PaginadorRelatorio.Paginar(fotos, new OpcoesRelatorio());
        Assert.True(curta.PodeGerar);
        Assert.Equal(["2 fotos estão sem legenda: 02 e 04."], curta.Avisos.Select(a => a.Mensagem));
        Assert.All(curta.Avisos, a => Assert.Equal(Gravidade.PrecisaConferir, a.Gravidade));

        var completa = PaginadorRelatorio.Paginar(fotos, new OpcoesRelatorio { Modo = ModoLegenda.DescricaoServico });
        Assert.Contains("3 fotos estão sem descrição do serviço: 02, 03 e 04.", completa.Avisos.Select(a => a.Mensagem));

        var uma = PaginadorRelatorio.Paginar([Horizontal(1)], new OpcoesRelatorio());
        Assert.Equal(["A foto 01 está sem legenda."], uma.Avisos.Select(a => a.Mensagem));
        var muitas = PaginadorRelatorio.Paginar([.. Enumerable.Range(1, 9).Select(n => Horizontal(n))], new OpcoesRelatorio());
        Assert.Equal(["9 fotos estão sem legenda: 01, 02, 03, 04, 05, 06 e mais 3."], muitas.Avisos.Select(a => a.Mensagem));
    }

    [Fact]
    public void Enter_emoji_e_outros_alfabetos_nao_quebram_o_layout_e_sao_avisados()
    {
        // 150 Enters numa descricao de 300 caracteres: antes virava 150 linhas e travava a geracao
        var enters = PaginadorRelatorio.Paginar([Horizontal(1, "linha\num\n\n\n\ndois", string.Concat(Enumerable.Repeat("a\n", 150)))],
            new OpcoesRelatorio { Modo = ModoLegenda.DescricaoServico, FotosPorPagina = 4 });
        Assert.True(enters.PodeGerar);
        Assert.Equal(["linha um dois"], enters.Paginas[0].Fotos[0].LinhasDaLegenda);

        var simbolos = PaginadorRelatorio.Paginar([Horizontal(1, "Fachada 👍 pronta ✅"), Horizontal(2, "Pátio — “aspas” … ½ ° º ª"), Horizontal(3, "中文 ok\u200b\u0007")], new OpcoesRelatorio());
        var fotos = simbolos.Paginas[0].Fotos;
        Assert.Equal("Fachada pronta", fotos[0].Titulo);
        Assert.Equal("Pátio — “aspas” … ½ ° º ª", fotos[1].Titulo); // sinais do portugues ficam
        Assert.Equal("ok", fotos[2].Titulo);
        Assert.Contains(simbolos.Avisos, a => a.Mensagem.StartsWith("Foto 01: 👍 ✅ não existe(m) na fonte"));
        Assert.Contains(simbolos.Avisos, a => a.Mensagem.StartsWith("Foto 03: 中 文"));
        Assert.DoesNotContain(simbolos.Avisos, a => a.Mensagem.StartsWith("Foto 02"));
        Assert.All(fotos.SelectMany(f => f.LinhasDaLegenda).SelectMany(l => l), c => Assert.True(TextoMedido.TemDesenho(c)));
        Assert.Equal("EMEF", CabecalhoRelatorio.Campos(new DadosRelatorio { Unidade = "EMEF 🏫" })[0].Valor);
    }

    [Fact]
    public void Quebra_de_texto_mede_como_a_fonte()
    {
        Assert.Equal(["curto"], TextoMedido.Quebrar("curto", 400, 10, false));
        Assert.Equal(["linha um", "linha dois"], TextoMedido.Quebrar("linha um\nlinha dois", 400, 10, false));
        var partida = TextoMedido.Quebrar(new string('a', 500), 100, 10, false);
        Assert.True(partida.Count > 5);
        Assert.Equal(new string('a', 500), string.Concat(partida));
        Assert.All(partida, l => Assert.True(TextoMedido.Largura(l, 10, false) <= 100));
        Assert.True(TextoMedido.Largura("MMMM", 10, true) > TextoMedido.Largura("iiii", 10, true));
        Assert.Equal(1000, TextoMedido.Milesimos('中', false)); // fora da tabela conta como o mais largo
    }
}
