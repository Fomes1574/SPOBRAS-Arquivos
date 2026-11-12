using Spobras.Core.Base;
using Spobras.Documentos.Modelos;
using Spobras.Documentos.Planilhas;

namespace Spobras.Documentos.Ferramentas;

/// <summary>Le uma planilha do usuario (sem alterar) e, se pedido, grava uma copia com as propostas aplicadas.</summary>
public static class ConferirPlanilha
{
    public static IReadOnlyList<AchadoPadrao> Conferir(string arquivo, IReadOnlyList<PortariaBase> portarias, IReadOnlyCollection<string> dotacoes)
    {
        using var leitor = new LeitorXlsx(arquivo);
        return [.. leitor.NomesDasAbas().SelectMany(aba => ConferenciaPadrao.Conferir(aba, leitor.LerAba(aba), portarias, dotacoes))];
    }

    /// <summary>Copia corrigida. Celula com formula nao e tocada (volta na lista de nao corrigidas).</summary>
    public static IReadOnlyList<AchadoPadrao> GravarCopiaCorrigida(string arquivo, IReadOnlyList<AchadoPadrao> achados, string saida)
    {
        if (File.Exists(saida))
        {
            throw new IOException($"Já existe \"{Path.GetFileName(saida)}\". Nada foi sobrescrito.");
        }

        var naoCorrigidos = new List<AchadoPadrao>();
        string parcial = saida + ".spobras-parcial";
        try
        {
            File.Copy(arquivo, parcial, overwrite: true);
            File.SetAttributes(parcial, FileAttributes.Normal);
            using (var editor = new EditorXlsx(parcial))
            {
                foreach (var achado in achados.Where(a => a.Corrigivel))
                {
                    try
                    {
                        editor.Escrever(achado.Aba, achado.Celula, achado.Proposta);
                    }
                    catch (EscritaBloqueadaException)
                    {
                        naoCorrigidos.Add(achado);
                    }
                }

                editor.MarcarRecalculo(); // formulas que dependem dos textos corrigidos recalculam ao abrir
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

        return naoCorrigidos;
    }
}
