namespace Spobras.Core.Tarefas;

public enum EstadoTarefa
{
    NaFila,
    EmExecucao,
    Concluida,
    Falhou,
    Cancelada,
}

public sealed record ProgressoTarefa(int Feitos, int Total, string? Detalhe);

public sealed class Tarefa
{
    private readonly CancellationTokenSource _cancelamento = new();

    internal Tarefa(string titulo, Func<IProgress<ProgressoTarefa>, CancellationToken, Task<string?>> trabalho)
    {
        Titulo = titulo;
        Trabalho = trabalho;
    }

    public Guid Id { get; } = Guid.NewGuid();
    public string Titulo { get; }
    public EstadoTarefa Estado { get; internal set; } = EstadoTarefa.NaFila;
    public ProgressoTarefa? Progresso { get; internal set; }

    /// <summary>Resumo do resultado ou motivo da falha.</summary>
    public string? Mensagem { get; internal set; }

    internal Func<IProgress<ProgressoTarefa>, CancellationToken, Task<string?>> Trabalho { get; }
    internal CancellationToken Cancelamento => _cancelamento.Token;
    internal Task Conclusao => Terminou.Task;
    internal TaskCompletionSource Terminou { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Cancelar() => _cancelamento.Cancel();
}

/// <summary>
/// Fila de trabalhos pesados fora da tela: um de cada vez, na ordem de chegada, com progresso
/// e cancelamento. O trabalho devolve o texto do resumo.
/// </summary>
public sealed class FilaTarefas
{
    private readonly object _trava = new();
    private readonly List<Tarefa> _tarefas = [];
    private bool _rodando;

    /// <summary>Disparado (fora da thread da tela) sempre que uma tarefa muda de estado ou de progresso.</summary>
    public event Action<Tarefa>? Mudou;

    public IReadOnlyList<Tarefa> Tarefas
    {
        get
        {
            lock (_trava)
            {
                return [.. _tarefas];
            }
        }
    }

    public Tarefa Enfileirar(string titulo, Func<IProgress<ProgressoTarefa>, CancellationToken, Task<string?>> trabalho)
    {
        var tarefa = new Tarefa(titulo, trabalho);
        bool iniciar;
        lock (_trava)
        {
            _tarefas.Add(tarefa);
            iniciar = !_rodando;
            _rodando = true;
        }

        Mudou?.Invoke(tarefa);
        if (iniciar)
        {
            _ = Task.Run(RodarAsync);
        }

        return tarefa;
    }

    /// <summary>Espera uma tarefa terminar (concluida, com falha ou cancelada).</summary>
    public static Task Aguardar(Tarefa tarefa) => tarefa.Conclusao;

    private async Task RodarAsync()
    {
        while (true)
        {
            Tarefa? proxima;
            lock (_trava)
            {
                proxima = _tarefas.FirstOrDefault(t => t.Estado == EstadoTarefa.NaFila);
                if (proxima is null)
                {
                    _rodando = false;
                    return;
                }

                proxima.Estado = EstadoTarefa.EmExecucao;
            }

            await RodarUmaAsync(proxima).ConfigureAwait(false);
        }
    }

    private async Task RodarUmaAsync(Tarefa tarefa)
    {
        if (tarefa.Cancelamento.IsCancellationRequested)
        {
            Terminar(tarefa, EstadoTarefa.Cancelada, "Cancelada antes de começar.");
            return;
        }

        Mudou?.Invoke(tarefa);
        var progresso = new ProgressoDireto(p =>
        {
            tarefa.Progresso = p;
            Mudou?.Invoke(tarefa);
        });

        try
        {
            string? resumo = await tarefa.Trabalho(progresso, tarefa.Cancelamento).ConfigureAwait(false);
            Terminar(tarefa, tarefa.Cancelamento.IsCancellationRequested ? EstadoTarefa.Cancelada : EstadoTarefa.Concluida, resumo);
        }
        catch (OperationCanceledException)
        {
            Terminar(tarefa, EstadoTarefa.Cancelada, "Cancelada.");
        }
        catch (Exception ex)
        {
            // Uma tarefa com erro nao derruba a fila: fica registrada com o motivo.
            Terminar(tarefa, EstadoTarefa.Falhou, ex.Message);
        }
    }

    private void Terminar(Tarefa tarefa, EstadoTarefa estado, string? mensagem)
    {
        tarefa.Estado = estado;
        tarefa.Mensagem = mensagem;
        Mudou?.Invoke(tarefa);
        tarefa.Terminou.TrySetResult();
    }

    /// <summary>Progresso entregue na hora, na thread do trabalho (sem depender de contexto de tela).</summary>
    private sealed class ProgressoDireto(Action<ProgressoTarefa> aoMudar) : IProgress<ProgressoTarefa>
    {
        public void Report(ProgressoTarefa value) => aoMudar(value);
    }
}
