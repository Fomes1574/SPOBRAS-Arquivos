using Spobras.Core.Relatorio;

namespace Spobras.Core.Tests;

/// <summary>Carimbo nas fotos (conta da posicao e do tamanho) e regras do endereco pelo GPS.</summary>
public sealed class CarimboEnderecoTests
{
    private static readonly DateTime Captura = new(2026, 11, 18, 14, 32, 0);
    private static readonly AjusteCarimbo Ligado = new() { Ligado = true };

    private static CarimboFoto Carimbo(AjusteCarimbo? a = null, string escola = "EMEF Escola Exemplo", string endereco = "Rua Exemplo, 123 – Vila Exemplo") =>
        CarimboFoto.Montar(a ?? Ligado, escola, endereco, Captura)!;

    [Fact]
    public void Montar_so_com_o_carimbo_ligado_e_algum_texto()
    {
        Assert.Null(CarimboFoto.Montar(null, "Escola", "Rua", Captura));
        Assert.Null(CarimboFoto.Montar(new AjusteCarimbo(), "Escola", "Rua", Captura));
        Assert.Null(CarimboFoto.Montar(Ligado, "", "", null));
        Assert.Null(CarimboFoto.Montar(Ligado with { Escola = false, Endereco = false, Data = false, Hora = false }, "Escola", "Rua", Captura));
        var c = Carimbo();
        Assert.Equal("EMEF Escola Exemplo", c.Escola);
        Assert.Equal("18/11/2026 14:32", c.DataHora);
        Assert.Equal("18/11/2026", Carimbo(Ligado with { Hora = false }).DataHora);
        Assert.Equal("14:32", Carimbo(Ligado with { Data = false }).DataHora);
        Assert.Equal("", Carimbo(Ligado with { Endereco = false }).Endereco);
        // emoji e quebra de linha nao vao para a foto
        Assert.Equal("Rua A, 1", CarimboFoto.Montar(Ligado, "", "Rua A,\n1 👍", null)!.Endereco);
    }

    public static TheoryData<int, int, CantoCarimbo, double> Casos()
    {
        var dados = new TheoryData<int, int, CantoCarimbo, double>();
        foreach (var (l, a) in new[] { (1600, 1200), (1200, 1600), (1600, 900), (900, 1600), (1200, 1200), (2400, 600), (300, 2000) })
        {
            foreach (var canto in Enum.GetValues<CantoCarimbo>())
            {
                foreach (double t in new[] { AjusteCarimbo.TamanhoMinimo, AjusteCarimbo.TamanhoPadrao, AjusteCarimbo.TamanhoMaximo })
                {
                    dados.Add(l, a, canto, t);
                }
            }
        }

        return dados;
    }

    [Theory]
    [MemberData(nameof(Casos))]
    public void A_faixa_fica_sempre_dentro_da_foto_com_margem_e_no_maximo_60_por_cento_da_largura(int largura, int altura, CantoCarimbo canto, double tamanho)
    {
        var c = Carimbo(Ligado with { Canto = canto, Tamanho = tamanho, DistanciaX = 0.9, DistanciaY = 0.9 },
            "Escola Municipal de Ensino Fundamental com um nome bem comprido para testar o corte do texto",
            "Avenida Professor Doutor Fulano de Tal Beltrano Sicrano, 12345 – Conjunto Habitacional Jardim das Flores do Parque");
        var g = GeometriaCarimbo.Calcular(largura, altura, c)!;
        double margem = AjusteCarimbo.Margem * Math.Min(largura, altura);
        Assert.True(g.X >= margem - 0.01 && g.Y >= margem - 0.01, $"saiu pela borda: {g}");
        Assert.True(g.X + g.Largura <= largura - margem + 0.01 && g.Y + g.Altura <= altura - margem + 0.01, $"saiu pela borda: {g}");
        Assert.True(g.Largura <= GeometriaCarimbo.LarguraMaxima * largura + 0.5, "largura demais");
        Assert.True(g.Altura <= GeometriaCarimbo.AlturaMaxima * altura + 0.5 || g.Altura <= altura - 2 * margem, "altura demais");
        Assert.InRange(g.Linhas.Count, 2, 4); // escola, endereco em ate 2 linhas, data
        foreach (var linha in g.Linhas)
        {
            double fim = linha.X + TextoMedido.Largura(linha.Texto, g.Letra, linha.Negrito);
            Assert.True(fim <= g.X + g.Largura + 0.5, $"texto passa da faixa: {linha.Texto}");
            Assert.True(linha.Base <= g.Y + g.Altura, $"linha abaixo da faixa: {linha.Texto}");
        }
    }

    [Fact]
    public void Endereco_longo_quebra_em_duas_linhas_e_termina_em_reticencias()
    {
        var c = Carimbo(endereco: string.Join(" ", Enumerable.Repeat("Avenida Comprida", 20)));
        var g = GeometriaCarimbo.Calcular(1600, 1200, c)!;
        var endereco = g.Linhas.Where(l => !l.Negrito && l.Texto != c.DataHora).ToList();
        Assert.Equal(2, endereco.Count);
        Assert.EndsWith("…", endereco[1].Texto);
        Assert.True(g.Linhas[0].Negrito); // escola em negrito, primeira linha
        Assert.Equal(c.DataHora, g.Linhas[^1].Texto);
    }

