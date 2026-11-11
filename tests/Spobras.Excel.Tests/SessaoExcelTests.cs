using Spobras.Documentos.Ambiente;

namespace Spobras.Excel.Tests;

/// <summary>So roda de verdade em Windows com Excel instalado; no CI (sem Excel) e pulado.</summary>
public class SessaoExcelTests
{
    [Fact]
    public void Abre_escreve_recalcula_exporta_e_fecha_sem_deixar_processo()
    {
        Assert.SkipUnless(SessaoExcel.ExcelInstalado(), "Excel não instalado nesta máquina.");

        string pasta = Directory.CreateTempSubdirectory("spobras-excel-").FullName;
        try
        {
            string xlsx = Path.Combine(pasta, "teste.xlsx");
            string pdf = Path.Combine(pasta, "teste.pdf");
            PlanilhaTeste.Criar(xlsx);

            var sessao = SessaoExcel.Criar();
            try
            {
                sessao.Abrir(xlsx);
                sessao.EscreverNumero(PlanilhaTeste.NomeAba, PlanilhaTeste.CelulaEntrada, 700);
                sessao.Recalcular();
                Assert.Equal((double)PlanilhaTeste.TotalEsperado(700), sessao.LerNumero(PlanilhaTeste.NomeAba, PlanilhaTeste.CelulaTotal), 6);
                sessao.ExportarPdf(pdf);
                sessao.Salvar();
            }
            finally
            {
                sessao.Dispose();
            }

            Assert.True(sessao.ProcessoEncerrado);
            Assert.True(new FileInfo(pdf).Length > 0);
            Assert.Equal(PlanilhaTeste.TotalEsperado(700), PlanilhaTeste.LerNumero(xlsx, PlanilhaTeste.CelulaTotal));
        }
        finally
        {
            Directory.Delete(pasta, recursive: true);
        }
    }
}
