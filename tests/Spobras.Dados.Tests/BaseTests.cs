using Spobras.Core.Base;
using Spobras.Dados.Base;
using Spobras.Testes;

namespace Spobras.Dados.Tests;

public class BaseTests : IDisposable
{
    private readonly string _pasta = Directory.CreateTempSubdirectory("spobras-base-").FullName;
    private readonly BaseRepositorio _base;

    public BaseTests()
    {
        _base = new BaseRepositorio(new BancoApp(Path.Combine(_pasta, "app.db")));
        _base.GravarMestra(ImportadorMestra.Importar(BaseSintetica.Mestra()), Fonte(Fontes.Mestra, 1));
    }

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    private static FonteInfo Fonte(string id, int versao, EstadoFonte estado = EstadoFonte.Atualizada, string mensagem = "") => new()
    {
        Id = id, Nome = id, Caminho = @"C:\exemplo\" + id + ".xlsx", Estado = estado, Mensagem = mensagem, Versao = versao,
        UltimaLeitura = new DateTimeOffset(2026, 11, 11, 10, 0, 0, TimeSpan.Zero), Linhas = 5,
    };

    private static FiltroObras Saldo(decimal? min, decimal? max, bool semInformacao = false) =>
        new() { Valores = new Dictionary<CampoValor, FaixaValor> { [CampoValor.SaldoAPagar] = new(min, max, semInformacao) } };

    private List<string> Escolas(FiltroObras f) => [.. _base.Buscar(f, ordem: OrdemObras.Escola).Obras.Select(o => $"{o.Contrato}:{o.Empreendimento}")];

    // ---- Importacao ----

    [Fact]
    public void Sugestoes_por_escola_mostram_os_contratos_e_ordenam_contratos_por_ano()
    {
        var escolas = ProcuraObras.SugestoesDeEscola(_base, "alfa");
        Assert.Equal(new ProcuraObras.Sugestao("EMEF Alfa", "2 contratos"), Assert.Single(escolas));
        Assert.Empty(ProcuraObras.SugestoesDeEscola(_base, "a"));
        Assert.Equal(["003/EX/2025", "001/EX/2024"],
            ProcuraObras.SugestoesDeContrato(_base, "/EX/202").Select(o => o.Valor).Where(o => o is "003/EX/2025" or "001/EX/2024"));
    }

    [Fact]
    public void Importa_obras_com_codigos_como_texto_dinheiro_exato_e_dotacao_padronizada()
    {
        var todas = _base.Buscar(new FiltroObras());
        Assert.Equal(5, todas.Total);

        var alfa = todas.Obras.First(o => o.Contrato == "001/EX/2024" && o.Empreendimento == "EMEF Alfa");
        Assert.Equal(10000.13m, alfa.ValorEmpreendimento); // 10000.129999 do Excel vira centavos exatos
        Assert.Equal(1500m, alfa.SaldoAPagar);
        Assert.Equal(new DateOnly(2026, 1, 10), alfa.DataOs);
        Assert.Equal("16.10.12.365.3010.2878.3390.3900.00", alfa.Dotacao); // espaco virou ponto
        Assert.Equal("2878", alfa.Atividade);
        Assert.Equal("015/EX/2025", alfa.ContratoReceita); // veio das medicoes do contrato
        Assert.Equal("7910.2024/0000001-0", alfa.ProcessoSei);

        var gama = todas.Obras.First(o => o.Empreendimento == "EMEI Gama");
        Assert.Null(gama.SaldoAPagar); // ausente nao e zero
        Assert.Null(gama.DataOs);
        Assert.Null(gama.ContratoReceita);
        Assert.Equal(0m, todas.Obras.First(o => o.Empreendimento == "CEI Beta").SaldoAPagar);
    }

