using Spobras.Core.Base;
using Spobras.Core.Planilhas;
using Spobras.Dados.Base;
using Spobras.Testes;

namespace Spobras.Dados.Tests;

/// <summary>Bases Em licitação e A licitar: leitura pelas colunas, regra dos 10%, tabelas separadas e avisos só da base afetada.</summary>
public class LicitacoesTests : IDisposable
{
    private readonly string _pasta = Directory.CreateTempSubdirectory("spobras-lic-").FullName;
    private readonly BaseRepositorio _base;

    public LicitacoesTests() => _base = new BaseRepositorio(new BancoApp(Path.Combine(_pasta, "app.db")));

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    private static FonteInfo Fonte() => new() { Id = Fontes.Mestra, Nome = "mestra", Caminho = "mestra.xlsx", Estado = EstadoFonte.Atualizada, Versao = 1 };

    private MestraImportada Gravar(PlanilhaFalsa planilha)
    {
        var dados = ImportadorMestra.Importar(planilha);
        _base.GravarMestra(dados, Fonte());
        return dados;
    }

    [Fact]
    public void Le_as_duas_abas_pelo_nome_das_colunas_ignorando_titulo_totais_e_linhas_vazias()
    {
        var dados = Gravar(BaseSintetica.Mestra());
        Assert.Empty(DasBases(dados));

        var lic = _base.Previstas(BaseDeObras.EmLicitacao);
        Assert.Equal(["CEU Épsilon", "EMEF Alfa", "CEI Zeta"], lic.Select(o => o.Nome));
        var epsilon = lic[0];
        Assert.Equal("4", epsilon.Portaria);
        Assert.Equal("Portaria 4", epsilon.PortariaTexto);
        Assert.Equal("3364", epsilon.Dotacao);
        Assert.Equal("LOTE 01", epsilon.Lote);
        Assert.Equal("Homologação", epsilon.Status);
        Assert.Equal("7910.2026/0000010-0", epsilon.Processo);
        Assert.Equal(5, epsilon.Linha);
        Assert.Null(lic[1].Empresa); // publicada, ainda sem vencedora
        Assert.True(lic[1].SemContratoReceita);
        Assert.Equal("Portaria 3 (2025)", lic[2].PortariaTexto);

        var aLicitar = _base.Previstas(BaseDeObras.ALicitar);
        Assert.Equal(3, aLicitar.Count);
        Assert.Null(aLicitar[0].Processo); // célula vazia: sem SEI
        Assert.Equal("7910.2026/0000020-0", aLicitar[1].Processo);
        Assert.Null(aLicitar[2].Lote);
        Assert.All(aLicitar, o => Assert.Null(o.Empresa));
        Assert.All(aLicitar, o => Assert.Equal(BaseDeObras.ALicitar, o.Base));
    }

    [Fact]
    public void Valor_da_obra_e_sempre_o_valor_antes_dos_10_porcento()
    {
        Gravar(BaseSintetica.Mestra());
        var epsilon = _base.Previstas(BaseDeObras.EmLicitacao)[0];
        Assert.Equal(1_000_000m, epsilon.ValorObra); // "Valor do Contrato com ADM", apesar do nome, é antes dos 10%
        Assert.Equal(1_100_000m, epsilon.ValorComRemuneracao); // "Recursos para 2027"
        var muro = _base.Previstas(BaseDeObras.ALicitar)[1];
        Assert.Equal(800_000m, muro.ValorObra); // "Valor do Contrato"
        Assert.Equal(880_000m, muro.ValorComRemuneracao); // "Valor com ADM"
    }

    [Fact]
    public void Valor_com_10_porcento_que_nao_confere_vira_aviso_e_nao_muda_o_valor_da_obra()
    {
        var p = BaseSintetica.Mestra();
        p.AbasPorNome["A Licitar"][4][9] = 600000m; // deveria ser 550.000
        var dados = Gravar(p);
        var aviso = Assert.Single(DasBases(dados));
        Assert.Contains("A licitar, linha 5", aviso);
        Assert.Contains("não confere", aviso);
        Assert.Equal(500_000m, _base.Previstas(BaseDeObras.ALicitar)[0].ValorObra);
    }

    [Fact]
    public void Valor_escrito_como_texto_tambem_vale_e_texto_que_nao_e_valor_avisa()
    {
        var p = BaseSintetica.Mestra();
        p.AbasPorNome["A Licitar"][4][8] = "R$ 500.000,00";
        p.AbasPorNome["A Licitar"][5][8] = "a definir";
        var dados = Gravar(p);
        Assert.Equal(500_000m, _base.Previstas(BaseDeObras.ALicitar)[0].ValorObra);
        Assert.Null(_base.Previstas(BaseDeObras.ALicitar)[1].ValorObra);
        Assert.Contains(DasBases(dados), a => a.Contains("linha 6") && a.Contains("\"a definir\" não é um número"));
    }

