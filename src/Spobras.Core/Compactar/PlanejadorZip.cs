using Spobras.Core.Abstracoes;
using Spobras.Core.Nomes;

namespace Spobras.Core.Compactar;

public sealed record EntradaZip(string CaminhoOrigem, string NomeNoZip, long Tamanho);

public sealed record ZipPlanejado(string CaminhoZip, IReadOnlyList<EntradaZip> Entradas)
{
    public string NomeZip => Path.GetFileName(CaminhoZip);
    public long TamanhoOriginal => Entradas.Sum(e => e.Tamanho);

    /// <summary>Tamanho maximo que o zip pode ter (como se nada comprimisse), com a estrutura do zip.</summary>
    public long TamanhoMaximoEstimado => PlanejadorZip.FimDoZip + Entradas.Sum(PlanejadorZip.CustoMaximo);
}

public sealed record PlanoZip(
    IReadOnlyList<ZipPlanejado> Zips,
    IReadOnlyList<Aviso> Avisos,
    IReadOnlyList<EntradaZip> AcimaDoLimite)
{
    public bool PodeExecutar => Zips.Count > 0 && !Avisos.Any(a => a.Gravidade == Gravidade.ImpedeGerar);
}

/// <summary>
/// Plano de compactacao (especificacao 4.3): um zip por pasta, tudo num zip, ou zips independentes
/// dentro de um limite. So planeja; quem grava o zip confere o tamanho final de verdade.
/// </summary>
public sealed class PlanejadorZip(IArquivos arquivos)
{
    internal const long FimDoZip = 22 + 76; // registro final + margem para zip64

    /// <summary>
    /// Copia que entra no zip no lugar do original (por exemplo, a foto reduzida), com o tamanho dela.
    /// O nome dentro do zip continua o do original. Null = usa o proprio arquivo.
    /// </summary>
    public Func<string, (string Caminho, long Tamanho)?>? Substituir { get; set; }

    private EntradaZip Entrada(string caminho, string nome, long tamanho) =>
        Substituir?.Invoke(caminho) is { } copia ? new EntradaZip(copia.Caminho, nome, copia.Tamanho) : new EntradaZip(caminho, nome, tamanho);

    public PlanoZip UmPorPasta(IReadOnlyList<string> pastas, string pastaDestino)
    {
        var avisos = new List<Aviso>();
        var zips = new List<ZipPlanejado>();
        var tomados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string pasta in pastas)
        {
            string limpa = pasta.TrimEnd('\\', '/');
            var entradas = EntradasDaPasta(limpa, prefixo: "");
            if (entradas.Count == 0)
            {
                avisos.Add(Aviso.Conferir($"A pasta \"{Path.GetFileName(limpa)}\" está vazia e ficou de fora."));
                continue;
            }

            zips.Add(new ZipPlanejado(CaminhoLivre(pastaDestino, Path.GetFileName(limpa) + ".zip", tomados, avisos), entradas));
        }

        if (zips.Count == 0)
        {
            avisos.Add(Aviso.Impede("Escolha pelo menos uma pasta com arquivos."));
        }

