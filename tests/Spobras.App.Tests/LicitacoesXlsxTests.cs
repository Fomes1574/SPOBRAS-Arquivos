using Spobras.Dados.Base;
using Spobras.Testes;

namespace Spobras.App.Tests;

public class LicitacoesXlsxTests
{
    [Fact]
    public void Abas_Em_licitacao_e_A_Licitar_de_um_xlsx_de_verdade()
    {
        string pasta = Directory.CreateTempSubdirectory("spobras-licx-").FullName;
        try
        {
            string arquivo = Path.Combine(pasta, "mestra.xlsx");
            XlsxSintetico.Gravar(BaseSintetica.Mestra(), arquivo);
            using var leitor = new Spobras.Documentos.Planilhas.LeitorXlsx(arquivo);
            var dados = ImportadorMestra.Importar(leitor);
            Assert.Empty(DasBases(dados));
            Assert.Equal(["CEU Épsilon", "EMEF Alfa", "CEI Zeta"], dados.EmLicitacao!.Select(o => o.Nome));
            Assert.Equal(1_000_000m, dados.EmLicitacao![0].ValorObra);
            Assert.Equal("4", dados.EmLicitacao![0].Portaria);
            Assert.Equal(3, dados.ALicitar!.Count);
            Assert.Null(dados.ALicitar![0].Processo);
        }
        finally
        {
            Directory.Delete(pasta, recursive: true);
        }
    }

    private static List<string> DasBases(MestraImportada dados) =>
        [.. dados.Avisos.Where(a => a.StartsWith("Em licitação", StringComparison.Ordinal) || a.StartsWith("A licitar", StringComparison.Ordinal))];
}
