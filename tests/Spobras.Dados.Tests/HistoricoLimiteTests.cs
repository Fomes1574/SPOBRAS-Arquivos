using Spobras.Core;
using Spobras.Core.Base;
using Spobras.Core.Operacoes;
using Spobras.Dados.Base;
using Spobras.Dados.Historico;

namespace Spobras.Dados.Tests;

/// <summary>Historico com tamanho certo: 20 acoes e 5 leituras da planilha; a mais antiga sai primeiro.</summary>
public sealed class HistoricoLimiteTests : IDisposable
{
    private readonly string _pasta = Directory.CreateTempSubdirectory("spobras-limite-").FullName;
    private readonly BancoApp _banco;

    public HistoricoLimiteTests() => _banco = new BancoApp(Path.Combine(_pasta, "app.db"));

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    private static Lote Lote(int n, EstadoItem estado = EstadoItem.Concluido, string? temporario = null) => new()
    {
        Tipo = TipoLote.Renomear,
        Descricao = $"Ação {n}",
        CriadoEm = new DateTimeOffset(2026, 11, 1, 8, 0, 0, TimeSpan.FromHours(-3)).AddMinutes(n),
        Itens = [new ItemLote { Origem = $"a{n}.pdf", Destino = $"b{n}.pdf", Estado = estado, Temporario = temporario }],
    };

    [Fact]
    public void Guarda_as_20_ultimas_acoes_e_apaga_as_mais_antigas()
    {
        var historico = new HistoricoSqlite(_banco);
        for (int n = 1; n <= 26; n++)
        {
            historico.CriarLote(Lote(n));
        }

        var lista = historico.Listar(100);
        Assert.Equal(20, lista.Count);
        Assert.Equal("Ação 26", lista[0].Descricao);
        Assert.Equal("Ação 7", lista[^1].Descricao);
        Assert.All(lista, l => Assert.Single(l.Itens)); // os itens das que sairam tambem sairam, os das que ficaram nao

        using var c = _banco.Abrir();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "select count(*) from LoteItem";
        Assert.Equal(20L, (long)cmd.ExecuteScalar()!);
    }

    [Fact]
    public void Acao_interrompida_no_meio_nao_sai_para_poder_ser_retomada()
    {
        var historico = new HistoricoSqlite(_banco);
        historico.CriarLote(Lote(1, EstadoItem.EmExecucao, temporario: "~spobras-troca-1.pdf"));
        for (int n = 2; n <= 30; n++)
        {
            historico.CriarLote(Lote(n));
        }

        var lista = historico.Listar(100);
        Assert.Equal(21, lista.Count);
        Assert.Contains(lista, l => l.Descricao == "Ação 1");
    }

    [Fact]
    public void Guarda_as_5_ultimas_leituras_de_cada_planilha()
    {
        var repo = new BaseRepositorio(_banco);
        var inicio = new DateTimeOffset(2026, 11, 16, 8, 0, 0, TimeSpan.FromHours(-3));
        for (int n = 1; n <= 9; n++)
        {
            repo.RegistrarImportacao(Fontes.Mestra, inicio.AddHours(n), true, $"Leitura {n}", 100, 10);
        }

        repo.RegistrarImportacao("outra", inicio, false, "Outra planilha", 0, 0);
        var leituras = repo.HistoricoDeImportacoes(Fontes.Mestra, 50);
        Assert.Equal(["Leitura 9", "Leitura 8", "Leitura 7", "Leitura 6", "Leitura 5"], leituras.Select(l => l.Mensagem));
        Assert.Single(repo.HistoricoDeImportacoes("outra", 50)); // cada planilha com as suas
    }

    [Fact]
    public void Horario_e_sempre_o_de_Brasilia()
    {
        var utc = new DateTimeOffset(2026, 11, 16, 17, 32, 0, TimeSpan.Zero);
        Assert.Equal("16/11/2026 às 14:32", HoraDeBrasilia.Texto(utc));
        Assert.Equal("16/11/2026 às 14:32", HoraDeBrasilia.Texto(utc.ToOffset(TimeSpan.FromHours(9)))); // aparelho em outro fuso
        Assert.Equal("01/01/2027 às 21:05", HoraDeBrasilia.Texto(new DateTimeOffset(2027, 1, 2, 0, 5, 0, TimeSpan.Zero)));
    }
}
