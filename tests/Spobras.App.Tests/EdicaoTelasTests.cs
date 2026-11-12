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
    public async Task Editar_obra_mostra_o_valor_da_planilha_e_permite_voltar()
    {
        var tela = await Abrir();
        tela.Selecionada = tela.Obras.First(o => o.Escola == "CEU Delta");
        tela.EditarObraCommand.Execute(null);
        var edicao = tela.Edicao!;
        Assert.True(tela.Editando);
        Assert.True(edicao.Campos.Single(c => c.Campo.Chave == "contrato").SomenteLeitura);

        edicao.Campos.Single(c => c.Campo.Chave == "lote").Valor = "LOTE 42";
        edicao.Campos.Single(c => c.Campo.Chave == "data_os").Valor = "31/02/2026";
        tela.SalvarEdicaoCommand.Execute(null);
        Assert.Contains("a data \"31/02/2026\" não foi entendida", edicao.Problemas);
        Assert.True(tela.Editando);

        edicao.Campos.Single(c => c.Campo.Chave == "data_os").Valor = "01/07/2025";
        tela.SalvarEdicaoCommand.Execute(null);
        Assert.False(tela.Editando);
        Assert.Equal("2 campos mudados em CEU Delta. A planilha mestra não foi alterada.", tela.Resultado);
        Assert.Equal(("LOTE 42", "01/07/2025", "2 campos editados no app"), (tela.Selecionada!.Lote, tela.Selecionada.DataOs, tela.Selecionada.Marca));

        tela.EditarObraCommand.Execute(null);
        var lote = tela.Edicao!.Campos.Single(c => c.Campo.Chave == "lote");
        Assert.True(lote.Editado);
        Assert.Equal("Na planilha: LOTE 01", lote.NaPlanilha);
        tela.VoltarAoDaPlanilhaCommand.Execute(lote);
        Assert.Equal("LOTE 01", tela.Selecionada!.Lote);
        Assert.Equal("1 campo editado no app", tela.Selecionada.Marca);
    }

    [Fact]
    public async Task Incluir_obra_nova_pede_contrato_e_escola_e_pode_sair()
    {
        var tela = await Abrir();
        tela.NovaObraCommand.Execute(null);
        tela.SalvarEdicaoCommand.Execute(null);
        Assert.Contains("Contrato não pode ficar vazio", tela.Edicao!.Problemas);

        var c = tela.Edicao.Campos;
        c.Single(x => x.Campo.Chave == "contrato").Valor = "900/NOVO/2026";
        c.Single(x => x.Campo.Chave == "empreendimento").Valor = "EMEI Nova";
        c.Single(x => x.Campo.Chave == "saldo_pagar").Valor = "1.000,00";
        tela.SalvarEdicaoCommand.Execute(null);

        Assert.StartsWith("EMEI Nova incluída no app", tela.Resultado);
        Assert.Equal(("EMEI Nova", "Incluída no app", "R$ 1.000,00"), (tela.Selecionada!.Escola, tela.Selecionada.Marca, tela.Selecionada.SaldoAPagar.Replace(' ', ' ')));

        Dialogos.Confirmacoes.Enqueue(true);
        tela.RemoverObraIncluidaCommand.Execute(null);
        Assert.DoesNotContain(tela.Obras, o => o.Contrato == "900/NOVO/2026");
    }

    [Fact]
    public async Task Fontes_de_dados_edita_inclui_tira_e_exporta_portarias_e_dotacoes()
    {
        await Abrir();
        var fontes = new FontesVm(Fontes, Base, Dialogos, new Servicos.InterfaceDireta(), Preferencias) { Operacoes = Operacoes, Relogio = Relogio };
        var v = fontes.Vinculos.First(x => x.DotacaoResumida == "3363");

        fontes.EditarVinculoCommand.Execute(v);
        fontes.VDescricao = "Construção de CEU revisada";
        fontes.SalvarVinculoCommand.Execute(null);
        Assert.Equal(("Construção de CEU revisada", "editado no app"), (fontes.Vinculos.First(x => x.DotacaoResumida == "3363").Descricao, fontes.Vinculos.First(x => x.DotacaoResumida == "3363").Origem));

        fontes.NovoVinculoCommand.Execute(null);
        fontes.SalvarVinculoCommand.Execute(null);
        Assert.Contains("obrigatórias", fontes.ProblemaVinculo);
        fontes.VPortaria = "Portaria 8 (2026)";
        fontes.VDotacao = "16.10.12.365.3010.4100.4490.5100 00";
        fontes.SalvarVinculoCommand.Execute(null);
        Assert.Contains(fontes.Vinculos, x => x.Portaria == "Portaria 8 (2026)" && x.Origem == "incluído no app");

        Dialogos.Confirmacoes.Enqueue(true);
        fontes.RemoverVinculoCommand.Execute(fontes.Vinculos.First(x => x.DotacaoResumida == "3359"));
        Assert.DoesNotContain(fontes.Vinculos, x => x.DotacaoResumida == "3359");

        fontes.ExportarVinculosCommand.Execute(null);
        string arquivo = Directory.GetFiles(Trabalho, "Portarias e dotações*.xlsx").Single();
        using var leitor = new Spobras.Documentos.Planilhas.LeitorXlsx(arquivo);
        var grade = leitor.LerAba("Portarias e dotações");
        Assert.Equal("Origem", grade[0][6]);
        Assert.Contains(grade, l => "Portaria 8 (2026)".Equals(l[0]) && "16.10.12.365.3010.4100.4490.5100.00".Equals(l[3]));
    }
}
