using Spobras.App.ViewModels;
using Spobras.Core.Base;
using Spobras.Dados.Configuracao;
using Spobras.Documentos.Planilhas;

namespace Spobras.App.Tests;

/// <summary>Tela Base: seletor das três bases, filtros e ficha das obras sem contrato, "também está em" e exportação.</summary>
public class BasePrevistasTests : ComApp
{
    private ApoioConsultas Apoio => new(Dialogos, new PerfilRepositorio(Banco), Operacoes, Preferencias, Relogio);

    private async Task<BaseVm> Abrir()
    {
        await Fontes.DefinirAsync(Spobras.Core.Base.Fontes.Mestra, GravarMestra());
        return new BaseVm(Base, Fontes, new Servicos.InterfaceDireta(), _ => { }, apoio: Apoio);
    }

    [Fact]
    public async Task Seletor_troca_a_base_sem_misturar_listas_nem_filtros()
    {
        var tela = await Abrir();
        Assert.True(tela.EmAndamento);
        Assert.Null(tela.Prevista);
        int andamento = tela.Obras.Count;

        tela.MostrarBaseCommand.Execute("licitacao");
        Assert.False(tela.EmAndamento);
        var lic = Assert.IsType<PrevistasVm>(tela.Prevista);
        Assert.Equal("Em licitação", lic.Titulo);
        Assert.Equal(["CEU Épsilon", "CEI Zeta", "EMEF Alfa"], lic.Obras.Select(o => o.Nome)); // por lote e nome
        Assert.Equal("3 obras em licitação", lic.Resumo);
        Assert.Contains("R$ 3.600.000,00", lic.Totais);
        Assert.Contains(FiltroPrevistas.SemEmpresa, lic.Empresas);

        tela.BaseAtual = BaseDeObras.ALicitar;
        Assert.Same(tela.ALicitar, tela.Prevista);
        Assert.False(tela.ALicitar.EhLicitacao);
        Assert.Empty(tela.ALicitar.Empresas); // a licitar não tem empresa
        Assert.Equal("Lote ou agrupamento", tela.ALicitar.RotuloLote);

        tela.BaseAtual = BaseDeObras.EmAndamento;
        Assert.Equal(andamento, tela.Obras.Count);
        Assert.DoesNotContain(tela.Obras, o => o.Escola is "CEU Épsilon" or "CEI Zeta" or "CEU Teta");
    }

    [Fact]
    public async Task Filtros_e_ficha_da_licitacao_com_nomes_claros_dos_valores()
    {
        var tela = await Abrir();
        var lic = tela.Licitacao;
        lic.Empresa = [FiltroPrevistas.SemEmpresa];
        var alfa = Assert.Single(lic.Obras);
        Assert.Equal("1 obra em licitação de 3", lic.Resumo);
        lic.Selecionada = alfa;
        var campos = alfa.Campos.ToDictionary(c => c.Key, c => c.Value);
        Assert.Equal("Sem vencedora ainda", campos["Empresa"]);
        Assert.Equal("R$ 2.000.000,00", campos["Valor da obra"]);
        Assert.Equal("R$ 2.200.000,00", campos["Com os 10% (recursos)"]);
        Assert.Equal("Sem Contrato Receita", campos["Contrato de receita"]);
        Assert.Equal("Publicada", campos["Situação"]);
        Assert.Contains("Sem vencedora ainda", alfa.Detalhe);

        lic.LimparFiltrosCommand.Execute(null);
        lic.Texto = "épsilon";
        var epsilon = Assert.Single(lic.Obras);
        Assert.Equal("015/EX/2025 (Portaria 4)", epsilon.Campos.Single(c => c.Key == "Contrato de receita").Value);

        lic.Texto = "nada disso";
        Assert.Empty(lic.Obras);
        Assert.Equal("Nenhuma obra com esses filtros.", lic.Resumo);
        Assert.Equal("", lic.Totais);
    }

