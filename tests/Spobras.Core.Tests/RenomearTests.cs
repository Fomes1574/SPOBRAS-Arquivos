using Spobras.Core.Abstracoes;
using Spobras.Core.Operacoes;
using Spobras.Core.Renomear;

namespace Spobras.Core.Tests;

public class RenomearTests : ComPastaTemporaria
{
    private readonly ArquivosDisco _disco = new();
    private readonly HistoricoMemoria _historico = new();
    private readonly RelogioFixo _relogio = new();

    private PlanoRenomear Planejar(IEnumerable<ItemRenomear> itens, OpcoesRenomear opcoes) =>
        new PlanejadorRenomear(_disco).Planejar([.. itens], opcoes);

    private Lote Executar(PlanoRenomear plano)
    {
        var lote = LoteRenomear.Criar(plano, _relogio);
        _historico.CriarLote(lote);
        new ExecutorLote(_disco, _historico, _relogio).Executar(lote);
        return lote;
    }

    // ---- T01 ----

    [Fact]
    public void T01_cem_arquivos_em_ordem_natural_com_numero_inicial()
    {
        var itens = Enumerable.Range(1, 100).Reverse()
            .Select(n => new ItemRenomear(Criar($"foto {n}.JPG", $"conteudo {n}")))
            .ToList();

        var plano = Planejar(itens, new OpcoesRenomear { NomeBase = "Relatório", NumeroInicial = 5 });

        Assert.True(plano.PodeExecutar);
        Assert.Equal(100, plano.Executaveis);
        // Largura acompanha o maior numero do lote (104 -> 3 digitos); extensao preservada como esta.
        Assert.Equal("foto 1.JPG", plano.Linhas[0].NomeAtual);
        Assert.Equal("Relatório 005.JPG", plano.Linhas[0].NomeNovo);
        Assert.Equal("foto 2.JPG", plano.Linhas[1].NomeAtual);
        Assert.Equal("foto 10.JPG", plano.Linhas[9].NomeAtual);
        Assert.Equal("Relatório 014.JPG", plano.Linhas[9].NomeNovo);
        Assert.Equal("Relatório 104.JPG", plano.Linhas[99].NomeNovo);

        var lote = Executar(plano);

        Assert.Equal(100, lote.Contar(EstadoItem.Concluido));
        Assert.Equal(plano.Linhas.Select(l => l.NomeNovo).Order(StringComparer.Ordinal), Nomes());
        // Resultado corresponde a previa: cada conteudo foi para o nome previsto.
        Assert.Equal("conteudo 1", File.ReadAllText(Em("Relatório 005.JPG")));
        Assert.Equal("conteudo 10", File.ReadAllText(Em("Relatório 014.JPG")));
        Assert.Equal("conteudo 100", File.ReadAllText(Em("Relatório 104.JPG")));
    }

    [Fact]
    public void T01_modo_nome_e_numero_com_e_sem_zero_e_sem_numero()
    {
        var itens = new[] { "b.pdf", "a.pdf", "c.pdf" }.Select(n => new ItemRenomear(Criar(n))).ToList();

        var comZero = Planejar(itens, new OpcoesRenomear { NomeBase = "Relatório" });
        var semZero = Planejar(itens, new OpcoesRenomear { NomeBase = "Relatório", ZeroEsquerda = false, Separador = "_", NumeroInicial = 9 });
        var umSo = Planejar(itens.Take(1).ToList(), new OpcoesRenomear { NomeBase = "Pauta", Numerar = false });

        Assert.Equal(["Relatório 01.pdf", "Relatório 02.pdf", "Relatório 03.pdf"], comZero.Linhas.Select(l => l.NomeNovo));
        Assert.Equal(["a.pdf", "b.pdf", "c.pdf"], comZero.Linhas.Select(l => l.NomeAtual));
        Assert.Equal(["Relatório_9.pdf", "Relatório_10.pdf", "Relatório_11.pdf"], semZero.Linhas.Select(l => l.NomeNovo));
        Assert.Equal("Pauta.pdf", umSo.Linhas[0].NomeNovo);
    }

