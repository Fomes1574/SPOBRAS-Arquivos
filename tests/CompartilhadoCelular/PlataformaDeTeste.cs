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
    public Func<Task>? AntesDeSalvar { get; set; }
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
        Copias++;
        return Task.FromResult(Copia.Ok);
    }

    /// <summary>Simula local que nao deixa apagar (ex.: pasta na nuvem).</summary>
    public bool ApagarFalha { get; set; }

    public Task<bool> ApagarAsync(Salvo arquivo)
    {
        if (ApagarFalha) return Task.FromResult(false);
        string caminho = arquivo.Id.StartsWith("arquivo:", StringComparison.Ordinal) ? arquivo.Id[8..] : arquivo.Id;
        if (File.Exists(caminho)) File.Delete(caminho);
        return Task.FromResult(true);
    }

    /// <summary>Quantas vezes a planilha foi copiada (para conferir que nao copia o que nao mudou).</summary>
    public int Copias { get; private set; }

    /// <summary>Simula aparelho ou nuvem que nao informa tamanho e data.</summary>
    public bool SemAssinatura { get; set; }

    public Task<string?> AssinaturaAsync(string id)
    {
        string origem = id.StartsWith("arquivo:", StringComparison.Ordinal) ? id[8..] : id;
        if (SemAssinatura || SemConexao || !File.Exists(origem)) return Task.FromResult<string?>(null);
        var f = new FileInfo(origem);
        return Task.FromResult<string?>($"{f.Length}|{f.LastWriteTimeUtc.Ticks}");
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

    public async Task<Salvo> SalvarAsync(string pastaId, string? subpasta, string arquivo)
    {
        if (AntesDeSalvar is not null) await AntesDeSalvar();
        string pasta = pastaId.StartsWith("pasta:", StringComparison.Ordinal) ? pastaId[6..] : pastaId;
        if (subpasta is not null)
        {
            pasta = Path.Combine(pasta, subpasta);
        }

        Directory.CreateDirectory(pasta);
        string nome = NomeArquivo.Livre(Path.GetFileName(arquivo), n => File.Exists(Path.Combine(pasta, n)));
        string destino = Path.Combine(pasta, nome);
        File.Copy(arquivo, destino);
        return new Salvo("arquivo:" + destino, nome, "Relatórios" + (subpasta is null ? "" : " › " + subpasta), new FileInfo(destino).Length);
    }

    /// <summary>Pasta que faz o papel da galeria: cada foto guardada e copiada para ca.</summary>
    public string Galeria => Path.Combine(Raiz, "galeria");

    public Task GuardarNaGaleriaAsync(string foto)
    {
        Directory.CreateDirectory(Galeria);
        string destino = Path.Combine(Galeria, NomeArquivo.Livre(Path.GetFileName(foto), n => File.Exists(Path.Combine(Galeria, n))));
        File.Copy(foto, destino);
        lock (NaGaleria)
        {
            NaGaleria.Add(destino);
        }

        return Task.CompletedTask;
    }

    // ---- GPS e endereco (simulados) ----

    public bool LocalizacaoPermitida { get; set; } = true;

    /// <summary>Leituras que o GPS devolve, uma por vez, na ordem (a ultima se repete). Vazio = sem sinal.</summary>
    public List<Spobras.Core.Relatorio.PontoGps> Pontos { get; } = [];

    /// <summary>Espera antes de responder (para ver o "Procurando sinal").</summary>
    public TimeSpan AtrasoDoGps { get; set; }

    public int Leituras { get; private set; }

    /// <summary>Endereco de cada ponto; sem entrada, o servico nao acha nada.</summary>
    public Func<double, double, ResultadoEndereco> Endereco { get; set; } = (_, _) => new ResultadoEndereco("Rua Exemplo, 123 – Vila Exemplo");

    public int Buscas { get; private set; }

    public Task<bool> PermitirLocalizacaoAsync() => Task.FromResult(LocalizacaoPermitida);

    public async Task<Spobras.Core.Relatorio.PontoGps?> LocalizarAsync(TimeSpan limite, double precisaoDesejada, Action<double>? aoMelhorar = null, CancellationToken cancelar = default)
    {
        int n = Leituras++;
        if (AtrasoDoGps > TimeSpan.Zero) await Task.Delay(AtrasoDoGps, cancelar);
        if (Pontos.Count == 0) return null;
        var p = Pontos[Math.Min(n, Pontos.Count - 1)];
        aoMelhorar?.Invoke(p.Precisao);
        return p;
    }

    public Task<ResultadoEndereco> EnderecoAsync(double latitude, double longitude)
    {
        Buscas++;
        return Task.FromResult(SemConexao ? new ResultadoEndereco(null, FalhaEndereco.SemInternet) : Endereco(latitude, longitude));
    }

    public int FotosAoMesmoTempo { get; set; } = 4;

    public Task CompartilharAsync(IReadOnlyList<string> arquivos)
    {
        Compartilhados.AddRange(arquivos);
        return Task.CompletedTask;
    }

    public Task AbrirAsync(Salvo arquivo) => Task.CompletedTask;

    public Task<bool> PermitirCameraAsync() => Task.FromResult(CameraPermitida);
}
