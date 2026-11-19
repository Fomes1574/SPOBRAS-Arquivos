using Spobras.Celular.Servicos;
using Spobras.Core.Base;
using Spobras.Documentos.Planilhas;
using Spobras.Testes;

namespace Spobras.Celular.Tests;

/// <summary>Celular: bases Em licitação e A licitar lidas da mestra, "também está em" e exportação em planilha.</summary>
public sealed class BasesPrevistasCelularTests : IDisposable
{
    private static readonly DateTimeOffset Hoje = new(2026, 11, 12, 19, 7, 0, TimeSpan.FromHours(-3));
    private readonly string _raiz = Directory.CreateTempSubdirectory("spobras-celular-prev-").FullName;
    private readonly PlataformaDeTeste _p;

    public BasesPrevistasCelularTests()
    {
        _p = new PlataformaDeTeste(_raiz);
        AmostraCelular.GravarMestra(_p.Mestra);
    }

    public void Dispose() => Directory.Delete(_raiz, recursive: true);

    private async Task<AppCelular> AppComBase()
    {
        var app = new AppCelular(_p, () => Hoje);
        Assert.True(await app.Base.EscolherPlanilhaAsync());
        return app;
    }

    [Fact]
    public async Task Le_as_duas_bases_e_liga_a_mesma_escola_sem_juntar()
    {
        var app = await AppComBase();
        var lic = app.Base.Previstas(BaseDeObras.EmLicitacao);
        Assert.Equal(3, lic.Count);
        Assert.Equal(2, app.Base.Previstas(BaseDeObras.ALicitar).Count);
        Assert.Equal(7, app.Base.TotalDeObras); // as obras sem contrato não entram na base em andamento

        var delta = lic.Single(o => o.Nome == "EMEI Exemplo Alfa");
        Assert.Null(delta.Empresa);
        var outras = app.Base.TambemEm(delta.Nome, BaseDeObras.EmLicitacao, delta.Id);
        Assert.Contains(outras, o => o.Base == BaseDeObras.EmAndamento && o.Detalhe.Contains("044/EX/2026"));
        Assert.Contains(outras, o => o.Base == BaseDeObras.ALicitar);
        Assert.Same(app.Base.Prevista(BaseDeObras.EmLicitacao, delta.Id)!.Nome, delta.Nome);
        Assert.Null(app.Base.Prevista(BaseDeObras.ALicitar, delta.Id)); // id de outra base não abre
    }

    [Fact]
    public async Task Filtro_de_cada_base_fica_guardado_separado()
    {
        var app = await AppComBase();
        app.FiltrosPrevistas[BaseDeObras.EmLicitacao] = new FiltroPrevistas { Lote = ["LOTE 64"] };
        Assert.True(app.FiltrosPrevistas[BaseDeObras.ALicitar].Vazio);
        Assert.Equal(2, Previstas.Filtrar(app.Base.Previstas(BaseDeObras.EmLicitacao), app.FiltrosPrevistas[BaseDeObras.EmLicitacao]).Count);
        Assert.True(app.FiltroBase.Vazio);
    }

    [Fact]
    public async Task Exporta_planilha_no_modelo_da_base_e_nao_duplica_com_dois_toques()
    {
        var app = await AppComBase();
        var obras = app.Base.Previstas(BaseDeObras.ALicitar);
        var entrou = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var continuar = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _p.AntesDeSalvar = () =>
        {
            entrou.TrySetResult();
            return continuar.Task;
        };

        var primeiro = app.ResumoPdf.ExportarPrevistasAsync(BaseDeObras.ALicitar, obras, new FiltroPrevistas());
        await entrou.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Null(await app.ResumoPdf.ExportarPrevistasAsync(BaseDeObras.ALicitar, obras, new FiltroPrevistas()));
        continuar.SetResult();
        var salvo = Assert.IsType<ResumoPdfSalvo>(await primeiro);
        Assert.True(salvo.Planilha);
        Assert.Equal("Consulta · A licitar", salvo.Titulo);
        Assert.Equal("Consulta - A licitar - 2026-11-12.xlsx", salvo.Arquivo.Nome);

        var arquivo = Assert.Single(Directory.GetFiles(_p.Saida, "*.xlsx"));
        using var leitor = new LeitorXlsx(arquivo);
        var linhas = leitor.LerAba("Obras");
        Assert.Equal(3, linhas.Count);
        Assert.Contains(linhas, l => "EMEI Exemplo Kappa".Equals(l[0]) && 800_000m.Equals(l[8]) && 880_000m.Equals(l[9]));
        salvo.Limpar();
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(_p.PastaDoApp, "resumos")));
        Assert.Null(await app.ResumoPdf.ExportarPrevistasAsync(BaseDeObras.ALicitar, [], new FiltroPrevistas())); // nada para exportar
    }
}
