using Spobras.Core.Abstracoes;
using Spobras.Core.Operacoes;

namespace Spobras.Core.Tests;

public class OperacoesTests : ComPastaTemporaria
{
    private readonly ArquivosDisco _disco = new();
    private readonly HistoricoMemoria _historico = new();
    private readonly RelogioFixo _relogio = new();

    private Lote NovoLote(TipoLote tipo, params (string Origem, string Destino)[] itens)
    {
        var lote = new Lote
        {
            Tipo = tipo,
            Descricao = "teste",
            CriadoEm = _relogio.Agora,
            Itens = [.. itens.Select((i, n) => new ItemLote { Ordem = n, Origem = i.Origem, Destino = i.Destino })],
        };
        _historico.CriarLote(lote);
        return lote;
    }

    private ExecutorLote Executor(IArquivos? arquivos = null) => new(arquivos ?? _disco, _historico, _relogio);

    private Lote DesfazerTudo(Lote original, out PlanoDesfazer plano)
    {
        var desfazer = new Desfazer(_disco, _relogio);
        plano = desfazer.Planejar(original);
        var lote = desfazer.CriarLote(plano);
        _historico.CriarLote(lote);
        Executor().Executar(lote);
        _historico.MarcarDesfeito(original.Id, lote.Id);
        return lote;
    }

    // ---- T03 ----

    [Fact]
    public void Desfazer_volta_renomeacao_e_troca_de_nomes()
    {
        var lote = NovoLote(TipoLote.Renomear,
            (Criar("A.pdf", "era A"), Em("B.pdf")),
            (Criar("B.pdf", "era B"), Em("A.pdf")),
            (Criar("c.pdf", "era c"), Em("Relatório 01.pdf")));
        Executor().Executar(lote);
        Assert.Equal("era B", File.ReadAllText(Em("A.pdf")));

        DesfazerTudo(lote, out var plano);

        Assert.Equal(3, plano.Reversiveis);
        Assert.Equal(["A.pdf", "B.pdf", "c.pdf"], Nomes());
        Assert.Equal("era A", File.ReadAllText(Em("A.pdf")));
        Assert.Equal("era B", File.ReadAllText(Em("B.pdf")));
        // Uma acao ja desfeita nao oferece Desfazer de novo.
        Assert.False(new Desfazer(_disco, _relogio).Planejar(lote).PodeDesfazer);
    }

    [Fact]
    public void T03_desfazer_apos_edicao_posterior_preserva_o_conteudo_atual()
    {
        var lote = NovoLote(TipoLote.Renomear, (Criar("a.xlsx", "v1"), Em("Medição.xlsx")), (Criar("b.xlsx", "v1"), Em("Pauta.xlsx")));
        Executor().Executar(lote);
        File.WriteAllText(Em("Medição.xlsx"), "editado depois, maior");

        DesfazerTudo(lote, out var plano);

        Assert.Equal(1, plano.Conflitos);
        Assert.Contains("alterado depois", plano.Linhas[0].Motivo);
        Assert.Equal("editado depois, maior", File.ReadAllText(Em("Medição.xlsx")));
        Assert.Equal(["Medição.xlsx", "b.xlsx"], Nomes());
    }

    [Fact]
    public void T03_desfazer_com_caminho_original_ocupado_explica_e_nao_sobrescreve()
    {
        var lote = NovoLote(TipoLote.Renomear, (Criar("a.pdf", "original"), Em("Relatório 01.pdf")));
        Executor().Executar(lote);
        Criar("a.pdf", "outro arquivo que chegou depois");

        var plano = new Desfazer(_disco, _relogio).Planejar(lote);

        Assert.False(plano.PodeDesfazer);
        Assert.Contains("caminho anterior está ocupado", plano.Linhas[0].Motivo);
        Assert.Equal("outro arquivo que chegou depois", File.ReadAllText(Em("a.pdf")));
        Assert.Equal("original", File.ReadAllText(Em("Relatório 01.pdf")));
    }

    [Fact]
    public void T03_desfazer_quando_o_arquivo_saiu_de_onde_o_app_deixou()
    {
        var lote = NovoLote(TipoLote.Mover, (Criar("a.pdf"), Em("Destino", "a.pdf")));
        Executor().Executar(lote);
        File.Move(Em("Destino", "a.pdf"), Em("Destino", "levado.pdf"));

        var plano = new Desfazer(_disco, _relogio).Planejar(lote);

        Assert.Contains("não está mais onde o app deixou", plano.Linhas[0].Motivo);
    }

    // ---- T04 ----

