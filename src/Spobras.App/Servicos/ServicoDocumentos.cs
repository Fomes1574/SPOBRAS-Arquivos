using System.Security.Cryptography;
using System.Text;
using Spobras.Documentos.Imagens;

namespace Spobras.App.Servicos;

/// <summary>Exporta uma planilha para PDF pelo Excel instalado (saida fiel a impressao).</summary>
public interface IExportadorPdf
{
    /// <summary>False quando nao ha Excel neste computador.</summary>
    bool Disponivel { get; }

    void Exportar(string planilha, string pdf);
}

/// <summary>Sem Excel (testes e computadores sem Office): informa que o PDF nao pode ser gerado.</summary>
public sealed class SemExportadorPdf : IExportadorPdf
{
    public bool Disponivel => false;

    public void Exportar(string planilha, string pdf) =>
        throw new InvalidOperationException("O Excel não foi encontrado neste computador; o PDF não pôde ser gerado.");
}

/// <summary>O que o Excel calculou no cronograma gerado (para conferir contra o que foi digitado).</summary>
public sealed record ConferenciaCronograma(
    decimal? TotalParaObras, decimal? TotalGeral, decimal? SomaDosMeses, string Resumo, int Erros, string? Pdf);

/// <summary>Abre o cronograma gerado no Excel instalado, recalcula, le os totais e salva com os resultados.</summary>
public interface IVerificadorCronograma
{
    bool Disponivel { get; }

    ConferenciaCronograma Conferir(string caminho, string aba, string? pdf);
}

public sealed class SemVerificadorCronograma : IVerificadorCronograma
{
    public bool Disponivel => false;

    public ConferenciaCronograma Conferir(string caminho, string aba, string? pdf) =>
        throw new InvalidOperationException("O Excel não foi encontrado neste computador.");
}

/// <summary>
/// Cadastro central do logo e cache de miniaturas, dentro da pasta de dados do app
/// (fora de Downloads e do OneDrive). O logo nunca vai para o instalador nem para o repositorio.
/// </summary>
public sealed class ServicoModelos(string pastaDados)
{
    private string PastaModelos => Path.Combine(pastaDados, "modelos");
    private string PastaMiniaturas => Path.Combine(pastaDados, "cache", "miniaturas");

    /// <summary>Caminho do logo cadastrado, ou null se ainda nao houver.</summary>
    public string? Logo =>
        Directory.Exists(PastaModelos)
            ? Directory.EnumerateFiles(PastaModelos, "logo.*").FirstOrDefault(Fotos.Suportada)
            : null;

    /// <summary>O logo foi cadastrado, trocado ou tirado.</summary>
    public event Action? LogoMudou;

    /// <summary>Guarda uma copia do logo escolhido. Vale para os documentos gerados dali em diante.</summary>
    public void DefinirLogo(string origem)
    {
        _ = Fotos.Ler(origem); // recusa o que nao for imagem valida, com mensagem
        Directory.CreateDirectory(PastaModelos);
        RemoverLogo();
        File.Copy(origem, Path.Combine(PastaModelos, "logo" + Path.GetExtension(origem).ToLowerInvariant()));
        LogoMudou?.Invoke();
    }

    /// <summary>
    /// Usa como logo a imagem do cabecalho de um relatorio pronto (.xlsx). O relatorio so e lido.
    /// Devolve false se ele nao tiver imagem no cabecalho.
    /// </summary>
    public bool UsarLogoDeRelatorio(string xlsx)
    {
        string temporaria = Path.Combine(Path.GetTempPath(), "ArquivosSPOBRAS", "logo-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            if (Spobras.Documentos.Imagens.LogoDePlanilha.Extrair(xlsx, temporaria) is not { } imagem)
            {
                return false;
            }

            DefinirLogo(imagem);
            return true;
        }
        finally
        {
            if (Directory.Exists(temporaria))
            {
                Directory.Delete(temporaria, recursive: true); // so a copia temporaria do app
            }
        }
    }

    /// <summary>Tira o logo do cadastro (apaga so a copia interna do app, nunca o arquivo do usuario).</summary>
    public void RemoverLogo()
    {
        if (!Directory.Exists(PastaModelos))
        {
            return;
        }

        foreach (string antigo in Directory.EnumerateFiles(PastaModelos, "logo.*"))
        {
            File.Delete(antigo);
        }

        LogoMudou?.Invoke();
    }

    /// <summary>Copia do modelo de cronograma cadastrado pelo usuario, ou null.</summary>
    public string? ModeloCronograma =>
        Directory.Exists(PastaModelos) ? Directory.EnumerateFiles(PastaModelos, "cronograma.xls?").FirstOrDefault() : null;

    /// <summary>
    /// Guarda uma copia do modelo de cronograma (depois de conferir que e mesmo o modelo). O arquivo do
    /// usuario nao e alterado; a copia interna e a que o app usa para gerar.
    /// </summary>
    public Spobras.Documentos.Modelos.ModeloCronogramaLido DefinirModeloCronograma(string origem)
    {
        var lido = Spobras.Documentos.Modelos.ModeloCronograma.Analisar(origem);
        Directory.CreateDirectory(PastaModelos);
        foreach (string antigo in Directory.EnumerateFiles(PastaModelos, "cronograma.xls?"))
        {
            File.Delete(antigo);
        }

        string destino = Path.Combine(PastaModelos, "cronograma" + Path.GetExtension(origem).ToLowerInvariant());
        File.Copy(origem, destino);
        File.SetAttributes(destino, FileAttributes.Normal);
        return lido;
    }

    /// <summary>
    /// Miniatura da foto (gerada uma vez e guardada). O nome depende do caminho, do tamanho, da data e da
    /// rotacao: se a foto mudar, a miniatura e refeita.
    /// </summary>
    public string Miniatura(string foto, int rotacao = 0)
    {
        var info = new FileInfo(foto);
        string chave = $"{foto}|{info.Length}|{info.LastWriteTimeUtc.Ticks}|{rotacao}";
        string nome = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(chave)))[..32] + ".jpg";
        string caminho = Path.Combine(PastaMiniaturas, nome);
        if (!File.Exists(caminho))
        {
            Directory.CreateDirectory(PastaMiniaturas);
            string temporario = caminho + "." + Guid.NewGuid().ToString("N")[..6] + ".tmp";
            File.WriteAllBytes(temporario, Fotos.Miniatura(foto, 360, rotacao));
            try
            {
                File.Move(temporario, caminho, overwrite: false);
            }
            catch (IOException)
            {
                File.Delete(temporario); // outra thread acabou de gerar a mesma miniatura
            }
        }

        return caminho;
    }
}
