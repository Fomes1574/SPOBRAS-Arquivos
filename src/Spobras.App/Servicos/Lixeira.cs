namespace Spobras.App.Servicos;

/// <summary>Manda um arquivo para a Lixeira (recuperavel), em vez de apagar de vez.</summary>
public interface ILixeira
{
    void Enviar(string caminho);
}

/// <summary>Lixeira do Windows (a mesma do Explorador de Arquivos).</summary>
public sealed class LixeiraDoWindows : ILixeira
{
    public void Enviar(string caminho) =>
        Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(caminho,
            Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
}
