using Spobras.App.ViewModels;
using Spobras.Core.Base;
using Spobras.Dados.Configuracao;
using Spobras.Documentos.Planilhas;
using Spobras.Testes;

namespace Spobras.App.Tests;

public class ConsultasTests : ComApp
{
    private ApoioConsultas Apoio => new(Dialogos, new PerfilRepositorio(Banco), Operacoes, Preferencias, Relogio);

    private async Task<BaseVm> Abrir()
    {
        await Fontes.DefinirAsync(Spobras.Core.Base.Fontes.Mestra, GravarMestra());
        return new BaseVm(Base, Fontes, new Servicos.InterfaceDireta(), _ => { }, apoio: Apoio);
    }

    [Fact]
    public void Sem_apoio_as_funcoes_de_consulta_ficam_escondidas()
    {
        var tela = new BaseVm(Base, Fontes, new Servicos.InterfaceDireta(), _ => { });
        Assert.False(tela.PodeSalvarConsultas);
        tela.ExportarCommand.Execute(null);
        Assert.Equal("", tela.Resultado);
    }

    [Fact]
    public async Task T33_consulta_salva_reaberta_usa_os_dados_novos_da_fonte()
    {
        var tela = await Abrir();
        tela.Portaria = BaseSintetica.P3De2021;
        tela.ValorDe = "1,00";
        Assert.Equal("1 obra encontrada", tela.Resumo);

        Dialogos.Textos.Enqueue("Portaria 3 com saldo");
        tela.SalvarConsultaCommand.Execute(null);
        Assert.Equal("Portaria 3 com saldo", tela.ConsultaAberta!.Nome);
        Assert.Contains("usa os dados mais recentes", tela.Resultado);

        // A fonte muda: nova obra da mesma portaria com saldo
        var nova = BaseSintetica.Mestra().Tabela("contratos_VW_ESCOLAS_CONTRATOS_MEDIDOS_PAGOS", BaseSintetica.ColunasObras,
            BaseSintetica.Obra("001/EX/2024", "EMEF Alfa", BaseSintetica.P3De2021, new DateTime(2026, 1, 10), 1500m),
            BaseSintetica.Obra("005/EX/2026", "EMEF Épsilon", BaseSintetica.P3De2021, new DateTime(2026, 5, 4), 300m));
        await Fontes.DefinirAsync(Spobras.Core.Base.Fontes.Mestra, GravarMestra(nova));

        tela.LimparFiltrosCommand.Execute(null);
        var reaberta = new BaseVm(Base, Fontes, new Servicos.InterfaceDireta(), _ => { }, apoio: Apoio);
        Assert.Equal(["Portaria 3 com saldo"], reaberta.Consultas.Select(c => c.Nome));
        reaberta.ConsultaAberta = reaberta.Consultas[0];
        Assert.Equal(BaseSintetica.P3De2021, reaberta.Portaria);
        Assert.Equal("1,00", reaberta.ValorDe);
        Assert.Equal("2 obras encontradas", reaberta.Resumo);

        // Salvar com o mesmo nome atualiza; tirar da lista pede confirmacao
        reaberta.SemDataOs = false;
        Dialogos.Textos.Enqueue("Portaria 3 com saldo");
        reaberta.SalvarConsultaCommand.Execute(null);
        Assert.Single(reaberta.Consultas);
        Dialogos.Confirmacoes.Enqueue(true);
        reaberta.RemoverConsultaCommand.Execute(null);
        Assert.Empty(reaberta.Consultas);
        Assert.Equal(BaseSintetica.P3De2021, reaberta.Portaria); // filtros continuam
    }

    [Fact]
    public async Task Exporta_todas_as_obras_do_filtro_com_tipos_do_excel_e_aba_sobre_a_consulta()
    {
        var tela = await Abrir();
        tela.Portaria = BaseSintetica.P3De2021;
        tela.ExportarCommand.Execute(null);

        Assert.False(tela.ResultadoComFalha, tela.Resultado);
        string arquivo = Em("Downloads", "Consulta - Base de dados - 2026-11-11.xlsx");
        Assert.Equal("“Consulta - Base de dados - 2026-11-11.xlsx” gerado com 3 obras.", tela.Resultado);
        Assert.True(tela.PodeAbrirExportacao);
        using (var leitor = new LeitorXlsx(arquivo))
        {
            Assert.Equal(["Obras", "Sobre esta consulta"], leitor.NomesDasAbas());
            var obras = leitor.LerAba("Obras");
            Assert.Equal(4, obras.Count);
            Assert.Equal("Contrato", obras[0][0]);
            int os = Array.IndexOf(obras[0], "Data da OS (INÍCIO EXECUÇÃO)");
            int saldo = Array.IndexOf(obras[0], "Saldo a pagar");
            var alfa = obras.Single(l => "EMEF Alfa".Equals(l[1]));
            Assert.Equal(new DateTime(2026, 1, 10), alfa[os]);
            Assert.Equal(1500m, alfa[saldo]);
            Assert.Null(obras.Single(l => "EMEI Gama".Equals(l[1]))[os]); // sem OS fica em branco

            var sobre = leitor.LerAba("Sobre esta consulta");
            Assert.Contains(sobre, l => "Filtro: Portaria".Equals(l[0]) && BaseSintetica.P3De2021.Equals(l[1]));
            Assert.Contains(sobre, l => "Obras exportadas".Equals(l[0]) && 3m.Equals(l[1]));
            Assert.Contains(sobre, l => (l[0] as string ?? "").StartsWith("Fonte: "));
        }

        Assert.Equal("Exportou 3 obras da Base de dados", Operacoes.Ultimos()[0].Descricao);

        // De novo: nao sobrescreve
        tela.ExportarCommand.Execute(null);
        Assert.True(File.Exists(Em("Downloads", "Consulta - Base de dados - 2026-11-11 (2).xlsx")));

        // Filtro sem resultado: nada a exportar
        tela.Texto = "não existe";
        tela.ExportarCommand.Execute(null);
        Assert.True(tela.ResultadoComFalha);
        Assert.Contains("nada para exportar", tela.Resultado);
    }

    [Fact]
    public async Task Pendencias_mostram_obras_sem_os_e_levam_ao_filtro()
    {
        var tela = await Abrir();
        Assert.True(tela.TemPendencias);
        Assert.Equal("1 obra sem Data da OS", tela.Pendencias);

        tela.Portaria = BaseSintetica.P6;
        tela.VerObrasSemOsCommand.Execute(null);
        Assert.Null(tela.Portaria);
        Assert.True(tela.SemDataOs);
        Assert.Equal("EMEI Gama", Assert.Single(tela.Obras).Escola);
    }
}