    [Fact]
    public void Coluna_Processo_e_opcional_e_o_ano_dos_Recursos_pode_mudar()
    {
        var p = BaseSintetica.Mestra();
        foreach (var linha in p.AbasPorNome["A Licitar"]) linha[6] = null; // planilha antiga, sem Processo
        p.AbasPorNome["Em licitação"][3][9] = "Recursos para 2028";
        var dados = Gravar(p);
        Assert.Empty(DasBases(dados));
        Assert.All(_base.Previstas(BaseDeObras.ALicitar), o => Assert.Null(o.Processo));
        Assert.Equal(1_100_000m, _base.Previstas(BaseDeObras.EmLicitacao)[0].ValorComRemuneracao);
    }

    [Fact]
    public void Aba_que_sumiu_avisa_so_dela_mantem_os_dados_anteriores_e_nao_impede_as_obras()
    {
        Gravar(BaseSintetica.Mestra());
        var p = BaseSintetica.Mestra();
        p.AbasPorNome.Remove("Em licitação");
        var cab = p.AbasPorNome["A Licitar"][3];
        cab[5] = "Escola"; // coluna obrigatória renomeada
        var dados = Gravar(p);
        Assert.Equal(2, DasBases(dados).Count);
        Assert.Contains(DasBases(dados), a => a.StartsWith("Em licitação: a aba", StringComparison.Ordinal) && a.Contains("anteriores foram mantidas"));
        Assert.Contains(DasBases(dados), a => a.StartsWith("A licitar: na aba", StringComparison.Ordinal) && a.Contains("\"Nome da Obra\""));
        Assert.Equal(3, _base.Previstas(BaseDeObras.EmLicitacao).Count);
        Assert.Equal(3, _base.Previstas(BaseDeObras.ALicitar).Count);
        Assert.NotEmpty(_base.Buscar(new FiltroObras(), 100).Obras);
    }

    [Fact]
    public void Bases_nunca_se_misturam_com_as_obras_em_andamento()
    {
        Gravar(BaseSintetica.Mestra());
        var andamento = _base.Buscar(new FiltroObras(), 1000).Obras;
        Assert.DoesNotContain(andamento, o => o.Empreendimento is "CEU Épsilon" or "CEI Zeta" or "CEU Teta");
        Assert.DoesNotContain(_base.OpcoesGerais().Contratos, c => c.Contains("SIURB", StringComparison.Ordinal) || c == "Sem Contrato Receita");
        Assert.DoesNotContain(_base.Previstas(BaseDeObras.EmLicitacao), o => o.Nome == "CEU Teta");
    }

    [Fact]
    public void Tambem_em_mostra_a_mesma_escola_nas_outras_bases_sem_juntar_nomes_diferentes()
    {
        Gravar(BaseSintetica.Mestra());
        var alfaLic = _base.Previstas(BaseDeObras.EmLicitacao).Single(o => o.Nome == "EMEF Alfa");
        var outras = _base.TambemEm(alfaLic.Nome, BaseDeObras.EmLicitacao, alfaLic.Id);
        Assert.Equal(2, outras.Count(o => o.Base == BaseDeObras.EmAndamento)); // 001/EX/2024 e 003/EX/2025
        Assert.Contains(outras, o => o.Base == BaseDeObras.ALicitar && o.Detalhe.Contains("Pista de Skate"));
        Assert.DoesNotContain(outras, o => o.Base == BaseDeObras.EmLicitacao);
        Assert.Empty(_base.TambemEm("EMEF - Alfa", BaseDeObras.EmLicitacao)); // outro nome, outra escola
    }

    [Fact]
    public void Releitura_troca_a_base_inteira_e_nao_duplica()
    {
        Gravar(BaseSintetica.Mestra());
        Gravar(BaseSintetica.Mestra());
        Assert.Equal(3, _base.Previstas(BaseDeObras.EmLicitacao).Count);
        var p = BaseSintetica.Mestra();
        p.AbasPorNome["Em licitação"].RemoveAt(4);
        Gravar(p);
        Assert.Equal(2, _base.Previstas(BaseDeObras.EmLicitacao).Count);
    }

    [Fact]
    public void Filtro_e_opcoes_das_bases_previstas()
    {
        Gravar(BaseSintetica.Mestra());
        var lic = _base.Previstas(BaseDeObras.EmLicitacao);
        Assert.Equal(["LOTE 01", "LOTE 64"], Previstas.Lotes(lic));
        Assert.Single(Previstas.Filtrar(lic, new FiltroPrevistas { Empresa = [FiltroPrevistas.SemEmpresa] }));
        Assert.Equal(2, Previstas.Filtrar(lic, new FiltroPrevistas { Status = ["Homologação"] }).Count);
        Assert.Equal("CEI Zeta", Assert.Single(Previstas.Filtrar(lic, new FiltroPrevistas { Texto = "zeta" })).Nome);
        Assert.Equal(2, Previstas.Filtrar(lic, new FiltroPrevistas { Portaria = ["Portaria 3", "Portaria 4"] }).Count);
        var (n, obra, com10) = Previstas.Totais(lic);
        Assert.Equal((3, 3_600_000m, 3_960_000m), (n, obra, com10));
    }

    private static List<string> DasBases(MestraImportada dados) =>
        [.. dados.Avisos.Where(a => a.StartsWith("Em licitação", StringComparison.Ordinal) || a.StartsWith("A licitar", StringComparison.Ordinal))];
}
