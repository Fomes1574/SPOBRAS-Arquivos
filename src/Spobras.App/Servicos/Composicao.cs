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
        IExportadorPdf? exportadorPdf = null)
    {
        PastaDados = pastaDados;
        var arquivos = new ArquivosDisco();
        var relogio = new RelogioSistema();
        var banco = new BancoApp(Path.Combine(pastaDados, "arquivos-spobras.db"));

        Preferencias = new ServicoPreferencias(new ConfigRepositorio(banco), pastaTrabalhoPadrao);
        Operacoes = new ServicoOperacoes(arquivos, new HistoricoSqlite(banco), relogio, new FilaTarefas());
        Novidades = new ServicoNovidades(arquivos, relogio, Preferencias, new RegraRepositorio(banco), pastaDados);
        Modelos = new ServicoModelos(pastaDados);

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
            new ModuloVm(PdfVm.Modulo, "PDF", "Juntar, separar, transformar fotos em PDF e reduzir o tamanho.",
                "M6 3h9l4 4v14H6zM15 3v4h4"),
        ];

        var telas = new Dictionary<string, TelaVm>
        {
            [OrganizarVm.Modulo] = new OrganizarVm(arquivos, dialogos, @interface, Operacoes, Novidades, Preferencias, relogio),
            [RenomearVm.Modulo] = new RenomearVm(arquivos, dialogos, Operacoes, Preferencias, new PerfilRepositorio(banco), relogio),
            [CompactarVm.Modulo] = new CompactarVm(arquivos, dialogos, Operacoes, Preferencias),
            [RelatorioVm.Modulo] = new RelatorioVm(dialogos, @interface, Operacoes, Preferencias, Modelos, new RascunhoRepositorio(banco),
                exportadorPdf ?? new SemExportadorPdf(), relogio),
            [PdfVm.Modulo] = new PdfVm(dialogos, Operacoes, Preferencias),
            ["historico"] = new HistoricoVm(Operacoes, dialogos, @interface),
            ["configuracoes"] = new ConfiguracoesVm(Preferencias, Novidades, dialogos, arquivos, relogio, versao, pastaDados, Modelos),
        };
        telas["inicio"] = new InicioVm(Modulos, Operacoes, Novidades, Preferencias, dialogos, @interface, id => Principal!.Navegar(id));
        Principal = new PrincipalVm(Modulos, telas, Novidades, Operacoes, @interface);
    }

    public string PastaDados { get; }
    public ServicoPreferencias Preferencias { get; }
    public ServicoOperacoes Operacoes { get; }
    public ServicoNovidades Novidades { get; }
    public ServicoModelos Modelos { get; }
    public IReadOnlyList<ModuloVm> Modulos { get; }
    public PrincipalVm Principal { get; }

    /// <summary>Ao abrir: acerta lotes interrompidos e liga a vigia, se o modo automatico estiver ligado.</summary>
    public void Iniciar()
    {
        Operacoes.ReconciliarInterrompidos();
        Novidades.Aplicar();
    }

    public void Dispose() => Novidades.Dispose();
}
