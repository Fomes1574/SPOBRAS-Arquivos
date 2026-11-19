using Spobras.Core.Base;
using Spobras.Core.Planilhas;
using Spobras.Dados.Base;
using Spobras.Testes;

namespace Spobras.Dados.Tests;

/// <summary>Busca "Adicionar contrato" do cronograma: ordem pelo começo do número e grupos sem contrato que nunca se misturam.</summary>
public class BuscaCronogramaTests : IDisposable
{
    private readonly string _pasta = Directory.CreateTempSubdirectory("spobras-busca-").FullName;
    private readonly BaseRepositorio _base;

    public BuscaCronogramaTests()
    {
        _base = new BaseRepositorio(new BancoApp(Path.Combine(_pasta, "app.db")));
        var p = BaseSintetica.Mestra();
        var obras = p.TabelasPorNome[ImportadorMestra.TabelaObras];
        var linhas = obras.Linhas.ToList();
        linhas.Add(BaseSintetica.Obra("035/SPOBRAS/2025", "CEU Sapopemba Exemplo", BaseSintetica.P6, new DateTime(2025, 3, 1), 10m, lote: "LOTE 53"));
        linhas.Add(BaseSintetica.Obra("035/SPOBRAS/2026", "EMEF Exemplo 1", BaseSintetica.P3De2021, new DateTime(2026, 3, 1), 10m, lote: "LOTE 204"));
        linhas.Add(BaseSintetica.Obra("035/SPOBRAS/2022", "CEU Exemplo 2", BaseSintetica.P3De2021, new DateTime(2022, 3, 1), 10m, lote: "LOTE 05"));
        linhas.Add(BaseSintetica.Obra("135/EX/2024", "EMEI Exemplo 3", BaseSintetica.P3De2021, new DateTime(2024, 3, 1), 10m, lote: "LOTE 12"));
        linhas.Add(BaseSintetica.Obra("001/EX/2020", "EMEI Exemplo 4", BaseSintetica.P3De2021, new DateTime(2020, 3, 1), 10m, lote: "LOTE 035"));
        p.Tabela(obras.Nome, [.. obras.Colunas], [.. linhas]);
        _base.GravarMestra(ImportadorMestra.Importar(p), new FonteInfo { Id = Fontes.Mestra, Nome = "m", Caminho = "m.xlsx", Estado = EstadoFonte.Atualizada, Versao = 1 });
    }

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    [Fact]
    public void Digitar_035_mostra_primeiro_os_contratos_que_comecam_com_035_do_mais_novo_ao_mais_antigo()
    {
        var achados = ProcuraObras.ContratosComDetalhes(_base, "035").Select(o => o.Contrato).ToList();
        Assert.Equal(["035/SPOBRAS/2026", "035/SPOBRAS/2025", "035/SPOBRAS/2022", "001/EX/2020"], achados); // o último é o LOTE 035
        // "35": nenhum começa assim; os que têm no código vêm antes (do ano mais novo), depois o que só bate pelo lote
        Assert.Equal(["035/SPOBRAS/2026", "035/SPOBRAS/2025", "135/EX/2024", "035/SPOBRAS/2022", "001/EX/2020"],
            ProcuraObras.ContratosComDetalhes(_base, "35").Select(o => o.Contrato));
        Assert.Equal(["035/SPOBRAS/2026", "035/SPOBRAS/2025", "035/SPOBRAS/2022"], ProcuraObras.ContratosComDetalhes(_base, "035/spobras").Select(o => o.Contrato));
        Assert.Equal(["035/SPOBRAS/2025"], ProcuraObras.ContratosComDetalhes(_base, "035-SPOBRAS-2025").Select(o => o.Contrato));
        Assert.Empty(ProcuraObras.ContratosComDetalhes(_base, "   "));
        Assert.Empty(ProcuraObras.ContratosComDetalhes(_base, "zzz"));
    }

    [Fact]
    public void Busca_nunca_traz_mais_que_o_limite()
    {
        Assert.True(ProcuraObras.ContratosComDetalhes(_base, "0", maximo: 3).Count <= 3);
    }

    [Fact]
    public void Grupos_sem_contrato_sao_base_mais_lote_mais_modalidade()
    {
        var todas = _base.Previstas(BaseDeObras.EmLicitacao).Concat(_base.Previstas(BaseDeObras.ALicitar)).ToList();
        var lote64 = Assert.Single(GrupoPrevisto.Montar(todas, "64"));
        Assert.Equal(BaseDeObras.EmLicitacao, lote64.Base);
        Assert.Equal(2, lote64.Obras.Count); // EMEF Alfa e CEI Zeta: mesmo lote e mesma modalidade
        Assert.Equal("LOTE 64 · Reforma e Manutenção", lote64.Titulo);
        Assert.Contains("2 escolas", lote64.Detalhe);

        var lote01 = Assert.Single(GrupoPrevisto.Montar(todas, "lote 1"));
        Assert.Equal("LOTE 01 · Manutenção CEU", lote01.Titulo); // o LOTE 01 em andamento (Manutenção) não entra aqui

        Assert.Equal("LOTE 09 · Pista de Skate", Assert.Single(GrupoPrevisto.Montar(todas, "skate")).Titulo);
        var porEscola = GrupoPrevisto.Montar(todas, "alfa");
        Assert.Equal(2, porEscola.Count); // a mesma escola em duas bases: dois grupos separados
        Assert.Equal([BaseDeObras.EmLicitacao, BaseDeObras.ALicitar], porEscola.Select(g => g.Base));
        Assert.Equal("Sem lote · Construção", Assert.Single(GrupoPrevisto.Montar(todas, "teta")).Titulo);
        Assert.Empty(GrupoPrevisto.Montar(todas, ""));
    }

    [Fact]
    public void Portaria_da_obra_sem_contrato_pega_o_ano_pela_base_de_dotacoes()
    {
        var vinculos = new List<VinculoBase>
        {
            new(1, "Portaria 3 (2021)", "015/EX/2025", null, "16.10.12.365.3010.3359.4490.5100.00", null, "3359", null),
            new(2, "Portaria 3 (2025)", "016/EX/2026", null, "16.10.12.365.3010.3360.4490.5100.00", null, "3360", null),
            new(3, "Portaria 4 (2022)", "015/EX/2025", null, "16.10.12.368.3010.3364.4490.5100.00", null, "3364", null),
        };
        ObraPrevista Com(string? portaria, string? receita) => new() { Nome = "X", Portaria = portaria, ContratoReceita = receita };
        Assert.Equal("Nº 03 (2021)", PrevistaNoCronograma.Portaria(Com("3", "015/EX/2025"), vinculos));
        Assert.Equal("Nº 03 (2025)", PrevistaNoCronograma.Portaria(Com("3", "016/EX/2026"), vinculos));
        Assert.Equal("Nº 03 (2025)", PrevistaNoCronograma.Portaria(Com("3 (2025)", "015/EX/2025"), vinculos)); // a aba manda
        Assert.Equal("Nº 03", PrevistaNoCronograma.Portaria(Com("3", "Não tem"), vinculos)); // dois anos possíveis: não adivinha
        Assert.Equal("Nº 04 (2022)", PrevistaNoCronograma.Portaria(Com("4", "Sem Contrato Receita"), vinculos)); // um ano só
        Assert.Equal("", PrevistaNoCronograma.Portaria(Com(null, null), vinculos));
        Assert.Equal("64", PrevistaNoCronograma.Lote("LOTE 64"));
        Assert.Equal("PREVISÃO DE MUROS", PrevistaNoCronograma.Lote("PREVISÃO DE MUROS"));
    }
}
