using Spobras.Celular.Servicos;
using Spobras.Core.Nomes;

namespace Spobras.Testes;

/// <summary>
/// Plataforma para testes e para o host de navegador: os "seletores" pegam arquivos de pastas conhecidas e salvar
/// copia para uma pasta de saida. Nao faz parte do app do celular.
/// </summary>
public sealed class PlataformaDeTeste : IPlataforma
{
    public PlataformaDeTeste(string raiz)
    {
        Raiz = raiz;
        PastaDoApp = Path.Combine(raiz, "app");
        Entrada = Path.Combine(raiz, "entrada");
        Saida = Path.Combine(raiz, "saida");
        Directory.CreateDirectory(PastaDoApp);
        Directory.CreateDirectory(Entrada);
        Directory.CreateDirectory(Saida);
    }

    public string Raiz { get; }
    public string PastaDoApp { get; }
    public string Entrada { get; }
    public string Saida { get; }

    /// <summary>Planilha que o seletor devolve.</summary>
    public string Mestra => Path.Combine(Entrada, "Escolas_Contratos_Com_Relatorio.xlsx");

    public bool SemConexao { get; set; }
    public bool CameraPermitida { get; set; } = true;
    public List<string> Compartilhados { get; } = [];
    public List<string> NaGaleria { get; } = [];

    /// <summary>Arquivos que o proximo seletor de arquivos devolve (nulo = todos os da pasta de entrada do tipo).</summary>
    public List<string>? ProximaEscolha { get; set; }

    public Task<Escolhido?> EscolherPlanilhaAsync() =>
        Task.FromResult<Escolhido?>(File.Exists(Mestra) ? new Escolhido("arquivo:" + Mestra, Path.GetFileName(Mestra)) : null);

    public Task<Copia> CopiarAsync(string id, string destino)
    {
        if (SemConexao)
        {
            return Task.FromResult(Copia.SemConexao);
        }

        string origem = id.StartsWith("arquivo:", StringComparison.Ordinal) ? id[8..] : id;
        if (!File.Exists(origem))
        {
            return Task.FromResult(Copia.Inacessivel);
        }

        File.Copy(origem, destino, overwrite: true);
        return Task.FromResult(Copia.Ok);
    }

    public Task<IReadOnlyList<Escolhido>> EscolherArquivosAsync(string tipo, bool varios, string pastaDestino)
    {
        Directory.CreateDirectory(pastaDestino);
        bool imagem = tipo.StartsWith("image", StringComparison.Ordinal);
        var origem = ProximaEscolha ?? [.. Directory.GetFiles(Entrada)
            .Where(f => imagem ? f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                               : f.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)];
        ProximaEscolha = null;
        var lista = new List<Escolhido>();
        foreach (string f in varios ? origem : origem.Take(1))
        {
            string nome = NomeArquivo.Livre(Path.GetFileName(f), n => File.Exists(Path.Combine(pastaDestino, n)));
            string copia = Path.Combine(pastaDestino, nome);
            File.Copy(f, copia);
            lista.Add(new Escolhido("arquivo:" + f, Path.GetFileName(f), copia));
        }

        return Task.FromResult<IReadOnlyList<Escolhido>>(lista);
    }

    public Task<Escolhido?> EscolherPastaAsync() => Task.FromResult<Escolhido?>(new Escolhido("pasta:" + Saida, "Relatórios"));

    public Task<Salvo> SalvarAsync(string pastaId, string? subpasta, string arquivo)
    {
        string pasta = pastaId.StartsWith("pasta:", StringComparison.Ordinal) ? pastaId[6..] : pastaId;
        if (subpasta is not null)
        {
            pasta = Path.Combine(pasta, subpasta);
        }

        Directory.CreateDirectory(pasta);
        string nome = NomeArquivo.Livre(Path.GetFileName(arquivo), n => File.Exists(Path.Combine(pasta, n)));
        string destino = Path.Combine(pasta, nome);
        File.Copy(arquivo, destino);
        return Task.FromResult(new Salvo("arquivo:" + destino, nome, "Relatórios" + (subpasta is null ? "" : " › " + subpasta), new FileInfo(destino).Length));
    }

    public Task GuardarNaGaleriaAsync(string foto)
    {
        NaGaleria.Add(foto);
        return Task.CompletedTask;
    }

    public Task CompartilharAsync(IReadOnlyList<string> arquivos)
    {
        Compartilhados.AddRange(arquivos);
        return Task.CompletedTask;
    }

    public Task AbrirAsync(Salvo arquivo) => Task.CompletedTask;

    public Task<bool> PermitirCameraAsync() => Task.FromResult(CameraPermitida);
}
