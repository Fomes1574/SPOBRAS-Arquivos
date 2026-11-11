namespace Spobras.Core.Ambiente;

public enum EstadoVerificacao
{
    Passou,
    Falhou,
    /// <summary>O item nao pode ser conferido neste PC (ex.: nenhum arquivo so na nuvem).</summary>
    NaoVerificado,
}

/// <summary>Resultado de um item do Teste de Ambiente (arquitetura, secao 9).</summary>
public sealed record ResultadoVerificacao(
    int Numero,
    string Titulo,
    EstadoVerificacao Estado,
    IReadOnlyList<string> Detalhes)
{
    public static ResultadoVerificacao Passou(int numero, string titulo, params string[] detalhes) =>
        new(numero, titulo, EstadoVerificacao.Passou, detalhes);

    public static ResultadoVerificacao Falhou(int numero, string titulo, params string[] detalhes) =>
        new(numero, titulo, EstadoVerificacao.Falhou, detalhes);

    public static ResultadoVerificacao NaoVerificado(int numero, string titulo, params string[] detalhes) =>
        new(numero, titulo, EstadoVerificacao.NaoVerificado, detalhes);

    public string EstadoTexto => Estado switch
    {
        EstadoVerificacao.Passou => "PASSOU",
        EstadoVerificacao.Falhou => "FALHOU",
        _ => "NÃO VERIFICADO",
    };
}
