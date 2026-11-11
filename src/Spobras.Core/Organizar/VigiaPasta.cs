using Spobras.Core.Abstracoes;

namespace Spobras.Core.Organizar;

/// <summary>
/// Vigia uma pasta (so o primeiro nivel) e avisa quando ha arquivos novos prontos.
/// Usa os eventos do sistema e tambem varre a pasta de tempos em tempos, porque os eventos
/// podem se perder (especificacao 4.1). So observa: nunca move nada.
/// </summary>
public sealed class VigiaPasta : IDisposable
{
    private const int AtributoSoNaNuvem = 0x1000 | 0x400000; // offline | baixar ao abrir

    private readonly string _pasta;
    private readonly IRelogio _relogio;
    private readonly FilaChegada _fila;
    private readonly TimeSpan _intervaloVarredura;
    private readonly object _trava = new();
    private readonly HashSet<string> _conhecidos = new(StringComparer.OrdinalIgnoreCase);
    private FileSystemWatcher? _vigia;
    private Timer? _relogioInterno;
    private DateTimeOffset _ultimaVarredura = DateTimeOffset.MinValue;
    private bool _primeiraVarredura = true;

    public VigiaPasta(string pasta, IRelogio relogio, TimeSpan? quietoPor = null, TimeSpan? intervaloVarredura = null)
    {
        _pasta = pasta;
        _relogio = relogio;
        _fila = new FilaChegada(quietoPor ?? TimeSpan.FromSeconds(3));
        _intervaloVarredura = intervaloVarredura ?? TimeSpan.FromSeconds(30);
    }

    /// <summary>Arquivos novos prontos para sugestao. Chamado fora da thread da tela.</summary>
    public event Action<IReadOnlyList<ArquivoParaOrganizar>>? Novos;

    /// <summary>
    /// Arquivos criados depois deste momento contam como novidade ja na primeira varredura
    /// (chegaram com o app fechado). Null: so o que chegar a partir de agora.
    /// </summary>
    public DateTimeOffset? NovidadesDesde { get; init; }

    public string Pasta => _pasta;

    public void Iniciar()
    {
        lock (_trava)
        {
            if (_relogioInterno is not null)
            {
                return;
            }

            LigarEventos();
            _relogioInterno = new Timer(_ => Rodada(), null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
        }
    }

    public void Dispose()
    {
        lock (_trava)
        {
            _relogioInterno?.Dispose();
            _relogioInterno = null;
            _vigia?.Dispose();
            _vigia = null;
        }
    }

    /// <summary>Uma rodada: varre se estiver na hora e entrega o que ficou pronto. Publico para testes.</summary>
    public void Rodada()
    {
        IReadOnlyList<ArquivoParaOrganizar> prontos;
        lock (_trava)
        {
            var agora = _relogio.Agora;
            if (agora - _ultimaVarredura >= _intervaloVarredura)
            {
                Varrer(agora);
                if (_vigia is null)
                {
                    LigarEventos(); // a pasta pode ter voltado (pen drive, rede)
                }
            }

            prontos = _fila.Prontos(agora, Disponivel);
            foreach (var pronto in prontos)
            {
                _conhecidos.Add(pronto.Caminho); // ja avisado: salvar de novo nao vira novidade outra vez
            }
        }

        if (prontos.Count > 0)
        {
            Novos?.Invoke(prontos);
        }
    }

    private void LigarEventos()
    {
        try
        {
            if (!Directory.Exists(_pasta))
            {
                return;
            }

            var vigia = new FileSystemWatcher(_pasta)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                IncludeSubdirectories = false,
            };
            vigia.Created += (_, e) => Visto(e.FullPath, chegouAgora: true);
            vigia.Changed += (_, e) => Visto(e.FullPath, chegouAgora: true);
            vigia.Renamed += (_, e) =>
            {
                lock (_trava)
                {
                    _fila.Esquecer(e.OldFullPath);
                    _conhecidos.Remove(e.OldFullPath);
                }

                Visto(e.FullPath, chegouAgora: true); // download terminado: .crdownload vira o nome final
            };
            vigia.Deleted += (_, e) =>
            {
                lock (_trava)
                {
                    _fila.Esquecer(e.FullPath);
                    _conhecidos.Remove(e.FullPath);
                }
            };
            vigia.Error += (_, _) =>
            {
                // Eventos perdidos: a proxima rodada varre a pasta inteira.
                lock (_trava)
                {
                    _ultimaVarredura = DateTimeOffset.MinValue;
                }
            };
            vigia.EnableRaisingEvents = true;
            _vigia = vigia;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _vigia = null; // segue so com a varredura periodica
        }
    }

    private void Visto(string caminho, bool chegouAgora)
    {
        try
        {
            var info = new FileInfo(caminho);
            if (!info.Exists)
            {
                return; // pasta, ou arquivo que ja saiu
            }

            lock (_trava)
            {
                // Arquivo que ja estava na pasta e so foi salvo de novo nao e novidade.
                if (chegouAgora && _conhecidos.Contains(caminho))
                {
                    return;
                }

                _fila.Observar(caminho, info.Length, info.LastWriteTimeUtc, _relogio.Agora, chegouAgora);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Ocupado agora; a varredura pega depois.
        }
    }

    private void Varrer(DateTimeOffset agora)
    {
        _ultimaVarredura = agora;
        try
        {
            if (!Directory.Exists(_pasta))
            {
                return;
            }

            foreach (var info in new DirectoryInfo(_pasta).EnumerateFiles())
            {
                if (_primeiraVarredura)
                {
                    bool chegouComAppFechado = NovidadesDesde is { } desde && info.CreationTimeUtc > desde.UtcDateTime;
                    if (chegouComAppFechado)
                    {
                        _fila.Observar(info.FullName, info.Length, info.LastWriteTimeUtc, agora, chegouAgora: false);
                    }
                    else
                    {
                        _conhecidos.Add(info.FullName);
                    }
                }
                else if (!_conhecidos.Contains(info.FullName))
                {
                    // Chegou sem evento (ou o evento se perdeu).
                    _fila.Observar(info.FullName, info.Length, info.LastWriteTimeUtc, agora, chegouAgora: true);
                }
            }

            _primeiraVarredura = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Pasta indisponivel agora (pen drive, rede): tenta de novo na proxima varredura.
        }
    }

    /// <summary>Pronto para uso: existe, esta no disco (nao so na nuvem) e ninguem esta gravando nele.</summary>
    private static bool Disponivel(string caminho)
    {
        try
        {
            var info = new FileInfo(caminho);
            if (!info.Exists || ((int)info.Attributes & AtributoSoNaNuvem) != 0)
            {
                return false;
            }

            using var fluxo = new FileStream(caminho, FileMode.Open, FileAccess.Read, FileShare.Read);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
