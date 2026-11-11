using Spobras.Core.Abstracoes;
using Spobras.Core.Operacoes;
using Spobras.Core.Tarefas;

namespace Spobras.App.Servicos;

/// <summary>
/// O fluxo unico de toda operacao em arquivos (arquitetura, secao 7): o plano ja confirmado na previa
/// vira lote, roda na fila de tarefas item a item gravando o historico, e devolve o resumo.
/// </summary>
public sealed class ServicoOperacoes(IArquivos arquivos, IHistorico historico, IRelogio relogio, FilaTarefas fila)
{
    private readonly ExecutorLote _executor = new(arquivos, historico, relogio);
    private readonly Desfazer _desfazer = new(arquivos, relogio);

    /// <summary>O historico mudou (lote novo, concluido, desfeito). Disparado fora da thread da tela.</summary>
    public event Action? HistoricoMudou;

    public FilaTarefas Fila => fila;

    public async Task<Lote> ExecutarAsync(Lote lote)
    {
        historico.CriarLote(lote);
        HistoricoMudou?.Invoke();
        await RodarAsync(lote, lote.Descricao).ConfigureAwait(false);
        return lote;
    }

    /// <summary>Continua um lote que ficou pela metade (cancelado ou interrompido por fechamento do app).</summary>
    public async Task<Lote> ContinuarAsync(Lote lote)
    {
        if (ExecutorLote.PrecisaReconciliar(lote))
        {
            _executor.Reconciliar(lote);
        }

        await RodarAsync(lote, "Continuar: " + lote.Descricao).ConfigureAwait(false);
        return lote;
    }

    public PlanoDesfazer PlanejarDesfazer(Lote lote) => _desfazer.Planejar(lote);

    public async Task<Lote> DesfazerAsync(PlanoDesfazer plano)
    {
        var volta = _desfazer.CriarLote(plano);
        historico.CriarLote(volta);
        await RodarAsync(volta, volta.Descricao).ConfigureAwait(false);
        if (volta.Contar(EstadoItem.Concluido) > 0)
        {
            historico.MarcarDesfeito(plano.Original.Id, volta.Id);
            plano.Original.DesfeitoPorLoteId = volta.Id;
        }

        HistoricoMudou?.Invoke();
        return volta;
    }

    /// <summary>Registra arquivos que o app gerou (zip, relatorio...), para aparecerem nas ultimas acoes.</summary>
    public Lote RegistrarCriados(string descricao, IEnumerable<string> caminhos)
    {
        var agora = relogio.Agora;
        var lote = new Lote
        {
            Tipo = TipoLote.Criar,
            Descricao = descricao,
            CriadoEm = agora,
            Itens = [.. caminhos.Select((c, ordem) =>
            {
                var info = arquivos.Info(c);
                return new ItemLote
                {
                    Ordem = ordem,
                    Origem = c,
                    Destino = c,
                    Estado = EstadoItem.Concluido,
                    Tamanho = info?.Tamanho,
                    ModificadoEm = info?.ModificadoEm,
                    ConcluidoEm = agora,
                };
            })],
        };
        historico.CriarLote(lote);
        HistoricoMudou?.Invoke();
        return lote;
    }

    public IReadOnlyList<Lote> Ultimos(int maximo = 100) => historico.Listar(maximo);

    /// <summary>Lotes que ficaram no meio do caminho quando o app fechou: acerta o historico pelo disco.</summary>
    public int ReconciliarInterrompidos()
    {
        int acertados = 0;
        foreach (var lote in historico.Listar(200).Where(ExecutorLote.PrecisaReconciliar))
        {
            _executor.Reconciliar(lote);
            acertados++;
        }

        if (acertados > 0)
        {
            HistoricoMudou?.Invoke();
        }

        return acertados;
    }

    private async Task RodarAsync(Lote lote, string titulo)
    {
        var tarefa = fila.Enfileirar(titulo, (progresso, cancelamento) =>
        {
            var resultado = _executor.Executar(lote, cancelamento,
                new Progresso(p => progresso.Report(new ProgressoTarefa(p.Feitos, p.Total, p.Atual))));
            return Task.FromResult<string?>(Resumo(resultado));
        });
        await FilaTarefas.Aguardar(tarefa).ConfigureAwait(false);
        HistoricoMudou?.Invoke();
    }

    public static string Resumo(ResultadoLote r)
    {
        var partes = new List<string> { $"{r.Concluidos} concluído(s)" };
        if (r.Falhas > 0)
        {
            partes.Add($"{r.Falhas} com falha");
        }

        if (r.Cancelados > 0)
        {
            partes.Add($"{r.Cancelados} pendente(s)");
        }

        if (r.Ignorados > 0)
        {
            partes.Add($"{r.Ignorados} ignorado(s)");
        }

        return string.Join(", ", partes);
    }

    private sealed class Progresso(Action<ProgressoLote> aoMudar) : IProgress<ProgressoLote>
    {
        public void Report(ProgressoLote value) => aoMudar(value);
    }
}
