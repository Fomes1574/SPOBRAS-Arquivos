using System.Diagnostics;
using Spobras.Core.Abstracoes;
using Spobras.Core.Base;
using Spobras.Core.Planilhas;
using Spobras.Core.Tarefas;
using Spobras.Dados.Base;

namespace Spobras.App.Servicos;

/// <summary>
/// Fontes de dados (arquitetura, secao 5.1): o usuario aponta o arquivo uma vez; quando ele e salvo, o app
/// percebe, espera a gravacao terminar e rele sozinho em segundo plano. Se a leitura falhar, a base anterior
/// continua valendo e a fonte mostra a data da ultima leitura valida e o motivo. O app so le: nunca escreve,
/// move ou renomeia as planilhas.
/// </summary>
public sealed class ServicoFontes : IDisposable
{
    private readonly BaseRepositorio _base;
    private readonly Func<string, ILeitorPlanilha> _abrir;
    private readonly IRelogio _relogio;
    private readonly FilaTarefas _fila;
    private readonly ServicoPreferencias _preferencias;
    private readonly object _trava = new();
    private readonly Dictionary<string, (DateTime Modificado, long Tamanho, DateTimeOffset VistoEm)> _observado = [];
    private readonly Dictionary<string, (DateTime Modificado, long Tamanho, DateTimeOffset Em)> _falhou = [];
    private readonly HashSet<string> _lendo = [];
    private readonly Dictionary<string, DateTimeOffset> _ausenteDesde = [];
    private readonly List<FileSystemWatcher> _vigias = [];
    private Timer? _relogioInterno;

    public ServicoFontes(
        BaseRepositorio @base, Func<string, ILeitorPlanilha> abrir, IRelogio relogio, FilaTarefas fila, ServicoPreferencias preferencias)
    {
        _base = @base;
        _abrir = abrir;
        _relogio = relogio;
        _fila = fila;
        _preferencias = preferencias;
    }

    /// <summary>Tempo sem mudanca no arquivo antes de reler (a gravacao/sincronizacao terminou).</summary>
    public TimeSpan Quieto { get; init; } = TimeSpan.FromSeconds(4);

