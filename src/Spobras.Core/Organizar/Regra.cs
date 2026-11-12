namespace Spobras.Core.Organizar;

/// <summary>
/// Regra sem programacao (especificacao 4.1): se o arquivo bater com as condicoes, sugere a pasta.
/// Condicao vazia nao restringe; as preenchidas valem juntas.
/// </summary>
public sealed record Regra
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Nome { get; init; }
    public bool Ativa { get; init; } = true;

    /// <summary>Numero maior ganha quando mais de uma regra serve.</summary>
    public int Prioridade { get; init; }

    /// <summary>Qualquer um destes trechos no nome do arquivo (sem diferenciar maiusculas e acentos).</summary>
    public IReadOnlyList<string> NomeContem { get; init; } = [];

    /// <summary>Extensoes com ou sem ponto: "pdf", ".xlsx".</summary>
    public IReadOnlyList<string> Extensoes { get; init; } = [];

    /// <summary>Trecho do caminho da pasta onde o arquivo esta.</summary>
    public string? PastaOrigemContem { get; init; }

    /// <summary>
    /// Qualquer um destes trechos DENTRO do arquivo (planilha, PDF com texto, Word, texto). Arquivo que
    /// nao da para ler nao atende a esta condicao.
    /// </summary>
    public IReadOnlyList<string> ConteudoContem { get; init; } = [];

    /// <summary>
    /// Pasta de destino. Aceita {ano} e {mes} (da chegada do arquivo; sem ela, da data do arquivo) e
    /// {contrato}, {escola}, {lote} e {portaria}, quando o arquivo e reconhecido na Base de dados.
    /// </summary>
    public required string Destino { get; init; }
}

public sealed record ArquivoParaOrganizar(string Caminho)
{
    /// <summary>Quando o app viu o arquivo chegar; null para arquivos que ja estavam na pasta.</summary>
    public DateTimeOffset? ChegouEm { get; init; }
    public DateTimeOffset? ModificadoEm { get; init; }
}

public enum OrigemSugestao
{
    /// <summary>Uma regra do usuario decidiu.</summary>
    Regra,

    /// <summary>Nenhuma regra serve, ou as regras se contradizem: o usuario escolhe.</summary>
    VoceDecide,

    /// <summary>Arquivo fora do organizador (protegido, temporario, ainda sendo gravado).</summary>
    Ignorado,
}

public sealed record Sugestao(
    string Caminho,
    string Tipo,
    string Achou,
    OrigemSugestao Origem,
    string? PastaDestino,
    string? CaminhoDestino,
    string Motivo,
    Guid? RegraId)
{
    public bool PodeMover => Origem == OrigemSugestao.Regra && CaminhoDestino is not null;
}
