namespace Spobras.App.Servicos;

/// <summary>Janelas do Windows que as telas precisam abrir. Separado para testar a logica sem tela.</summary>
public interface IDialogos
{
    string? EscolherPasta(string titulo, string? pastaInicial = null);
    IReadOnlyList<string> EscolherPastas(string titulo, string? pastaInicial = null);
    IReadOnlyList<string> EscolherArquivos(string titulo, string? filtro = null, string? pastaInicial = null);
    bool Confirmar(string titulo, string mensagem, string botaoSim = "Sim", string botaoNao = "Cancelar");
    void Avisar(string titulo, string mensagem);
    string? PedirTexto(string titulo, string mensagem, string valorInicial = "");
    string? LerAreaDeTransferencia();
    void MostrarNaPasta(string caminho);
    void AbrirPasta(string pasta);
}

/// <summary>Leva uma acao para a thread da tela.</summary>
public interface IInterface
{
    void Executar(Action acao);
}

/// <summary>Para testes e servicos sem tela: executa na hora.</summary>
public sealed class InterfaceDireta : IInterface
{
    public void Executar(Action acao) => acao();
}