    [Fact]
    public void Selecoes_no_mesmo_filtro_somam_e_filtros_diferentes_se_combinam()
    {
        var filtro = new FiltroObras
        {
            Contrato = ["001/EX/2024", "004/EX/2025"],
            ContratoReceita = ["015/EX/2025", "128/EX/2021"],
        };
        var resultado = _base.Buscar(filtro).Obras;
        Assert.Contains(resultado, o => o.Contrato == "001/EX/2024");
        Assert.Contains(resultado, o => o.Contrato == "004/EX/2025");
        Assert.Equal(["004/EX/2025:CEU Delta"], Escolas(filtro with { Escola = ["CEU Delta"] }));
        Assert.Empty(_base.Buscar(filtro with { ContratoReceita = ["15/EX/2025"] }).Obras);
    }

    [Fact]
    public void T16_tabela_ou_coluna_que_sumiu_recusa_a_importacao_e_mantem_a_base_anterior()
    {
        var semTabela = BaseSintetica.Mestra();
        semTabela.TabelasPorNome.Remove(ImportadorMestra.TabelaEmpenhos);
        var erroTabela = Assert.Throws<ImportacaoInvalidaException>(() => ImportadorMestra.Importar(semTabela));
        Assert.Contains("contratos_VW_EMPENHOS_SOF", erroTabela.Message);

        var semColuna = BaseSintetica.Mestra();
        var t = semColuna.TabelasPorNome[ImportadorMestra.TabelaObras];
        semColuna.Tabela(t.Nome, [.. t.Colunas.Select(c => c == "SALDO A PAGAR" ? "SALDO PAGAR NOVO" : c == "PORTARIA" ? "PORT" : c)], [.. t.Linhas]);
        var erroColuna = Assert.Throws<ImportacaoInvalidaException>(() => ImportadorMestra.Importar(semColuna));
        Assert.Contains("\"SALDO A PAGAR\"", erroColuna.Message);
        Assert.Contains("\"PORTARIA\"", erroColuna.Message);
        Assert.Contains("A base anterior foi mantida", erroColuna.Message);

        var vazia = BaseSintetica.Mestra();
        vazia.Tabela(t.Nome, [.. t.Colunas]);
        Assert.Throws<ImportacaoInvalidaException>(() => ImportadorMestra.Importar(vazia));

        Assert.Equal(5, _base.TotalDeObras()); // nada mudou na base
    }

    [Fact]
    public void T16_nova_importacao_substitui_tudo_de_uma_vez_e_falha_de_leitura_so_muda_o_estado()
    {
        var nova = BaseSintetica.Mestra();
        var t = nova.TabelasPorNome[ImportadorMestra.TabelaObras];
        nova.Tabela(t.Nome, [.. t.Colunas], [.. t.Linhas.Take(2), BaseSintetica.Obra("005/EX/2026", "EMEF Nova", BaseSintetica.P6, null, 10m)]);
        _base.GravarMestra(ImportadorMestra.Importar(nova), Fonte(Fontes.Mestra, 2));

        Assert.Equal(3, _base.TotalDeObras());
        Assert.Contains("005/EX/2026:EMEF Nova", Escolas(new FiltroObras()));
        Assert.Equal(2, _base.Fonte(Fontes.Mestra)!.Versao);

        // Planilha ocupada: os dados ficam, o estado avisa que nao e leitura atual.
        _base.GravarFonte(Fonte(Fontes.Mestra, 2, EstadoFonte.Desatualizada, "A planilha está aberta em outro programa."));
        _base.RegistrarImportacao(Fontes.Mestra, DateTimeOffset.UtcNow, false, "A planilha está aberta em outro programa.", 0, 12);

        Assert.Equal(3, _base.TotalDeObras());
        var fonte = _base.Fonte(Fontes.Mestra)!;
        Assert.Equal(EstadoFonte.Desatualizada, fonte.Estado);
        Assert.Equal(new DateTimeOffset(2026, 11, 11, 10, 0, 0, TimeSpan.Zero), fonte.UltimaLeitura); // data da ultima leitura valida
        Assert.False(_base.HistoricoDeImportacoes(Fontes.Mestra)[0].Ok);
    }

