using Spobras.Core.Abstracoes;

namespace Spobras.Core.Operacoes;

public sealed record ResumoReconciliacao(int Concluidos, int Pendentes, int Conflitos);

/// <summary>
/// Executa um lote de renomear/mover item a item, gravando o historico a cada passo
/// (especificacao 3.3 e 3.4). Nunca sobrescreve e nunca apaga conteudo do usuario:
/// a unica remocao e a da origem de um "mover" entre volumes, depois de a copia ser verificada.
/// </summary>
public sealed class ExecutorLote(IArquivos arquivos, IHistorico historico, IRelogio relogio)
{
    public const string SufixoParcial = ".spobras-parcial";
    private const string PrefixoTemporario = "~spobras-troca-";

    private static readonly StringComparison Comparar = StringComparison.OrdinalIgnoreCase;

    /// <summary>
    /// Executa os itens pendentes (inclusive os cancelados numa execucao anterior).
    /// Itens concluidos, ignorados ou que falharam nao sao repetidos.
    /// </summary>
    public ResultadoLote Executar(Lote lote, CancellationToken cancelamento = default, IProgress<ProgressoLote>? progresso = null)
    {
        foreach (var item in lote.Itens.Where(i => i.Estado == EstadoItem.Cancelado))
        {
            Gravar(lote, item, EstadoItem.Pendente, null);
        }

        int total = lote.Itens.Count;
        while (true)
        {
            var pendentes = lote.Itens.Where(i => i.Estado == EstadoItem.Pendente).ToList();
            if (pendentes.Count == 0)
            {
                break;
            }

            if (cancelamento.IsCancellationRequested)
            {
                Cancelar(lote, pendentes);
                break;
            }

            bool andou = false;
            foreach (var item in pendentes)
            {
                if (cancelamento.IsCancellationRequested)
                {
                    break;
                }

                string fonte = item.Temporario ?? item.Origem;
                if (!arquivos.ArquivoExiste(fonte))
                {
                    Gravar(lote, item, EstadoItem.Falhou, "O arquivo não está mais na pasta de origem.");
                    andou = true;
                    continue;
                }

                bool soMaiusculas = string.Equals(fonte, item.Destino, Comparar)
                                    && !string.Equals(fonte, item.Destino, StringComparison.Ordinal);
                if (arquivos.ArquivoExiste(item.Destino) && !soMaiusculas)
                {
                    continue; // destino ocupado por enquanto; pode ser outro item do lote que ainda vai sair
                }

                progresso?.Report(new ProgressoLote(total - pendentes.Count, total, Path.GetFileName(item.Origem)));
                Transferir(lote, item, fonte);
                andou = true;
            }

            if (andou || cancelamento.IsCancellationRequested)
            {
                continue;
            }

            // Ninguem andou: todos os destinos estao ocupados.
            var bloqueados = lote.Itens.Where(i => i.Estado == EstadoItem.Pendente).ToList();
            var paraEstacionar = bloqueados
                .Select(b => bloqueados.FirstOrDefault(o => o.Temporario is null && string.Equals(o.Origem, b.Destino, Comparar)))
                .FirstOrDefault(o => o is not null);
            if (paraEstacionar is not null)
            {
                // Troca A <-> B: tira um dos dois do caminho por um nome temporario registrado.
                Estacionar(lote, paraEstacionar);
                continue;
            }

            foreach (var item in bloqueados)
            {
                Gravar(lote, item, EstadoItem.Falhou, "Já existe um arquivo com este nome no destino. Nada foi sobrescrito.");
            }
        }

        progresso?.Report(new ProgressoLote(total - lote.Itens.Count(i => i.Estado == EstadoItem.Pendente), total, null));
        return ResultadoLote.De(lote);
    }

    /// <summary>True se o lote ficou com itens no meio do caminho (o app fechou durante a execucao).</summary>
    public static bool PrecisaReconciliar(Lote lote) =>
        lote.Itens.Any(i => i.Estado == EstadoItem.EmExecucao
                            || (i.Temporario is not null && i.Estado is EstadoItem.Pendente or EstadoItem.Cancelado));