    [Fact]
    public void Fica_no_mesmo_lugar_visual_em_pe_e_deitada()
    {
        var c = Carimbo(Ligado with { Canto = CantoCarimbo.SuperiorDireito, DistanciaX = 0.05, DistanciaY = 0.08, Tamanho = 0.04 });
        var deitada = GeometriaCarimbo.Calcular(4000, 3000, c)!;
        var emPe = GeometriaCarimbo.Calcular(3000, 4000, c)!;
        // mesma letra (fracao do lado menor) e mesma distancia ate o canto de cima a direita
        Assert.Equal(deitada.Letra, emPe.Letra, 3);
        Assert.Equal(4000 - deitada.X - deitada.Largura, 3000 - emPe.X - emPe.Largura, 1);
        Assert.Equal(deitada.Y, emPe.Y, 1);
        Assert.Equal(0.05 * 3000, 4000 - deitada.X - deitada.Largura, 1);
    }

    [Theory]
    [InlineData(100, 100, CantoCarimbo.SuperiorEsquerdo)]
    [InlineData(1300, 80, CantoCarimbo.SuperiorDireito)]
    [InlineData(60, 900, CantoCarimbo.InferiorEsquerdo)]
    [InlineData(1100, 1000, CantoCarimbo.InferiorDireito)]
    public void Soltar_gruda_no_canto_mais_perto_e_a_conta_volta_ao_mesmo_lugar(double x, double y, CantoCarimbo esperado)
    {
        var c = Carimbo();
        var g0 = GeometriaCarimbo.Calcular(1600, 1200, c)!;
        x = Math.Min(x, 1600 - g0.Largura - 30);
        y = Math.Min(y, 1200 - g0.Altura - 30);
        var ajuste = Ligado.Soltar(x, y, g0.Largura, g0.Altura, 1600, 1200);
        Assert.Equal(esperado, ajuste.Canto);
        var g = GeometriaCarimbo.Calcular(1600, 1200, c with { Ajuste = ajuste })!;
        Assert.Equal(x, g.X, 0.5);
        Assert.Equal(y, g.Y, 0.5);
    }

    [Fact]
    public void Soltar_fora_da_foto_volta_para_dentro_e_valores_estranhos_viram_padrao()
    {
        var a = Ligado.Soltar(-500, -500, 300, 100, 1600, 1200);
        Assert.Equal(AjusteCarimbo.Margem, a.DistanciaX);
        var estranho = (Ligado with { Tamanho = double.NaN, DistanciaX = -3, Canto = (CantoCarimbo)99 }).Normalizado();
        Assert.Equal(AjusteCarimbo.TamanhoPadrao, estranho.Tamanho);
        Assert.Equal(AjusteCarimbo.Margem, estranho.DistanciaX);
        Assert.Equal(CantoCarimbo.InferiorEsquerdo, estranho.Canto);
        Assert.Equal(AjusteCarimbo.TamanhoMaximo, (Ligado with { Tamanho = 5 }).Normalizado().Tamanho);
        var padrao = (Ligado with { Canto = CantoCarimbo.SuperiorDireito, Tamanho = 0.07, Escola = false }).Padrao();
        Assert.Equal(CantoCarimbo.InferiorEsquerdo, padrao.Canto);
        Assert.False(padrao.Escola); // padrao so volta posicao e tamanho
    }

    [Fact]
    public void Assinatura_muda_com_qualquer_coisa_do_carimbo()
    {
        var c = Carimbo();
        var variacoes = new[]
        {
            c with { Escola = "Outra" }, c with { Endereco = "Outra rua" }, c with { DataHora = "19/11/2026" },
            c with { Ajuste = c.Ajuste with { Canto = CantoCarimbo.SuperiorDireito } }, c with { Ajuste = c.Ajuste with { DistanciaX = 0.1 } },
            c with { Ajuste = c.Ajuste with { DistanciaY = 0.1 } }, c with { Ajuste = c.Ajuste with { Tamanho = 0.07 } },
        };
        var todas = variacoes.Select(v => v.Assinatura).Append(c.Assinatura).ToList();
        Assert.Equal(todas.Count, todas.Distinct().Count());
        Assert.Equal(c.Assinatura, Carimbo().Assinatura);
        Assert.Matches("^[0-9a-f]{10}$", c.Assinatura);
    }

