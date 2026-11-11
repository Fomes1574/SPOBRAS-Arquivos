using Spobras.Core.Ambiente;
using Spobras.Dados.Ambiente;

namespace Spobras.Dados.Tests;

public class VerificacaoSqliteTests : IDisposable
{
    private readonly string _pasta = Directory.CreateTempSubdirectory("spobras-dados-").FullName;

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    [Fact]
    public void Grava_le_e_remove_o_banco_de_teste()
    {
        string pastaDados = Path.Combine(_pasta, "ArquivosSPOBRAS");

        var r = VerificacaoSqlite.Executar(pastaDados);

        Assert.Equal(EstadoVerificacao.Passou, r.Estado);
        Assert.Contains("SQLite 3.", r.Detalhes[1]);
        Assert.Empty(Directory.GetFileSystemEntries(pastaDados));
    }

    [Fact]
    public void Falha_com_motivo_quando_a_pasta_nao_pode_ser_criada()
    {
        string arquivo = Path.Combine(_pasta, "ocupado");
        File.WriteAllText(arquivo, "x");

        var r = VerificacaoSqlite.Executar(Path.Combine(arquivo, "dentro"));

        Assert.Equal(EstadoVerificacao.Falhou, r.Estado);
        Assert.StartsWith("Falha:", r.Detalhes[1]);
    }
}
