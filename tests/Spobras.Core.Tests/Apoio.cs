using Spobras.Core.Abstracoes;

namespace Spobras.Core.Tests;

/// <summary>Pasta temporaria por teste, removida no fim.</summary>
public abstract class ComPastaTemporaria : IDisposable
{
    protected string Pasta { get; } = Directory.CreateTempSubdirectory("spobras-teste-").FullName;

    public void Dispose()
    {
        Directory.Delete(Pasta, recursive: true);
        GC.SuppressFinalize(this);
    }

    protected string Criar(string nomeRelativo, string conteudo = "x")
    {
        string caminho = Path.Combine(Pasta, nomeRelativo);
        Directory.CreateDirectory(Path.GetDirectoryName(caminho)!);
        File.WriteAllText(caminho, conteudo);
        return caminho;
    }

    protected string Em(params string[] partes) => Path.Combine([Pasta, .. partes]);

    protected string[] Nomes(string? subpasta = null) =>
        [.. Directory.GetFiles(subpasta is null ? Pasta : Em(subpasta)).Select(c => Path.GetFileName(c)!).Order(StringComparer.Ordinal)];
}

public sealed class RelogioFixo : IRelogio
{
    public DateTimeOffset Agora { get; set; } = new(2026, 11, 11, 10, 0, 0, TimeSpan.FromHours(-3));
}

/// <summary>Fechamento inesperado do app no meio de uma operacao.</summary>
public sealed class QuedaSimulada : Exception;

/// <summary>Disco real com pontos de falha: simula queda, outro volume e copia corrompida.</summary>
public sealed class ArquivosComFalha : IArquivos
{
    private readonly ArquivosDisco _disco = new();

    /// <summary>Chamado antes de cada mover/copiar/remover; pode lancar para simular falha.</summary>
    public Action<string, string, string?>? Antes { get; set; }
    public bool OutroVolume { get; set; }
    public bool CorromperCopia { get; set; }
    public List<string> Removidos { get; } = [];

    public bool ArquivoExiste(string caminho) => _disco.ArquivoExiste(caminho);
    public bool PastaExiste(string caminho) => _disco.PastaExiste(caminho);
    public InfoArquivo? Info(string caminho) => _disco.Info(caminho);
    public IReadOnlyList<string> ListarArquivos(string pasta) => _disco.ListarArquivos(pasta);
    public IReadOnlyList<string> ListarArquivosRecursivo(string pasta) => _disco.ListarArquivosRecursivo(pasta);
    public void CriarPasta(string caminho) => _disco.CriarPasta(caminho);
    public Stream AbrirLeitura(string caminho) => _disco.AbrirLeitura(caminho);
    public string Hash(string caminho) => _disco.Hash(caminho);
    public bool MesmoVolume(string a, string b) => !OutroVolume;

    public void Mover(string origem, string destino)
    {
        Antes?.Invoke("mover", origem, destino);
        _disco.Mover(origem, destino);
    }

    public void Copiar(string origem, string destino)
    {
        Antes?.Invoke("copiar", origem, destino);
        _disco.Copiar(origem, destino);
        if (CorromperCopia)
        {
            File.AppendAllText(destino, "lixo");
        }
    }

    public void Remover(string caminho)
    {
        Antes?.Invoke("remover", caminho, null);
        Removidos.Add(caminho);
        _disco.Remover(caminho);
    }
}
