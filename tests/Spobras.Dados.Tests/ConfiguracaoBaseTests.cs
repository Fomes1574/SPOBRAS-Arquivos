using Spobras.Core.Base;
using Spobras.Core.Planilhas;
using Spobras.Dados.Base;
using Spobras.Testes;

namespace Spobras.Dados.Tests;

/// <summary>Configuracoes da base (colunas e tabelas configuraveis) e as consultas mais rapidas.</summary>
public class ConfiguracaoBaseTests : IDisposable
{
    private readonly string _pasta = Directory.CreateTempSubdirectory("spobras-cfgbase-").FullName;
    private readonly BaseRepositorio _base;

    public ConfiguracaoBaseTests()
    {
        _base = new BaseRepositorio(new BancoApp(Path.Combine(_pasta, "app.db")));
        _base.GravarMestra(ImportadorMestra.Importar(BaseSintetica.Mestra()), Fonte());
    }

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    private static FonteInfo Fonte() => new() { Id = Fontes.Mestra, Nome = "mestra", Caminho = "mestra.xlsx", Estado = EstadoFonte.Atualizada, Versao = 1 };

    /// <summary>Mestra sintetica com uma coluna e uma tabela renomeadas (como se a planilha tivesse mudado).</summary>
    private static PlanilhaFalsa Renomeada()
    {
        var p = BaseSintetica.Mestra();
        var obras = p.TabelasPorNome[ImportadorMestra.TabelaObras];
        p.TabelasPorNome.Remove(ImportadorMestra.TabelaObras);
        var colunas = obras.Colunas.Select(c => c == "INÍCIO EXECUÇÃO" ? "DATA INÍCIO OBRA" : c).ToArray();
        p.Tabela("Obras_2027", colunas, [.. obras.Linhas]);
        return p;
    }

    [Fact]
    public void Planilha_que_mudou_e_recusada_com_dica_e_lida_depois_de_ajustar_o_mapeamento()
    {
        var ex = Assert.Throws<ImportacaoInvalidaException>(() => ImportadorMestra.Importar(Renomeada()));
        Assert.Contains("Obras", ex.Message);

        var mapa = MapeamentoMestra.Padrao.ComTabela(TabelaMestra.Obras, "Obras_2027");
        ex = Assert.Throws<ImportacaoInvalidaException>(() => ImportadorMestra.Importar(Renomeada(), mapa));
        Assert.Contains("INÍCIO EXECUÇÃO", ex.Message);
        Assert.Contains("engrenagem", ex.Message);

        mapa = mapa.ComColuna(CamposMestra.Campo(TabelaMestra.Obras, "INÍCIO EXECUÇÃO"), "DATA INÍCIO OBRA");
        var dados = ImportadorMestra.Importar(Renomeada(), mapa);
        Assert.Equal(5, dados.Obras.Count);
        Assert.Equal(new DateOnly(2026, 1, 10), dados.Obras[0].DataOs);
        Assert.False(mapa.EPadrao);

        // voltar a coluna ao nome padrao tira a entrada do mapeamento
        Assert.Empty(mapa.ComColuna(CamposMestra.Campo(TabelaMestra.Obras, "INÍCIO EXECUÇÃO"), "INÍCIO EXECUÇÃO").Colunas);
    }

