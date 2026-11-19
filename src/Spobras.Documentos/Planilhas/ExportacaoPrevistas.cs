using System.Globalization;
using Spobras.Core.Base;

namespace Spobras.Documentos.Planilhas;

/// <summary>
/// Exportacao das bases Em licitacao e A licitar (computador e celular): aba "Obras" no modelo da base, so com as
/// colunas que ela tem, e aba "Sobre esta consulta" com a base, os filtros, a data e os totais.
/// </summary>
public static class ExportacaoPrevistas
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static string Nome(BaseDeObras qual, DateTimeOffset agora) =>
        Spobras.Core.Nomes.NomeArquivo.Corrigir($"Consulta - {BasesDeObras.Nome(qual)} - " + agora.ToString("yyyy-MM-dd", PtBr)) + ".xlsx";

    public static void Gravar(string caminho, BaseDeObras qual, IReadOnlyList<ObraPrevista> obras, FiltroPrevistas filtro, DateTimeOffset agora, string? logo)
    {
        string titulo = BasesDeObras.Nome(qual);
        var (n, valor, com10) = Previstas.Totais(obras);
        List<object?[]> sobre =
        [
            ["Base", titulo],
            ["Data da consulta", agora.ToString("dd/MM/yyyy HH:mm", PtBr)],
            ["Obras exportadas", n],
            ["Valor das obras (antes dos 10%)", valor],
            ["Com os 10%", com10],
        ];
        void Filtrado(string nome, IReadOnlyList<string> valores)
        {
            if (valores.Count > 0) sobre.Add([$"Filtro: {nome}", string.Join(", ", valores)]);
        }

        if (filtro.Texto.Trim().Length > 0) sobre.Add(["Filtro: procurar", filtro.Texto.Trim()]);
        Filtrado("Portaria", filtro.Portaria);
        Filtrado(qual == BaseDeObras.ALicitar ? "Lote ou agrupamento" : "Lote", filtro.Lote);
        Filtrado("Modalidade", filtro.Modalidade);
        Filtrado("Dotação", filtro.Dotacao);
        Filtrado("Situação", filtro.Status);
        Filtrado("Empresa", filtro.Empresa);
        if (filtro.Vazio) sobre.Add(["Filtros", "nenhum (todas as obras da base)"]);
        sobre.Add(["Observação", "O valor da obra é o da planilha antes dos 10%; a coluna com os 10% é a da própria planilha. Obras sem contrato: não há medições nem pagamentos."]);

        GravadorXlsx.Gravar(caminho,
        [
            new AbaNova("Obras", FichaPrevista.Titulos(qual), [.. obras.Select(FichaPrevista.Linha)]),
            new AbaNova("Sobre esta consulta", ["Item", "Valor"], sobre, ComFiltro: false),
        ], logo, $"Consulta da Base de dados - {titulo}");
    }
}
