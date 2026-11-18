using System.Collections.Concurrent;
using Spobras.Core.Relatorio;
using Spobras.Documentos.Imagens;

namespace Spobras.Documentos.Relatorio;

/// <summary>
/// Fotos do relatorio ja reduzidas, preparadas uma vez so (em paralelo) e usadas pelo Excel e pelo PDF.
/// Com <paramref name="pastaCache"/>, as preparadas ficam em arquivo (o celular as prepara em segundo plano enquanto
/// o usuario mexe no rascunho, e gerar fica so montar o arquivo); sem pasta, ficam na memoria durante a geracao.
/// </summary>
public sealed class PreparoDeFotos(string? pastaCache = null)
{
    private readonly ConcurrentDictionary<string, byte[]> _memoria = new();

    /// <summary>Quantas fotos sao preparadas ao mesmo tempo (memoria baixa no celular: no maximo 4).</summary>
    public static int Paralelo => Math.Clamp(Environment.ProcessorCount - 1, 1, Math.Clamp(Limite, 1, 4));

    /// <summary>Quantas fotos no maximo ao mesmo tempo (o celular com pouca memoria baixa para 1 ou 2).</summary>
    public static int Limite { get; set; } = 4;

    /// <summary>
    /// Nome do arquivo preparado: muda se a foto, o giro, o tamanho, a qualidade ou o carimbo mudarem
    /// ("foto-90-1600-82.jpg"; com carimbo, "foto-90-1600-82c0a1b2c3d4.jpg").
    /// </summary>
    public static string NomeNoCache(string caminho, int rotacao, int lado, int qualidade, CarimboFoto? carimbo = null) =>
        $"{Path.GetFileNameWithoutExtension(caminho)}-{rotacao}-{lado}-{qualidade}{(carimbo is null ? "" : "c" + carimbo.Assinatura)}.jpg";

    private static string Chave(FotoRelatorio foto, int lado, int qualidade) => $"{foto.Caminho}|{foto.Rotacao}|{lado}|{qualidade}|{foto.Carimbo?.Assinatura}";

    public byte[] Obter(FotoRelatorio foto, int lado, int qualidade)
    {
        if (pastaCache is not null)
        {
            string arquivo = Path.Combine(pastaCache, NomeNoCache(foto.Caminho, foto.Rotacao, lado, qualidade, foto.Carimbo));
            try
            {
                if (File.Exists(arquivo))
                {
                    return File.ReadAllBytes(arquivo);
                }
            }
            catch (IOException)
            {
                // sendo gravada agora pelo segundo plano: prepara de novo aqui
            }

            byte[] pronta = Fotos.Preparar(foto.Caminho, lado, qualidade, foto.Rotacao, foto.Carimbo);
            Gravar(arquivo, pronta);
            return pronta;
        }

        return _memoria.GetOrAdd(Chave(foto, lado, qualidade), _ => Fotos.Preparar(foto.Caminho, lado, qualidade, foto.Rotacao, foto.Carimbo));
    }

    /// <summary>Publica a primeira copia completa da foto preparada (o cache e imutavel).</summary>
    public static void Gravar(string arquivo, byte[] dados)
    {
        if (File.Exists(arquivo)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(arquivo)!);
        string temp = arquivo + "." + Guid.NewGuid().ToString("N")[..6] + ".tmp";
        try
        {
            File.WriteAllBytes(temp, dados);
            File.Move(temp, arquivo);
        }
        catch (IOException)
        {
            // outra preparacao da mesma foto ganhou a corrida: tanto faz qual fica
        }
        catch (UnauthorizedAccessException) when (File.Exists(arquivo))
        {
            // No Windows o leitor pode estar com a primeira copia aberta, sem compartilhar exclusao.
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    /// <summary>Prepara todas as fotos do plano em paralelo, avisando cada foto pronta (1, 2, 3...).</summary>
    public void PrepararTodas(PlanoRelatorio plano, IProgress<int>? prontas = null, CancellationToken cancelamento = default)
    {
        var fotos = plano.Paginas.SelectMany(p => p.Fotos).Select(f => f.Foto).ToList();
        int feitas = 0;
        var ordem = new object();
        try
        {
            Parallel.ForEach(fotos, new ParallelOptions { MaxDegreeOfParallelism = Paralelo, CancellationToken = cancelamento }, foto =>
            {
                cancelamento.ThrowIfCancellationRequested();
                Obter(foto, plano.Opcoes.LadoMaximoFoto, plano.Opcoes.QualidadeFoto);
                lock (ordem) // avisos sempre em ordem crescente, mesmo com varias fotos ficando prontas juntas
                {
                    prontas?.Report(++feitas);
                }
            });
        }
        catch (AggregateException ex) when (ex.InnerExceptions.Count > 0)
        {
            // a mesma excecao que a geracao foto a foto dava (foto que sumiu, foto estragada, cancelado)
            var primeira = ex.InnerExceptions.FirstOrDefault(e => e is not OperationCanceledException) ?? ex.InnerExceptions[0];
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(primeira).Throw();
        }
    }
}