    [Fact]
    public void Conferencia_mostra_o_que_falta_as_colunas_da_planilha_e_um_exemplo_de_cada()
    {
        var c = ConferenciaMestra.Conferir(Renomeada(), MapeamentoMestra.Padrao);
        Assert.False(c.Ok);
        var obras = c.Tabelas.Single(t => t.Tabela == TabelaMestra.Obras);
        Assert.False(obras.Encontrada);
        Assert.Contains("Obras_2027", c.TabelasDaPlanilha);

        var mapa = MapeamentoMestra.Padrao.ComTabela(TabelaMestra.Obras, "Obras_2027");
        c = ConferenciaMestra.Conferir(Renomeada(), mapa);
        var falta = Assert.Single(c.Faltando);
        Assert.Equal("Data da OS", falta.Campo.Rotulo);
        var col = c.Tabelas.Single(t => t.Tabela == TabelaMestra.Obras).Colunas.Single(x => x.Nome == "DATA INÍCIO OBRA");
        Assert.Equal("10/01/2026", col.Exemplo);
        Assert.Equal(5, c.Tabelas.Single(t => t.Tabela == TabelaMestra.Obras).Linhas);

        // coluna opcional (reajuste) que falta nao impede
        Assert.True(ConferenciaMestra.Conferir(BaseSintetica.Mestra(), MapeamentoMestra.Padrao).Ok);
    }

    [Fact]
    public void Todos_os_campos_do_catalogo_existem_na_mestra_padrao()
    {
        var c = ConferenciaMestra.Conferir(BaseSintetica.Mestra(), MapeamentoMestra.Padrao);
        Assert.All(c.Campos, x => Assert.True(x.Encontrada, x.Campo.Chave));
        Assert.Equal(CamposMestra.Todos.Count, CamposMestra.Todos.Select(x => x.Chave).Distinct().Count());
    }

    [Fact]
    public void Empresa_com_grafias_diferentes_vira_uma_opcao_e_filtra_todas_as_grafias()
    {
        var p = BaseSintetica.Mestra();
        var t = p.TabelasPorNome[ImportadorMestra.TabelaObras];
        int ic = t.Indice("CONTRATADA");
        var linhas = t.Linhas.Select((l, i) => { var c = (object?[])l.Clone(); c[ic] = i switch { 0 => "Construtora  Roy ", 1 => "CONSTRUTORA ROY", 2 => "Construtora Roy", _ => "Outra Ltda" }; return c; }).ToArray();
        p.Tabela(ImportadorMestra.TabelaObras, [.. t.Colunas], linhas);
        _base.GravarMestra(ImportadorMestra.Importar(p), Fonte());

        var empresas = _base.Opcoes(new FiltroObras()).Empresas;
        Assert.Equal(2, empresas.Count);
        Assert.Contains("Outra Ltda", empresas);
        string roy = empresas.Single(e => e != "Outra Ltda");
        Assert.Equal(3, _base.Buscar(new FiltroObras { Empresa = [roy] }).Total);
    }

    [Fact]
    public void Ocultar_status_no_banco_medicoes_de_varios_contratos_e_contagem_de_status()
    {
        var todos = _base.StatusDoContrato();
        Assert.Equal(5, todos.Sum(s => s.Obras));
        string vigente = todos.First().Status;
        Assert.Equal(0, _base.Buscar(new FiltroObras { OcultarStatusContrato = [vigente.ToLowerInvariant()] }).Total);
        Assert.True(new FiltroObras { OcultarStatusContrato = [vigente] }.Vazio); // nao conta como filtro do usuario

        var medicoes = _base.Medicoes(["001/EX/2024", "004/ex/2025", "inexistente"]);
        Assert.Equal(3, medicoes["001/EX/2024"].Count);
        Assert.Equal(4, medicoes["004/EX/2025"].Count);
        Assert.Empty(medicoes["inexistente"]);
        Assert.Equal(_base.Medicoes("004/EX/2025").Count, medicoes["004/ex/2025"].Count);
    }

    [Fact]
    public void Opcoes_gerais_sao_guardadas_e_refeitas_depois_de_uma_nova_leitura()
    {
        var antes = _base.OpcoesGerais();
        Assert.Same(antes, _base.OpcoesGerais());
        _base.GravarMestra(ImportadorMestra.Importar(BaseSintetica.Mestra()), Fonte());
        Assert.NotSame(antes, _base.OpcoesGerais());
        Assert.Equal(antes.Escolas, _base.OpcoesGerais().Escolas);
    }
}
