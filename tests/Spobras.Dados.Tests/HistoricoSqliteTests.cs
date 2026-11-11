using Microsoft.Data.Sqlite;
using Spobras.Core.Abstracoes;
using Spobras.Core.Operacoes;
using Spobras.Dados.Historico;

namespace Spobras.Dados.Tests;

public class HistoricoSqliteTests : IDisposable
{
    private readonly string _pasta = Directory.CreateTempSubdirectory("spobras-hist-").FullName;
    private readonly Relogio _relogio = new();

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    private string Em(params string[] partes) => Path.Combine([_pasta, .. partes]);

    private string Criar(string nome, string conteudo)
    {
        File.WriteAllText(Em(nome), conteudo);
        return Em(nome);
    }

    private HistoricoSqlite Abrir() => new(new BancoApp(Em("dados", "app.db")));

    private Lote NovoLote(params (string Origem, string Destino)[] itens) => new()
    {
        Tipo = TipoLote.Renomear,
        Descricao = "Renomeou arquivos de “Relatório 01” a “Relatório 03”",
        CriadoEm = _relogio.Agora,
        Itens = [.. itens.Select((i, n) => new ItemLote { Ordem = n, Origem = i.Origem, Destino = i.Destino })],
    };

    [Fact]
    public void Grava_e_le_o_lote_com_os_itens_depois_de_reabrir_o_banco()
    {
        var lote = NovoLote((Criar("a.pdf", "aaa"), Em("Relatório 01.pdf")), (Criar("b.pdf", "bb"), Em("Relatório 02.pdf")));
        var historico = Abrir();
        historico.CriarLote(lote);
        new ExecutorLote(new ArquivosDisco(), historico, _relogio).Executar(lote);

        var lido = Abrir().Obter(lote.Id)!; // outra instancia: veio mesmo do disco

        Assert.Equal(lote.Descricao, lido.Descricao);
        Assert.Equal(lote.CriadoEm, lido.CriadoEm);
        Assert.Equal(TipoLote.Renomear, lido.Tipo);
        Assert.Equal(2, lido.Itens.Count);
        Assert.All(lido.Itens, i => Assert.Equal(EstadoItem.Concluido, i.Estado));
        Assert.Equal(Em("Relatório 01.pdf"), lido.Itens[0].Destino);
        Assert.Equal(3, lido.Itens[0].Tamanho);
        // A data volta exata: e ela que o Desfazer compara com o disco.
        Assert.Equal(new FileInfo(Em("Relatório 01.pdf")).LastWriteTimeUtc.Ticks, lido.Itens[0].ModificadoEm!.Value.UtcTicks);
        Assert.Equal(_relogio.Agora, lido.Itens[0].ConcluidoEm);
    }

    [Fact]
    public void T04_queda_com_historico_no_banco_reconcilia_a_partir_do_que_foi_gravado()
    {
        var lote = NovoLote(
            (Criar("a.pdf", "a"), Em("1.pdf")), (Criar("b.pdf", "b"), Em("2.pdf")), (Criar("c.pdf", "c"), Em("3.pdf")));
        var historico = Abrir();
        historico.CriarLote(lote);

        var cai = new CaiNoSegundo();
        Assert.Throws<InvalidProgramException>(() => new ExecutorLote(cai, historico, _relogio).Executar(lote));

        // "Reabre o app": tudo vem do banco, nada da memoria.
        var depois = Abrir();
        var recuperado = depois.Obter(lote.Id)!;
        Assert.Equal([EstadoItem.Concluido, EstadoItem.EmExecucao, EstadoItem.Pendente], recuperado.Itens.Select(i => i.Estado));
        Assert.True(ExecutorLote.PrecisaReconciliar(recuperado));

        var executor = new ExecutorLote(new ArquivosDisco(), depois, _relogio);
        var resumo = executor.Reconciliar(recuperado);
        executor.Executar(recuperado);

        Assert.Equal(new ResumoReconciliacao(0, 1, 0), resumo);
        Assert.All(Abrir().Obter(lote.Id)!.Itens, i => Assert.Equal(EstadoItem.Concluido, i.Estado));
        Assert.Equal(["1.pdf", "2.pdf", "3.pdf"], Directory.GetFiles(_pasta).Select(Path.GetFileName).Order());
    }

