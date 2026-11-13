using Spobras.Celular.Servicos;
using Spobras.Core.Base;
using Spobras.Testes;
using UglyToad.PdfPig;

namespace Spobras.Celular.Tests;

public sealed class CelularTests : IDisposable
{
    private static readonly DateTimeOffset Hoje = new(2026, 11, 12, 19, 7, 0, TimeSpan.FromHours(-3));
    private readonly string _raiz = Directory.CreateTempSubdirectory("spobras-celular-").FullName;
    private readonly PlataformaDeTeste _p;

    public CelularTests()
    {
        _p = new PlataformaDeTeste(_raiz);
        AmostraCelular.GravarMestra(_p.Mestra);
    }

    public void Dispose() => Directory.Delete(_raiz, recursive: true);

    private AppCelular NovoApp() => new(_p, () => Hoje);

    private async Task<AppCelular> AppComBase()
    {
        var app = NovoApp();
        Assert.True(await app.Base.EscolherPlanilhaAsync());
        return app;
    }

    // ---- Base ----

    [Fact]
    public async Task Le_a_mestra_escolhida_e_esconde_obra_sob_gestao_da_SIURB()
    {
        var app = NovoApp();
        Assert.Equal(EstadoBase.SemPlanilha, app.Base.Estado);
        await app.Base.EscolherPlanilhaAsync();

        Assert.Equal(EstadoBase.Atualizada, app.Base.Estado);
        Assert.Equal(7, app.Base.TotalDeObras);
        var (obras, total) = app.Base.Buscar(new FiltroObras(), 100);
        Assert.Equal(6, total);
        Assert.DoesNotContain(obras, o => o.Empreendimento.Contains("Zeta", StringComparison.Ordinal));
        Assert.Equal(Hoje, app.Base.UltimaLeitura);
    }

    [Fact]
    public async Task Nao_importa_de_novo_se_a_planilha_nao_mudou_e_nunca_altera_a_original()
    {
        var app = await AppComBase();
        byte[] original = File.ReadAllBytes(_p.Mestra);
        int versao = app.Base.Repositorio.Fonte(Fontes.Mestra)!.Versao;

        await app.Base.AtualizarAsync();
        Assert.Equal(versao, app.Base.Repositorio.Fonte(Fontes.Mestra)!.Versao);

        await app.Base.AtualizarAsync(forcar: true);
        Assert.Equal(versao + 1, app.Base.Repositorio.Fonte(Fontes.Mestra)!.Versao);
        Assert.Equal(original, File.ReadAllBytes(_p.Mestra));
    }

    [Fact]
    public async Task Sem_internet_ou_planilha_movida_mantem_a_base_anterior()
    {
        var app = await AppComBase();
        _p.SemConexao = true;
        await app.Base.AtualizarAsync();
        Assert.Equal(EstadoBase.SemConexao, app.Base.Estado);
        Assert.Contains("Sem internet", app.Base.Mensagem);
        Assert.Equal(7, app.Base.TotalDeObras);

        _p.SemConexao = false;
        File.Move(_p.Mestra, _p.Mestra + ".movida");
        await app.Base.AtualizarAsync();
        Assert.Equal(EstadoBase.Inacessivel, app.Base.Estado);
        Assert.Contains("renomeada, movida", app.Base.Mensagem);
        Assert.Equal(7, app.Base.TotalDeObras);
    }

    [Fact]
    public async Task Planilha_errada_nao_apaga_a_base_que_ja_existia()
    {
        var app = await AppComBase();
        File.WriteAllText(_p.Mestra, "isto nao e uma planilha");
        await app.Base.AtualizarAsync();
        Assert.Equal(EstadoBase.Invalida, app.Base.Estado);
        Assert.Contains("base anterior continua valendo", app.Base.Mensagem);
        Assert.Equal(7, app.Base.TotalDeObras);
    }

