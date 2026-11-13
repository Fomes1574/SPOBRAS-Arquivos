using System.Text.Json;

namespace Spobras.Celular.Servicos;

public enum FormatoGerado
{
    ExcelEPdf,
    Excel,
    Pdf,
}

public enum FormatoFoto
{
    Padrao43,
    Largo169,
    TelaCheia,
    Quadrado,
}

public enum Flash
{
    Auto,
    Ligado,
    Desligado,
    Lanterna,
}

/// <summary>Configuracoes do celular, guardadas num JSON na pasta do app.</summary>
public sealed record Preferencias
{
    public Escolhido? Mestra { get; init; }
    public Escolhido? Pasta { get; init; }
    public bool PastaPorEscola { get; init; } = true;
    public FormatoGerado Formato { get; init; } = FormatoGerado.ExcelEPdf;
    public FormatoFoto FormatoFoto { get; init; } = FormatoFoto.Padrao43;
    public Flash Flash { get; init; } = Flash.Auto;
    public bool FotosNaGaleria { get; init; }

    /// <summary>Copia do logo dentro do app (o usuario escolhe a imagem uma vez).</summary>
    public string? Logo { get; init; }

    /// <summary>Hash da ultima copia da planilha lida (se nao mudou, nao precisa ler de novo).</summary>
    public string? HashDaMestra { get; init; }
    public DateTimeOffset? UltimaLeitura { get; init; }

    public IReadOnlyList<Salvo> UltimosGerados { get; init; } = [];
}

public sealed class ArquivoPreferencias(string pasta)
{
    private static readonly JsonSerializerOptions Opcoes = new() { WriteIndented = true };
    private readonly string _caminho = Path.Combine(pasta, "preferencias.json");
    private readonly object _trava = new();

    public Preferencias Ler()
    {
        lock (_trava)
        {
            try
            {
                return File.Exists(_caminho) ? JsonSerializer.Deserialize<Preferencias>(File.ReadAllText(_caminho), Opcoes) ?? new() : new();
            }
            catch (JsonException)
            {
                return new();
            }
        }
    }

    public void Gravar(Preferencias p)
    {
        lock (_trava)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_caminho)!);
            string temp = _caminho + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(p, Opcoes));
            File.Move(temp, _caminho, overwrite: true);
        }
    }
}