    [Fact]
    public void Linhas_sem_chave_ou_repetidas_viram_aviso()
    {
        var planilha = BaseSintetica.Mestra();
        var t = planilha.TabelasPorNome[ImportadorMestra.TabelaObras];
        planilha.Tabela(t.Nome, [.. t.Colunas], [.. t.Linhas, t.Linhas[0], BaseSintetica.Obra("", "Sem contrato", BaseSintetica.P6, null, 1m)]);

        var importada = ImportadorMestra.Importar(planilha);

        Assert.Equal(5, importada.Obras.Count);
        Assert.Contains(importada.Avisos, a => a.StartsWith("1 linha(s) sem contrato"));
        Assert.Contains(importada.Avisos, a => a.StartsWith("1 linha(s) repetiam"));
        Assert.Contains(importada.Avisos, a => a.Contains("espaço no lugar do ponto"));
    }

    // ---- Filtros vinculados ----

    [Fact]
    public void T27_comecar_por_portaria_contrato_ou_escola_mostra_so_as_opcoes_compativeis()
    {
        var porPortaria = _base.Opcoes(new FiltroObras { Portaria = [BaseSintetica.P3De2021] });
        Assert.Equal(["001/EX/2024", "002/EX/2024"], porPortaria.Contratos);
        Assert.Equal(["CEI Beta", "EMEF Alfa", "EMEI Gama"], porPortaria.Escolas);
        Assert.Equal(3, porPortaria.Portarias.Count); // o proprio campo continua com todas, para poder trocar

        var porContrato = _base.Opcoes(new FiltroObras { Contrato = ["001/EX/2024"] });
        Assert.Equal(["CEI Beta", "EMEF Alfa"], porContrato.Escolas);
        Assert.Equal([BaseSintetica.P3De2021], porContrato.Portarias);

        var porEscola = _base.Opcoes(new FiltroObras { Escola = ["EMEF Alfa"] });
        Assert.Equal(["001/EX/2024", "003/EX/2025"], porEscola.Contratos);
        Assert.Equal([BaseSintetica.P3De2021, BaseSintetica.P3De2025], porEscola.Portarias); // duas "Portaria 3", distinguidas pelo ano
    }

    [Fact]
    public void T27_a_ordem_de_selecao_nao_muda_o_resultado_e_limpar_um_filtro_amplia_as_opcoes()
    {
        var a = new FiltroObras { Portaria = [BaseSintetica.P3De2021] } with { Escola = ["EMEF Alfa"] };
        var b = new FiltroObras { Escola = ["EMEF Alfa"] } with { Portaria = [BaseSintetica.P3De2021] };
        Assert.Equal(Escolas(a), Escolas(b));
        Assert.Equal(["001/EX/2024:EMEF Alfa"], Escolas(a));
        Assert.Equal(_base.Opcoes(a).Contratos, _base.Opcoes(b).Contratos);

        // Limpar a portaria: a escola Alfa volta a ter os dois contratos.
        Assert.Equal(["001/EX/2024", "003/EX/2025"], _base.Opcoes(a with { Portaria = [] }).Contratos);
        Assert.Equal(2, _base.Buscar(a with { Portaria = [] }).Total);
    }

    [Fact]
    public void T28_combinacao_portaria_e_contrato_devolve_so_as_escolas_daquele_vinculo_sem_duplicar()
    {
        var f = new FiltroObras { Portaria = [BaseSintetica.P3De2021], Contrato = ["001/EX/2024"] };

        Assert.Equal(["001/EX/2024:CEI Beta", "001/EX/2024:EMEF Alfa"], Escolas(f));
        // Tres medicoes no contrato nao triplicam a obra nem as opcoes.
        Assert.Equal(2, _base.Buscar(f).Total);
        Assert.Equal(["CEI Beta", "EMEF Alfa"], _base.Opcoes(f).Escolas);
        Assert.Equal(3, _base.Medicoes("001/EX/2024").Count);
        Assert.Empty(_base.Buscar(new FiltroObras { Portaria = [BaseSintetica.P3De2025], Contrato = ["001/EX/2024"] }).Obras);
    }