    [Fact]
    public void T01_modo_nome_base_com_complemento_por_arquivo()
    {
        var itens = new[]
        {
            new ItemRenomear(Criar("scan1.pdf")) { Complemento = "Lote 12" },
            new ItemRenomear(Criar("scan2.pdf")) { Complemento = " Lote 3 " },
            new ItemRenomear(Criar("scan3.pdf")),
        };

        var plano = Planejar(itens, new OpcoesRenomear { Modo = ModoRenomear.NomeComplemento, NomeBase = "Muros de arrimo", Separador = " - " });

        Assert.Equal(
            ["Muros de arrimo - Lote 12.pdf", "Muros de arrimo - Lote 3.pdf", "Muros de arrimo.pdf"],
            plano.Linhas.Select(l => l.NomeNovo));
        Executar(plano);
        Assert.Equal(["Muros de arrimo - Lote 12.pdf", "Muros de arrimo - Lote 3.pdf", "Muros de arrimo.pdf"], Nomes());
    }

    [Fact]
    public void T01_modo_nomes_livres_sem_numeracao()
    {
        var itens = new[]
        {
            new ItemRenomear(Criar("x1.xlsx")) { NomeLivre = "Medição outubro" },
            new ItemRenomear(Criar("x2.xlsx")) { NomeLivre = "Cronograma lote 4.xlsx" },
            new ItemRenomear(Criar("x3.xlsx")),
        };

        var plano = Planejar(itens, new OpcoesRenomear { Modo = ModoRenomear.NomesLivres });

        Assert.Equal(["Medição outubro.xlsx", "Cronograma lote 4.xlsx", "x3.xlsx"], plano.Linhas.Select(l => l.NomeNovo));
        Assert.Equal(SituacaoRenomear.SemMudanca, plano.Linhas[2].Situacao);
        Assert.Equal(2, plano.Executaveis);
    }

    [Fact]
    public void Ordem_pela_data_e_ordem_manual()
    {
        var dia = new DateTimeOffset(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);
        var itens = new[]
        {
            new ItemRenomear(Criar("a.jpg")) { Data = dia.AddDays(2) },
            new ItemRenomear(Criar("b.jpg")),
            new ItemRenomear(Criar("c.jpg")) { Data = dia },
        };

        var porData = Planejar(itens, new OpcoesRenomear { NomeBase = "Foto", Ordem = OrdemRenomear.Data });
        var manual = Planejar(itens.Reverse(), new OpcoesRenomear { NomeBase = "Foto", Ordem = OrdemRenomear.Manual });

        Assert.Equal(["c.jpg", "a.jpg", "b.jpg"], porData.Linhas.Select(l => l.NomeAtual));
        Assert.Equal(["c.jpg", "b.jpg", "a.jpg"], manual.Linhas.Select(l => l.NomeAtual));
    }

    [Fact]
    public void Continuar_numeracao_considera_so_o_padrao_e_nao_reaproveita_lacunas()
    {
        Criar("Relatório 01.pdf");
        Criar("Relatório 02.pdf");
        Criar("Relatório 20.pdf");          // lacuna entre 02 e 20: nao e reaproveitada
        Criar("Relatório final 99.pdf");    // nao segue o padrao
        Criar("Relatorio 50.pdf");          // outro nome base (sem acento)
        var itens = new[] { new ItemRenomear(Criar("novo b.pdf")), new ItemRenomear(Criar("novo a.pdf")) };

        var plano = Planejar(itens, new OpcoesRenomear { NomeBase = "Relatório", ContinuarNumeracao = true });

        Assert.Equal(["Relatório 21.pdf", "Relatório 22.pdf"], plano.Linhas.Select(l => l.NomeNovo));
        Assert.Contains(plano.Avisos, a => a.Gravidade == Gravidade.Informacao && a.Mensagem.Contains("começam no 21"));
    }

    [Fact]
    public void Opcoes_de_perfil_data_substituir_espacos_e_acentos()
    {
        var item = new ItemRenomear(Criar("x.pdf")) { Data = new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.Zero), NomeLivre = "Medição   de  outubro" };

        var plano = Planejar([item], new OpcoesRenomear
        {
            Modo = ModoRenomear.NomesLivres,
            PosicaoData = PosicaoData.Inicio,
            SubstituirDe = "outubro",
            SubstituirPor = "novembro",
            RemoverEspacosExcedentes = true,
            RemoverAcentos = true,
        });

