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
