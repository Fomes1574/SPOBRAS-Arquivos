using Spobras.Documentos.Planilhas;
using Spobras.Testes;

namespace Spobras.Documentos.Tests;

public class LeitorXlsxTests : IDisposable
{
    private readonly string _pasta = Directory.CreateTempSubdirectory("spobras-xlsx-").FullName;

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    private string Mestra()
    {
        string caminho = Path.Combine(_pasta, "mestra.xlsx");
        XlsxSintetico.Gravar(BaseSintetica.Mestra(), caminho);
        return caminho;
    }

    [Fact]
    public void Le_tabela_pelo_nome_com_tipos_certos_sem_depender_do_nome_da_aba()
    {
        using var leitor = new LeitorXlsx(Mestra());

        var descricoes = leitor.Tabelas();
        Assert.Equal(3, descricoes.Count);
        var obras = descricoes.Single(d => d.Nome == "contratos_VW_ESCOLAS_CONTRATOS_MEDIDOS_PAGOS");
        Assert.Equal(5, obras.Linhas);
        Assert.Equal(BaseSintetica.ColunasObras, obras.Colunas);
        Assert.NotEqual(obras.Nome, obras.Aba); // a aba tem outro nome

        var tabela = leitor.LerTabela("CONTRATOS_vw_escolas_contratos_medidos_pagos"); // sem diferenciar maiusculas
        Assert.Equal(5, tabela.Linhas.Count);
        var linha = tabela.Linhas[0];
        Assert.Equal("001/EX/2024", linha[tabela.Indice("CONTRATO")]);
        Assert.Equal(new DateTime(2026, 1, 10), linha[tabela.Indice("INÍCIO EXECUÇÃO")]); // data, nao numero
        Assert.Equal(1500m, linha[tabela.Indice("SALDO A PAGAR")]);
        Assert.Equal(10000.129999m, linha[tabela.Indice("VALOR EMPREENDIMENTO")]); // decimal exato
        Assert.Equal("2878", linha[tabela.Indice("ATIVIDADE")]); // codigo continua texto
        Assert.Null(tabela.Linhas[2][tabela.Indice("SALDO A PAGAR")]); // vazio e null, nao zero
        Assert.Null(tabela.Linhas[2][tabela.Indice("INÍCIO EXECUÇÃO")]);
        Assert.Equal(-1, tabela.Indice("NAO EXISTE"));

        Assert.Contains("não existe mais", Assert.Throws<PlanilhaInvalidaException>(() => leitor.LerTabela("sumiu")).Message);
    }

    [Fact]
    public void Le_aba_sem_tabela_preservando_linhas_e_colunas_vazias()
    {
        string caminho = Path.Combine(_pasta, "dotacoes.xlsx");
        XlsxSintetico.Gravar(BaseSintetica.Dotacoes(), caminho);
        using var leitor = new LeitorXlsx(caminho);

        var grade = leitor.LerAba("planilha2");

        Assert.Equal(["Planilha2", "Planilha1"], leitor.NomesDasAbas());
        Assert.True(grade[0].All(v => v is null)); // a linha 1 esta vazia: os cabecalhos estao na linha 2
        Assert.Null(grade[1][0]);
        Assert.Equal("Portarias", grade[1][1]);
        Assert.Equal("16.10.12.365.3010.2878.3390.3900 00", grade[3][3]);
        Assert.Equal(9, grade.Count);
        Assert.Throws<PlanilhaInvalidaException>(() => leitor.LerAba("Planilha9"));
    }

    [Fact]
    public void T16_le_com_o_arquivo_aberto_por_outro_programa_e_nao_altera_a_planilha()
    {
        string caminho = Mestra();
        var antes = (new FileInfo(caminho).Length, File.GetLastWriteTimeUtc(caminho));

        // O Excel mantem o arquivo aberto permitindo leitura.
        using (var aberto = new FileStream(caminho, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
        using (var leitor = new LeitorXlsx(caminho))
        {
            Assert.Equal(7, leitor.LerTabela("preCadastro_VW_RELATORIO_MEDICOES").Linhas.Count);
        }

        Assert.Equal(antes, (new FileInfo(caminho).Length, File.GetLastWriteTimeUtc(caminho)));
    }

    [Fact]
    public void T16_arquivo_ausente_ou_pela_metade_tem_mensagem_propria()
    {
        Assert.Contains("Não foi possível abrir", Assert.Throws<PlanilhaInvalidaException>(() => new LeitorXlsx(Path.Combine(_pasta, "nao-existe.xlsx"))).Message);

        string pelaMetade = Path.Combine(_pasta, "sincronizando.xlsx");
        File.WriteAllBytes(pelaMetade, File.ReadAllBytes(Mestra()).Take(900).ToArray());
        Assert.Contains("pode estar sendo salvo ou sincronizado", Assert.Throws<PlanilhaInvalidaException>(() => new LeitorXlsx(pelaMetade)).Message);
    }
}

public class GravadorXlsxTests : IDisposable
{
    private readonly string _pasta = Directory.CreateTempSubdirectory("spobras-gravador-").FullName;

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    [Fact]
    public void Grava_tipos_do_excel_varias_abas_e_nunca_sobrescreve()
    {
        string caminho = Path.Combine(_pasta, "sub", "export.xlsx");
        GravadorXlsx.Gravar(caminho,
        [
            new AbaNova("Obras", ["Contrato", "Valor", "Data da OS", "Qtd"], [["001/EX", 1250.5m, new DateOnly(2026, 11, 10), 3], ["002/EX", null, null, null]]),
            new AbaNova("Consulta: filtros?", ["Filtro", "Valor"], [["Portaria", "3"]], ComFiltro: false),
        ]);

        using (var leitor = new Spobras.Documentos.Planilhas.LeitorXlsx(caminho))
        {
            Assert.Equal(["Obras", "Consulta- filtros-"], leitor.NomesDasAbas());
            var grade = leitor.LerAba("Obras");
            Assert.Equal("Contrato", grade[0][0]);
            Assert.Equal(1250.5m, grade[1][1]);
            Assert.Equal(new DateTime(2026, 11, 10), grade[1][2]); // formato de data reconhecido
            Assert.Equal(3m, grade[1][3]);
            Assert.Equal("002/EX", grade[2][0]);
        }

        Assert.Throws<IOException>(() => GravadorXlsx.Gravar(caminho, []));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(caminho)!, "*.spobras-parcial"));
    }