    [Fact]
    public void T29_contrato_de_receita_dotacao_escola_e_faixa_de_saldo_em_qualquer_ordem()
    {
        var f1 = new FiltroObras { ContratoReceita = ["015/EX/2025"] };
        Assert.Equal(["001/EX/2024:CEI Beta", "004/EX/2025:CEU Delta", "001/EX/2024:EMEF Alfa"], Escolas(f1)); // inclui o contrato com dois de receita
        Assert.Equal(["015/EX/2025", "016/EX/2026", "128/EX/2021"], _base.Opcoes(new FiltroObras()).ContratosReceita);

        var combinado = f1 with { Dotacao = ["2878"] } with { Valores = Saldo(1000m, null).Valores };
        var outraOrdem = Saldo(1000m, null) with { Dotacao = ["2878"] } with { ContratoReceita = ["015/EX/2025"] };
        Assert.Equal(["001/EX/2024:EMEF Alfa"], Escolas(combinado));
        Assert.Equal(Escolas(combinado), Escolas(outraOrdem));

        // Limites inclusivos; zero e ausente sao coisas diferentes.
        Assert.Equal(["004/EX/2025:CEU Delta", "001/EX/2024:EMEF Alfa"], Escolas(Saldo(1000m, 1500m)));
        Assert.Equal(["003/EX/2025:EMEF Alfa"], Escolas(Saldo(999.99m, 999.99m))); // valor exato
        Assert.Equal(["001/EX/2024:CEI Beta"], Escolas(Saldo(0m, 0m)));
        Assert.Equal(["002/EX/2024:EMEI Gama"], Escolas(Saldo(null, null, semInformacao: true)));
        Assert.Equal(4, _base.Buscar(Saldo(0m, null)).Total); // "a partir de zero" nao inclui o ausente
        Assert.Equal(5, _base.Buscar(Saldo(0m, null, semInformacao: true)).Total);

        // Varias medicoes nao duplicam o total do contrato: os valores sao os da obra.
        Assert.Equal(1500m + 0m, _base.Buscar(new FiltroObras { Contrato = ["001/EX/2024"] }).Obras.Sum(o => o.SaldoAPagar));
    }

    [Fact]
    public void T31_data_da_os_por_periodo_com_limites_e_sem_data()
    {
        var periodo = new FiltroObras { OsDe = new DateOnly(2026, 1, 10), OsAte = new DateOnly(2026, 3, 1) };
        Assert.Equal(["001/EX/2024:CEI Beta", "001/EX/2024:EMEF Alfa"], Escolas(periodo)); // as duas pontas entram

        // Duas obras do mesmo contrato com datas diferentes: cada data fica na sua obra.
        Assert.Equal(["001/EX/2024:EMEF Alfa"], Escolas(new FiltroObras { Contrato = ["001/EX/2024"], OsAte = new DateOnly(2026, 1, 31) }));
        Assert.Equal(["001/EX/2024:EMEF Alfa"], Escolas(new FiltroObras { OsDe = new DateOnly(2026, 1, 10), OsAte = new DateOnly(2026, 1, 10) }));

        Assert.Equal(["002/EX/2024:EMEI Gama"], Escolas(new FiltroObras { SemDataOs = true }));
        Assert.Equal(3, _base.Buscar(periodo with { SemDataOs = true }).Total);

        // Combinada com portaria e saldo.
        var combinado = new FiltroObras { Portaria = [BaseSintetica.P3De2021], OsDe = new DateOnly(2026, 1, 1), OsAte = new DateOnly(2026, 12, 31) } with { Valores = Saldo(1000m, null).Valores };
        Assert.Equal(["001/EX/2024:EMEF Alfa"], Escolas(combinado));

        var porData = _base.Buscar(new FiltroObras(), ordem: OrdemObras.DataOs).Obras.Select(o => o.DataOs).ToList();
        Assert.Equal(new DateOnly(2025, 6, 15), porData[0]);
        Assert.Null(porData[^1]); // sem data vai para o fim
    }