    [Fact]
    public async Task Ficha_e_totais_do_lote_com_medicoes_em_tramite()
    {
        var app = await AppComBase();
        var alfa = app.Base.Buscar(new FiltroObras { Contrato = "044/EX/2026" }, 1).Obras[0];
        var tramite = app.Base.EmTramite(alfa);
        Assert.Equal(["7", "6", "5"], tramite.Select(m => m.Numero));
        Assert.Equal(847_230.52m, tramite.Sum(m => m.Valor));
        Assert.Single(app.Base.OutrosContratos(alfa));

        var concluida = app.Base.Buscar(new FiltroObras { Contrato = "007/EX/2022" }, 1).Obras[0];
        Assert.Equal(SituacaoContrato.Concluido, app.Base.Situacao(concluida));
        Assert.Empty(app.Base.EmTramite(concluida));

        var t = app.Base.Totais(new FiltroObras { Lote = "LOTE 216" });
        Assert.Equal(3, t.Obras);
        Assert.Equal(7_640_000m, t.Original);
        Assert.Equal(442_428.24m, t.Aditado);
        Assert.Equal(4, t.MedicoesEmTramite);
        Assert.Equal(847_230.52m + 120_000m, t.EmTramite);
        Assert.Equal(t.Contratado, t.Pago + t.SaldoAPagar);
    }

    // ---- Rascunho do relatorio ----

    private static void ComFotos(AppCelular app, PlataformaDeTeste p, int n)
    {
        for (int i = 1; i <= n; i++)
        {
            AmostraCelular.Foto(p.Entrada, $"f{i}.jpg", i % 2 == 0 ? 900 : 1200, i % 2 == 0 ? 1200 : 900, i);
        }

        var escolhidas = p.EscolherArquivosAsync("image/*", true, app.Rascunho.PastaDasFotos).Result;
        Assert.Empty(app.Rascunho.AdicionarCopiadas(escolhidas.Select(e => e.Local!)));
    }

    [Fact]
    public async Task Rascunho_continua_igual_depois_de_fechar_o_app()
    {
        var app = await AppComBase();
        var obra = app.Base.Buscar(new FiltroObras { Contrato = "044/EX/2026" }, 1).Obras[0];
        var mudou = app.Rascunho.AplicarObra(obra);
        Assert.Contains("Unidade", mudou);
        Assert.Equal("0000.2026/0000000-0", app.Rascunho.Dados.ProcessoSei);
        app.Rascunho.Alterar(d => d with { ProcessoSei = "" });
        Assert.Contains("Processo SEI", app.Rascunho.AplicarObra(obra));
        Assert.Equal("0000.2026/0000000-0", app.Rascunho.Dados.ProcessoSei);
        ComFotos(app, _p, 3);
        app.Rascunho.Legendar(0, "Piso da sala 3");
        app.Rascunho.Girar(1);
        app.Rascunho.Mover(2, 0);

        var reaberto = NovoApp();
        var d = reaberto.Rascunho.Dados;
        Assert.Equal("EMEI Exemplo Alfa", d.Unidade);
        Assert.Equal("13/05/2026", d.DataOs);
        Assert.Equal("12/11/2026", d.Data);
        Assert.Equal(3, d.Fotos.Count);
        Assert.Equal("Piso da sala 3", d.Fotos[1].Legenda);
        Assert.Equal(90, d.Fotos[2].Rotacao);
        Assert.Empty(reaberto.Rascunho.AplicarObra(obra)); // nada mudou
        Assert.False(reaberto.Rascunho.Vazio);
    }

    [Fact]
    public async Task Remover_foto_apaga_so_a_copia_do_app_e_nunca_a_original()
    {
        var app = await AppComBase();
        ComFotos(app, _p, 2);
        string copia = app.Rascunho.Caminho(app.Rascunho.Dados.Fotos[0]);
        app.Rascunho.Remover(0);
        Assert.False(File.Exists(copia));
        Assert.True(File.Exists(Path.Combine(_p.Entrada, "f1.jpg")));
        Assert.Single(app.Rascunho.Dados.Fotos);

        app.Rascunho.Recomecar();
        Assert.True(app.Rascunho.Vazio);
        Assert.True(File.Exists(Path.Combine(_p.Entrada, "f2.jpg")));
    }

    [Fact]
    public async Task Foto_da_camera_e_arquivo_que_nao_e_imagem()
    {
        var app = await AppComBase();
        string foto = AmostraCelular.Foto(Path.Combine(_raiz, "cam"), "c.jpg", 640, 480, 1);
        app.Rascunho.AdicionarFoto(File.ReadAllBytes(foto));
        File.WriteAllText(Path.Combine(app.Rascunho.PastaDasFotos, "falsa.jpg"), "nao e foto");
        var recusadas = app.Rascunho.AdicionarCopiadas([Path.Combine(app.Rascunho.PastaDasFotos, "falsa.jpg")]);
        Assert.Single(recusadas);
        Assert.Single(app.Rascunho.Dados.Fotos);
        Assert.False(File.Exists(Path.Combine(app.Rascunho.PastaDasFotos, "falsa.jpg")));
    }