        Assert.Equal("2026-11-10 Medicao de novembro.pdf", plano.Linhas[0].NomeNovo);
    }

    [Fact]
    public void Nome_editado_na_previa_passa_na_frente_e_e_validado()
    {
        var itens = new[]
        {
            new ItemRenomear(Criar("a.pdf")) { NomeEditado = "Capa" },
            new ItemRenomear(Criar("b.pdf")),
            new ItemRenomear(Criar("c.pdf")) { NomeEditado = "Anexo 1/2" },
        };

        var plano = Planejar(itens, new OpcoesRenomear { NomeBase = "Relatório" });

        Assert.Equal(["Capa.pdf", "Relatório 02.pdf", "Anexo 1/2.pdf"], plano.Linhas.Select(l => l.NomeNovo));
        Assert.Equal(SituacaoRenomear.Impedido, plano.Linhas[2].Situacao);
        Assert.Contains("Sugestão: \"Anexo 1-2.pdf\"", plano.Linhas[2].Motivo);
    }

    [Fact]
    public void Sem_nome_ou_sem_arquivos_impede()
    {
        Assert.False(Planejar([], new OpcoesRenomear { NomeBase = "x" }).PodeExecutar);

        var semNome = Planejar([new ItemRenomear(Criar("a.pdf"))], new OpcoesRenomear());
        Assert.False(semNome.PodeExecutar);
        Assert.Contains(semNome.Avisos, a => a.Gravidade == Gravidade.ImpedeGerar && a.Mensagem == "Digite o nome.");
    }

    // ---- T02 ----

    [Fact]
    public void T02_colisao_com_arquivo_existente_sugere_sufixo_e_nao_sobrescreve()
    {
        Criar("Relatório 01.pdf", "antigo");
        var itens = new[] { new ItemRenomear(Criar("a.pdf", "novo a")), new ItemRenomear(Criar("b.pdf", "novo b")) };

        var plano = Planejar(itens, new OpcoesRenomear { NomeBase = "Relatório" });

        Assert.Equal(SituacaoRenomear.Ajustado, plano.Linhas[0].Situacao);
        Assert.Equal("Relatório 01 (2).pdf", plano.Linhas[0].NomeNovo);
        Assert.Contains("para não sobrescrever", plano.Linhas[0].Motivo);
        Assert.Equal("Relatório 02.pdf", plano.Linhas[1].NomeNovo);

        Executar(plano);

        Assert.Equal("antigo", File.ReadAllText(Em("Relatório 01.pdf")));
        Assert.Equal("novo a", File.ReadAllText(Em("Relatório 01 (2).pdf")));
        Assert.Equal(["Relatório 01 (2).pdf", "Relatório 01.pdf", "Relatório 02.pdf"], Nomes());
    }

    [Fact]
    public void T02_colisao_dentro_do_proprio_lote()
    {
        var itens = new[]
        {
            new ItemRenomear(Criar("a.pdf")) { NomeLivre = "Pauta" },
            new ItemRenomear(Criar("b.pdf")) { NomeLivre = "pauta" },
            new ItemRenomear(Criar("c.docx")) { NomeLivre = "Pauta" },
        };

        var plano = Planejar(itens, new OpcoesRenomear { Modo = ModoRenomear.NomesLivres });

        Assert.Equal(["Pauta.pdf", "pauta (2).pdf", "Pauta.docx"], plano.Linhas.Select(l => l.NomeNovo));
        Assert.Equal(SituacaoRenomear.Ajustado, plano.Linhas[1].Situacao);
    }

    [Fact]
    public void T02_nomes_invalidos_ficam_impedidos_e_os_validos_executam()
    {
        var itens = new[]
        {
            new ItemRenomear(Criar("a.pdf")) { NomeLivre = "Medição 10/11" },
            new ItemRenomear(Criar("b.pdf")) { NomeLivre = "CON" },
            new ItemRenomear(Criar("c.pdf")) { NomeLivre = "Certo" },
            new ItemRenomear(Em("sumiu.pdf")) { NomeLivre = "Outro" },
        };

        var plano = Planejar(itens, new OpcoesRenomear { Modo = ModoRenomear.NomesLivres });

        Assert.Equal(3, plano.Impedidos);
        Assert.Contains("não aceita", plano.Linhas[0].Motivo);
        Assert.Contains("reservado", plano.Linhas[1].Motivo);
        Assert.Contains("não está mais", plano.Linhas[3].Motivo);
        Assert.True(plano.PodeExecutar);

        var lote = Executar(plano);

        Assert.Equal(1, lote.Contar(EstadoItem.Concluido));
        Assert.Equal(3, lote.Contar(EstadoItem.Ignorado));
        Assert.Equal(["Certo.pdf", "a.pdf", "b.pdf"], Nomes());
    }

    [Fact]
    public void T02_troca_A_B_e_ciclo_de_tres()
    {
        var a = new ItemRenomear(Criar("A.pdf", "era A")) { NomeLivre = "B" };
        var b = new ItemRenomear(Criar("B.pdf", "era B")) { NomeLivre = "C" };
        var c = new ItemRenomear(Criar("C.pdf", "era C")) { NomeLivre = "A" };

        var plano = Planejar([a, b, c], new OpcoesRenomear { Modo = ModoRenomear.NomesLivres });

        Assert.All(plano.Linhas, l => Assert.Equal(SituacaoRenomear.Ok, l.Situacao));
        var lote = Executar(plano);

        Assert.Equal(3, lote.Contar(EstadoItem.Concluido));
        Assert.Equal(["A.pdf", "B.pdf", "C.pdf"], Nomes()); // nenhum temporario sobrou
        Assert.Equal("era C", File.ReadAllText(Em("A.pdf")));
        Assert.Equal("era A", File.ReadAllText(Em("B.pdf")));
        Assert.Equal("era B", File.ReadAllText(Em("C.pdf")));
        Assert.All(lote.Itens, i => Assert.Null(i.Temporario));
    }

    [Fact]
    public void T02_troca_interrompida_e_recuperavel()
    {
        var a = new ItemRenomear(Criar("A.pdf", "era A")) { NomeLivre = "B" };
        var b = new ItemRenomear(Criar("B.pdf", "era B")) { NomeLivre = "A" };
        var plano = Planejar([a, b], new OpcoesRenomear { Modo = ModoRenomear.NomesLivres });
        var lote = LoteRenomear.Criar(plano, _relogio);
        _historico.CriarLote(lote);

        // O app "fecha" logo depois de um arquivo ir para o nome temporario.
        int movimentos = 0;
        var comQueda = new ArquivosComFalha { Antes = (op, _, _) => { if (op == "mover" && ++movimentos == 2) throw new QuedaSimulada(); } };
        Assert.Throws<QuedaSimulada>(() => new ExecutorLote(comQueda, _historico, _relogio).Executar(lote));

        Assert.True(ExecutorLote.PrecisaReconciliar(lote));
        Assert.Equal(2, Nomes().Length); // os dois conteudos continuam no disco
        var executor = new ExecutorLote(_disco, _historico, _relogio);
        var resumo = executor.Reconciliar(lote);
        Assert.Equal(0, resumo.Conflitos);

        executor.Executar(lote);

        Assert.Equal(2, lote.Contar(EstadoItem.Concluido));
        Assert.Equal(["A.pdf", "B.pdf"], Nomes());
        Assert.Equal("era B", File.ReadAllText(Em("A.pdf")));
        Assert.Equal("era A", File.ReadAllText(Em("B.pdf")));
    }

    [Fact]
    public void T02_destino_que_aparece_depois_da_previa_nao_e_sobrescrito()
    {
        var plano = Planejar([new ItemRenomear(Criar("a.pdf", "novo"))], new OpcoesRenomear { NomeBase = "Relatório" });
        Criar("Relatório 01.pdf", "apareceu depois");

        var lote = Executar(plano);

        Assert.Equal(EstadoItem.Falhou, lote.Itens[0].Estado);
        Assert.Contains("Nada foi sobrescrito", lote.Itens[0].Mensagem);
        Assert.Equal("apareceu depois", File.ReadAllText(Em("Relatório 01.pdf")));
        Assert.Equal("novo", File.ReadAllText(Em("a.pdf")));
    }

    [Fact]
    public void Mudanca_so_de_maiusculas_e_aceita()
    {
        var plano = Planejar([new ItemRenomear(Criar("relatorio.pdf")) { NomeLivre = "Relatorio" }], new OpcoesRenomear { Modo = ModoRenomear.NomesLivres });

        Assert.Equal(SituacaoRenomear.Ok, plano.Linhas[0].Situacao);
        Assert.Equal("Relatorio.pdf", plano.Linhas[0].NomeNovo);
    }
}
