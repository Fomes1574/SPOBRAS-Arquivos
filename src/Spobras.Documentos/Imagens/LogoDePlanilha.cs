using DocumentFormat.OpenXml.Packaging;
using Spobras.Documentos.Planilhas;
using Xdr = DocumentFormat.OpenXml.Drawing.Spreadsheet;

namespace Spobras.Documentos.Imagens;

/// <summary>
/// Tira o logo de um relatorio pronto (.xlsx): a imagem presa no cabecalho, nas primeiras linhas da primeira
/// aba. O arquivo do usuario so e lido.
/// </summary>
public static class LogoDePlanilha
{
    /// <summary>Grava a imagem do logo em <paramref name="pasta"/> e devolve o caminho, ou null se nao achar.</summary>
    public static string? Extrair(string xlsx, string pasta)
    {
        SpreadsheetDocument doc;
        try
        {
            doc = SpreadsheetDocument.Open(xlsx, false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DocumentFormat.OpenXml.Packaging.OpenXmlPackageException or InvalidDataException or FileFormatException)
        {
            throw new PlanilhaInvalidaException($"\"{Path.GetFileName(xlsx)}\" não pôde ser aberto como planilha do Excel.");
        }

        using (doc)
        {
            var livro = doc.WorkbookPart;
            var primeira = livro?.Workbook?.Sheets?.Elements<DocumentFormat.OpenXml.Spreadsheet.Sheet>().FirstOrDefault();
            if (livro is null || primeira?.Id?.Value is not { } id || livro.GetPartById(id) is not WorksheetPart folha || folha.DrawingsPart is not { } desenhos)
            {
                return null;
            }

            // A imagem mais alta no cabecalho (linhas 1 a 8); entre elas, a que comeca mais acima e mais a esquerda.
            var candidatas = desenhos.WorksheetDrawing!.ChildElements
                .Select(a => (Ancora: a, De: a.GetFirstChild<Xdr.FromMarker>(), Foto: a.Descendants<Xdr.Picture>().FirstOrDefault()))
                .Where(c => c.De is not null && c.Foto is not null && int.Parse(c.De.RowId!.Text) < 8)
                .OrderBy(c => int.Parse(c.De!.RowId!.Text)).ThenBy(c => int.Parse(c.De!.ColumnId!.Text))
                .ToList();
            foreach (var (_, _, foto) in candidatas)
            {
                string? rel = foto!.BlipFill?.Blip?.Embed?.Value;
                if (rel is null || desenhos.GetPartById(rel) is not ImagePart imagem)
                {
                    continue;
                }

                string extensao = imagem.ContentType switch
                {
                    "image/png" => ".png",
                    "image/jpeg" => ".jpg",
                    "image/bmp" => ".bmp",
                    "image/gif" => ".gif",
                    _ => "",
                };
                if (extensao.Length == 0)
                {
                    continue;
                }

                Directory.CreateDirectory(pasta);
                string destino = Path.Combine(pasta, "logo-extraido" + extensao);
                using (var origem = imagem.GetStream())
                using (var saida = File.Create(destino))
                {
                    origem.CopyTo(saida);
                }

                return destino;
            }

            return null;
        }
    }
}