    [Fact]
    public void Desfazer_a_partir_do_banco_e_marca_o_lote_como_desfeito()
    {
        var lote = NovoLote((Criar("a.pdf", "a"), Em("Relatório 01.pdf")));
        var historico = Abrir();
        historico.CriarLote(lote);
        var disco = new ArquivosDisco();
        new ExecutorLote(disco, historico, _relogio).Executar(lote);

        var original = Abrir().Obter(lote.Id)!;
        var desfazer = new Desfazer(disco, _relogio);
        var plano = desfazer.Planejar(original);
        Assert.True(plano.PodeDesfazer);
        var volta = desfazer.CriarLote(plano);
        historico.CriarLote(volta);
        new ExecutorLote(disco, historico, _relogio).Executar(volta);
        historico.MarcarDesfeito(original.Id, volta.Id);

        Assert.True(File.Exists(Em("a.pdf")));
        var lotes = Abrir().Listar();
        Assert.Equal(2, lotes.Count);
        Assert.Equal(volta.Id, lotes.Single(l => l.Id == lote.Id).DesfeitoPorLoteId);
        Assert.Equal(lote.Id, lotes.Single(l => l.Id == volta.Id).DesfazLoteId);
        Assert.False(desfazer.Planejar(Abrir().Obter(lote.Id)!).PodeDesfazer);
    }

    [Fact]
    public void Lista_do_mais_novo_para_o_mais_antigo_e_respeita_o_maximo()
    {
        var historico = Abrir();
        for (int i = 0; i < 5; i++)
        {
            _relogio.Agora = _relogio.Agora.AddMinutes(1);
            historico.CriarLote(new Lote { Tipo = TipoLote.Mover, Descricao = $"lote {i}", CriadoEm = _relogio.Agora });
        }

        Assert.Equal(["lote 4", "lote 3", "lote 2"], historico.Listar(3).Select(l => l.Descricao));
        Assert.Null(historico.Obter(Guid.NewGuid()));
    }

    [Fact]
    public void Banco_migra_uma_vez_e_recusa_versao_mais_nova()
    {
        string caminho = Em("dados", "app.db");
        _ = new BancoApp(caminho);
        _ = new BancoApp(caminho); // abrir de novo nao recria nada

        using (var conexao = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = caminho, Pooling = false }.ToString()))
        {
            conexao.Open();
            using var cmd = conexao.CreateCommand();
            cmd.CommandText = "pragma user_version";
            Assert.Equal(BancoApp.VersaoAtual, Convert.ToInt32(cmd.ExecuteScalar()));
            cmd.CommandText = "pragma user_version = 999";
            cmd.ExecuteNonQuery();
        }

        var erro = Assert.Throws<InvalidOperationException>(() => new BancoApp(caminho));
        Assert.Contains("versão mais nova", erro.Message);
    }

    private sealed class Relogio : IRelogio
    {
        public DateTimeOffset Agora { get; set; } = new(2026, 11, 11, 10, 0, 0, TimeSpan.FromHours(-3));
    }

    /// <summary>Disco real que "derruba o app" no segundo movimento.</summary>
    private sealed class CaiNoSegundo : IArquivos
    {
        private readonly ArquivosDisco _disco = new();
        private int _movimentos;

        public void Mover(string origem, string destino)
        {
            if (++_movimentos == 2)
            {
                throw new InvalidProgramException("queda simulada");
            }

            _disco.Mover(origem, destino);
        }

        public bool ArquivoExiste(string caminho) => _disco.ArquivoExiste(caminho);
        public bool PastaExiste(string caminho) => _disco.PastaExiste(caminho);
        public InfoArquivo? Info(string caminho) => _disco.Info(caminho);
        public IReadOnlyList<string> ListarArquivos(string pasta) => _disco.ListarArquivos(pasta);
        public IReadOnlyList<string> ListarArquivosRecursivo(string pasta) => _disco.ListarArquivosRecursivo(pasta);
        public void CriarPasta(string caminho) => _disco.CriarPasta(caminho);
        public void Copiar(string origem, string destino) => _disco.Copiar(origem, destino);
        public void Remover(string caminho) => _disco.Remover(caminho);
        public bool MesmoVolume(string a, string b) => true;
        public Stream AbrirLeitura(string caminho) => _disco.AbrirLeitura(caminho);
        public string Hash(string caminho) => _disco.Hash(caminho);
    }
}
