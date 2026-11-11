using System.Globalization;

namespace Spobras.Core.Relatorio;

/// <summary>Textos do cabecalho do relatorio. Campo vazio nao gera rotulo nem espaco.</summary>
public static class CabecalhoRelatorio
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Quantas linhas de dados cabem na ultima linha fixa do cabecalho.</summary>
    public const int LinhasFixasDeDados = 2;

    public static string Data(DateOnly? data) => data?.ToString("dd/MM/yyyy", PtBr) ?? "";

    /// <summary>"Contrato: X  ·  Lote: Y  ·  ..." so com os campos preenchidos.</summary>
    public static string LinhaDeDados(DadosRelatorio d)
    {
        var partes = new List<string>();
        void Somar(string rotulo, string valor)
        {
            if (!string.IsNullOrWhiteSpace(valor))
            {
                partes.Add($"{rotulo}: {valor.Trim()}");
            }
        }

        Somar("Contrato", d.Contrato);
        Somar("Lote", d.Lote);
        Somar("Portaria", d.Portaria);
        Somar("Processo SEI", d.ProcessoSei);
        Somar("Data da OS", Data(d.DataOs));
        Somar("Objeto", d.Objeto);
        Somar("Responsável", d.Responsavel);
        return string.Join("   ·   ", partes);
    }

    public static int LinhasDeDados(DadosRelatorio d)
    {
        string texto = LinhaDeDados(d);
        return texto.Length == 0 ? 0 : PaginadorRelatorio.LinhasDe(texto, GradeRelatorio.Largura);
    }

    /// <summary>Linhas que os dados ocupam alem das que ja existem no cabecalho.</summary>
    public static int LinhasExtras(DadosRelatorio d) => Math.Max(0, LinhasDeDados(d) - LinhasFixasDeDados);

    /// <summary>"Relatório Fotográfico - Escola - 2026-11-10", omitindo o que estiver vazio.</summary>
    public static string NomeSugerido(DadosRelatorio d)
    {
        var partes = new List<string> { "Relatório Fotográfico" };
        if (!string.IsNullOrWhiteSpace(d.Unidade))
        {
            partes.Add(Nomes.NomeArquivo.Corrigir(d.Unidade.Trim()));
        }

        if (d.Data is { } data)
        {
            partes.Add(data.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        return string.Join(" - ", partes);
    }
}
