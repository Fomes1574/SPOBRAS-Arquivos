using Spobras.Core.Modelos;
using Spobras.Dados.Configuracao;
using Spobras.Documentos.Modelos;

namespace Spobras.App.Servicos;

/// <summary>
/// Cadastro dos modelos Excel do usuario. O app guarda uma copia de cada modelo na pasta de dados; o
/// arquivo original nunca e alterado e nao precisa continuar no mesmo lugar.
/// </summary>
public sealed class ServicoModelosExcel(string pastaDados, ConfigRepositorio config)
{
    private const string Chave = "modelos-excel";

    private string Pasta => Path.Combine(pastaDados, "modelos", "excel");

    public IReadOnlyList<ModeloExcel> Listar() =>
        [.. (config.Ler<List<ModeloExcel>>(Chave) ?? []).OrderBy(m => m.Nome, StringComparer.CurrentCultureIgnoreCase)];

    public string Caminho(ModeloExcel modelo) => Path.Combine(Pasta, modelo.Arquivo);

    /// <summary>
    /// Grava o cadastro. Com <paramref name="arquivoNovo"/>, guarda uma copia dele como o arquivo do modelo
    /// (cadastro novo ou troca de versao) e registra a estrutura lida.
    /// </summary>
    public ModeloExcel Salvar(ModeloExcel modelo, string? arquivoNovo)
    {
        if (arquivoNovo is not null)
        {
            var estrutura = PreenchedorModelo.Ler(arquivoNovo);
            Directory.CreateDirectory(Pasta);
            string nome = modelo.Id.ToString("N") + Path.GetExtension(arquivoNovo).ToLowerInvariant();
            string destino = Path.Combine(Pasta, nome);
            string temporario = destino + ".novo";
            File.Copy(arquivoNovo, temporario, overwrite: true);
            File.SetAttributes(temporario, FileAttributes.Normal);
            File.Move(temporario, destino, overwrite: true); // so a copia interna do app e trocada
            if (modelo.Arquivo.Length > 0 && modelo.Arquivo != nome && File.Exists(Path.Combine(Pasta, modelo.Arquivo)))
            {
                File.Delete(Path.Combine(Pasta, modelo.Arquivo));
            }

            modelo = modelo with { Arquivo = nome, Assinatura = estrutura.Assinatura };
        }

        var todos = (config.Ler<List<ModeloExcel>>(Chave) ?? []).Where(m => m.Id != modelo.Id).Append(modelo).ToList();
        config.Gravar(Chave, todos);
        return modelo;
    }

    /// <summary>Tira o modelo do cadastro e apaga so a copia interna do app. Os arquivos ja gerados ficam.</summary>
    public void Remover(ModeloExcel modelo)
    {
        config.Gravar(Chave, (config.Ler<List<ModeloExcel>>(Chave) ?? []).Where(m => m.Id != modelo.Id).ToList());
        string copia = Caminho(modelo);
        if (modelo.Arquivo.Length > 0 && File.Exists(copia))
        {
            File.Delete(copia);
        }
    }
}
