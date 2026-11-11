using Spobras.App.Servicos;
using Spobras.Excel;

namespace Spobras.App.Windows;

/// <summary>
/// Abre o cronograma gerado numa instancia invisivel do Excel criada pelo app, recalcula, le os totais,
/// salva com os resultados e, se pedido, exporta a aba em PDF. O Excel do usuario nao e tocado.
/// </summary>
public sealed class VerificadorCronogramaExcel : IVerificadorCronograma
{
    private readonly Lazy<bool> _disponivel = new(SessaoExcel.ExcelInstalado);

    public bool Disponivel => _disponivel.Value;

    public ConferenciaCronograma Conferir(string caminho, string aba, string? pdf)
    {
        using var sessao = SessaoExcel.Criar();
        sessao.Abrir(caminho);
        sessao.Recalcular();

        decimal? Reais(string celula) => sessao.LerNumeroOuNada(aba, celula) is { } v ? Math.Round((decimal)v, 2, MidpointRounding.AwayFromZero) : null;

        // Meses nas colunas L a AE (20 colunas), linha do TOTAL.
        decimal soma = 0;
        bool temMes = false;
        foreach (string coluna in ColunasDosMeses())
        {
            if (Reais(coluna + "18") is { } v)
            {
                soma += v;
                temMes = true;
            }
        }

        int erros = sessao.ContarErros(aba, "B1:AG31");
        string resumo = "";
        try
        {
            resumo = sessao.LerTexto("Parametros", "B20").Trim();
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            // Modelo sem a aba Parametros: fica sem essa conferencia.
        }

        var conferencia = new ConferenciaCronograma(Reais("G18"), Reais("I18"), temMes ? soma : null, resumo, erros, null);
        sessao.Salvar();
        if (pdf is not null)
        {
            sessao.ExportarAbaPdf(aba, pdf);
            conferencia = conferencia with { Pdf = pdf };
        }

        return conferencia;
    }

    private static IEnumerable<string> ColunasDosMeses()
    {
        for (char c = 'L'; c <= 'Z'; c++)
        {
            yield return c.ToString();
        }

        for (char c = 'A'; c <= 'E'; c++)
        {
            yield return "A" + c;
        }
    }
}