    [Fact]
    public async Task Gera_excel_e_pdf_na_pasta_da_escola_sem_sobrescrever()
    {
        var app = await AppComBase();
        var obra = app.Base.Buscar(new FiltroObras { Contrato = "044/EX/2026" }, 1).Obras[0];
        app.Rascunho.AplicarObra(obra);
        ComFotos(app, _p, 7);
        app.Configurar(c => c with { Pasta = new Escolhido("pasta:" + _p.Saida, "Relatórios") });

        var r = await app.Rascunho.GerarAsync(_p, app.Config);
        Assert.Equal(2, r.Arquivos.Count);
        Assert.Equal(2, r.Paginas);
        Assert.Equal("Relatórios › EMEI Exemplo Alfa", r.Onde);
        string pasta = Path.Combine(_p.Saida, "EMEI Exemplo Alfa");
        Assert.True(File.Exists(Path.Combine(pasta, "Relatório Fotográfico - EMEI Exemplo Alfa - 2026-11-12.xlsx")));
        using (var pdf = PdfDocument.Open(Path.Combine(pasta, "Relatório Fotográfico - EMEI Exemplo Alfa - 2026-11-12.pdf")))
        {
            Assert.Equal(2, pdf.NumberOfPages);
            Assert.Contains("044/EX/2026", string.Join(" ", pdf.GetPage(1).GetWords().Select(w => w.Text)));
        }

        var segunda = await app.Rascunho.GerarAsync(_p, app.Config with { Formato = FormatoGerado.Pdf, PastaPorEscola = false });
        Assert.Single(segunda.Arquivos);
        Assert.Equal("Relatório Fotográfico - EMEI Exemplo Alfa - 2026-11-12.pdf", segunda.Arquivos[0].Nome);
        var terceira = await app.Rascunho.GerarAsync(_p, app.Config with { Formato = FormatoGerado.Pdf, PastaPorEscola = false });
        Assert.Equal("Relatório Fotográfico - EMEI Exemplo Alfa - 2026-11-12 (2).pdf", terceira.Arquivos[0].Nome);
    }

