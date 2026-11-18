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
    public Flash Flash { get; init; } = Flash.Desligado;

    /// <summary>
    /// O usuario ja escolheu o flash. Antes disso vale Desligado (a foto sai na hora); quem tinha o antigo padrao
    /// Auto sem nunca ter escolhido passa para Desligado.
    /// </summary>
    public bool FlashEscolhido { get; init; }
    public bool FotosNaGaleria { get; init; }



    /// <summary>Copia do logo dentro do app (o usuario escolhe a imagem uma vez).</summary>
    public string? Logo { get; init; }

    /// <summary>Hash da ultima copia da planilha lida (se nao mudou, nao precisa ler de novo).</summary>
    public string? HashDaMestra { get; init; }

    /// <summary>Tamanho e data da planilha na ultima leitura: igual, nem copia de novo.</summary>
    public string? AssinaturaDaMestra { get; init; }
    public DateTimeOffset? UltimaLeitura { get; init; }

    public IReadOnlyList<Salvo> UltimosGerados { get; init; } = [];

    /// <summary>Configuracoes da base: onde estao as tabelas e colunas, se a planilha mudou de estrutura.</summary>
    public Spobras.Core.Base.MapeamentoMestra Mapeamento { get; init; } = new();

    /// <summary>Configuracoes da base: situacao escolhida para cada status do contrato (por cima das padrao).</summary>
    public IReadOnlyDictionary<string, Spobras.Core.Base.SituacaoContrato> RegrasStatus { get; init; } = new Dictionary<string, Spobras.Core.Base.SituacaoContrato>();
}

public sealed class ArquivoPreferencias(string pasta)
{
    private static readonly JsonSerializerOptions Opcoes = new() { WriteIndented = true };
    private readonly string _caminho = Path.Combine(pasta, "preferencias.json");
    private readonly object _trava = new();

    private Preferencias? _atual;

    private string Copia => _caminho + ".bak";

    /// <summary>Lida uma vez e guardada na memoria (as telas consultam varias vezes por desenho).</summary>
    public Preferencias Ler()
    {
        lock (_trava)
        {
            return _atual ??= Normalizar(LerArquivo(_caminho) ?? LerArquivo(Copia) ?? new());
        }
    }

    private static Preferencias? LerArquivo(string caminho)
    {
        try
        {
            return File.Exists(caminho) ? JsonSerializer.Deserialize<Preferencias>(File.ReadAllText(caminho), Opcoes) : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    private static Preferencias Normalizar(Preferencias p) => p with
    {
        Flash = p.FlashEscolhido ? p.Flash : Flash.Desligado,
        UltimosGerados = p.UltimosGerados ?? [],
        Mapeamento = p.Mapeamento ?? new(),
        RegrasStatus = p.RegrasStatus ?? new Dictionary<string, Spobras.Core.Base.SituacaoContrato>(),
    };

    public void Gravar(Preferencias p)
    {
        lock (_trava)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_caminho)!);
            string temp = _caminho + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(p, Opcoes));
            if (File.Exists(_caminho))
            {
                File.Copy(_caminho, Copia, overwrite: true);
            }

            File.Move(temp, _caminho, overwrite: true);
            _atual = p;
        }
    }
}
