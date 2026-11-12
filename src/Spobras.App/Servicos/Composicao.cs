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
        Spobras.Documentos.Pdf.IOcr? ocr = null)
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

        // Organizador por conteudo: texto lido com o indice da busca; contratos e escolas da base atual
        // (refeito quando a base muda).
        var indice = new Spobras.Dados.Busca.IndiceSqlite(banco);
        Spobras.Core.Organizar.ReconhecedorObras? reconhecedor = null;
        Fontes.Mudou += () => reconhecedor = null;
        Novidades.LerConteudo = caminho => Spobras.Documentos.Ferramentas.BuscaArquivos.LerComIndice(caminho, indice);
        Novidades.Reconhecedor = () => reconhecedor ??= new Spobras.Core.Organizar.ReconhecedorObras(Base.Buscar(new Spobras.Core.Base.FiltroObras(), int.MaxValue).Obras);

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
            new ModuloVm(CronogramaVm.Modulo, "Cronograma padrão", "Meses, anos, remuneração e dotações calculados, prontos para enviar.",
                "M3 5h18v15H3zM3 10h18M8 3v4M16 3v4"),
            new ModuloVm(ModelosVm.Modulo, "Modelos Excel", "Preenche as suas planilhas modelo, uma ou várias de uma vez.",
                "M4 4h16v16H4zM4 9h16M4 14h16M10 4v16"),
            new ModuloVm(PdfVm.Modulo, "PDF", "Juntar, separar, fotos em PDF, reduzir, numerar e passar para o Excel.",
                "M6 3h9l4 4v14H6zM15 3v4h4"),
            new ModuloVm(FerramentasVm.Modulo, "Ferramentas", "Buscar dentro dos arquivos, juntar planilhas, achar repetidos e Excel para PDF.",
                "M11 4a7 7 0 1 0 0 14a7 7 0 1 0 0-14M16 16l5 5"),
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
            [PdfVm.Modulo] = new PdfVm(dialogos, Operacoes, Preferencias, () => Modelos.Logo, ocr),
            [CronogramaVm.Modulo] = new CronogramaVm(dialogos, Operacoes, Preferencias, Modelos, new RascunhoRepositorio(banco),
                exportadorPdf ?? new SemExportadorPdf(), relogio, () => Base.Vinculos()),
            [ModelosVm.Modulo] = new ModelosVm(dialogos, Operacoes, Preferencias, ModelosExcel, new RascunhoRepositorio(banco),
                exportadorPdf ?? new SemExportadorPdf(), relogio),
            [FerramentasVm.Modulo] = new FerramentasVm(dialogos, Operacoes, Preferencias, indice,
                exportadorPdf ?? new SemExportadorPdf(), relogio),
            [FontesVm.Modulo] = new FontesVm(Fontes, Base, dialogos, @interface, Preferencias, id => Principal!.Navegar(id)),
            ["historico"] = new HistoricoVm(Operacoes, dialogos, @interface),
            ["configuracoes"] = new ConfiguracoesVm(Preferencias, Novidades, dialogos, arquivos, relogio, versao, pastaDados, Modelos, banco),
        };
        var relatorio = (RelatorioVm)telas[RelatorioVm.Modulo];
        relatorio.ObrasDoContrato = ObrasDoContrato;
        relatorio.ObrasDaEscola = ObrasDaEscola;
        var cronograma = (CronogramaVm)telas[CronogramaVm.Modulo];
        cronograma.ObrasDaEscola = ObrasDaEscola;
        cronograma.ObrasDoLote = ObrasDoLote;
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
        ((BaseVm)telas[BaseVm.Modulo]).AoBuscarNosArquivos = termo =>
        {
            ((FerramentasVm)telas[FerramentasVm.Modulo]).BuscarPor(termo);
            Principal!.Navegar(FerramentasVm.Modulo);
        };
        telas["inicio"] = new InicioVm(Modulos, Operacoes, Novidades, Preferencias, dialogos, @interface, id => Principal!.Navegar(id));
        void Ir(string modulo)
        {
            if (modulo == "ferramentas-duplicados")
            {
                ((FerramentasVm)telas[FerramentasVm.Modulo]).Ferramenta = FerramentaExtra.Duplicados;
                modulo = FerramentasVm.Modulo;
            }

            Principal!.Navegar(modulo);
        }

        // Padrao de portaria = como a planilha mestra escreve (orgao, numero e ano por extenso, especificacao 4.6).
        ((FerramentasVm)telas[FerramentasVm.Modulo]).DadosDaBase = () =>
        {
            var daMestra = Base.PortariasDasObras()
                .Select((rotulo, i) => (Rotulo: rotulo, Lida: Spobras.Core.Base.Normalizacao.Portaria(rotulo), Id: i))
                .Where(p => p.Lida.Numero is not null)
                .Select(p => new Spobras.Core.Base.PortariaBase(p.Id, p.Rotulo, p.Lida.Numero, p.Lida.Ano))
                .ToList();
            return (daMestra.Count > 0 ? daMestra : Base.Portarias(), Base.Vinculos().Select(v => v.DotacaoCompleta).Distinct().ToList());
        };
        ((BaseVm)telas[BaseVm.Modulo]).AoConferirPlanilha = () =>
        {
            ((FerramentasVm)telas[FerramentasVm.Modulo]).Ferramenta = FerramentaExtra.Padrao;
            Principal!.Navegar(FerramentasVm.Modulo);
        };
        ((FontesVm)telas[FontesVm.Modulo]).Operacoes = Operacoes;
        ((FontesVm)telas[FontesVm.Modulo]).Relogio = relogio;
        ((OrganizarVm)telas[OrganizarVm.Modulo]).Navegar = Ir;
        ((ModelosVm)telas[ModelosVm.Modulo]).Navegar = Ir;
        var inicio = (InicioVm)telas["inicio"];
        inicio.EstadoDaMestra = () =>
        {
            var mestra = Base.Fonte(Spobras.Core.Base.Fontes.Mestra);
            if (mestra is null || mestra.Caminho.Length == 0)
            {
                return ("Planilha mestra ainda não escolhida", "Aponte a planilha em Base de dados → Fontes de dados. Depois disso o app relê sozinho sempre que ela for salva.");
            }

            string quando = mestra.UltimaLeitura is { } u ? Formato.DataHora(u) : "ainda não lida";
            return (Path.GetFileName(mestra.Caminho), mestra.Estado == Spobras.Core.Base.EstadoFonte.Atualizada
                ? $"Lida pela última vez em {quando}. Quando ela for salva com dados novos, o app relê sozinho."
                : $"Última leitura boa: {quando}. Atenção: {mestra.Mensagem}");
        };
        Fontes.Mudou += () => @interface.Executar(inicio.AtualizarMestra);
        inicio.AtualizarMestra();
        Principal = new PrincipalVm(Modulos, telas, Novidades, Operacoes, @interface) { Logo = Modelos.Logo };
        Modelos.LogoMudou += () => @interface.Executar(() =>
        {
            Principal.Logo = null; // forca a tela a reler a imagem trocada
            Principal.Logo = Modelos.Logo;
        });
        ((BaseVm)telas[BaseVm.Modulo]).Logo = () => Modelos.Logo;
        ((FontesVm)telas[FontesVm.Modulo]).Logo = () => Modelos.Logo;
        ((FerramentasVm)telas[FerramentasVm.Modulo]).Logo = () => Modelos.Logo;
    }

    public string PastaDados { get; }

    /// <summary>Obras de um contrato digitado (aceita "-" no lugar da barra, sem diferenciar maiusculas).</summary>
    public IReadOnlyList<Spobras.Core.Base.ObraLida> ObrasDoContrato(string digitado)
    {
        string alvo = digitado.Trim().Replace('-', '/').Replace(' ', '/');
        string? contrato = Base.Opcoes(new Spobras.Core.Base.FiltroObras()).Contratos
            .FirstOrDefault(c => string.Equals(c.Trim(), alvo, StringComparison.OrdinalIgnoreCase));
        return contrato is null ? [] : Base.Buscar(new Spobras.Core.Base.FiltroObras { Contrato = contrato }, 100).Obras;
    }

    /// <summary>
    /// Obras da escola digitada: primeiro o nome exato (sem acento e caixa); se nao houver, as escolas que
    /// contem o texto. Uma escola pode estar em varios contratos.
    /// </summary>
    public IReadOnlyList<Spobras.Core.Base.ObraLida> ObrasDaEscola(string digitado)
    {
        string alvo = Spobras.Core.Base.Normalizacao.Busca(digitado.Trim());
        if (alvo.Length < 4)
        {
            return [];
        }

        var escolas = Base.Opcoes(new Spobras.Core.Base.FiltroObras()).Escolas;
        var exatas = escolas.Where(e => Spobras.Core.Base.Normalizacao.Busca(e.Trim()) == alvo).ToList();
        var achadas = exatas.Count > 0 ? exatas : escolas.Where(e => Spobras.Core.Base.Normalizacao.Busca(e).Contains(alvo, StringComparison.Ordinal)).Take(10).ToList();
        return [.. achadas.SelectMany(e => Base.Buscar(new Spobras.Core.Base.FiltroObras { Escola = e }, 100).Obras).Take(20)];
    }

    /// <summary>Obras de um lote: "216", "lote 216" e "LOTE 216" sao o mesmo lote.</summary>
    public IReadOnlyList<Spobras.Core.Base.ObraLida> ObrasDoLote(string digitado)
    {
        static string Chave(string lote)
        {
            string digitos = new([.. lote.Where(char.IsDigit)]);
            return digitos.Length > 0 ? digitos.TrimStart('0') : Spobras.Core.Base.Normalizacao.Busca(lote.Trim());
        }

        string alvo = Chave(digitado);
        if (alvo.Length == 0)
        {
            return [];
        }

        var lotes = Base.Opcoes(new Spobras.Core.Base.FiltroObras()).Lotes.Where(l => Chave(l) == alvo).ToList();
        return [.. lotes.SelectMany(l => Base.Buscar(new Spobras.Core.Base.FiltroObras { Lote = l }, 200).Obras)];
    }

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
