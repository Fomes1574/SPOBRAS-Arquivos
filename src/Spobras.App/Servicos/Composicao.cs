using Spobras.App.ViewModels;
using Spobras.Core.Abstracoes;
using Spobras.Core.Tarefas;
using Spobras.Dados;
using Spobras.Dados.Configuracao;
using Spobras.Dados.Historico;

namespace Spobras.App.Servicos;

/// <summary>Monta os servicos e as telas do app. Um lugar so, usado pelo app e pelos testes.</summary>
public sealed class Composicao : IDisposable
{
    public Composicao(
        string pastaDados, string pastaTrabalhoPadrao, IDialogos dialogos, IInterface @interface, string versao,
        IExportadorPdf? exportadorPdf = null, Func<string, Spobras.Core.Planilhas.ILeitorPlanilha>? abrirPlanilha = null, bool eventosDasFontes = true,
        IVerificadorCronograma? verificadorCronograma = null)
    {
        PastaDados = pastaDados;
        var arquivos = new ArquivosDisco();
        var relogio = new RelogioSistema();
        var banco = new BancoApp(Path.Combine(pastaDados, "arquivos-spobras.db"));

        Preferencias = new ServicoPreferencias(new ConfigRepositorio(banco), pastaTrabalhoPadrao);
        Operacoes = new ServicoOperacoes(arquivos, new HistoricoSqlite(banco), relogio, new FilaTarefas());
        Novidades = new ServicoNovidades(arquivos, relogio, Preferencias, new RegraRepositorio(banco), pastaDados);
        Modelos = new ServicoModelos(pastaDados);
        ModelosExcel = new ServicoModelosExcel(pastaDados, new ConfigRepositorio(banco));
        Base = new Spobras.Dados.Base.BaseRepositorio(banco);
        Fontes = new ServicoFontes(Base, abrirPlanilha ?? (caminho => new Spobras.Documentos.Planilhas.LeitorXlsx(caminho)), relogio, Operacoes.Fila, Preferencias)
        {
            UsarEventos = eventosDasFontes,
        };

        // So entram no menu os modulos que ja funcionam (especificacao 9.1).
        Modulos =
        [
            new ModuloVm(OrganizarVm.Modulo, "Organizar arquivos", "Separa o que chega na pasta de trabalho e sugere a pasta certa.",
                "M3 7h6l2 2h10v10H3z"),
            new ModuloVm(RenomearVm.Modulo, "Renomear em lote", "Relatório 01, Relatório 02, Relatório 03… com prévia e Desfazer.",
                "M4 20h4L19 9l-4-4L4 16z"),
            new ModuloVm(CompactarVm.Modulo, "Compactar", "Vários .zip de uma vez, um por pasta ou dividido por tamanho.",
                "M5 3h14v18H5zM12 3v10M10 15h4v3h-4z"),
            new ModuloVm(RelatorioVm.Modulo, "Relatório fotográfico", "50 fotos ou mais no modelo SPOBRAS, em Excel ou PDF.",
                "M3 6h18v13H3zM3 16l5-5 4 4 3-3 6 5"),
            new ModuloVm(CronogramaVm.Modulo, "Cronograma padrão", "Cronograma novo a partir do modelo, com as fórmulas intactas.",
                "M3 5h18v15H3zM3 10h18M8 3v4M16 3v4"),
            new ModuloVm(ModelosVm.Modulo, "Modelos Excel", "Preenche as suas planilhas modelo, uma ou várias de uma vez.",
                "M4 4h16v16H4zM4 9h16M4 14h16M10 4v16"),
            new ModuloVm(PdfVm.Modulo, "PDF", "Juntar, separar, transformar fotos em PDF e reduzir o tamanho.",
                "M6 3h9l4 4v14H6zM15 3v4h4"),
            new ModuloVm(BaseVm.Modulo, "Base de dados", "Portarias, dotações, contratos e escolas, sempre atualizados.",
                "M4 6a8 3 0 1 0 16 0a8 3 0 1 0 -16 0M4 6v12c0 1.7 3.6 3 8 3s8-1.3 8-3V6M4 12c0 1.7 3.6 3 8 3s8-1.3 8-3"),
        ];

        var telas = new Dictionary<string, TelaVm>
        {
            [OrganizarVm.Modulo] = new OrganizarVm(arquivos, dialogos, @interface, Operacoes, Novidades, Preferencias, relogio),
            [RenomearVm.Modulo] = new RenomearVm(arquivos, dialogos, Operacoes, Preferencias, new PerfilRepositorio(banco), relogio),
            [CompactarVm.Modulo] = new CompactarVm(arquivos, dialogos, Operacoes, Preferencias),
            [RelatorioVm.Modulo] = new RelatorioVm(dialogos, @interface, Operacoes, Preferencias, Modelos, new RascunhoRepositorio(banco),
                exportadorPdf ?? new SemExportadorPdf(), relogio),
            [PdfVm.Modulo] = new PdfVm(dialogos, Operacoes, Preferencias),
            [CronogramaVm.Modulo] = new CronogramaVm(dialogos, Operacoes, Preferencias, Modelos, new RascunhoRepositorio(banco),
                verificadorCronograma ?? new SemVerificadorCronograma(), relogio, () => Base.Apelidos()),
            [ModelosVm.Modulo] = new ModelosVm(dialogos, Operacoes, Preferencias, ModelosExcel, new RascunhoRepositorio(banco),
                exportadorPdf ?? new SemExportadorPdf(), relogio),
            [FontesVm.Modulo] = new FontesVm(Fontes, Base, dialogos, @interface, Preferencias, id => Principal!.Navegar(id)),
            ["historico"] = new HistoricoVm(Operacoes, dialogos, @interface),
            ["configuracoes"] = new ConfiguracoesVm(Preferencias, Novidades, dialogos, arquivos, relogio, versao, pastaDados, Modelos),
        };
        var relatorio = (RelatorioVm)telas[RelatorioVm.Modulo];
        telas[BaseVm.Modulo] = new BaseVm(Base, Fontes, @interface, id => Principal!.Navegar(id), obra =>
        {
            // Leva os dados da obra escolhida para o relatorio; o usuario confere e edita la.
            relatorio.PreencherDaObra(obra);
            Principal!.Navegar(RelatorioVm.Modulo);
        }, obra =>
        {
            ((CronogramaVm)telas[CronogramaVm.Modulo]).AdicionarDaBase(obra);
            Principal!.Navegar(CronogramaVm.Modulo);
        }, obra =>
        {
            ((ModelosVm)telas[ModelosVm.Modulo]).AdicionarDaBase(obra);
            Principal!.Navegar(ModelosVm.Modulo);
        }, new ApoioConsultas(dialogos, new PerfilRepositorio(banco), Operacoes, Preferencias, relogio));
        telas["inicio"] = new InicioVm(Modulos, Operacoes, Novidades, Preferencias, dialogos, @interface, id => Principal!.Navegar(id));
        Principal = new PrincipalVm(Modulos, telas, Novidades, Operacoes, @interface);
    }

    public string PastaDados { get; }
    public ServicoPreferencias Preferencias { get; }
    public ServicoOperacoes Operacoes { get; }
    public ServicoNovidades Novidades { get; }
    public ServicoModelos Modelos { get; }
    public ServicoModelosExcel ModelosExcel { get; }
    public Spobras.Dados.Base.BaseRepositorio Base { get; }
    public ServicoFontes Fontes { get; }
    public IReadOnlyList<ModuloVm> Modulos { get; }
    public PrincipalVm Principal { get; }

    /// <summary>Ao abrir: acerta lotes interrompidos e liga a vigia, se o modo automatico estiver ligado.</summary>
    public void Iniciar()
    {
        Operacoes.ReconciliarInterrompidos();
        Novidades.Aplicar();
        Fontes.Iniciar(VigiarFontes);
    }

    /// <summary>False nos testes: as fontes so sao conferidas quando o teste manda.</summary>
    public bool VigiarFontes { get; init; } = true;

    public void Dispose()
    {
        Novidades.Dispose();
        Fontes.Dispose();
    }
}
