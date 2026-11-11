using Spobras.Core.Ambiente;

namespace Spobras.Core.Tests;

public class AmbienteTests : IDisposable
{
    private readonly string _pasta = Directory.CreateTempSubdirectory("spobras-core-").FullName;

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    [Fact]
    public void Arquivos_passa_e_nao_deixa_nada_na_pasta()
    {
        File.WriteAllText(Path.Combine(_pasta, "do-usuario.txt"), "intocado");

        var r = VerificacaoArquivos.Executar(_pasta);

        Assert.Equal(EstadoVerificacao.Passou, r.Estado);
        Assert.Equal(5, r.Numero);
        Assert.Equal(["do-usuario.txt"], Directory.GetFileSystemEntries(_pasta).Select(Path.GetFileName));
        Assert.Equal("intocado", File.ReadAllText(Path.Combine(_pasta, "do-usuario.txt")));
    }

    [Fact]
    public void Arquivos_falha_quando_a_pasta_nao_existe()
    {
        var r = VerificacaoArquivos.Executar(Path.Combine(_pasta, "nao-existe"));

        Assert.Equal(EstadoVerificacao.Falhou, r.Estado);
        Assert.Contains("não encontrada", r.Detalhes[0]);
    }

    [Fact]
    public async Task Vigia_detecta_arquivo_novo_e_limpa()
    {
        var r = await VerificacaoVigia.ExecutarAsync(_pasta, TimeSpan.FromSeconds(10));

        Assert.Equal(EstadoVerificacao.Passou, r.Estado);
        Assert.Empty(Directory.GetFileSystemEntries(_pasta));
    }

    [Fact]
    public async Task Vigia_falha_quando_a_pasta_nao_existe()
    {
        var r = await VerificacaoVigia.ExecutarAsync(Path.Combine(_pasta, "nao-existe"), TimeSpan.FromSeconds(1));

        Assert.Equal(EstadoVerificacao.Falhou, r.Estado);
    }

    [Theory]
    [InlineData((ushort)0x014C, "32 bits")]
    [InlineData((ushort)0x8664, "64 bits")]
    [InlineData((ushort)0xAA64, "ARM64")]
    [InlineData((ushort)0x1234, null)]
    public void LeitorPE_identifica_a_arquitetura(ushort maquina, string? esperado)
    {
        var bytes = new byte[0x100];
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';
        bytes[0x3C] = 0x80;
        bytes[0x80] = (byte)'P';
        bytes[0x81] = (byte)'E';
        BitConverter.GetBytes(maquina).CopyTo(bytes, 0x84);

        Assert.Equal(esperado, LeitorPE.LerArquitetura(new MemoryStream(bytes)));
    }

    [Fact]
    public void LeitorPE_devolve_null_para_arquivo_que_nao_e_executavel()
    {
        Assert.Null(LeitorPE.LerArquitetura(new MemoryStream("texto qualquer"u8.ToArray())));
        Assert.Null(LeitorPE.LerArquitetura(new MemoryStream([])));
        Assert.Null(LeitorPE.LerArquitetura(new MemoryStream([(byte)'M', (byte)'Z', 0])));
    }

    [Fact]
    public void Mascara_troca_perfil_e_usuario()
    {
        var m = new MascaraCaminhos(@"C:\Users\fulano.silva\", "fulano.silva");

        Assert.Equal(@"Pasta: %USERPROFILE%\Downloads", m.Aplicar(@"Pasta: c:\users\FULANO.SILVA\Downloads"));
        Assert.Equal(@"D:\dados\<usuario>\x", m.Aplicar(@"D:\dados\fulano.silva\x"));
    }

    [Fact]
    public void Mascara_sem_dados_nao_altera_o_texto()
    {
        Assert.Equal("abc", new MascaraCaminhos(null, "ab").Aplicar("abc"));
    }

    [Fact]
    public void Relatorio_tem_resumo_itens_em_ordem_e_caminhos_mascarados()
    {
        var resultados = new[]
        {
            ResultadoVerificacao.Falhou(3, "Excel encontrado", "Excel não encontrado."),
            ResultadoVerificacao.Passou(1, "O programa abre", @"Local: C:\Users\fulano\Downloads\teste.exe", "linha a\nlinha b"),
            ResultadoVerificacao.NaoVerificado(6, "OneDrive"),
        };

        string texto = RelatorioAmbiente.Gerar(
            resultados,
            new DateTimeOffset(2026, 11, 11, 9, 30, 0, TimeSpan.FromHours(-3)),
            "0.1.0",
            new MascaraCaminhos(@"C:\Users\fulano", "fulano"));

        string esperado = string.Join("\r\n",
            "Arquivos SPOBRAS - Teste de Ambiente",
            "Versão do teste: 0.1.0",
            "Data: 11/11/2026 09:30:00 -03:00",
            "",
            "Resumo: 1 passou, 1 falhou, 1 não verificado",
            "",
            "[PASSOU] 1. O programa abre",
            @"    Local: %USERPROFILE%\Downloads\teste.exe",
            "    linha a",
            "    linha b",
            "",
            "[FALHOU] 3. Excel encontrado",
            "    Excel não encontrado.",
            "",
            "[NÃO VERIFICADO] 6. OneDrive",
            "");
        Assert.Equal(esperado, texto);
    }
}