    [Fact]
    public void Nome_de_aba_valido_curto_e_sem_repetir()
    {
        var usados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Assert.Equal("a-b", GravadorXlsx.NomeDeAba("a/b", usados));
        Assert.Equal("A-B (2)", GravadorXlsx.NomeDeAba("A/B", usados));
        Assert.Equal(31, GravadorXlsx.NomeDeAba(new string('x', 40), usados).Length);
        Assert.Equal(31, GravadorXlsx.NomeDeAba(new string('x', 40), usados).Length);
        Assert.Equal("Planilha", GravadorXlsx.NomeDeAba("  ", usados));
        Assert.Equal(["A", "Z", "AA", "AZ", "BA"], new[] { 0, 25, 26, 51, 52 }.Select(GravadorXlsx.Coluna));
    }
}

public class LogoTests : IDisposable
{
    private readonly string _pasta = Directory.CreateTempSubdirectory("spobras-logo-").FullName;

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    private string Logo()
    {
        string caminho = Path.Combine(_pasta, "logo.png");
        using var b = new SkiaSharp.SKBitmap(195, 138);
        b.Erase(SkiaSharp.SKColors.DarkRed);
        using var i = SkiaSharp.SKImage.FromBitmap(b);
        File.WriteAllBytes(caminho, i.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100).ToArray());
        return caminho;
    }

    [Fact]
    public void Exportacao_com_logo_poe_logo_e_titulo_no_topo_e_a_tabela_embaixo()
    {
        string saida = Path.Combine(_pasta, "consulta.xlsx");
        GravadorXlsx.Gravar(saida, [new AbaNova("Obras", ["Contrato", "Valor"], [["001/EX", 10m]]), new AbaNova("Sobre", ["Item"], [["x"]])], Logo(), "Consulta da Base de dados");

        using (var leitor = new Spobras.Documentos.Planilhas.LeitorXlsx(saida))
        {
            var obras = leitor.LerAba("Obras");
            Assert.Equal("Consulta da Base de dados", obras[1].First(c => c is not null));
            Assert.Equal(["Contrato", "Valor"], obras[3].Take(2));
            Assert.Equal("001/EX", obras[4][0]);
            Assert.Equal("Item", leitor.LerAba("Sobre")[0][0]); // so a primeira aba ganha o topo
        }

        using var doc = DocumentFormat.OpenXml.Packaging.SpreadsheetDocument.Open(saida, false);
        var folha = doc.WorkbookPart!.WorksheetParts.First(p => p.DrawingsPart is not null);
        Assert.Single(folha.DrawingsPart!.ImageParts);
        Assert.Equal("A4:B5", folha.Worksheet!.GetFirstChild<DocumentFormat.OpenXml.Spreadsheet.AutoFilter>()!.Reference!.Value);

        // Sem logo, nada muda
        string semLogo = Path.Combine(_pasta, "sem.xlsx");
        GravadorXlsx.Gravar(semLogo, [new AbaNova("Obras", ["Contrato"], [["x"]])], logo: Path.Combine(_pasta, "nao-existe.png"));
        using var l2 = new Spobras.Documentos.Planilhas.LeitorXlsx(semLogo);
        Assert.Equal("Contrato", l2.LerAba("Obras")[0][0]);
    }

    [Fact]
    public void Tira_o_logo_do_cabecalho_de_um_relatorio()
    {
        string relatorio = Path.Combine(_pasta, "relatorio.xlsx");
        GravadorXlsx.Gravar(relatorio, [new AbaNova("Relatório", ["A"], [["x"]])], Logo(), "Relatório");

        string? extraido = Spobras.Documentos.Imagens.LogoDePlanilha.Extrair(relatorio, Path.Combine(_pasta, "saida"));

        Assert.NotNull(extraido);
        var info = Spobras.Documentos.Imagens.Fotos.Ler(extraido);
        Assert.Equal((195, 138), (info.Largura, info.Altura));

        string semImagem = Path.Combine(_pasta, "sem.xlsx");
        GravadorXlsx.Gravar(semImagem, [new AbaNova("A", ["x"], [])]);
        Assert.Null(Spobras.Documentos.Imagens.LogoDePlanilha.Extrair(semImagem, Path.Combine(_pasta, "saida2")));
        File.WriteAllText(Path.Combine(_pasta, "texto.xlsx"), "x");
        Assert.Throws<PlanilhaInvalidaException>(() => Spobras.Documentos.Imagens.LogoDePlanilha.Extrair(Path.Combine(_pasta, "texto.xlsx"), _pasta));
    }
}
