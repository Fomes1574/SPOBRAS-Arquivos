using Spobras.App.Servicos;
using Spobras.App.ViewModels;
using Spobras.Core.Organizar;
using Spobras.Testes;

namespace Spobras.App.Tests;

/// <summary>O app montado como no Windows (sem janela): os modulos conversam entre si.</summary>
public class ComposicaoTests : IDisposable
{
    private readonly string _raiz = Directory.CreateTempSubdirectory("spobras-comp-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_raiz, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task Organizador_usa_a_base_lida_e_a_base_leva_a_busca()
    {
        string trabalho = Path.Combine(_raiz, "Downloads");
        Directory.CreateDirectory(trabalho);
        using var app = new Composicao(Path.Combine(_raiz, "dados"), trabalho, new DialogosFalsos(), new InterfaceDireta(), "teste", eventosDasFontes: false)
        {
            VigiarFontes = false,
        };
        string mestra = Path.Combine(_raiz, "Base", Spobras.Core.Base.Fontes.NomeArquivoMestra);
        Directory.CreateDirectory(Path.GetDirectoryName(mestra)!);
        XlsxSintetico.Gravar(BaseSintetica.Mestra(), mestra);
        await app.Fontes.DefinirAsync(Spobras.Core.Base.Fontes.Mestra, mestra);

        string arquivo = Path.Combine(trabalho, "Medição 004-EX-2025.xlsx");
        File.WriteAllText(arquivo, "x");
        var regra = new Regra { Nome = "Por obra", Destino = Path.Combine(_raiz, "Obras", "{contrato}", "{escola}") };
        var s = app.Novidades.Planejador.Planejar([new ArquivoParaOrganizar(arquivo)], [regra], []);
        Assert.Equal(Path.Combine(_raiz, "Obras", "004-EX-2025", "CEU Delta", "Medição 004-EX-2025.xlsx"), s[0].CaminhoDestino);

        app.Principal.Navegar(BaseVm.Modulo);
        var tela = (BaseVm)app.Principal.PaginaAtual!;
        tela.Selecionada = tela.Obras.First(o => o.Contrato == "004/EX/2025");
        tela.BuscarNosArquivosCommand.Execute("contrato");
        var busca = Assert.IsType<FerramentasVm>(app.Principal.PaginaAtual);
        Assert.Equal("004/EX/2025", busca.Termo);
        Assert.True(busca.EhBuscar);
    }
}
