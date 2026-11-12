using System.Globalization;
using System.Text.RegularExpressions;
using Spobras.Core.Base;
using Spobras.Core.Cronograma;
using Spobras.Core.Nomes;

namespace Spobras.Core.Modelos;

public enum TipoCampo
{
    Texto,
    Numero,
    Data,
}

/// <summary>Um campo que o usuario preenche e a celula do modelo que recebe o valor.</summary>
public sealed record CampoModelo
{
    public string Nome { get; init; } = "";
    public string Aba { get; init; } = "";
    public string Celula { get; init; } = "";
    public TipoCampo Tipo { get; init; }
    public bool Obrigatorio { get; init; }

    /// <summary>Chave de <see cref="OrigensCampo"/> quando o campo pode vir da Base de dados; vazio se nao.</summary>
    public string Origem { get; init; } = "";
}

/// <summary>Modelo Excel cadastrado: uma copia guardada pelo app e a lista de campos mapeados.</summary>
public sealed record ModeloExcel
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Nome { get; init; } = "";

    /// <summary>Nome do arquivo da copia, dentro da pasta de modelos do app.</summary>
    public string Arquivo { get; init; } = "";

    /// <summary>Resumo da estrutura do arquivo no cadastro; se mudar, o mapeamento precisa ser revisto.</summary>
    public string Assinatura { get; init; } = "";

    /// <summary>Nome dos arquivos gerados. Aceita {Nome do campo}. Vazio = nome do modelo.</summary>
    public string PadraoNome { get; init; } = "";
    public List<CampoModelo> Campos { get; init; } = [];
}

/// <summary>Dados da obra que um campo pode trazer da Base de dados.</summary>
public static class OrigensCampo
{
    public static readonly IReadOnlyList<(string Chave, string Rotulo)> Todas =
    [
        ("", "Digitado por mim"),
        ("escola", "Escola / obra"),
        ("contratada", "Contratada"),
        ("contrato", "Contrato"),
        ("lote", "Lote"),
        ("portaria", "Portaria"),
        ("sei", "Processo SEI"),
        ("dotacao", "Dotação"),
        ("escopo", "Escopo"),
        ("data-os", "Data da OS"),
    ];

    public static string Rotulo(string chave) => Todas.FirstOrDefault(o => o.Chave == chave).Rotulo ?? "Digitado por mim";

    /// <summary>Valor do campo para a obra, como texto que o usuario confere; vazio se a base nao tiver.</summary>
    public static string Valor(ObraLida obra, string chave) => chave switch
    {
        "escola" => obra.Empreendimento,
        "contratada" => obra.Contratada ?? "",
        "contrato" => obra.Contrato,
        "lote" => obra.Lote ?? "",
        "portaria" => obra.Portaria ?? "",
        "sei" => obra.ProcessoSei ?? "",
        "dotacao" => obra.Dotacao ?? "",
        "escopo" => obra.Escopo ?? "",
        "data-os" => obra.DataOs?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "",
        _ => "",
    };
}

public static partial class ValidadorModelo
{
    [GeneratedRegex(@"^\$?([A-Za-z]{1,3})\$?([1-9][0-9]{0,6})$")]
    private static partial Regex Referencia();

    [GeneratedRegex(@"\{([^{}]+)\}")]
    private static partial Regex Marcador();

    /// <summary>"b12" ou "$B$12" viram "B12"; null se nao for uma celula.</summary>
    public static string? Celula(string texto)
    {
        var m = Referencia().Match(texto.Trim());
        return m.Success ? m.Groups[1].Value.ToUpperInvariant() + m.Groups[2].Value : null;
    }

    /// <summary>Converte o que foi digitado para o tipo do campo. Vazio vira null (celula limpa).</summary>
    public static (object? Valor, string? Erro) Converter(CampoModelo campo, string? texto)
    {
        texto = texto?.Trim() ?? "";
        if (texto.Length == 0)
        {
            return (null, campo.Obrigatorio ? $"Falta “{campo.Nome}”" : null);
        }

        switch (campo.Tipo)
        {
            case TipoCampo.Numero:
                return Normalizacao.LerReais(texto) is { } numero ? (numero, null) : (null, $"“{campo.Nome}”: o número \"{texto}\" não foi entendido");
            case TipoCampo.Data:
                return ColagemCronograma.Data(texto) is { } data ? (data, null) : (null, $"“{campo.Nome}”: a data \"{texto}\" não foi entendida (use dia/mês/ano)");
            default:
                return (texto, null);
        }
    }

    /// <summary>Problemas do cadastro que impedem salvar o modelo.</summary>
    public static IReadOnlyList<string> ConferirCadastro(ModeloExcel modelo)
    {
        var problemas = new List<string>();
        if (string.IsNullOrWhiteSpace(modelo.Nome))
        {
            problemas.Add("Dê um nome ao modelo.");
        }

        if (modelo.Campos.Count == 0)
        {
            problemas.Add("Cadastre pelo menos um campo.");
        }

        var nomes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var destinos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < modelo.Campos.Count; i++)
        {
            var c = modelo.Campos[i];
            string quem = string.IsNullOrWhiteSpace(c.Nome) ? $"Campo {i + 1}" : $"“{c.Nome.Trim()}”";
            if (string.IsNullOrWhiteSpace(c.Nome))
            {
                problemas.Add($"{quem}: falta o nome.");
            }
            else if (!nomes.Add(c.Nome.Trim()))
            {
                problemas.Add($"{quem}: já existe outro campo com esse nome.");
            }

            if (string.IsNullOrWhiteSpace(c.Aba))
            {
                problemas.Add($"{quem}: escolha a aba.");
            }

            if (Celula(c.Celula) is not { } celula)
            {
                problemas.Add($"{quem}: \"{c.Celula}\" não é uma célula. Use a letra da coluna e o número da linha, como B12.");
            }
            else if (!destinos.TryAdd(c.Aba + "!" + celula, quem))
            {
                problemas.Add($"{quem}: a célula {celula} já recebe o campo {destinos[c.Aba + "!" + celula]}.");
            }
        }

        foreach (Match m in Marcador().Matches(modelo.PadraoNome))
        {
            if (!modelo.Campos.Any(c => string.Equals(c.Nome.Trim(), m.Groups[1].Value.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                problemas.Add($"O nome do arquivo usa {{{m.Groups[1].Value}}}, mas não há campo com esse nome.");
            }
        }

        return problemas;
    }

    /// <summary>Nome do arquivo gerado (sem extensao), trocando {Campo} pelo valor digitado.</summary>
    public static string NomeDoArquivo(ModeloExcel modelo, IReadOnlyDictionary<string, string> valores)
    {
        string padrao = string.IsNullOrWhiteSpace(modelo.PadraoNome) ? modelo.Nome : modelo.PadraoNome;
        string nome = Marcador().Replace(padrao, m =>
        {
            string chave = m.Groups[1].Value.Trim();
            var achado = valores.FirstOrDefault(v => string.Equals(v.Key.Trim(), chave, StringComparison.OrdinalIgnoreCase));
            return achado.Key is null ? "" : achado.Value.Trim().Replace('/', '-');
        });
        nome = NomeArquivo.Corrigir(nome).Trim(' ', '-', '.');
        return nome.Length == 0 ? NomeArquivo.Corrigir(modelo.Nome) : nome;
    }
}
