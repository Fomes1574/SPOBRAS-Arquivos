namespace Spobras.Core;

/// <summary>Especificacao 3.1: so "impede gerar" bloqueia a acao.</summary>
public enum Gravidade
{
    Informacao,
    PrecisaConferir,
    ImpedeGerar,
}

public sealed record Aviso(Gravidade Gravidade, string Mensagem)
{
    public static Aviso Info(string mensagem) => new(Gravidade.Informacao, mensagem);
    public static Aviso Conferir(string mensagem) => new(Gravidade.PrecisaConferir, mensagem);
    public static Aviso Impede(string mensagem) => new(Gravidade.ImpedeGerar, mensagem);
}
