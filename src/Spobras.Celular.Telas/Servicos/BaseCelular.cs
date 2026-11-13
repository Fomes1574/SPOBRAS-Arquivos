using System.Security.Cryptography;
using Spobras.Core.Base;
using Spobras.Dados;
using Spobras.Dados.Base;
using Spobras.Documentos.Planilhas;

namespace Spobras.Celular.Servicos;

public enum EstadoBase
{
    /// <summary>Ainda nao escolheu a planilha mestra.</summary>
    SemPlanilha,
    Lendo,
    Atualizada,

    /// <summary>Sem internet: vale a base da ultima leitura.</summary>
    SemConexao,

    /// <summary>Planilha renomeada, movida ou sem acesso: vale a base da ultima leitura.</summary>
    Inacessivel,

    /// <summary>A planilha foi lida mas nao esta no formato esperado: vale a base anterior.</summary>
    Invalida,
}

/// <summary>Resumo das obras de um lote (soma das obras e das medicoes em tramite de cada uma).</summary>
public sealed record TotaisLote(string Lote, int Obras, int Escolas, decimal Original, decimal Aditado, decimal Contratado, decimal Pago,
    int MedicoesEmTramite, decimal EmTramite, decimal SaldoAPagar);

/// <summary>
/// A base do celular: a planilha mestra escolhida uma vez (so leitura), copiada para dentro do app e importada
/// com o mesmo importador do computador. Relida ao abrir e ao voltar para o app; se nao conseguir, vale a ultima.
/// </summary>
public sealed class BaseCelular
{
    private readonly IPlataforma _plataforma;
    private readonly ArquivoPreferencias _preferencias;
    private readonly Func<DateTimeOffset> _agora;
    private readonly SemaphoreSlim _leitura = new(1, 1);
    private bool _deNovo, _deNovoForcado;

    public BaseCelular(IPlataforma plataforma, ArquivoPreferencias preferencias, Func<DateTimeOffset>? agora = null)
    {
        _plataforma = plataforma;
        _preferencias = preferencias;
        _agora = agora ?? (() => DateTimeOffset.Now);
        Repositorio = new BaseRepositorio(new BancoApp(Path.Combine(plataforma.PastaDoApp, "base", "base.db"), copiasGuardadas: 2));
        var p = preferencias.Ler();
        Estado = p.Mestra is null ? EstadoBase.SemPlanilha : EstadoBase.Atualizada;
    }

    public BaseRepositorio Repositorio { get; }
    public EstadoBase Estado { get; private set; }
    public string Mensagem { get; private set; } = "";
    public DateTimeOffset Agora => _agora();
    public event Action? Mudou;

    public Escolhido? Planilha => _preferencias.Ler().Mestra;
    public DateTimeOffset? UltimaLeitura => _preferencias.Ler().UltimaLeitura;
    public bool TemDados => Repositorio.TotalDeObras() > 0;
    public int TotalDeObras => Repositorio.TotalDeObras();

    public async Task<bool> EscolherPlanilhaAsync()
    {
        var escolhida = await _plataforma.EscolherPlanilhaAsync();
        if (escolhida is null)
        {
            return false;
        }

        _preferencias.Gravar(_preferencias.Ler() with { Mestra = escolhida, HashDaMestra = null });
        await AtualizarAsync();
        return true;
    }

    /// <summary>
    /// Rele a planilha. Copia para dentro do app, compara com a ultima lida e so importa se mudou (ou se forcar).
    /// A base anterior so e trocada quando a leitura nova da certo.
    /// </summary>
    public async Task AtualizarAsync(bool forcar = false)
    {
        var p = _preferencias.Ler();
        if (p.Mestra is null)
        {
            Definir(EstadoBase.SemPlanilha, "");
            return;
        }

        if (!await _leitura.WaitAsync(0))
        {
            // Ja esta lendo (por exemplo, ao voltar do seletor de arquivos): le de novo ao terminar, para nunca
            // ficar com a planilha antiga quando o usuario acabou de trocar.
            _deNovo = true;
            _deNovoForcado |= forcar;
            return;
        }

        try
        {
            Definir(EstadoBase.Lendo, "");
            string pasta = Path.Combine(_plataforma.PastaDoApp, "base");
            Directory.CreateDirectory(pasta);
            string copia = Path.Combine(pasta, "mestra-lida.xlsx");
            var resultado = await _plataforma.CopiarAsync(p.Mestra.Id, copia);
            if (resultado != Copia.Ok)
            {
                string desde = p.UltimaLeitura is { } u ? $" Você está vendo a base lida {Formato.Quando(u, _agora())}." : " Ainda não há dados lidos.";
                Definir(resultado == Copia.SemConexao ? EstadoBase.SemConexao : EstadoBase.Inacessivel,
                    (resultado == Copia.SemConexao
                        ? "Sem internet."
                        : "Não foi possível reler a planilha mestra: ela foi renomeada, movida ou você perdeu o acesso.") + desde);
                return;
            }

            string hash = await Task.Run(() => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(copia))));
            if (!forcar && hash == p.HashDaMestra && TemDados)
            {
                _preferencias.Gravar(p with { UltimaLeitura = _agora() });
                Definir(EstadoBase.Atualizada, "");
                return;
            }

