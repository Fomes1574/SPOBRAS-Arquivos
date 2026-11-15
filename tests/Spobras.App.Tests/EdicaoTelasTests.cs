using Spobras.App.ViewModels;
using Spobras.Dados.Configuracao;
using Spobras.Testes;

namespace Spobras.App.Tests;

public class EdicaoTelasTests : ComApp
{
    private ApoioConsultas Apoio => new(Dialogos, new PerfilRepositorio(Banco), Operacoes, Preferencias, Relogio);

    private async Task<BaseVm> Abrir()
    {
        await Fontes.DefinirAsync(Spobras.Core.Base.Fontes.Mestra, GravarMestra());
        await Fontes.DefinirAsync(Spobras.Core.Base.Fontes.Dotacoes, GravarDotacoes());
        return new BaseVm(Base, Fontes, new Servicos.InterfaceDireta(), _ => { }, apoio: Apoio);
    }

    [Fact]
    public async Task Base_e_dotacoes_sao_so_consulta_sem_editar_nem_incluir()
    {
        var tela = await Abrir();
        tela.Selecionada = tela.Obras.First(o => o.Escola == "CEU Delta");
        Assert.False(tela.EditarObraCommand.CanExecute(null));
        Assert.False(tela.NovaObraCommand.CanExecute(null));
        Assert.False(tela.RemoverObraIncluidaCommand.CanExecute(null));
        tela.EditarObraCommand.Execute(null); // mesmo chamado direto, nada acontece
        Assert.False(tela.Editando);

        var fontes = new FontesVm(Fontes, Base, Dialogos, new Servicos.InterfaceDireta(), Preferencias) { Operacoes = Operacoes, Relogio = Relogio };
        var v = fontes.Vinculos.First();
        Assert.False(fontes.NovoVinculoCommand.CanExecute(null));
        Assert.False(fontes.EditarVinculoCommand.CanExecute(v));
        Assert.False(fontes.RemoverVinculoCommand.CanExecute(v));
    }

    [Fact]
    public async Task Fontes_de_dados_exporta_portarias_e_dotacoes()
    {
        await Abrir();
        var fontes = new FontesVm(Fontes, Base, Dialogos, new Servicos.InterfaceDireta(), Preferencias) { Operacoes = Operacoes, Relogio = Relogio };

        fontes.ExportarVinculosCommand.Execute(null);
        string arquivo = Directory.GetFiles(Trabalho, "Portarias e dotações*.xlsx").Single();
        using var leitor = new Spobras.Documentos.Planilhas.LeitorXlsx(arquivo);
        var grade = leitor.LerAba("Portarias e dotações");
        Assert.Equal("Origem", grade[0][6]);
        Assert.Contains(grade, l => "3363".Equals(l[4]) || "3363".Equals(l[3]?.ToString()?.Split('.').ElementAtOrDefault(5)));
    }
}