    [Theory]
    [InlineData(1, 6)]
    [InlineData(2, 6)]
    [InlineData(4, 6)]
    [InlineData(6, 6)]
    public void No_tamanho_padrao_a_letra_no_papel_fica_legivel_ate_6_por_pagina(int porPagina, int fotos)
    {
        var lista = Enumerable.Range(0, fotos).Select(i => new FotoRelatorio($"f{i}.jpg", i % 2 == 0 ? 1600 : 1200, i % 2 == 0 ? 1200 : 1600)
        {
            Legenda = "Legenda", Carimbo = Carimbo(),
        }).ToList();
        // cabecalho completo (o que deixa as fotos menores), com textos de tamanho comum
        var dados = new DadosRelatorio
        {
            Unidade = "EMEF Escola Exemplo do Jardim das Flores", Contratada = "Construtora Exemplo Engenharia Ltda", Contrato = "001/EX/2026",
            Lote = "LOTE 21", Portaria = "PORTARIA EXEMPLO Nº 06 (2022)", ProcessoSei = "0000.2026/0000000-0", Responsavel = "Fiscal Exemplo",
            Objeto = "Reforma da cobertura, pintura e acessibilidade da unidade escolar.", Descricao = "Visita de acompanhamento da obra.",
            Data = new DateOnly(2026, 11, 18), DataOs = new DateOnly(2026, 4, 17),
        };
        var plano = PaginadorRelatorio.Paginar(lista, new OpcoesRelatorio { FotosPorPagina = porPagina, Assinaturas = true }, dados);
        var letras = plano.Paginas.SelectMany(p => p.Fotos).Select(GeometriaCarimbo.LetraNoPapel).ToList();
        Assert.All(letras, l => Assert.True(l >= GeometriaCarimbo.MenorLetraNoPapel, $"letra de {l:0.0} pt com {porPagina} por página"));
    }

    // ---- Endereco ----

    [Fact]
    public void Chave_da_escola_ignora_acento_espaco_e_traco_mas_separa_pelo_contrato()
    {
        Assert.Equal("emef escola gemea do exemplo", EnderecoDasFotos.Normalizar("EMEF  - Escola  Gêmea  do Exemplo"));
        Assert.Equal(EnderecoDasFotos.Chave("EMEF  - Escola Gemea", "039/EX/2023"), EnderecoDasFotos.Chave("emef escola gêmea", "039/ex/2023"));
        Assert.NotEqual(EnderecoDasFotos.Chave("EMEF Escola Gêmea", "039/EX/2023"), EnderecoDasFotos.Chave("EMEF Escola Gêmea", "216/EX/2023"));
        Assert.Equal(EnderecoDasFotos.NomeDaChave(EnderecoDasFotos.Chave("EMEF Escola Gêmea", "039/EX/2023")!),
            EnderecoDasFotos.NomeDaChave(EnderecoDasFotos.Chave("EMEF - Escola Gemea", "216/EX/2023")!));
        Assert.Null(EnderecoDasFotos.Chave("  ", "039/EX/2023"));
        Assert.Equal("emef exemplo|", EnderecoDasFotos.Chave("EMEF Exemplo", null));
    }

    [Theory]
    [InlineData("Rua Exemplo", "123", "Vila Exemplo", "São Paulo", "Rua Exemplo, 123 – Vila Exemplo")]
    [InlineData("Rua Exemplo", "", "Vila Exemplo", "São Paulo", "Rua Exemplo – Vila Exemplo")]
    [InlineData("Rua Exemplo", "100-198", "", "São Paulo", "Rua Exemplo")]
    [InlineData("", "", "Vila Exemplo", "São Paulo", "Vila Exemplo")]
    [InlineData(null, null, null, "São Paulo", "São Paulo")]
    [InlineData(" ", null, " ", null, null)]
    public void Endereco_curto_para_leitura(string? rua, string? numero, string? bairro, string? cidade, string? esperado) =>
        Assert.Equal(esperado, EnderecoDasFotos.Formatar(rua, numero, bairro, cidade));

    [Fact]
    public void Aviso_de_distancia_considera_300_m_mais_a_margem_de_erro()
    {
        var escola = new PontoGps(-23.5505, -46.6333, 15, DateTimeOffset.Now);
        double metroDeLatitude = 1 / 111_195.0;
        PontoGps Ao(double metros, double precisao) => escola with { Latitude = escola.Latitude + metros * metroDeLatitude, Precisao = precisao };
        Assert.Equal(1000, EnderecoDasFotos.Distancia(escola, Ao(1000, 10)), 1);
        Assert.False(EnderecoDasFotos.Longe(escola, Ao(250, 10)));  // andando pelo terreno de um CEU
        Assert.False(EnderecoDasFotos.Longe(escola, Ao(380, 80)));  // sinal fraco: a margem cobre
        Assert.True(EnderecoDasFotos.Longe(escola, Ao(400, 10)));
        Assert.True(EnderecoDasFotos.Longe(escola, Ao(1200, 20)));
        Assert.Equal("350 m", EnderecoDasFotos.TextoDistancia(347));
        Assert.Equal("1,2 km", EnderecoDasFotos.TextoDistancia(1234));
        Assert.Equal("15 km", EnderecoDasFotos.TextoDistancia(15_300));
        Assert.False(new PontoGps(double.NaN, 0, 1, DateTimeOffset.Now).Valido);
        Assert.False(new PontoGps(91, 0, 1, DateTimeOffset.Now).Valido);
    }
}