            var agora = _agora();
            var info = new FileInfo(copia);
            await Task.Run(() =>
            {
                using var planilha = new LeitorXlsx(copia);
                var dados = ImportadorMestra.Importar(planilha);
                var fonte = (Repositorio.Fonte(Fontes.Mestra) ?? new FonteInfo { Id = Fontes.Mestra, Nome = "Planilha mestra" }) with
                {
                    Caminho = p.Mestra.Nome,
                    Estado = EstadoFonte.Atualizada,
                    Mensagem = "",
                    UltimaLeitura = agora,
                    ModificadoArquivo = agora,
                    TamanhoArquivo = info.Length,
                    Linhas = dados.Obras.Count,
                    Avisos = dados.Avisos,
                };
                Repositorio.GravarMestra(dados, fonte with { Versao = fonte.Versao + 1 });
            });
            _preferencias.Gravar(_preferencias.Ler() with { HashDaMestra = hash, UltimaLeitura = agora });
            Definir(EstadoBase.Atualizada, "");
        }
        catch (Exception ex) when (ex is ImportacaoInvalidaException or PlanilhaInvalidaException or IOException or InvalidDataException)
        {
            Definir(EstadoBase.Invalida, $"A planilha escolhida não pôde ser lida: {ex.Message}" +
                (TemDados ? " A base anterior continua valendo." : ""));
        }
        finally
        {
            _leitura.Release();
        }

        if (_deNovo)
        {
            bool f = _deNovoForcado;
            (_deNovo, _deNovoForcado) = (false, false);
            await AtualizarAsync(f);
        }
    }

    private void Definir(EstadoBase estado, string mensagem)
    {
        Estado = estado;
        Mensagem = mensagem;
        Mudou?.Invoke();
    }

    // ---- Consultas ----

    /// <summary>Obras do filtro, sem as de contrato sob gestao da SIURB (nao aparecem no app).</summary>
    public (IReadOnlyList<ObraLida> Obras, int Total) Buscar(FiltroObras filtro, int limite)
    {
        var r = Repositorio.Buscar(filtro, int.MaxValue, 0, OrdemObras.Contrato);
        var visiveis = r.Obras.Where(o => MedicoesEmTramite.Classificar(o.StatusContrato) != SituacaoContrato.Ignorado).ToList();
        return (visiveis.Take(limite).ToList(), visiveis.Count);
    }

    public OpcoesFiltro Opcoes(FiltroObras filtro) => Repositorio.Opcoes(filtro);

    public ObraLida? Obra(long id) => Repositorio.Obra(id);

    public SituacaoContrato Situacao(ObraLida obra) => MedicoesEmTramite.Situacao(obra, Repositorio.Medicoes(obra.Contrato));

    public IReadOnlyList<MedicaoEmTramite> EmTramite(ObraLida obra) => MedicoesEmTramite.DaObra(obra, Repositorio.Medicoes(obra.Contrato));

    /// <summary>A mesma escola em outros contratos.</summary>
    public IReadOnlyList<ObraLida> OutrosContratos(ObraLida obra) =>
        [.. ProcuraObras.DaEscola(Repositorio, obra.Empreendimento).Where(o => o.Id != obra.Id && ProcuraObras.MesmaEscola(o.Empreendimento, obra.Empreendimento))];

    public TotaisLote Totais(FiltroObras filtro)
    {
        var obras = Buscar(filtro, int.MaxValue).Obras;
        decimal Soma(Func<ObraLida, decimal?> valor) => obras.Sum(o => valor(o) ?? 0m);
        var tramite = obras.SelectMany(EmTramite).ToList();
        return new TotaisLote(FiltroObras.TituloLotes(filtro.Lote), obras.Count, obras.Select(o => Normalizacao.Busca(o.Empreendimento.Trim())).Distinct().Count(),
            Soma(o => o.ValorEmpreendimento), Soma(o => o.ValorAditado), Soma(o => o.ValorContratadoComAditamento), Soma(o => o.ValorPago),
            tramite.Count, tramite.Sum(m => m.Valor), Soma(o => o.SaldoAPagar));
    }
}
