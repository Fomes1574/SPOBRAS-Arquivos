using Spobras.Core;

namespace Spobras.Core.Tests;

public sealed class RegistroLimitadoTests : IDisposable
{
    private readonly string _pasta = Directory.CreateTempSubdirectory("spobras-registro-").FullName;

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    [Fact]
    public void Registro_de_erros_nunca_passa_do_limite_e_guarda_o_mais_recente()
    {
        string arquivo = Path.Combine(_pasta, "registro", "erros.txt");
        for (int i = 1; i <= 2000; i++)
        {
            RegistroLimitado.Acrescentar(arquivo, $"erro {i:0000} {new string('x', 80)}\r\n", maximo: 20_000);
            Assert.True(new FileInfo(arquivo).Length <= 20_000);
        }

        string[] linhas = File.ReadAllLines(arquivo);
        Assert.StartsWith("erro ", linhas[0]); // comeca numa linha inteira
        Assert.StartsWith("erro 2000", linhas[^1]);
    }
}
