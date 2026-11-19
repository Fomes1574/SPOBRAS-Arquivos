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
        MedicoesEmTramite.DefinirRegras(p.RegrasStatus);
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

        _preferencias.Gravar(_preferencias.Ler() with { Mestra = escolhida, HashDaMestra = null, AssinaturaDaMestra = null });
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
            // Tamanho e data iguais aos da ultima leitura: nem baixa a planilha de novo (economiza internet).
            string? assinatura = await _plataforma.AssinaturaAsync(p.Mestra.Id);
            if (!forcar && TemDados && assinatura is not null && assinatura == p.AssinaturaDaMestra)
            {
                Repositorio.RegistrarImportacao(Fontes.Mestra, _agora(), true, "Sem mudança.", TotalDeObras, 0);
                _preferencias.Gravar(p with { UltimaLeitura = _agora() });
                Definir(EstadoBase.Atualizada, "");
                return;
            }

            var resultado = await _plataforma.CopiarAsync(p.Mestra.Id, copia);
            if (resultado != Copia.Ok)
            {
                string desde = p.UltimaLeitura is { } u ? $" Você está vendo a base lida {Formato.Quando(u, _agora())}." : " Ainda não há dados lidos.";
                Repositorio.RegistrarImportacao(Fontes.Mestra, _agora(), false, resultado == Copia.SemConexao ? "Sem internet." : "Planilha sem acesso.", 0, 0);
                Definir(resultado == Copia.SemConexao ? EstadoBase.SemConexao : EstadoBase.Inacessivel,
                    (resultado == Copia.SemConexao
                        ? "Sem internet."
                        : "Não foi possível reler a planilha mestra: ela foi renomeada, movida ou você perdeu o acesso.") + desde);
                return;
            }

            string hash = await Task.Run(() => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(copia))));
            if (!forcar && hash == p.HashDaMestra && TemDados)
            {
                Repositorio.RegistrarImportacao(Fontes.Mestra, _agora(), true, "Sem mudança.", TotalDeObras, 0);
                _preferencias.Gravar(p with { UltimaLeitura = _agora(), AssinaturaDaMestra = assinatura });
                Definir(EstadoBase.Atualizada, "");
                return;
            }

            var agora = _agora();
            var info = new FileInfo(copia);
            var relogio = System.Diagnostics.Stopwatch.StartNew();
            int linhas = 0;
            await Task.Run(() =>
            {
                using var planilha = new LeitorXlsx(copia);
                var dados = ImportadorMestra.Importar(planilha, p.Mapeamento);
                linhas = dados.Obras.Count;
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
            _preferencias.Gravar(_preferencias.Ler() with { HashDaMestra = hash, UltimaLeitura = agora, AssinaturaDaMestra = assinatura });
            Repositorio.RegistrarImportacao(Fontes.Mestra, agora, true, "Leitura concluída.", linhas, relogio.ElapsedMilliseconds);
            Definir(EstadoBase.Atualizada, "");
        }
        catch (Exception ex) when (ex is ImportacaoInvalidaException or PlanilhaInvalidaException or IOException or InvalidDataException)
        {
            Repositorio.RegistrarImportacao(Fontes.Mestra, _agora(), false, ex.Message, 0, 0);
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

    // ---- Configuracoes da base ----

    public MapeamentoMestra Mapeamento => _preferencias.Ler().Mapeamento;

    /// <summary>Confere a ultima copia lida da planilha contra um mapeamento (sem importar nada).</summary>
    public Task<ConferenciaMapeamento?> ConferirAsync(MapeamentoMestra mapa) => Task.Run(() =>
    {
        string copia = Path.Combine(_plataforma.PastaDoApp, "base", "mestra-lida.xlsx");
        if (!File.Exists(copia))
        {
            return null;
        }

        using var planilha = new LeitorXlsx(copia);
        return (ConferenciaMapeamento?)ConferenciaMestra.Conferir(planilha, mapa);
    });

    /// <summary>Le a ultima copia com o mapeamento, sem gravar nada: o "Testar leitura".</summary>
    public Task<MestraImportada> TestarAsync(MapeamentoMestra mapa) => Task.Run(() =>
    {
        using var planilha = new LeitorXlsx(Path.Combine(_plataforma.PastaDoApp, "base", "mestra-lida.xlsx"));
        return ImportadorMestra.Importar(planilha, mapa);
    });

    /// <summary>Guarda o mapeamento e le a planilha de novo com ele.</summary>
    public async Task SalvarMapeamentoAsync(MapeamentoMestra mapa)
    {
        _preferencias.Gravar(_preferencias.Ler() with { Mapeamento = mapa });
        await AtualizarAsync(forcar: true);
    }

    public IReadOnlyList<(string Status, int Obras)> StatusDoContrato() => Repositorio.StatusDoContrato();

    public void DefinirRegra(string status, SituacaoContrato situacao)
    {
        var regras = new Dictionary<string, SituacaoContrato>(_preferencias.Ler().RegrasStatus, StringComparer.OrdinalIgnoreCase);
        if (MedicoesEmTramite.ClassificarPadrao(status) == situacao)
        {
            regras.Remove(status);
        }
        else
        {
            regras[status] = situacao;
        }

        _preferencias.Gravar(_preferencias.Ler() with { RegrasStatus = regras });
        MedicoesEmTramite.DefinirRegras(regras);
        Mudou?.Invoke();
    }

    /// <summary>Colunas, tabelas e status voltam aos que vem com o app; a planilha e lida de novo.</summary>
    public async Task VoltarAoPadraoAsync()
    {
        _preferencias.Gravar(_preferencias.Ler() with { Mapeamento = new MapeamentoMestra(), RegrasStatus = new Dictionary<string, SituacaoContrato>() });
        MedicoesEmTramite.DefinirRegras(null);
        await AtualizarAsync(forcar: true);
    }

    public IReadOnlyList<(DateTimeOffset Quando, bool Ok, string Mensagem, int Linhas)> Leituras(int maximo = BaseRepositorio.LeiturasGuardadas) =>
        Repositorio.HistoricoDeImportacoes(Fontes.Mestra, maximo);

    private void Definir(EstadoBase estado, string mensagem)
    {
        Estado = estado;
        Mensagem = mensagem;
        Mudou?.Invoke();
    }

    // ---- Consultas ----

    /// <summary>Status do contrato que nao aparecem no app (ex.: sob gestao da SIURB), pelas regras atuais.</summary>
    private IReadOnlyList<string> Ocultos() =>
        [.. Repositorio.StatusDoContrato().Where(s => s.Status.Length > 0 && MedicoesEmTramite.Classificar(s.Status) == SituacaoContrato.Ignorado).Select(s => s.Status)];

    public IReadOnlyList<ResumoContrato> ResumosDosContratos(IReadOnlyList<string> contratos) => Repositorio.ResumosDosContratos(contratos, Ocultos());

    private FiltroObras Visivel(FiltroObras filtro) => filtro with { OcultarStatusContrato = Ocultos() };

    /// <summary>Obras do filtro, sem as que nao aparecem no app. O banco ja devolve so a pagina pedida.</summary>
    public (IReadOnlyList<ObraLida> Obras, int Total) Buscar(FiltroObras filtro, int limite)
    {
        var r = Repositorio.Buscar(Visivel(filtro), limite, 0, OrdemObras.Contrato);
        return (r.Obras, r.Total);
    }

    public OpcoesFiltro Opcoes(FiltroObras filtro) => Repositorio.Opcoes(Visivel(filtro));

    public ObraLida? Obra(long id) => Repositorio.Obra(id);

    /// <summary>Obras de uma base sem contrato (Em licitação ou A licitar).</summary>
    public IReadOnlyList<ObraPrevista> Previstas(BaseDeObras qual) => Repositorio.Previstas(qual);

    public ObraPrevista? Prevista(BaseDeObras qual, long id) => Repositorio.Prevista(qual, id);

    /// <summary>Onde mais a escola aparece (atalho; os dados nao se juntam).</summary>
    public IReadOnlyList<OutraBase> TambemEm(string escola, BaseDeObras naoIncluir, long? idAtual = null) =>
        [.. Repositorio.TambemEm(escola, naoIncluir, idAtual).Where(o => o.Base != BaseDeObras.EmAndamento || Visivel(o.Id))];

    private bool Visivel(long id) => Repositorio.Obra(id) is { } obra && !Ocultos().Contains(obra.StatusContrato ?? "", StringComparer.OrdinalIgnoreCase);

    public SituacaoContrato Situacao(ObraLida obra) => MedicoesEmTramite.Situacao(obra, Repositorio.Medicoes(obra.Contrato));

    public IReadOnlyList<MedicaoEmTramite> EmTramite(ObraLida obra) => MedicoesEmTramite.DaObra(obra, Repositorio.Medicoes(obra.Contrato));

    /// <summary>A mesma escola em outros contratos.</summary>
    public IReadOnlyList<ObraLida> OutrosContratos(ObraLida obra) =>
        [.. ProcuraObras.DaEscola(Repositorio, obra.Empreendimento).Where(o => o.Id != obra.Id && ProcuraObras.MesmaEscola(o.Empreendimento, obra.Empreendimento))];

    public TotaisLote Totais(FiltroObras filtro)
    {
        var obras = Buscar(filtro, int.MaxValue).Obras;
        decimal Soma(Func<ObraLida, decimal?> valor) => obras.Sum(o => valor(o) ?? 0m);
        var medicoes = Repositorio.Medicoes(obras.Select(o => o.Contrato));
        var tramite = obras.SelectMany(o => MedicoesEmTramite.DaObra(o, medicoes[o.Contrato])).ToList();
        return new TotaisLote(FiltroObras.TituloLotes(filtro.Lote), obras.Count, obras.Select(o => Normalizacao.Busca(o.Empreendimento.Trim())).Distinct().Count(),
            Soma(o => o.ValorEmpreendimento), Soma(o => o.ValorAditado), Soma(o => o.ValorContratadoComAditamento), Soma(o => o.ValorPago),
            tramite.Count, tramite.Sum(m => m.Valor), Soma(o => o.SaldoAPagar));
    }
}
