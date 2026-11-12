using Spobras.Core.Base;

namespace Spobras.Core.Ferramentas;

/// <summary>Texto ja lido de um arquivo, guardado para a proxima busca. Motivo preenchido quando nao deu para ler.</summary>
public sealed record TextoIndexado(string? Texto, string? Motivo);

/// <summary>Indice local da busca: um registro por arquivo, valido enquanto tamanho e data nao mudarem.</summary>
public interface IIndiceTexto
{
    TextoIndexado? Ler(string caminho, long tamanho, long modificado);
    void Gravar(string caminho, long tamanho, long modificado, TextoIndexado texto);
}

public sealed record AchadoBusca(string Caminho, bool NoNome, string Trecho);

public static class TrechoBusca
{
    /// <summary>Procura sem acento nem caixa; devolve um trecho de ~60 caracteres em volta do achado, ou null.</summary>
    public static string? Trecho(string texto, string termo, int largura = 60)
    {
        string normal = Normalizacao.Busca(texto);
        string alvo = Normalizacao.Busca(termo.Trim());
        if (alvo.Length == 0)
        {
            return null;
        }

        int i = normal.IndexOf(alvo, StringComparison.Ordinal);
        if (i < 0)
        {
            return null;
        }

        // RemoverAcentos mantem o tamanho para o portugues; se mudar, usa o inicio do texto.
        if (normal.Length != texto.Length)
        {
            i = 0;
        }

        int inicio = Math.Max(0, i - (largura / 2));
        int fim = Math.Min(texto.Length, i + alvo.Length + (largura / 2));
        // Nao comecar nem terminar no meio de uma palavra.
        while (inicio > 0 && inicio < i && !char.IsWhiteSpace(texto[inicio - 1]))
        {
            inicio++;
        }

        while (fim < texto.Length && fim > i + alvo.Length && !char.IsWhiteSpace(texto[fim]))
        {
            fim--;
        }
        string trecho = string.Join(" ", texto[inicio..fim].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return (inicio > 0 ? "…" : "") + trecho + (fim < texto.Length ? "…" : "");
    }
}