    [Fact]
    public void T04_queda_durante_renomeacao_reconcilia_e_continua_so_os_pendentes()
    {
        var itens = Enumerable.Range(1, 6).Select(n => (Criar($"f{n}.pdf", $"c{n}"), Em($"Relatório 0{n}.pdf"))).ToArray();
        var lote = NovoLote(TipoLote.Renomear, itens);

        int movimentos = 0;
        var comQueda = new ArquivosComFalha { Antes = (op, _, _) => { if (op == "mover" && ++movimentos == 4) throw new QuedaSimulada(); } };
        Assert.Throws<QuedaSimulada>(() => Executor(comQueda).Executar(lote));

        // O historico foi gravado por arquivo: 3 concluidos, 1 no meio, 2 pendentes.
        Assert.Equal(3, lote.Contar(EstadoItem.Concluido));
        Assert.Equal(1, lote.Contar(EstadoItem.EmExecucao));
        Assert.True(ExecutorLote.PrecisaReconciliar(lote));

        var resumo = Executor().Reconciliar(lote);
        Assert.Equal(new ResumoReconciliacao(0, 1, 0), resumo);

        var repetidos = new List<string>();
        var contador = new ArquivosComFalha { Antes = (op, origem, _) => { if (op == "mover") repetidos.Add(Path.GetFileName(origem)); } };
        Executor(contador).Executar(lote);

        Assert.Equal(["f4.pdf", "f5.pdf", "f6.pdf"], repetidos); // os concluidos nao sao repetidos
        Assert.Equal(6, lote.Contar(EstadoItem.Concluido));
        Assert.Equal("c4", File.ReadAllText(Em("Relatório 04.pdf")));
    }

    [Fact]
    public void T04_queda_depois_de_mover_e_antes_de_gravar_o_historico()
    {
        var lote = NovoLote(TipoLote.Renomear, (Criar("a.pdf", "c"), Em("b.pdf")));
        // O arquivo ja foi movido no disco, mas o historico ficou "em execucao".
        File.Move(Em("a.pdf"), Em("b.pdf"));
        lote.Itens[0].Estado = EstadoItem.EmExecucao;

        var resumo = Executor().Reconciliar(lote);

        Assert.Equal(new ResumoReconciliacao(1, 0, 0), resumo);
        Assert.Equal(EstadoItem.Concluido, lote.Itens[0].Estado);
        Assert.NotNull(lote.Itens[0].Tamanho);
    }

    [Fact]
    public void T04_mover_entre_volumes_copia_confere_e_so_entao_tira_a_origem()
    {
        var outroVolume = new ArquivosComFalha { OutroVolume = true };
        var lote = NovoLote(TipoLote.Mover, (Criar("foto.jpg", "conteudo da foto"), Em("Pendrive", "foto.jpg")));

        Executor(outroVolume).Executar(lote);

        Assert.Equal(EstadoItem.Concluido, lote.Itens[0].Estado);
        Assert.False(File.Exists(Em("foto.jpg")));
        Assert.Equal("conteudo da foto", File.ReadAllText(Em("Pendrive", "foto.jpg")));
        Assert.Equal(["foto.jpg"], Nomes("Pendrive")); // sem arquivo parcial
        Assert.Equal([Em("foto.jpg")], outroVolume.Removidos);
    }

    [Fact]
    public void T04_copia_que_nao_confere_mantem_a_origem()
    {
        var corrompe = new ArquivosComFalha { OutroVolume = true, CorromperCopia = true };
        var lote = NovoLote(TipoLote.Mover, (Criar("foto.jpg", "conteudo"), Em("Pendrive", "foto.jpg")));

        Executor(corrompe).Executar(lote);

        Assert.Equal(EstadoItem.Falhou, lote.Itens[0].Estado);
        Assert.Contains("continua na origem", lote.Itens[0].Mensagem);
        Assert.Equal("conteudo", File.ReadAllText(Em("foto.jpg")));
        Assert.Empty(Nomes("Pendrive"));
    }

    [Theory]
    [InlineData("copiar")]   // caiu antes de copiar
    [InlineData("mover")]    // caiu com a copia parcial pronta, antes de publicar o nome final
    [InlineData("remover")]  // caiu com o destino publicado, antes de tirar a origem
    public void T04_queda_entre_volumes_mantem_copia_integra_e_reconcilia(string pontoDaQueda)
    {
        var lote = NovoLote(TipoLote.Mover, (Criar("planilha.xlsx", "dados importantes"), Em("Pendrive", "planilha.xlsx")));
        var comQueda = new ArquivosComFalha { OutroVolume = true };
        comQueda.Antes = (op, _, _) => { if (op == pontoDaQueda) throw new QuedaSimulada(); };

        Assert.Throws<QuedaSimulada>(() => Executor(comQueda).Executar(lote));

        // Em qualquer ponto existe pelo menos uma copia integra.
        Assert.True(File.Exists(Em("planilha.xlsx")));
        Assert.Equal("dados importantes", File.ReadAllText(Em("planilha.xlsx")));

        var semQueda = new ArquivosComFalha { OutroVolume = true };
        var executor = Executor(semQueda);
        executor.Reconciliar(lote);
        executor.Executar(lote);

        Assert.Equal(EstadoItem.Concluido, lote.Itens[0].Estado);
        Assert.False(File.Exists(Em("planilha.xlsx")));
        Assert.Equal("dados importantes", File.ReadAllText(Em("Pendrive", "planilha.xlsx")));
        Assert.Equal(["planilha.xlsx"], Nomes("Pendrive"));
    }

