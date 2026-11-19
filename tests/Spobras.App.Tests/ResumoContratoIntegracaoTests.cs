using Spobras.App.Servicos;
using Spobras.App.ViewModels;
using Spobras.Celular.Servicos;
using Spobras.Core.Base;
using Spobras.Dados.Base;
using Spobras.Dados.Configuracao;
using Spobras.Testes;
using UglyToad.PdfPig;

namespace Spobras.App.Tests;

public sealed class ResumoContratoIntegracaoTests : ComApp
{
    private async Task<BaseVm> BaseComDados()
    {
        await Fontes.DefinirAsync(Spobras.Core.Base.Fontes.Mestra, GravarMestra());
        return new(Base, Fontes, new InterfaceDireta(), _ => { },
            apoio: new ApoioConsultas(Dialogos, Perfis, Operacoes, Preferencias, Relogio));
    }

    [Fact]
    public async Task Computador_e_celular_exportam_as_mesmas_paginas_e_textos()
    {
        var desktop = await BaseComDados();
        desktop.Contrato = ["001/EX/2024"];
        desktop.Escola = ["Escola inexistente"];
        Assert.Empty(desktop.Obras);
        Assert.Equal(3, Assert.Single(desktop.ResumosContratos).Escolas.Count);
        await desktop.ExportarContratosPdfCommand.ExecuteAsync(null);
        Assert.False(desktop.ResultadoComFalha, desktop.Resultado);
        string arquivoPc = Assert.Single(Directory.GetFiles(Trabalho, "*.pdf"));
        var plataforma = new PlataformaDeTeste(Em("celular"));
        File.Copy(GravarMestra(), plataforma.Mestra);
        var celular = new AppCelular(plataforma, () => Relogio.Agora);
        Assert.True(await celular.Base.EscolherPlanilhaAsync());
        var resumo = celular.Base.Repositorio.ResumosDosContratos(desktop.Contrato);
        var salvo = Assert.IsType<ResumoPdfSalvo>(await celular.ResumoPdf.SalvarAsync(resumo));
        using var pc = PdfDocument.Open(arquivoPc);
        using var android = PdfDocument.Open(salvo.Local);
        Assert.Equal(pc.NumberOfPages, android.NumberOfPages);
        Assert.Equal(pc.GetPages().Select(p => p.Text), android.GetPages().Select(p => p.Text));
        salvo.Limpar();
        Assert.False(File.Exists(salvo.Local));
        Assert.Single(Directory.GetFiles(plataforma.Saida, "*.pdf"));
    }

    [Fact]
    public async Task Consulta_antiga_com_lote_migra_para_contratos_sem_misturar_totais()
    {
        await BaseComDados();
        Perfis.Salvar("consulta-base", "Exemplo antigo", new { Lote = "LOTE 01" });
        var tela = new BaseVm(Base, Fontes, new InterfaceDireta(), _ => { },
            apoio: new ApoioConsultas(Dialogos, Perfis, Operacoes, Preferencias, Relogio));
        tela.ConsultaAberta = Assert.Single(tela.Consultas);
        Assert.Empty(tela.Lote);
        Assert.Contains("O filtro Lote foi trocado pelos contratos do lote", tela.Resultado);
        Assert.True(tela.Contrato.Count > 1);
        Assert.Equal(tela.Contrato.Count, tela.ResumosContratos.Count);
    }

    [Fact]
    public void Cronograma_busca_lotes_iguais_mas_inclui_so_escolas_do_contrato_escolhido()
    {
        var cronograma = NovoCronograma();
        var escolas = new[]
        {
            new ObraLida { Id = 1, Contrato = "010/EX/2026", Lote = "LOTE 14", Modalidade = "Manutenção", Empreendimento = "EMEI Alfa" },
            new ObraLida { Id = 2, Contrato = "010/EX/2026", Lote = "LOTE 14", Modalidade = "Manutenção", Empreendimento = "EMEI Beta" },
            new ObraLida { Id = 3, Contrato = "011/EX/2026", Lote = "LOTE 14", Modalidade = "Reforma", Empreendimento = "EMEI Gama" },
            new ObraLida { Id = 4, Contrato = "012/EX/2026", Lote = "AGRUPAMENTO 14", Modalidade = "Quadras", Empreendimento = "EMEI Delta" },
        };
        cronograma.ObrasDoContrato = c => escolas.Where(e => e.Contrato == c).ToArray();
        cronograma.BuscarContratos = _ => escolas.DistinctBy(e => e.Contrato)
            .Select(o => new OpcaoContratoResumo(o.Contrato, o.Lote, o.Modalidade, o.Portaria, escolas.Count(e => e.Contrato == o.Contrato))).ToArray();
        cronograma.PesquisaContrato = "14";
        Assert.Equal(3, cronograma.ItensBusca.Count);
        var escolhido = cronograma.ItensBusca[0];
        Assert.Equal(BaseDeObras.EmAndamento, escolhido.Base);
        cronograma.AdicionarDaBase(escolas[0]);
        Assert.Equal("EMEI Beta", Assert.Single(cronograma.SugestoesDoContrato).Obra.Empreendimento);
        cronograma.AdicionarItemBuscaCommand.Execute(escolhido);
        Assert.Equal("1 escola adicionada; 1 já estava no cronograma.", cronograma.Resultado);
        cronograma.AdicionarItemBuscaCommand.Execute(escolhido);
        Assert.Equal("Nenhuma escola nova: 2 já estavam no cronograma.", cronograma.Resultado);
        Assert.Equal(["EMEI Alfa", "EMEI Beta"], cronograma.Linhas.Select(l => l.Nome));
        Assert.All(cronograma.Linhas, l => Assert.Equal(escolhido.Contrato!.Contrato, l.ContratoOrigem));
    }
}
