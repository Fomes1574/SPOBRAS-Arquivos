namespace Spobras.Core.Organizar;

/// <summary>
/// Junta os eventos de uma pasta vigiada numa entrada por arquivo e so libera o arquivo quando a
/// gravacao terminou (especificacao 4.1, "Regras para vigiar pastas"). Logica pura: quem chama
/// informa o que viu no disco e que horas sao.
/// </summary>
public sealed class FilaChegada(TimeSpan quietoPor)
{
    private sealed class Entrada
    {
        public long Tamanho;
        public DateTimeOffset ModificadoEm;
        public DateTimeOffset UltimaMudanca;
        public DateTimeOffset? ChegouEm;
        public bool Entregue;
    }

    private readonly Dictionary<string, Entrada> _entradas = new(StringComparer.OrdinalIgnoreCase);

    public int Aguardando => _entradas.Values.Count(e => !e.Entregue);

    /// <summary>
    /// Registra o que foi visto de um arquivo (por evento do Windows ou por varredura da pasta).
    /// </summary>
    /// <param name="chegouAgora">
    /// True quando o app viu o arquivo aparecer; false para o que ja estava na pasta (chegada desconhecida).
    /// </param>
    public void Observar(string caminho, long tamanho, DateTimeOffset modificadoEm, DateTimeOffset agora, bool chegouAgora)
    {
        if (TipoArquivo.Temporario(caminho))
        {
            return;
        }

        if (!_entradas.TryGetValue(caminho, out var entrada))
        {
            _entradas[caminho] = new Entrada
            {
                Tamanho = tamanho,
                ModificadoEm = modificadoEm,
                UltimaMudanca = agora,
                ChegouEm = chegouAgora ? agora : null,
            };
            return;
        }

        if (entrada.Tamanho != tamanho || entrada.ModificadoEm != modificadoEm)
        {
            // Ainda esta sendo gravado (ou foi salvo de novo): recomeca a espera e volta para a fila.
            entrada.Tamanho = tamanho;
            entrada.ModificadoEm = modificadoEm;
            entrada.UltimaMudanca = agora;
            entrada.Entregue = false;
        }
    }

    /// <summary>O arquivo saiu da pasta (movido, renomeado ou removido pelo usuario).</summary>
    public void Esquecer(string caminho) => _entradas.Remove(caminho);

    /// <summary>
    /// Arquivos que ficaram quietos pelo tempo configurado e que podem ser abertos. Cada arquivo sai uma vez so.
    /// </summary>
    /// <param name="disponivel">Diz se o arquivo pode ser aberto agora (nao esta ocupado nem so na nuvem).</param>
    public IReadOnlyList<ArquivoParaOrganizar> Prontos(DateTimeOffset agora, Func<string, bool> disponivel)
    {
        var prontos = new List<ArquivoParaOrganizar>();
        foreach (var (caminho, entrada) in _entradas)
        {
            if (entrada.Entregue || agora - entrada.UltimaMudanca < quietoPor)
            {
                continue;
            }

            if (!disponivel(caminho))
            {
                continue; // ocupado: tenta de novo na proxima rodada, sem travar o app
            }

            entrada.Entregue = true;
            prontos.Add(new ArquivoParaOrganizar(caminho) { ChegouEm = entrada.ChegouEm, ModificadoEm = entrada.ModificadoEm });
        }

        return prontos;
    }
}
