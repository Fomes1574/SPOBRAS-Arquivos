using System.Security.Cryptography;
using System.Text;
using Spobras.Core.Modelos;
using Spobras.Documentos.Planilhas;

namespace Spobras.Documentos.Modelos;

/// <summary>Abas do arquivo e um resumo da estrutura (nomes das abas e quantidade de formulas de cada uma).</summary>
public sealed record EstruturaModelo(IReadOnlyList<string> Abas, string Assinatura);

/// <summary>
/// Preenche uma copia de um modelo Excel cadastrado. So escreve nas celulas mapeadas, nunca sobre formula,
/// nunca no arquivo de origem e nunca por cima de um arquivo que ja existe. Macros nao sao executadas.
/// </summary>
public static class PreenchedorModelo
{
    private const string SufixoParcial = ".spobras-parcial";

    /// <summary>Le a estrutura sem alterar o arquivo.</summary>
    public static EstruturaModelo Ler(string caminho)
    {
        using var editor = new EditorXlsx(caminho, somenteLeitura: true);
        var abas = editor.Abas;
        string resumo = string.Join("|", abas.Select(a => $"{a}:{editor.ContarFormulas(a)}"));
        return new EstruturaModelo(abas, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(resumo)))[..16]);
    }

    /// <summary>Problemas do mapeamento neste arquivo: aba que nao existe, celula com formula ou mesclada.</summary>
    public static IReadOnlyList<string> Conferir(string caminho, IEnumerable<CampoModelo> campos)
    {
        var problemas = new List<string>();
        using var editor = new EditorXlsx(caminho, somenteLeitura: true);
        var abas = editor.Abas;
        foreach (var campo in campos)
        {
            if (ValidadorModelo.Celula(campo.Celula) is not { } celula)
            {
                continue; // ja apontado pela conferencia do cadastro
            }

            if (!abas.Contains(campo.Aba, StringComparer.OrdinalIgnoreCase))
            {
                problemas.Add($"“{campo.Nome}”: a aba \"{campo.Aba}\" não existe neste arquivo.");
            }
            else if (editor.TemFormula(campo.Aba, celula))
            {
                problemas.Add($"“{campo.Nome}”: a célula {celula} da aba \"{campo.Aba}\" tem fórmula. O app não escreve sobre fórmulas; escolha a célula de entrada.");
            }
            else if (editor.PrimeiraDaMesclagem(campo.Aba, celula) is { } primeira)
            {
                problemas.Add($"“{campo.Nome}”: a célula {celula} faz parte de células mescladas. Use {primeira}, que é a primeira do grupo.");
            }
        }

        return problemas;
    }

    /// <summary>Copia o arquivo de base e escreve os valores (null limpa a celula).</summary>
    public static void Preencher(string arquivoBase, IReadOnlyList<(CampoModelo Campo, object? Valor)> valores, string saida)
    {
        if (File.Exists(saida))
        {
            throw new IOException($"Já existe \"{Path.GetFileName(saida)}\" na pasta de destino. Nada foi sobrescrito.");
        }

        string? pasta = Path.GetDirectoryName(saida);
        if (!string.IsNullOrEmpty(pasta))
        {
            Directory.CreateDirectory(pasta);
        }

        string parcial = saida + SufixoParcial;
        try
        {
            File.Copy(arquivoBase, parcial, overwrite: true);
            File.SetAttributes(parcial, FileAttributes.Normal);
            using (var editor = new EditorXlsx(parcial))
            {
                foreach (var (campo, valor) in valores)
                {
                    string celula = ValidadorModelo.Celula(campo.Celula)
                                    ?? throw new PlanilhaInvalidaException($"“{campo.Nome}”: \"{campo.Celula}\" não é uma célula.");
                    if (editor.PrimeiraDaMesclagem(campo.Aba, celula) is { } primeira)
                    {
                        throw new EscritaBloqueadaException($"“{campo.Nome}”: a célula {celula} faz parte de células mescladas. Use {primeira} no cadastro do modelo.");
                    }

                    editor.Escrever(campo.Aba, celula, valor);
                }

                editor.MarcarRecalculo();
                editor.Salvar();
            }

            File.Move(parcial, saida, overwrite: false);
        }
        finally
        {
            if (File.Exists(parcial))
            {
                File.Delete(parcial);
            }
        }
    }
}