    [Fact]
    public void T04_origem_e_destino_diferentes_viram_conflito_sem_apagar_nada()
    {
        var lote = NovoLote(TipoLote.Mover, (Criar("a.pdf", "um"), Em("Destino", "a.pdf")));
        Criar(Path.Combine("Destino", "a.pdf"), "outro conteudo");
        lote.Itens[0].Estado = EstadoItem.EmExecucao;

        var resumo = Executor(new ArquivosComFalha { OutroVolume = true }).Reconciliar(lote);

        Assert.Equal(1, resumo.Conflitos);
        Assert.Contains("Os dois foram mantidos", lote.Itens[0].Mensagem);
        Assert.Equal("um", File.ReadAllText(Em("a.pdf")));
        Assert.Equal("outro conteudo", File.ReadAllText(Em("Destino", "a.pdf")));
    }

    // ---- T05 ----

    [Fact]
    public void T05_cancelar_no_meio_e_executar_de_novo_so_os_pendentes()
    {
        var itens = Enumerable.Range(1, 8).Select(n => (Criar($"f{n}.pdf"), Em("Destino", $"f{n}.pdf"))).ToArray();
        var lote = NovoLote(TipoLote.Mover, itens);
        using var cancelamento = new CancellationTokenSource();
        int movimentos = 0;
        var cancelaNoTerceiro = new ArquivosComFalha { Antes = (op, _, _) => { if (op == "mover" && ++movimentos == 3) cancelamento.Cancel(); } };

        var resultado = Executor(cancelaNoTerceiro).Executar(lote, cancelamento.Token);

        Assert.Equal(3, resultado.Concluidos);
        Assert.Equal(5, resultado.Cancelados);
        Assert.Equal(3, Nomes("Destino").Length);
        Assert.Equal(5, Nomes().Length); // saidas concluidas preservadas; nada pela metade
        Assert.DoesNotContain(Nomes("Destino"), n => n.EndsWith(ExecutorLote.SufixoParcial));

        var movidosDepois = new List<string>();
        var contador = new ArquivosComFalha { Antes = (op, origem, _) => { if (op == "mover") movidosDepois.Add(Path.GetFileName(origem)); } };
        var segundo = Executor(contador).Executar(lote);

        Assert.Equal(8, segundo.Concluidos);
        Assert.Equal(5, movidosDepois.Count);
        Assert.DoesNotContain("f1.pdf", movidosDepois);
    }

    [Fact]
    public void Falha_em_um_arquivo_nao_para_o_lote()
    {
        var lote = NovoLote(TipoLote.Mover,
            (Criar("a.pdf"), Em("Destino", "a.pdf")),
            (Em("nao-existe.pdf"), Em("Destino", "nao-existe.pdf")),
            (Criar("c.pdf"), Em("Destino", "c.pdf")));
        var negado = new ArquivosComFalha { Antes = (op, origem, _) => { if (op == "mover" && origem.EndsWith("c.pdf")) throw new UnauthorizedAccessException("Acesso negado."); } };

        var resultado = Executor(negado).Executar(lote);

        Assert.Equal(1, resultado.Concluidos);
        Assert.Equal(2, resultado.Falhas);
        Assert.Contains("não está mais", lote.Itens[1].Mensagem);
        Assert.Equal("Acesso negado.", lote.Itens[2].Mensagem);
        Assert.True(File.Exists(Em("c.pdf")));
    }

    [Fact]
    public void Progresso_informa_feitos_e_total()
    {
        var lote = NovoLote(TipoLote.Renomear, (Criar("a.pdf"), Em("1.pdf")), (Criar("b.pdf"), Em("2.pdf")));
        var vistos = new List<ProgressoLote>();

        Executor().Executar(lote, progresso: new Coletor(vistos));

        Assert.Equal(new ProgressoLote(2, 2, null), vistos[^1]);
        Assert.Contains(vistos, p => p.Atual == "a.pdf");
    }

    private sealed class Coletor(List<ProgressoLote> vistos) : IProgress<ProgressoLote>
    {
        public void Report(ProgressoLote value) => vistos.Add(value);
    }
}
