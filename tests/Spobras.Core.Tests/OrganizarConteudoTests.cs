using Spobras.Core.Abstracoes;
using Spobras.Core.Base;
using Spobras.Core.Ferramentas;
using Spobras.Core.Organizar;

namespace Spobras.Core.Tests;

public class OrganizarConteudoTests : ComPastaTemporaria
{
    private static readonly ObraLida[] Obras =
    [
        new() { Contrato = "001/EX/2024", Empreendimento = "EMEF Alfa Centauri", Lote = "LOTE 01", Portaria = "Portaria 3" },
        new() { Contrato = "001/EX/2024", Empreendimento = "CEI Beta Pictoris", Lote = "LOTE 01", Portaria = "Portaria 3" },
        new() { Contrato = "003/EX/2025", Empreendimento = "EMEF Alfa Centauri", Lote = "LOTE 07", Portaria = "Portaria 3 (2025)" },
        new() { Contrato = "004/EX/2025", Empreendimento = "CEU Delta Pavonis", Lote = "LOTE 02", Portaria = "Portaria 6" },
    ];

    private readonly Dictionary<string, string> _conteudos = new(StringComparer.OrdinalIgnoreCase);
    private int _leituras;

    private IReadOnlyList<Sugestao> Planejar(IReadOnlyList<Regra> regras, params string[] caminhos)
    {
        var planejador = new PlanejadorOrganizacao(new ArquivosDisco(), c =>
        {
            _leituras++;
            return _conteudos.TryGetValue(Path.GetFileName(c), out string? t) ? new TextoIndexado(t, null) : new TextoIndexado(null, "PDF sem texto (digitalizado)");
        }, new ReconhecedorObras(Obras));
        return planejador.Planejar([.. caminhos.Select(c => new ArquivoParaOrganizar(c))], regras, []);
    }

    private Regra PorObra => new() { Nome = "Por obra", Destino = Em("Obras", "{contrato}", "{escola}") };

    [Fact]
    public void Reconhece_contrato_no_nome_com_hifen_e_escola_no_conteudo()
    {
        string a = Criar("Medição 004-EX-2025 outubro.xlsx");
        string b = Criar("medicao.pdf");
        _conteudos["medicao.pdf"] = "Boletim de medição do contrato 001/EX/2024 — CEI Beta Pictoris";

        var s = Planejar([PorObra], a, b);

        Assert.Equal(Em("Obras", "004-EX-2025", "CEU Delta Pavonis", "Medição 004-EX-2025 outubro.xlsx"), s[0].CaminhoDestino);
        Assert.Contains("achou contrato 004/EX/2025 no nome", s[0].Achou);
        Assert.Equal(Em("Obras", "001-EX-2024", "CEI Beta Pictoris", "medicao.pdf"), s[1].CaminhoDestino);
        Assert.Contains("contrato 001/EX/2024 e escola CEI Beta Pictoris no conteúdo", s[1].Achou);
    }

    [Fact]
    public void T25_ambiguidade_e_falta_de_dado_vao_para_voce_decide_com_evidencia()
    {
        string escolaEmDois = Criar("Fotos EMEF Alfa Centauri.zip");     // escola em dois contratos
        string contratoSemEscola = Criar("OS 001.EX.2024.pdf");          // contrato com duas escolas
        string nada = Criar("documento qualquer.pdf");
        string semMatch = Criar("1001-EX-2024.pdf");                      // numero parecido nao conta
        _conteudos["OS 001.EX.2024.pdf"] = "Ordem de serviço";

        var s = Planejar([PorObra], escolaEmDois, contratoSemEscola, nada, semMatch);

        Assert.All(s, x => Assert.Equal(OrigemSugestao.VoceDecide, x.Origem));
        Assert.Contains("Mais de uma obra combina", s[0].Achou);
        Assert.Contains("contrato não é único", s[0].Achou);
        Assert.Equal("Achou contrato 001/EX/2024 no nome, mas falta escola para montar a pasta", s[1].Achou);
        Assert.Equal("Não achou contrato nem escola da Base de dados no nome", s[2].Achou); // PDF sem texto
        Assert.Contains("Não achou", s[3].Achou);
        Assert.Contains("precisa saber a obra", s[0].Motivo);
    }

    [Fact]
    public void Conteudo_so_e_lido_quando_alguma_regra_precisa_e_sem_texto_e_explicado()
    {
        string pdf = Criar("scan.pdf");
        string os = Criar("doc.pdf");
        _conteudos["doc.pdf"] = "ORDEM DE SERVIÇO nº 12";
        var porNome = new Regra { Nome = "PDFs", Extensoes = ["pdf"], Destino = Em("PDFs") };

        Planejar([porNome], pdf, os);
        Assert.Equal(0, _leituras);

        var porConteudo = new Regra { Nome = "Ordens de serviço", ConteudoContem = ["ordem de servico"], Destino = Em("OS"), Prioridade = 5 };
        var s = Planejar([porConteudo], pdf, os);

        Assert.Equal(OrigemSugestao.VoceDecide, s[0].Origem);
        Assert.Contains("O conteúdo não foi lido (PDF sem texto (digitalizado))", s[0].Achou);
        Assert.Equal(Em("OS", "doc.pdf"), s[1].CaminhoDestino);
        Assert.Contains("conteúdo tem \"ordem de servico\"", s[1].Achou);
        Assert.Equal(2, _leituras);
    }

    [Fact]
    public void Sem_base_a_regra_por_obra_explica()
    {
        string a = Criar("Medição 004-EX-2025.xlsx");
        var s = new PlanejadorOrganizacao(new ArquivosDisco(), null, new ReconhecedorObras([]))
            .Planejar([new ArquivoParaOrganizar(a)], [PorObra], []);
        Assert.Equal("A regra usa dados da obra, mas a Base de dados ainda está vazia", s[0].Achou);
    }
}