    [Fact]
    public async Task Tambem_esta_em_leva_para_a_mesma_escola_na_outra_base()
    {
        var tela = await Abrir();
        tela.BaseAtual = BaseDeObras.EmLicitacao;
        tela.Licitacao.Selecionada = tela.Licitacao.Obras.Single(o => o.Nome == "EMEF Alfa");
        Assert.True(tela.Licitacao.TemTambemEm);
        Assert.Equal(3, tela.Licitacao.TambemEm.Count); // 2 contratos em andamento + 1 a licitar

        var aLicitar = tela.Licitacao.TambemEm.Single(o => o.Base == BaseDeObras.ALicitar);
        Assert.Equal("A licitar (Pista de Skate · LOTE 09)", aLicitar.Texto);
        tela.Licitacao.AbrirOutraCommand.Execute(aLicitar);
        Assert.Equal(BaseDeObras.ALicitar, tela.BaseAtual);
        Assert.Equal("EMEF Alfa", tela.ALicitar.Selecionada!.Nome);

        var contrato = tela.ALicitar.TambemEm.First(o => o.Base == BaseDeObras.EmAndamento);
        tela.ALicitar.AbrirOutraCommand.Execute(contrato);
        Assert.True(tela.EmAndamento);
        Assert.Equal(contrato.Id, tela.Selecionada!.Obra.Id);
        Assert.Equal(["EMEF Alfa"], tela.Escola);
        Assert.Equal(2, tela.TambemEm.Count); // na ficha da obra contratada: licitação e a licitar
    }

    [Fact]
    public async Task Exporta_cada_base_no_seu_modelo_com_o_valor_antes_dos_10_porcento()
    {
        var tela = await Abrir();
        tela.Licitacao.Status = ["Homologação"];
        tela.Licitacao.ExportarCommand.Execute(null);
        Assert.False(tela.Licitacao.ResultadoComFalha, tela.Licitacao.Resultado);
        string arquivo = Em("Downloads", "Consulta - Em licitação - 2026-11-11.xlsx");
        Assert.Equal("“Consulta - Em licitação - 2026-11-11.xlsx” gerado com 2 obras.", tela.Licitacao.Resultado);
        using (var leitor = new LeitorXlsx(arquivo))
        {
            var obras = leitor.LerAba("Obras");
            Assert.Equal(FichaPrevista.Titulos(BaseDeObras.EmLicitacao), obras[0].Where(v => v is not null).Select(v => v!.ToString()));
            Assert.Equal(3, obras.Count);
            var epsilon = obras.Single(l => "CEU Épsilon".Equals(l[0]));
            Assert.Equal(1_000_000m, epsilon[8]);
            Assert.Equal(1_100_000m, epsilon[9]);
            var sobre = leitor.LerAba("Sobre esta consulta");
            Assert.Contains(sobre, l => "Filtro: Situação".Equals(l[0]) && "Homologação".Equals(l[1]));
            Assert.Contains(sobre, l => "Base".Equals(l[0]) && "Em licitação".Equals(l[1]));
        }

        tela.ALicitar.ExportarCommand.Execute(null);
        using (var leitor = new LeitorXlsx(Em("Downloads", "Consulta - A licitar - 2026-11-11.xlsx")))
        {
            var obras = leitor.LerAba("Obras");
            Assert.Equal(FichaPrevista.Titulos(BaseDeObras.ALicitar), obras[0].Where(v => v is not null).Select(v => v!.ToString()));
            Assert.DoesNotContain("Empresa", obras[0]);
            Assert.Equal(4, obras.Count);
        }

        tela.ALicitar.Texto = "nada";
        tela.ALicitar.ExportarCommand.Execute(null);
        Assert.True(tela.ALicitar.ResultadoComFalha);
    }

    [Fact]
    public async Task Base_vazia_explica_de_onde_vem_e_releitura_atualiza()
    {
        var p = Spobras.Testes.BaseSintetica.Mestra();
        p.AbasPorNome["A Licitar"].RemoveRange(4, 3);
        await Fontes.DefinirAsync(Spobras.Core.Base.Fontes.Mestra, GravarMestra(p));
        var tela = new BaseVm(Base, Fontes, new Servicos.InterfaceDireta(), _ => { }, apoio: Apoio);
        Assert.False(tela.ALicitar.TemObras);
        Assert.Contains("aba “A Licitar”", tela.ALicitar.Resumo);

        await Fontes.DefinirAsync(Spobras.Core.Base.Fontes.Mestra, GravarMestra());
        Assert.True(tela.ALicitar.TemObras);
        Assert.Equal(3, tela.ALicitar.Obras.Count);
    }
}
