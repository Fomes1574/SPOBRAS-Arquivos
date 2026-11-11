using System.Text.RegularExpressions;

namespace Spobras.Core.Organizar;

/// <summary>Descricao do tipo do arquivo pela extensao e pelo nome (sem abrir o arquivo).</summary>
public static partial class TipoArquivo
{
    private static readonly Dictionary<string, string> PorExtensao = new(StringComparer.OrdinalIgnoreCase)
    {
        [".xlsx"] = "Planilha Excel", [".xlsm"] = "Planilha Excel com macro", [".xls"] = "Planilha Excel antiga",
        [".xlsb"] = "Planilha Excel", [".csv"] = "Tabela de texto (CSV)",
        [".pdf"] = "PDF",
        [".doc"] = "Documento Word", [".docx"] = "Documento Word",
        [".ppt"] = "Apresentação", [".pptx"] = "Apresentação",
        [".jpg"] = "Foto", [".jpeg"] = "Foto", [".png"] = "Imagem", [".heic"] = "Foto", [".webp"] = "Imagem",
        [".bmp"] = "Imagem", [".tif"] = "Imagem", [".tiff"] = "Imagem", [".gif"] = "Imagem",
        [".zip"] = "Compactado", [".rar"] = "Compactado", [".7z"] = "Compactado",
        [".dwg"] = "Desenho (DWG)", [".dxf"] = "Desenho (DXF)",
        [".txt"] = "Texto", [".msg"] = "E-mail", [".eml"] = "E-mail",
        [".mp4"] = "Vídeo", [".mov"] = "Vídeo",
    };

    private static readonly HashSet<string> ExtensoesTemporarias = new(StringComparer.OrdinalIgnoreCase)
    {
        ".crdownload", ".part", ".partial", ".download", ".opdownload", ".tmp", ".temp",
    };

    public static string Descrever(string caminho)
    {
        string nome = Path.GetFileName(caminho);
        string extensao = Path.GetExtension(nome);
        if (!PorExtensao.TryGetValue(extensao, out string? tipo))
        {
            return extensao.Length > 1 ? $"Arquivo {extensao.ToLowerInvariant()}" : "Arquivo sem extensão";
        }

        return WhatsApp().IsMatch(nome) ? $"{tipo} (WhatsApp)" : tipo;
    }

    /// <summary>Downloads pela metade, temporarios do Office e arquivos do sistema: fora do organizador.</summary>
    public static bool Temporario(string caminho)
    {
        string nome = Path.GetFileName(caminho);
        return ExtensoesTemporarias.Contains(Path.GetExtension(nome))
               || nome.StartsWith("~$", StringComparison.Ordinal)
               || nome.StartsWith("~spobras-", StringComparison.OrdinalIgnoreCase)
               || nome.EndsWith(Operacoes.ExecutorLote.SufixoParcial, StringComparison.OrdinalIgnoreCase)
               || nome.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)
               || nome.Equals("Thumbs.db", StringComparison.OrdinalIgnoreCase);
    }

    // IMG-20261110-WA0007.jpg, VID-..., DOC-..., PTT-...
    [GeneratedRegex(@"^(IMG|VID|DOC|AUD|PTT|STK)-\d{8}-WA\d+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WhatsApp();
}