    /// <summary>Depois de uma falha com o mesmo arquivo, espera isto antes de tentar de novo (sem insistir em laco).</summary>
    public TimeSpan EsperaAposFalha { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>Usar os eventos do sistema alem da conferencia periodica. False nos testes, que mandam conferir.</summary>
    public bool UsarEventos { get; init; } = true;

    /// <summary>Uma fonte foi lida, falhou ou mudou de cadastro. Disparado fora da thread da tela.</summary>
    public event Action? Mudou;

    public IReadOnlyList<FonteInfo> Fontes()
    {
        var gravadas = _base.Fontes().ToDictionary(f => f.Id);
        // As duas fontes conhecidas aparecem sempre, mesmo antes de o arquivo ser apontado.
        return
        [
            gravadas.GetValueOrDefault(Core.Base.Fontes.Mestra) ?? new FonteInfo { Id = Core.Base.Fontes.Mestra, Nome = "Planilha mestra", Mensagem = "Aponte o arquivo " + Core.Base.Fontes.NomeArquivoMestra + "." },
            gravadas.GetValueOrDefault(Core.Base.Fontes.Dotacoes) ?? new FonteInfo { Id = Core.Base.Fontes.Dotacoes, Nome = "Portarias e dotações", Mensagem = "Aponte o arquivo " + Core.Base.Fontes.NomeArquivoDotacoes + "." },
        ];
    }

    /// <summary>
    /// Ao abrir o app: procura pelo nome, na pasta das planilhas base, as fontes ainda nao apontadas;
    /// confere se alguma mudou com o app fechado; e passa a vigiar.
    /// </summary>
    public void Iniciar(bool vigiar = true)
    {
        foreach (var fonte in Fontes().Where(f => f.Caminho.Length == 0))
        {
            if (Procurar(fonte.Id) is { } achado)
            {
                _base.GravarFonte(fonte with { Caminho = achado, Mensagem = "Encontrada na pasta das planilhas base. Aguardando a primeira leitura." });
            }
        }

        Mudou?.Invoke();
        if (vigiar)
        {
            LigarVigias();
            _relogioInterno = new Timer(_ => ConferirMudancas(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5));
        }
    }

    public void Dispose()
    {
        _relogioInterno?.Dispose();
        foreach (var vigia in _vigias)
        {
            vigia.Dispose();
        }
    }

    /// <summary>O usuario apontou (ou trocou) o arquivo de uma fonte: guarda o caminho e le.</summary>
    public Task DefinirAsync(string id, string caminho)
    {
        var atual = Fontes().First(f => f.Id == id);
        _base.GravarFonte(atual with { Caminho = caminho, Mensagem = "Aguardando a primeira leitura deste arquivo." });
        lock (_trava)
        {
            _falhou.Remove(id);
            _observado.Remove(id);
        }

        LigarVigias();
        Mudou?.Invoke();
        return RelerAsync(id);
    }

    /// <summary>"Reler agora".</summary>
    public Task RelerAsync(string id)
    {
        lock (_trava)
        {
            if (!_lendo.Add(id))
            {
                return Task.CompletedTask; // ja ha uma leitura desta fonte em andamento
            }
        }

        var fonte = Fontes().First(f => f.Id == id);
        var tarefa = _fila.Enfileirar($"Lendo {fonte.Nome.ToLowerInvariant()}", (_, _) =>
        {
            try
            {
                return Task.FromResult<string?>(Importar(fonte));
            }
            finally
            {
                lock (_trava)
                {
                    _lendo.Remove(id);
                }

                Mudou?.Invoke();
            }
        });
        return FilaTarefas.Aguardar(tarefa);
    }

    /// <summary>
    /// Compara cada arquivo com o que foi lido por ultimo. Mudou e ficou quieto pelo tempo pedido: rele.
    /// Chamado pelo relogio interno, pelos eventos do sistema e ao abrir o app; publico para testes.
    /// </summary>
    public void ConferirMudancas()
    {
        var agora = _relogio.Agora;
        foreach (var fonte in Fontes().Where(f => f.Caminho.Length > 0))
        {
            FileInfo info;
            try
            {
                info = new FileInfo(fonte.Caminho);
                if (!info.Exists)
                {
                    // Ao salvar, o Excel troca o arquivo e ele some por um instante: so vale como
                    // "nao encontrado" se continuar ausente pelo tempo de espera.
                    bool ausenteDeVerdade;
                    lock (_trava)
                    {
                        if (!_ausenteDesde.TryGetValue(fonte.Id, out var desde))
                        {
                            _ausenteDesde[fonte.Id] = desde = agora;
                        }

                        ausenteDeVerdade = agora - desde >= Quieto;
                    }

                    if (ausenteDeVerdade && fonte.Estado != EstadoFonte.ArquivoNaoEncontrado)
                    {
                        string? candidato = Procurar(fonte.Id);
                        _base.GravarFonte(fonte with
                        {
                            Estado = EstadoFonte.ArquivoNaoEncontrado,
                            Mensagem = candidato is null
                                ? "O arquivo não está mais neste lugar. Os dados mostrados são da última leitura válida. Aponte o arquivo de novo."
                                : $"O arquivo não está mais neste lugar, mas há um com o mesmo nome em: {candidato}. Confirme para usar esse.",
                        });
                        Mudou?.Invoke();
                    }

                    continue;
                }

                lock (_trava)
                {
                    _ausenteDesde.Remove(fonte.Id);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                continue; // pasta fora do ar agora; tenta na proxima rodada
            }

            var chave = (info.LastWriteTimeUtc, info.Length);
            bool igualAoLido = fonte.Estado == EstadoFonte.Atualizada && fonte.ModificadoArquivo?.UtcDateTime == chave.LastWriteTimeUtc && fonte.TamanhoArquivo == chave.Length;
            bool ler;
            lock (_trava)
            {
                if (igualAoLido || _lendo.Contains(fonte.Id))
                {
                    _observado.Remove(fonte.Id);
                    continue;
                }

                if (_falhou.TryGetValue(fonte.Id, out var falha) && falha.Modificado == chave.LastWriteTimeUtc && falha.Tamanho == chave.Length
                    && agora - falha.Em < EsperaAposFalha)
                {
                    continue; // mesmo arquivo que acabou de falhar: nao insiste
                }

                if (!_observado.TryGetValue(fonte.Id, out var visto) || visto.Modificado != chave.LastWriteTimeUtc || visto.Tamanho != chave.Length)
                {
                    _observado[fonte.Id] = (chave.LastWriteTimeUtc, chave.Length, agora); // ainda mudando: recomeca a espera
                    continue;
                }

                ler = agora - visto.VistoEm >= Quieto;
            }

            if (ler)
            {
                _ = RelerAsync(fonte.Id);
            }
        }
    }

    /// <summary>Arquivo com o nome padrao da fonte na pasta das planilhas base (so nela), ou null.</summary>
    public string? Procurar(string id)
    {
        string pasta = _preferencias.Atual.PastaPlanilhasBase;
        if (string.IsNullOrWhiteSpace(pasta))
        {
            return null;
        }

        string nome = id == Core.Base.Fontes.Mestra ? Core.Base.Fontes.NomeArquivoMestra : Core.Base.Fontes.NomeArquivoDotacoes;
        try
        {
            string caminho = Path.Combine(pasta, nome);
            return File.Exists(caminho) ? caminho : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Le e valida a fonte inteira; so entao troca a base. Devolve o resumo para a fila de tarefas.</summary>
    private string Importar(FonteInfo fonte)
    {
        var relogio = Stopwatch.StartNew();
        var agora = _relogio.Agora;
        DateTime modificado = default;
        long tamanho = 0;
        try
        {
            var info = new FileInfo(fonte.Caminho);
            if (!info.Exists)
            {
                throw new PlanilhaAusenteException("O arquivo não foi encontrado neste lugar.");
            }

            (modificado, tamanho) = (info.LastWriteTimeUtc, info.Length);
            int linhas;
            IReadOnlyList<string> avisos;
            var nova = fonte with
            {
                Estado = EstadoFonte.Atualizada,
                Mensagem = "",
                UltimaLeitura = agora,
                ModificadoArquivo = new DateTimeOffset(modificado, TimeSpan.Zero),
                TamanhoArquivo = tamanho,
                Versao = fonte.Versao + 1,
            };

            using (var planilha = _abrir(fonte.Caminho))
            {
                if (fonte.Id == Core.Base.Fontes.Mestra)
                {
                    var dados = ImportadorMestra.Importar(planilha);
                    ConferirQueNaoMudou(fonte.Caminho, modificado, tamanho);
                    (linhas, avisos) = (dados.Obras.Count, dados.Avisos);
                    _base.GravarMestra(dados, nova with { Linhas = linhas, Avisos = avisos });
                }
                else
                {
                    var dados = ImportadorDotacoes.Importar(planilha);
                    ConferirQueNaoMudou(fonte.Caminho, modificado, tamanho);
                    (linhas, avisos) = (dados.Vinculos.Count, dados.Avisos);
                    _base.GravarDotacoes(dados, nova with { Linhas = linhas, Avisos = avisos });
                }
            }

            lock (_trava)
            {
                _falhou.Remove(fonte.Id);
                _observado.Remove(fonte.Id);
            }

            _base.RegistrarImportacao(fonte.Id, agora, true, "Leitura concluída.", linhas, relogio.ElapsedMilliseconds);
            return $"{linhas} linha(s) lidas";
        }
        catch (Exception ex) when (ex is PlanilhaAusenteException or ImportacaoInvalidaException or IOException or UnauthorizedAccessException
                                       or Spobras.Documentos.Planilhas.PlanilhaInvalidaException)
        {
            // A base anterior continua valendo; a fonte mostra o motivo e a data da ultima leitura valida.
            bool ausente = ex is PlanilhaAusenteException;
            string quando = fonte.UltimaLeitura is { } u ? $" Os dados mostrados são da leitura de {u.ToLocalTime():dd/MM/yyyy 'às' HH:mm}." : " Ainda não há dados desta fonte.";
            _base.GravarFonte(fonte with
            {
                Estado = ausente ? EstadoFonte.ArquivoNaoEncontrado : fonte.UltimaLeitura is null ? EstadoFonte.NuncaLida : EstadoFonte.Desatualizada,
                Mensagem = ex.Message + quando,
            });
            _base.RegistrarImportacao(fonte.Id, agora, false, ex.Message, 0, relogio.ElapsedMilliseconds);
            lock (_trava)
            {
                _falhou[fonte.Id] = (modificado, tamanho, agora);
                _observado.Remove(fonte.Id);
            }

            return "Não foi possível ler: " + ex.Message;
        }
    }

    /// <summary>Se o arquivo foi salvo de novo durante a leitura, o que foi lido pode estar pela metade: descarta.</summary>
    private static void ConferirQueNaoMudou(string caminho, DateTime modificado, long tamanho)
    {
        var info = new FileInfo(caminho);
        if (!info.Exists || info.LastWriteTimeUtc != modificado || info.Length != tamanho)
        {
            throw new IOException("A planilha foi salva de novo durante a leitura. O app vai ler outra vez em instantes.");
        }
    }

    private void LigarVigias()
    {
        foreach (var vigia in _vigias)
        {
            vigia.Dispose();
        }

        _vigias.Clear();
        if (!UsarEventos)
        {
            return;
        }

        foreach (var fonte in Fontes().Where(f => f.Caminho.Length > 0))
        {
            try
            {
                string? pasta = Path.GetDirectoryName(fonte.Caminho);
                if (pasta is null || !Directory.Exists(pasta))
                {
                    continue;
                }

                var vigia = new FileSystemWatcher(pasta, Path.GetFileName(fonte.Caminho))
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                };
                vigia.Changed += (_, _) => ConferirMudancas();
                vigia.Created += (_, _) => ConferirMudancas();
                vigia.Renamed += (_, _) => ConferirMudancas();
                vigia.EnableRaisingEvents = true;
                _vigias.Add(vigia);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // Sem eventos nesta pasta: a conferencia periodica cobre.
            }
        }
    }

    private sealed class PlanilhaAusenteException(string mensagem) : Exception(mensagem);
}
