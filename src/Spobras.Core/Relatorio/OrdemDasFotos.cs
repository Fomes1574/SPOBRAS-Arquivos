namespace Spobras.Core.Relatorio;

/// <summary>
/// Reordenacoes da lista de fotos que o PC e o celular fazem igual. Todas sao estaveis: dentro de cada grupo, a
/// ordem que o usuario ja tinha fica como estava.
/// </summary>
public static class OrdemDasFotos
{
    /// <summary>As marcadas vao juntas para o fim (ou para o inicio), na ordem em que estavam; as outras nao mudam de ordem.</summary>
    public static List<T> Marcadas<T>(IReadOnlyList<T> fotos, Func<int, bool> marcada, bool paraOFim)
    {
        var marcadas = new List<T>();
        var outras = new List<T>();
        for (int i = 0; i < fotos.Count; i++)
        {
            (marcada(i) ? marcadas : outras).Add(fotos[i]);
        }

        return paraOFim ? [.. outras, .. marcadas] : [.. marcadas, .. outras];
    }

    /// <summary>
    /// Deitadas primeiro, em pe no fim. Vale o formato depois do giro (largura e altura ja giradas); foto quadrada
    /// fica com as deitadas.
    /// </summary>
    public static List<T> DeitadasPrimeiro<T>(IReadOnlyList<T> fotos, Func<T, (int Largura, int Altura)> tamanho)
    {
        static bool EmPe((int Largura, int Altura) t) => t.Altura > t.Largura;
        return [.. fotos.Where(f => !EmPe(tamanho(f))), .. fotos.Where(f => EmPe(tamanho(f)))];
    }

    /// <summary>Quantas fotos mudariam de lugar (0 = a lista ja esta assim, nada a fazer).</summary>
    public static int Mudancas<T>(IReadOnlyList<T> antes, IReadOnlyList<T> depois) where T : class =>
        antes.Where((f, i) => !ReferenceEquals(f, depois[i])).Count();
}