        return new PlanoZip(zips, avisos, []);
    }

    public PlanoZip Unico(IReadOnlyList<string> arquivosEPastas, string nomeZip, string pastaDestino)
    {
        var avisos = new List<Aviso>();
        var entradas = Entradas(arquivosEPastas);
        if (entradas.Count == 0)
        {
            avisos.Add(Aviso.Impede("Escolha pelo menos um arquivo."));
            return new PlanoZip([], avisos, []);
        }

        string? problema = NomeArquivo.Problema(ComExtensaoZip(nomeZip), pastaDestino);
        if (problema is not null)
        {
            avisos.Add(Aviso.Impede("Nome do zip: " + problema));
            return new PlanoZip([], avisos, []);
        }

        string caminho = CaminhoLivre(pastaDestino, ComExtensaoZip(nomeZip), new HashSet<string>(StringComparer.OrdinalIgnoreCase), avisos);
        return new PlanoZip([new ZipPlanejado(caminho, entradas)], avisos, []);
    }

    /// <summary>
    /// Distribui os arquivos, na ordem recebida, em zips independentes que cabem no limite.
    /// A conta usa o pior caso (arquivo que nao comprime nada), entao o zip final nunca passa do limite.
    /// </summary>
    public PlanoZip PorLimite(IReadOnlyList<string> arquivosEPastas, string nomeBase, long limiteBytes, string pastaDestino)
    {
        var avisos = new List<Aviso>();
        if (limiteBytes <= FimDoZip)
        {
            avisos.Add(Aviso.Impede("Informe o tamanho máximo de cada parte."));
            return new PlanoZip([], avisos, []);
        }

        var entradas = Entradas(arquivosEPastas);
        if (entradas.Count == 0)
        {
            avisos.Add(Aviso.Impede("Escolha pelo menos um arquivo."));
            return new PlanoZip([], avisos, []);
        }

        string? problema = NomeArquivo.Problema(nomeBase + " - parte 01.zip", pastaDestino);
        if (problema is not null)
        {
            avisos.Add(Aviso.Impede("Nome do zip: " + problema));
            return new PlanoZip([], avisos, []);
        }

        var grandes = entradas.Where(e => FimDoZip + CustoMaximo(e) > limiteBytes).ToList();
        if (grandes.Count > 0)
        {
            // Nao gera parte acima do limite como se a operacao tivesse sido cumprida.
            foreach (var g in grandes)
            {
                avisos.Add(Aviso.Impede(
                    $"\"{g.NomeNoZip}\" tem {Tamanho.Formatar(g.Tamanho)} e não cabe sozinho no limite de {Tamanho.Formatar(limiteBytes)}. " +
                    "Aumente o limite, reduza o arquivo ou tire-o da lista."));
            }

            return new PlanoZip([], avisos, grandes);
        }

        var partes = new List<List<EntradaZip>> { new() };
        long usado = FimDoZip;
        foreach (var entrada in entradas)
        {
            long custo = CustoMaximo(entrada);
            if (usado + custo > limiteBytes && partes[^1].Count > 0)
            {
                partes.Add([]);
                usado = FimDoZip;
            }

            partes[^1].Add(entrada);
            usado += custo;
        }

        int largura = Math.Max(2, partes.Count.ToString(System.Globalization.CultureInfo.InvariantCulture).Length);
        var tomados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var zips = partes
            .Select((p, i) => new ZipPlanejado(
                CaminhoLivre(pastaDestino,
                    partes.Count == 1 ? nomeBase + ".zip" : $"{nomeBase} - parte {(i + 1).ToString().PadLeft(largura, '0')}.zip",
                    tomados, avisos),
                p))
            .ToList();

        if (zips.Count > 1)
        {
            avisos.Add(Aviso.Info($"{zips.Count} zips independentes de até {Tamanho.Formatar(limiteBytes)}; cada um abre sozinho."));
        }

        return new PlanoZip(zips, avisos, []);
    }

    /// <summary>Quanto uma entrada pode ocupar no zip no pior caso: dados sem compressao + cabecalhos.</summary>
    internal static long CustoMaximo(EntradaZip e)
    {
        int nome = System.Text.Encoding.UTF8.GetByteCount(e.NomeNoZip);
        long expansao = (e.Tamanho / 16_000 + 1) * 5 + 16; // blocos "armazenados" do deflate
        return e.Tamanho + expansao + (30 + nome + 36) + (46 + nome + 36);
    }

    private List<EntradaZip> Entradas(IReadOnlyList<string> arquivosEPastas)
    {
        var entradas = new List<EntradaZip>();
        var nomes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string caminho in arquivosEPastas)
        {
            if (arquivos.PastaExiste(caminho))
            {
                string limpa = caminho.TrimEnd('\\', '/');
                entradas.AddRange(EntradasDaPasta(limpa, Path.GetFileName(limpa) + "/", nomes));
            }
            else if (arquivos.Info(caminho) is { } info)
            {
                // Dois arquivos de pastas diferentes com o mesmo nome nao se sobrepoem dentro do zip.
                string nome = NomeArquivo.Livre(Path.GetFileName(caminho), nomes.Contains);
                nomes.Add(nome);
                entradas.Add(Entrada(caminho, nome, info.Tamanho));
            }
        }

        return entradas;
    }

    private List<EntradaZip> EntradasDaPasta(string pasta, string prefixo, HashSet<string>? nomes = null)
    {
        var entradas = new List<EntradaZip>();
        foreach (string caminho in arquivos.ListarArquivosRecursivo(pasta).OrderBy(c => c, OrdemNatural.Instancia))
        {
            if (Organizar.TipoArquivo.Temporario(caminho) || arquivos.Info(caminho) is not { } info)
            {
                continue;
            }

            string relativo = prefixo + Path.GetRelativePath(pasta, caminho).Replace('\\', '/');
            if (nomes is not null && !nomes.Add(relativo))
            {
                continue;
            }

            entradas.Add(Entrada(caminho, relativo, info.Tamanho));
        }

        return entradas;
    }

    private string CaminhoLivre(string pastaDestino, string nome, HashSet<string> tomados, List<Aviso> avisos)
    {
        string livre = NomeArquivo.Livre(nome, n => tomados.Contains(n) || arquivos.ArquivoExiste(Path.Combine(pastaDestino, n)));
        tomados.Add(livre);
        if (livre != nome)
        {
            avisos.Add(Aviso.Conferir($"Já existe \"{nome}\" na pasta de destino; o novo será \"{livre}\"."));
        }

        return Path.Combine(pastaDestino, livre);
    }

    private static string ComExtensaoZip(string nome) =>
        nome.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? nome : nome + ".zip";
}
