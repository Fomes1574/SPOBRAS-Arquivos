using Spobras.App.Servicos;
using Spobras.Excel;

namespace Spobras.App.Windows;

/// <summary>
/// PDF fiel a impressao, feito pelo Excel instalado numa instancia invisivel criada pelo app.
/// Abre a planilha so para leitura; o Excel do usuario nao e tocado.
/// </summary>
public sealed class ExportadorPdfExcel : IExportadorPdf
{
    private readonly Lazy<bool> _disponivel = new(SessaoExcel.ExcelInstalado);

    public bool Disponivel => _disponivel.Value;

    public void Exportar(string planilha, string pdf)
    {
        using var sessao = SessaoExcel.Criar();
        sessao.Abrir(planilha, somenteLeitura: true);
        sessao.ExportarPdf(pdf);
    }
}