    [Fact]
    public async Task Nao_gera_com_data_invalida_sem_fotos_ou_sem_pasta()
    {
        var app = await AppComBase();
        await Assert.ThrowsAsync<InvalidOperationException>(() => app.Rascunho.GerarAsync(_p, app.Config));
        app.Configurar(c => c with { Pasta = new Escolhido("pasta:" + _p.Saida, "Relatórios") });
        app.Rascunho.Alterar(d => d with { Data = "31/02/2026" });
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => app.Rascunho.GerarAsync(_p, app.Config));
        Assert.Contains("data do relatório", ex.Message);
        Assert.Contains("pelo menos uma foto", ex.Message);
        Assert.Empty(Directory.GetFiles(_p.Saida, "*", SearchOption.AllDirectories));
    }

    // ---- PDF ----

    [Fact]
    public async Task Ferramentas_de_pdf_salvam_na_pasta_escolhida()
    {
        var app = NovoApp();
        app.Configurar(c => c with { Pasta = new Escolhido("pasta:" + _p.Saida, "Relatórios") });
        string fotos = Path.Combine(_raiz, "fotos");
        Spobras.Documentos.Pdf.FerramentasPdf.FotosEmPdf([AmostraCelular.Foto(fotos, "a.jpg", 800, 600, 1), AmostraCelular.Foto(fotos, "b.jpg", 800, 600, 2)], Path.Combine(_p.Entrada, "A.pdf"));
        Spobras.Documentos.Pdf.FerramentasPdf.FotosEmPdf([AmostraCelular.Foto(fotos, "c.jpg", 800, 600, 3)], Path.Combine(_p.Entrada, "B.pdf"));

        var pdfs = await app.Pdf.EscolherPdfsAsync(varios: true);
        var juntado = await app.Pdf.JuntarAsync(pdfs);
        Assert.Equal("A - juntado.pdf", juntado.Arquivos[0].Nome);
        Assert.Equal(3, PdfCelular.Paginas(Path.Combine(_p.Saida, "A - juntado.pdf")));

        var separado = await app.Pdf.SepararAsync(pdfs[0], "2");
        Assert.Single(separado.Arquivos);
        await Assert.ThrowsAsync<InvalidOperationException>(() => app.Pdf.SepararAsync(pdfs[0], "9"));

        var numerado = await app.Pdf.NumerarAsync(pdfs[0], numerar: true, logo: false, aPartirDa: 1);
        Assert.Contains("2 páginas", numerado.Mensagem);
        await Assert.ThrowsAsync<InvalidOperationException>(() => app.Pdf.NumerarAsync(pdfs[0], numerar: false, logo: true, aPartirDa: 1));

        var excel = await app.Pdf.ParaExcelAsync(pdfs[0]);
        Assert.Empty(excel.Arquivos); // PDF so com imagens: avisa e nao salva nada
        Assert.Contains("não tem texto", excel.Mensagem);

        _p.ProximaEscolha = [Path.Combine(fotos, "a.jpg"), Path.Combine(fotos, "b.jpg")];
        var emPdf = await app.Pdf.FotosEmPdfAsync(await app.Pdf.EscolherFotosAsync(), "Visita");
        Assert.Equal("Visita.pdf", emPdf.Arquivos[0].Nome);
        Assert.True(File.Exists(Path.Combine(_p.Entrada, "A.pdf"))); // originais intactos
    }

    [Fact]
    public async Task Logo_escolhido_fica_copiado_no_app()
    {
        var app = NovoApp();
        string logo = AmostraCelular.Logo(Path.Combine(_raiz, "logo"));
        _p.ProximaEscolha = [logo];
        Assert.Null(await app.EscolherLogoAsync());
        Assert.StartsWith(_p.PastaDoApp, app.Config.Logo);
        Assert.NotNull(app.LogoUrl());
        Assert.True(File.Exists(logo));
    }

    // ---- Erros que so apareceram no aparelho ----

    [Fact]
    public void Pagina_do_android_nao_inicia_o_blazor_por_conta_propria()
    {
        // O BlazorWebView do Android chama Blazor.start() sozinho; chamar de novo na pagina deixou o app parado em "Abrindo…".
        string raiz = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(raiz, "ArquivosSpobras.sln")))
        {
            raiz = Path.GetDirectoryName(raiz)!;
        }

        string html = File.ReadAllText(Path.Combine(raiz, "src", "Spobras.Celular", "wwwroot", "index.html"));
        Assert.Contains("autostart=\"false\"", html);
        Assert.DoesNotContain("Blazor.start(", html.Replace("chama Blazor.start()", "", StringComparison.Ordinal));
        Assert.Contains("blazor-error-ui", html);
    }

    [Fact]
    public async Task Trocar_a_planilha_durante_uma_leitura_le_a_nova_ao_terminar()
    {
        var app = await AppComBase();
        string outra = Path.Combine(_raiz, "outra.xlsx");
        var menor = AmostraCelular.Mestra();
        menor.TabelasPorNome["contratos_VW_ESCOLAS_CONTRATOS_MEDIDOS_PAGOS"] = menor.TabelasPorNome["contratos_VW_ESCOLAS_CONTRATOS_MEDIDOS_PAGOS"] with
        {
            Linhas = [.. menor.TabelasPorNome["contratos_VW_ESCOLAS_CONTRATOS_MEDIDOS_PAGOS"].Linhas.Take(2)],
        };
        XlsxSintetico.Gravar(menor, outra);

        var primeira = app.Base.AtualizarAsync(forcar: true);      // leitura em andamento (planilha antiga)
        app.Configurar(c => c with { Mestra = new Escolhido("arquivo:" + outra, "outra.xlsx"), HashDaMestra = null });
        var segunda = app.Base.AtualizarAsync();                   // pedida no meio: nao pode se perder
        await Task.WhenAll(primeira, segunda);

        Assert.Equal(2, app.Base.TotalDeObras);
        Assert.Equal(EstadoBase.Atualizada, app.Base.Estado);
    }

    [Fact]
    public async Task Voltar_para_o_app_logo_em_seguida_nao_baixa_a_planilha_de_novo()
    {
        var agora = Hoje;
        var app = new AppCelular(_p, () => agora);
        await app.Base.EscolherPlanilhaAsync();
        await app.AoAbrirAsync();
        File.Delete(_p.Mestra);                        // se tentasse reler, daria "planilha movida"
        agora = agora.AddSeconds(30);
        await app.AoAbrirAsync();
        Assert.Equal(EstadoBase.Atualizada, app.Base.Estado);
        agora = agora.AddMinutes(5);
        await app.AoAbrirAsync();
        Assert.Equal(EstadoBase.Inacessivel, app.Base.Estado);
    }
}
