using Spobras.Core.Abstracoes;
using Spobras.Core.Organizar;
using Spobras.Dados.Configuracao;

namespace Spobras.App.Servicos;

/// <summary>
/// Modo automatico: vigia a pasta de trabalho, prepara as sugestoes e avisa. Nunca move sozinho
/// (especificacao 4.1). Tambem guarda as regras do organizador.
/// </summary>
public sealed class ServicoNovidades : IDisposable
{
    private readonly IArquivos _arquivos;
    private readonly IRelogio _relogio;
    private readonly ServicoPreferencias _preferencias;
    private readonly RegraRepositorio _regrasGuardadas;
    private readonly string _pastaDadosDoApp;
    private readonly object _trava = new();
    private readonly Dictionary<string, ArquivoParaOrganizar> _novidades = new(StringComparer.OrdinalIgnoreCase);
    private VigiaPasta? _vigia;

    public ServicoNovidades(
        IArquivos arquivos, IRelogio relogio, ServicoPreferencias preferencias, RegraRepositorio regras, string pastaDadosDoApp)
    {
        _arquivos = arquivos;
        _relogio = relogio;
        _preferencias = preferencias;
        _regrasGuardadas = regras;
        _pastaDadosDoApp = pastaDadosDoApp;
        Regras = regras.Listar();
    }

    /// <summary>Chegaram arquivos novos (quantos nesta leva). Disparado fora da thread da tela.</summary>
    public event Action<int>? Chegaram;

    /// <summary>A lista de novidades mudou (chegada, movimentacao, limpeza).</summary>
    public event Action? Mudou;

    public IReadOnlyList<Regra> Regras { get; private set; }

    public bool Vigiando => _vigia is not null;

    public int Quantidade
    {
        get
        {
            lock (_trava)
            {
                return _novidades.Count;
            }
        }
    }

    public void GravarRegras(IReadOnlyList<Regra> regras)
    {
        _regrasGuardadas.GravarTodas(regras);
        Regras = regras;
        Mudou?.Invoke();
    }

    /// <summary>Liga ou desliga a vigia conforme as preferencias (chamar ao iniciar e quando elas mudarem).</summary>
    public void Aplicar()
    {
        var p = _preferencias.Atual;
        bool deveVigiar = p.ModoAutomatico && _arquivos.PastaExiste(p.PastaTrabalho);
        if (_vigia is not null && (!deveVigiar || !string.Equals(_vigia.Pasta, p.PastaTrabalho, StringComparison.OrdinalIgnoreCase)))
        {
            _vigia.Dispose();
            _vigia = null;
        }

        if (deveVigiar && _vigia is null)
        {
            _vigia = new VigiaPasta(p.PastaTrabalho, _relogio) { NovidadesDesde = p.NovidadesVistasAte };
            _vigia.Novos += AoChegar;
            _vigia.Iniciar();
        }

        Mudou?.Invoke();
    }

    /// <summary>Sugestoes para as novidades vistas pelo modo automatico.</summary>
    public IReadOnlyList<Sugestao> SugestoesDasNovidades()
    {
        List<ArquivoParaOrganizar> entradas;
        lock (_trava)
        {
            // Quem ja saiu da pasta (movido pelo app ou pelo usuario) deixa de ser novidade.
            foreach (string sumiu in _novidades.Keys.Where(c => !_arquivos.ArquivoExiste(c)).ToList())
            {
                _novidades.Remove(sumiu);
            }

            entradas = [.. _novidades.Values];
        }

        return Sugerir(entradas);
    }

    /// <summary>"Procurar de novo" / "Organizar agora": olha todos os arquivos da pasta, nao so os novos.</summary>
    public IReadOnlyList<Sugestao> SugestoesDaPasta(string pasta)
    {
        var entradas = _arquivos.ListarArquivos(pasta)
            .Select(nome => Path.Combine(pasta, nome))
            .Select(caminho =>
            {
                ArquivoParaOrganizar? novidade;
                lock (_trava)
                {
                    _novidades.TryGetValue(caminho, out novidade);
                }

                return novidade ?? new ArquivoParaOrganizar(caminho) { ModificadoEm = _arquivos.Info(caminho)?.ModificadoEm };
            })
            .ToList();
        return Sugerir(entradas);
    }

    /// <summary>O usuario viu as novidades: a partir daqui so conta o que chegar depois.</summary>
    public void MarcarComoVistas()
    {
        lock (_trava)
        {
            _novidades.Clear();
        }

        _preferencias.Alterar(p => p with { NovidadesVistasAte = _relogio.Agora });
        Mudou?.Invoke();
    }

    public PlanejadorOrganizacao Planejador => new(_arquivos);

    public void Dispose() => _vigia?.Dispose();

    private IReadOnlyList<Sugestao> Sugerir(List<ArquivoParaOrganizar> entradas) =>
        [.. Planejador
            .Planejar(entradas, Regras, _preferencias.Protegidos(_pastaDadosDoApp))
            .Where(s => s.Origem != OrigemSugestao.Ignorado)];

    private void AoChegar(IReadOnlyList<ArquivoParaOrganizar> novos)
    {
        int contados = 0;
        lock (_trava)
        {
            foreach (var novo in novos)
            {
                if (_novidades.TryAdd(novo.Caminho, novo))
                {
                    contados++;
                }
            }
        }

        if (contados > 0)
        {
            Chegaram?.Invoke(contados);
            Mudou?.Invoke();
        }
    }
}
