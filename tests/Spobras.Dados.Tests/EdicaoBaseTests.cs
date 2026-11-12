using Microsoft.Data.Sqlite;
using Spobras.Core.Base;
using Spobras.Dados.Base;
using Spobras.Testes;

namespace Spobras.Dados.Tests;

public class EdicaoBaseTests : IDisposable
{
    private static readonly DateTimeOffset Agora = new(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);
    private readonly string _pasta = Directory.CreateTempSubdirectory("spobras-edicao-").FullName;
    private readonly BaseRepositorio _base;

    public EdicaoBaseTests()
    {
        _base = new BaseRepositorio(new BancoApp(Path.Combine(_pasta, "app.db")));
        Reler();
        _base.GravarDotacoes(ImportadorDotacoes.Importar(BaseSintetica.Dotacoes()), Fonte(Fontes.Dotacoes));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_pasta, recursive: true);
    }

    private static FonteInfo Fonte(string id) => new() { Id = id, Nome = id, Caminho = id + ".xlsx", Estado = EstadoFonte.Atualizada, Versao = 1, UltimaLeitura = Agora };

    private void Reler(PlanilhaFalsa? mestra = null) => _base.GravarMestra(ImportadorMestra.Importar(mestra ?? BaseSintetica.Mestra()), Fonte(Fontes.Mestra));

    private ObraLida Obra(string contrato, string escola) =>
        _base.Buscar(new FiltroObras { Contrato = contrato }, 50).Obras.Single(o => o.Empreendimento == escola);

    private static object? V(string campo, string texto) => CamposObra.Converter(CamposObra.Por(campo)!, texto).Valor;

    [Fact]
    public void Edicao_vale_por_cima_da_planilha_sobrevive_a_releitura_e_pode_voltar()
    {
        var alfa = Obra("001/EX/2024", "EMEF Alfa");
        _base.GravarEdicao(alfa, new Dictionary<string, object?>
        {
            ["lote"] = V("lote", "LOTE 09"),
            ["data_os"] = V("data_os", "05/02/2026"),
            ["saldo_pagar"] = V("saldo_pagar", "2.000,50"),
            ["contratada"] = V("contratada", alfa.Contratada!), // igual: nao vira edicao
        }, Agora);

        var editada = Obra("001/EX/2024", "EMEF Alfa");
        Assert.Equal(("LOTE 09", new DateOnly(2026, 2, 5), 2000.50m, 3), (editada.Lote, editada.DataOs, editada.SaldoAPagar, editada.CamposEditados));
        var ajustes = _base.Ajustes("001/EX/2024", "EMEF Alfa");
        Assert.Equal(["data_os", "lote", "saldo_pagar"], ajustes.Select(a => a.Campo).Order());
        Assert.Equal("LOTE 01", ajustes.Single(a => a.Campo == "lote").ValorDaPlanilha);
        Assert.Single(_base.Buscar(new FiltroObras { Texto = "lote 09" }).Obras); // a busca usa o valor editado

        Reler(); // a planilha foi salva de novo, sem mudar esses campos
        Assert.Equal("LOTE 09", Obra("001/EX/2024", "EMEF Alfa").Lote);
        Assert.DoesNotContain(_base.Ajustes("001/EX/2024", "EMEF Alfa"), a => a.PlanilhaMudou);

        _base.VoltarAoDaPlanilha(editada, "lote");
        Assert.Equal("LOTE 01", Obra("001/EX/2024", "EMEF Alfa").Lote);
        Assert.Equal(2, Obra("001/EX/2024", "EMEF Alfa").CamposEditados);

        // Editar de volta para o valor da planilha deixa de ser edicao
        _base.GravarEdicao(Obra("001/EX/2024", "EMEF Alfa"), new Dictionary<string, object?> { ["data_os"] = V("data_os", "10/01/2026") }, Agora);
        Assert.Equal(["saldo_pagar"], _base.Ajustes("001/EX/2024", "EMEF Alfa").Select(a => a.Campo));
    }

    [Fact]
    public void Se_a_planilha_muda_o_campo_editado_o_app_avisa_e_mantem_a_edicao()
    {
        _base.GravarEdicao(Obra("004/EX/2025", "CEU Delta"), new Dictionary<string, object?> { ["lote"] = V("lote", "LOTE 99") }, Agora);

        var nova = BaseSintetica.Mestra().Tabela("contratos_VW_ESCOLAS_CONTRATOS_MEDIDOS_PAGOS", BaseSintetica.ColunasObras,
            BaseSintetica.Obra("004/EX/2025", "CEU Delta", BaseSintetica.P6, new DateTime(2025, 6, 15), 1000m, lote: "LOTE 03"));
        Reler(nova);

        var ajuste = Assert.Single(_base.Ajustes("004/EX/2025", "CEU Delta"));
        Assert.True(ajuste.PlanilhaMudou);
        Assert.Equal("LOTE 03", ajuste.ValorDaPlanilha); // o que a planilha diz agora
        Assert.Equal("LOTE 99", Obra("004/EX/2025", "CEU Delta").Lote); // a edicao continua valendo
        _base.VoltarAoDaPlanilha(Obra("004/EX/2025", "CEU Delta"), "lote");
        Assert.Equal("LOTE 03", Obra("004/EX/2025", "CEU Delta").Lote);
    }

    [Fact]
    public void Obra_incluida_no_app_aparece_nos_filtros_volta_depois_da_releitura_e_pode_sair()
    {
        var incluida = _base.IncluirObra(new ObraLida
        {
            Contrato = "900/NOVO/2026", Empreendimento = "EMEI Nova", Portaria = BaseSintetica.P6, Lote = "LOTE 02", DataOs = new DateOnly(2026, 12, 1), SaldoAPagar = 500m,
        }, Agora);

        Assert.True(incluida.Id < 0);
        Assert.Contains("900/NOVO/2026", _base.Opcoes(new FiltroObras()).Contratos);
        Reler();
        var lida = Obra("900/NOVO/2026", "EMEI Nova");
        Assert.True(lida.IncluidaNoApp);
        Assert.Equal(500m, lida.SaldoAPagar);

        _base.GravarEdicao(lida, new Dictionary<string, object?> { ["empreendimento"] = V("empreendimento", "EMEI Nova Esperança") }, Agora);
        Reler();
        Assert.Equal("EMEI Nova Esperança", _base.Buscar(new FiltroObras { Contrato = "900/NOVO/2026" }).Obras.Single().Empreendimento);

        Assert.Throws<InvalidOperationException>(() => _base.IncluirObra(new ObraLida { Contrato = "001/EX/2024", Empreendimento = "EMEF Alfa" }, Agora));
        Assert.Throws<InvalidOperationException>(() => _base.RemoverObraIncluida(Obra("001/EX/2024", "EMEF Alfa")));
        _base.RemoverObraIncluida(_base.Buscar(new FiltroObras { Contrato = "900/NOVO/2026" }).Obras.Single());
        Reler();
        Assert.Empty(_base.Buscar(new FiltroObras { Contrato = "900/NOVO/2026" }).Obras);
    }

    [Fact]
    public void Portarias_e_dotacoes_editadas_no_app_ficam_marcadas_e_sobrevivem_a_releitura()
    {
        var antes = _base.Vinculos();
        var alterar = antes.First(v => v.DotacaoResumida == "3363");
        var remover = antes.First(v => v.DotacaoResumida == "3359");

        _base.AlterarVinculo(alterar, alterar with { Descricao = "Construção de CEU (corrigido)" }, Agora);
        _base.RemoverVinculo(remover, Agora);
        _base.IncluirVinculo(new VinculoBase(0, "Portaria 7 (2026)", "999/EX/2026", null, "16.10.12.365.3010.4000.4490.5100 00", null, "4000", "Nova dotação"), Agora);

        void Conferir()
        {
            var depois = _base.Vinculos();
            Assert.Equal(BaseRepositorio.OrigemEditado, depois.Single(v => v.DotacaoResumida == "3363").Origem);
            Assert.Equal("Construção de CEU (corrigido)", depois.Single(v => v.DotacaoResumida == "3363").Descricao);
            Assert.DoesNotContain(depois, v => v.DotacaoResumida == "3359");
            var nova = depois.Single(v => v.DotacaoResumida == "4000");
            Assert.Equal((BaseRepositorio.OrigemIncluido, "16.10.12.365.3010.4000.4490.5100.00"), (nova.Origem, nova.DotacaoCompleta)); // espaco corrigido
            Assert.Contains(_base.Portarias(), p => p.Rotulo == "Portaria 7 (2026)" && p.Numero == 7 && p.Ano == 2026);
        }

        Conferir();
        _base.GravarDotacoes(ImportadorDotacoes.Importar(BaseSintetica.Dotacoes()), Fonte(Fontes.Dotacoes));
        Conferir();
        Assert.Equal(3, _base.EdicoesDeVinculos());
        Assert.Throws<InvalidOperationException>(() => _base.IncluirVinculo(new VinculoBase(0, " ", "x", null, "", null, null, null), Agora));
    }
}