    [Fact]
    public void Busca_por_texto_ignora_acentos_e_maiusculas_e_pagina()
    {
        Assert.Equal(2, _base.Buscar(new FiltroObras { Texto = "emef ALFA" }).Total);
        Assert.Equal(1, _base.Buscar(new FiltroObras { Texto = "lote 07" }).Total);
        Assert.Equal(5, _base.Buscar(new FiltroObras { Texto = "manutencao" }).Total);
        Assert.Equal(0, _base.Buscar(new FiltroObras { Texto = "inexistente" }).Total);

        var pagina = _base.Buscar(new FiltroObras(), limite: 2, pular: 2, ordem: OrdemObras.Escola);
        Assert.Equal(5, pagina.Total);
        Assert.Equal(["EMEF Alfa", "EMEF Alfa"], pagina.Obras.Select(o => o.Empreendimento));
        Assert.Equal("EMEF Alfa", _base.Obra(pagina.Obras[1].Id)!.Empreendimento);
    }

    [Fact]
    public void T18_medicoes_e_empenhos_ficam_por_registro_ligados_pelo_contrato()
    {
        var medicoes = _base.Medicoes("001/ex/2024");
        Assert.Equal(["00001", "00002", "00003"], medicoes.Select(m => m.Numero));
        Assert.Equal(600m, medicoes.Sum(m => m.ValorPago));
        Assert.Equal(2, _base.Empenhos("015/EX/2025").Count);
        Assert.Equal(7500.5m, _base.Empenhos("015/EX/2025").Sum(e => e.TotalEmpenhado));
        Assert.Empty(_base.Empenhos("001/EX/2024")); // empenho de outro contrato nao e atribuido a este

        // Reajuste ("VALOR REJUSTE") e status do contrato de cada medicao entram na base.
        var gec = _base.Medicoes("004/EX/2025").Single(m => m.Numero == "00002");
        Assert.Equal((80m, 5.5m, "VIGENTE", "ANÁLISE GEC"), (gec.ValorConsiderado, gec.ValorReajuste, gec.StatusContrato, gec.Status));
    }

    // ---- Portarias e dotacoes ----

    [Fact]
    public void Dotacoes_completas_acha_cabecalhos_corrige_espaco_e_separa_finalidade()
    {
        var importadas = ImportadorDotacoes.Importar(BaseSintetica.Dotacoes());
        _base.GravarDotacoes(importadas, Fonte(Fontes.Dotacoes, 1));

        var vinculos = _base.Vinculos();
        Assert.Equal(5, vinculos.Count);
        Assert.Equal(["Portaria 2 (2024)", "Portaria 3", "Portaria 4"], _base.Portarias().Select(p => p.Rotulo));
        Assert.Equal((2, 2024), (_base.Portarias()[0].Numero, _base.Portarias()[0].Ano));

        // A Portaria 3 tem dois contratos de receita; "(Construção Nova)" e a finalidade do segundo.
        var p3 = vinculos.Where(v => v.Portaria == "Portaria 3").ToList();
        Assert.Equal(["015/EX/2025", "015/EX/2025", "164/EX/2025"], p3.Select(v => v.ContratoReceita));
        Assert.Equal("Construção Nova", p3[2].Finalidade);
        Assert.Null(p3[0].Finalidade);

        // Espaco no lugar do ponto: corrigido, com o original guardado.
        var corrigida = vinculos.First(v => v.DotacaoResumida == "2878");
        Assert.Equal("16.10.12.365.3010.2878.3390.3900.00", corrigida.DotacaoCompleta);
        Assert.Equal("16.10.12.365.3010.2878.3390.3900 00", corrigida.DotacaoOriginal);
        Assert.Contains(importadas.Avisos, a => a.Contains("corrigida para \"16.10.12.365.3010.2878.3390.3900.00\""));

        // A mesma dotacao em duas portarias e normal (3362 na 3 e na 4).
        Assert.Equal(["Portaria 3", "Portaria 4"], vinculos.Where(v => v.DotacaoResumida == "3362").Select(v => v.Portaria));
        Assert.Contains(importadas.Avisos, a => a.Contains("vínculo repetido"));
        Assert.Contains(importadas.Avisos, a => a.Contains("falta portaria, contrato de receita ou dotação"));
        Assert.Contains(importadas.Avisos, a => a.Contains("Portaria 6 com o contrato 303/EX/2022 está na Planilha1 mas não na Planilha2"));
        Assert.Contains(importadas.Avisos, a => a.Contains("3359.4490.5100.00 está na Planilha2 mas não na lista da Planilha1"));
    }