    /// <summary>
    /// Depois de um fechamento inesperado: confere no disco o que aconteceu com cada item que
    /// estava em andamento e acerta o historico. Nao repete nada; o usuario decide se continua.
    /// </summary>
    public ResumoReconciliacao Reconciliar(Lote lote)
    {
        int concluidos = 0, pendentes = 0, conflitos = 0;
        foreach (var item in lote.Itens)
        {
            bool emAndamento = item.Estado == EstadoItem.EmExecucao
                               || (item.Temporario is not null && item.Estado is EstadoItem.Pendente or EstadoItem.Cancelado);
            if (!emAndamento)
            {
                continue;
            }

            // Copia incompleta de um mover entre volumes: e nossa (sufixo proprio) e nunca teve o nome final.
            string parcial = item.Destino + SufixoParcial;
            if (arquivos.ArquivoExiste(parcial))
            {
                arquivos.Remover(parcial);
            }

            bool naOrigem = arquivos.ArquivoExiste(item.Origem);
            bool noDestino = arquivos.ArquivoExiste(item.Destino);
            bool noTemporario = item.Temporario is not null && arquivos.ArquivoExiste(item.Temporario);
            bool mesmoCaminho = string.Equals(item.Origem, item.Destino, Comparar);

            if (noTemporario)
            {
                Gravar(lote, item, EstadoItem.Pendente, "Parado no meio de uma troca de nomes; pode continuar.");
                pendentes++;
            }
            else if (noDestino && (!naOrigem || mesmoCaminho))
            {
                item.Temporario = null;
                Concluir(lote, item);
                concluidos++;
            }
            else if (naOrigem && !noDestino)
            {
                item.Temporario = null;
                Gravar(lote, item, EstadoItem.Pendente, null);
                pendentes++;
            }
            else if (naOrigem && noDestino)
            {
                bool iguais = arquivos.Info(item.Origem)?.Tamanho == arquivos.Info(item.Destino)?.Tamanho
                              && arquivos.Hash(item.Origem) == arquivos.Hash(item.Destino);
                if (iguais && lote.Tipo == TipoLote.Mover && !arquivos.MesmoVolume(item.Origem, item.Destino))
                {
                    // A copia para o outro volume terminou e confere; faltava so tirar a entrada de origem.
                    arquivos.Remover(item.Origem);
                    item.Temporario = null;
                    Concluir(lote, item);
                    concluidos++;
                }
                else
                {
                    Gravar(lote, item, EstadoItem.Falhou,
                        "O arquivo existe na origem e no destino. Os dois foram mantidos; confira qual é o certo.");
                    conflitos++;
                }
            }
            else
            {
                Gravar(lote, item, EstadoItem.Falhou, "O arquivo não foi encontrado nem na origem nem no destino.");
                conflitos++;
            }
        }

        return new ResumoReconciliacao(concluidos, pendentes, conflitos);
    }

    private void Transferir(Lote lote, ItemLote item, string fonte)
    {
        Gravar(lote, item, EstadoItem.EmExecucao, null);
        try
        {
            string? pastaDestino = Path.GetDirectoryName(item.Destino);
            if (!string.IsNullOrEmpty(pastaDestino) && !arquivos.PastaExiste(pastaDestino))
            {
                arquivos.CriarPasta(pastaDestino);
            }

            if (arquivos.MesmoVolume(fonte, item.Destino))
            {
                arquivos.Mover(fonte, item.Destino);
            }
            else
            {
                MoverEntreVolumes(fonte, item.Destino);
            }

            item.Temporario = null;
            Concluir(lote, item);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // O arquivo continua na origem (ou no nome temporario registrado no item).
            Gravar(lote, item, EstadoItem.Falhou, ex.Message);
        }
    }

    /// <summary>Copia, confere e so entao tira a origem. Falha em qualquer ponto deixa a origem intacta.</summary>
    private void MoverEntreVolumes(string fonte, string destino)
    {
        string parcial = destino + SufixoParcial;
        if (arquivos.ArquivoExiste(parcial))
        {
            arquivos.Remover(parcial);
        }

        arquivos.Copiar(fonte, parcial);
        bool confere = arquivos.Info(fonte)?.Tamanho == arquivos.Info(parcial)?.Tamanho
                       && arquivos.Hash(fonte) == arquivos.Hash(parcial);
        if (!confere)
        {
            arquivos.Remover(parcial);
            throw new IOException("A cópia não ficou igual ao original. O arquivo continua na origem.");
        }

        arquivos.Mover(parcial, destino); // publica o nome final so depois de conferir
        arquivos.Remover(fonte);
    }

    private void Estacionar(Lote lote, ItemLote item)
    {
        string pasta = Path.GetDirectoryName(item.Origem) ?? "";
        string temporario = Path.Combine(pasta, $"{PrefixoTemporario}{item.Id:N}{Path.GetExtension(item.Origem)}");

        // Grava o nome temporario antes de mover: se o app fechar aqui, da para achar o arquivo.
        item.Temporario = temporario;
        historico.AtualizarItem(lote.Id, item);
        try
        {
            arquivos.Mover(item.Origem, temporario);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            item.Temporario = null;
            Gravar(lote, item, EstadoItem.Falhou, ex.Message);
        }
    }

    private void Cancelar(Lote lote, List<ItemLote> pendentes)
    {
        foreach (var item in pendentes)
        {
            // Quem estava num nome temporario volta para o nome original, se ele estiver livre.
            if (item.Temporario is not null && arquivos.ArquivoExiste(item.Temporario) && !arquivos.ArquivoExiste(item.Origem))
            {
                try
                {
                    arquivos.Mover(item.Temporario, item.Origem);
                    item.Temporario = null;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Fica registrado no item; continuar o lote resolve.
                }
            }

            Gravar(lote, item, EstadoItem.Cancelado, item.Temporario is null ? null : "Parado no meio de uma troca de nomes; continue o lote para concluir.");
        }
    }

    private void Concluir(Lote lote, ItemLote item)
    {
        var info = arquivos.Info(item.Destino);
        item.Tamanho = info?.Tamanho;
        item.ModificadoEm = info?.ModificadoEm;
        item.ConcluidoEm = relogio.Agora;
        Gravar(lote, item, EstadoItem.Concluido, null);
    }

    private void Gravar(Lote lote, ItemLote item, EstadoItem estado, string? mensagem)
    {
        item.Estado = estado;
        item.Mensagem = mensagem;
        historico.AtualizarItem(lote.Id, item);
    }
}
