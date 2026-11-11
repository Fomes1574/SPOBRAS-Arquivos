using Spobras.Core.Tarefas;

namespace Spobras.Core.Tests;

public class TarefasTests
{
    [Fact]
    public async Task Roda_uma_de_cada_vez_na_ordem_e_falha_nao_para_a_fila()
    {
        var fila = new FilaTarefas();
        var ordem = new List<string>();
        int simultaneas = 0, maximo = 0;

        Func<IProgress<ProgressoTarefa>, CancellationToken, Task<string?>> Trabalho(string nome, bool falha = false) => async (p, ct) =>
        {
            maximo = Math.Max(maximo, Interlocked.Increment(ref simultaneas));
            await Task.Delay(20, ct);
            lock (ordem)
            {
                ordem.Add(nome);
            }

            p.Report(new ProgressoTarefa(1, 1, nome));
            Interlocked.Decrement(ref simultaneas);
            return falha ? throw new IOException("disco cheio") : $"{nome} pronto";
        };

        var a = fila.Enfileirar("A", Trabalho("a"));
        var b = fila.Enfileirar("B", Trabalho("b", falha: true));
        var c = fila.Enfileirar("C", Trabalho("c"));
        await Task.WhenAll(FilaTarefas.Aguardar(a), FilaTarefas.Aguardar(b), FilaTarefas.Aguardar(c));

        Assert.Equal(["a", "b", "c"], ordem);
        Assert.Equal(1, maximo);
        Assert.Equal((EstadoTarefa.Concluida, "a pronto"), (a.Estado, a.Mensagem));
        Assert.Equal((EstadoTarefa.Falhou, "disco cheio"), (b.Estado, b.Mensagem));
        Assert.Equal(EstadoTarefa.Concluida, c.Estado);
        Assert.Equal(new ProgressoTarefa(1, 1, "c"), c.Progresso);
        Assert.Equal(3, fila.Tarefas.Count);
    }

    [Fact]
    public async Task Cancelar_em_execucao_e_na_fila()
    {
        var fila = new FilaTarefas();
        var comecou = new TaskCompletionSource();
        var mudancas = new List<EstadoTarefa>();
        fila.Mudou += t =>
        {
            lock (mudancas)
            {
                mudancas.Add(t.Estado);
            }
        };

        var longa = fila.Enfileirar("Longa", async (_, ct) =>
        {
            comecou.SetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return null;
        });
        var naFila = fila.Enfileirar("Na fila", (_, _) => Task.FromResult<string?>("não deveria rodar"));

        await comecou.Task;
        naFila.Cancelar();
        longa.Cancelar();
        await Task.WhenAll(FilaTarefas.Aguardar(longa), FilaTarefas.Aguardar(naFila));

        Assert.Equal(EstadoTarefa.Cancelada, longa.Estado);
        Assert.Equal(EstadoTarefa.Cancelada, naFila.Estado);
        Assert.Equal("Cancelada antes de começar.", naFila.Mensagem);
        Assert.Contains(EstadoTarefa.EmExecucao, mudancas);

        // A fila continua aceitando trabalho depois de esvaziar.
        var nova = fila.Enfileirar("Nova", (_, _) => Task.FromResult<string?>("ok"));
        await FilaTarefas.Aguardar(nova);
        Assert.Equal(EstadoTarefa.Concluida, nova.Estado);
    }
}