    [Fact]
    public void Dotacoes_sem_cabecalhos_ou_sem_aba_sao_recusadas()
    {
        var semAba = BaseSintetica.Dotacoes();
        semAba.AbasPorNome.Remove("Planilha2");
        Assert.Contains("Planilha2", Assert.Throws<ImportacaoInvalidaException>(() => ImportadorDotacoes.Importar(semAba)).Message);

        var semCabecalho = BaseSintetica.Dotacoes();
        semCabecalho.AbasPorNome["Planilha2"][1][3] = "Outra coisa";
        Assert.Contains("cabeçalhos", Assert.Throws<ImportacaoInvalidaException>(() => ImportadorDotacoes.Importar(semCabecalho)).Message);
    }

    [Fact]
    public void T17_apelido_de_portaria_e_escolha_do_usuario_e_sobrevive_a_nova_importacao()
    {
        _base.GravarDotacoes(ImportadorDotacoes.Importar(BaseSintetica.Dotacoes()), Fonte(Fontes.Dotacoes, 1));
        Assert.Equal([BaseSintetica.P3De2021, BaseSintetica.P3De2025, BaseSintetica.P6], _base.PortariasDasObras());
        Assert.Empty(_base.Apelidos()); // nada e ligado por suposicao

        _base.GravarApelido(BaseSintetica.P3De2021, "Portaria 3");
        _base.GravarMestra(ImportadorMestra.Importar(BaseSintetica.Mestra()), Fonte(Fontes.Mestra, 2));
        _base.GravarDotacoes(ImportadorDotacoes.Importar(BaseSintetica.Dotacoes()), Fonte(Fontes.Dotacoes, 2));

        Assert.Equal("Portaria 3", _base.Apelidos()[BaseSintetica.P3De2021]);
        _base.GravarApelido(BaseSintetica.P3De2021, null);
        Assert.Empty(_base.Apelidos());
    }

    [Theory]
    [InlineData("Portaria 3", 3, null, null)]
    [InlineData("Portaria 2 (2024)", 2, 2024, null)]
    [InlineData("Portaria 3 (Construção Nova)", 3, null, "Construção Nova")]
    [InlineData("PORTARIA CONJUNTA SME/SIURB Nº 03 (2021)", 3, 2021, null)]
    [InlineData("SEM PORTARIA - 96 Quadras", null, null, null)]
    [InlineData("0", null, null, null)]
    public void Le_numero_ano_e_complemento_da_portaria_sem_adivinhar(string rotulo, int? numero, int? ano, string? complemento)
    {
        Assert.Equal(new PortariaLida(numero, ano, complemento), Normalizacao.Portaria(rotulo));
    }

    [Fact]
    public void Dotacao_e_valores_em_reais()
    {
        Assert.Equal(new DotacaoNormalizada("16.10.12.122.3011.3002.4490.5100.00", "16.10.12.122.3011.3002.4490 5100.00", true),
            Normalizacao.Dotacao(" 16.10.12.122.3011.3002.4490 5100.00 "));
        Assert.False(Normalizacao.Dotacao("16.10.12.361.3010.2879.3390.3900.00").Corrigida);

        Assert.Equal(1234.56m, Normalizacao.LerReais("R$ 1.234,56"));
        Assert.Equal(1000m, Normalizacao.LerReais("1000"));
        Assert.Equal(1000.5m, Normalizacao.LerReais("1.000,5"));
        Assert.Null(Normalizacao.LerReais("mil"));
        Assert.Null(Normalizacao.LerReais(""));
        Assert.Equal(46589712L, Normalizacao.Centavos(465897.1200000001m));
        Assert.Equal(0.01m, Normalizacao.Reais(1));
    }
}
