using System.Security.Cryptography;
using System.Text;
using Spobras.Core.Cronograma;
using Spobras.Core.Nomes;
using Spobras.Documentos.Planilhas;

namespace Spobras.Documentos.Modelos;

public sealed record ModeloCronogramaLido(
    IReadOnlyList<string> AbasDeCronograma,
    string AbaPrincipal,
    IReadOnlyList<string> Dotacoes,
    string Assinatura);

public sealed record CronogramaGerado(string Caminho, string Aba, int Obras);

/// <summary>
/// O modelo oficial do cronograma: confere a estrutura, le as dotacoes da aba Dotacoes e preenche uma copia
/// escrevendo so nas celulas de entrada (linhas 3 a 17, colunas B a G, J e K). Toda a matematica fica com
/// as formulas do proprio modelo.
/// </summary>
public static class ModeloCronograma
{
    public const int PrimeiraLinha = 3;
    private const string SufixoParcial = ".spobras-parcial";

    // Coluna do modelo -> o que entra nela.
    private static readonly string[] ColunasDeEntrada = ["B", "C", "D", "E", "F", "G", "J", "K"];

    /// <summary>Confere se o arquivo e mesmo o modelo do cronograma e descobre abas e dotacoes.</summary>
    public static ModeloCronogramaLido Analisar(string caminho)
    {
        using var leitor = new LeitorXlsx(caminho);
        var abas = new List<string>();
        foreach (string aba in leitor.NomesDasAbas())
        {
            var grade = leitor.LerAba(aba);
            // Cabecalhos na linha 2 (indice 1), colunas B a K (indices 1 a 10), e TOTAL na linha 18.
            if (grade.Count >= 18
                && Igual(grade[1], 1, "Portaria") && Igual(grade[1], 4, "Nome da Obra") && Comeca(grade[1], 6, "Valores para Obras")
                && Igual(grade[1], 9, "Início") && Igual(grade[1], 10, "Fim") && Igual(grade[17], 1, "TOTAL"))
            {
                abas.Add(aba);
            }
        }

        if (abas.Count == 0)
        {
            throw new PlanilhaInvalidaException(
                "Este arquivo não tem a estrutura do modelo de cronograma (cabeçalhos Portaria, Nome da Obra, Valores para Obras, Início e Fim na linha 2 e TOTAL na linha 18).");
        }

        var dotacoes = new List<string>();
        if (leitor.NomesDasAbas().Contains("Dotacoes", StringComparer.OrdinalIgnoreCase))
        {
            var grade = leitor.LerAba("Dotacoes");
            int coluna = grade.Count > 0 ? Array.FindIndex(grade[0], v => v is string s && Core.Base.Normalizacao.Busca(s).Trim() == "dotacao resumida") : -1;
            if (coluna >= 0)
            {
                dotacoes.AddRange(grade.Skip(1).Select(l => Texto(l[coluna])).Where(t => t is not null).Distinct(StringComparer.OrdinalIgnoreCase)!);
            }
        }

        // Aba principal: a que a aba Parametros usa no Resumo pedido; sem isso, a primeira.
        string principal = abas[0];
        string copia = Path.Combine(Path.GetTempPath(), "spobras-modelo-" + Guid.NewGuid().ToString("N")[..8] + ".xlsx");
        File.Copy(caminho, copia);
        try
        {
            using var editor = new EditorXlsx(copia);
            if (editor.Abas.Contains("Parametros", StringComparer.OrdinalIgnoreCase))
            {
                string formulas = string.Concat(Enumerable.Range(13, 8).Select(l => editor.Formula("Parametros", "B" + l)));
                principal = abas.FirstOrDefault(a => formulas.Contains($"'{a}'!", StringComparison.OrdinalIgnoreCase)) ?? principal;
            }

            // As celulas de entrada nao podem ter formula; as de calculo precisam ter.
            var problemas = new List<string>();
            foreach (string coluna in ColunasDeEntrada)
            {
                if (editor.TemFormula(principal, coluna + PrimeiraLinha))
                {
                    problemas.Add($"{coluna}{PrimeiraLinha} tem fórmula (deveria ser entrada)");
                }
            }

            foreach (string celula in new[] { "H3", "I3", "L2", "G18", "I18" })
            {
                if (!editor.TemFormula(principal, celula))
                {
                    problemas.Add($"{celula} não tem fórmula (deveria ser cálculo)");
                }
            }

            if (problemas.Count > 0)
            {
                throw new PlanilhaInvalidaException(
                    $"A aba \"{principal}\" não está como o modelo de cronograma esperado: {string.Join("; ", problemas)}. O modelo pode ter mudado; nada foi gerado.");
            }

            // Assinatura das formulas: se o modelo mudar, o app percebe e pede conferencia.
            var texto = new StringBuilder();
            foreach (string celula in new[] { "B1", "L2", "M2", "H3", "I3", "L3", "AF3", "AG3", "G18", "H18", "I18", "L18" })
            {
                texto.Append(celula).Append('=').Append(editor.Formula(principal, celula)).Append('|');
            }

            return new ModeloCronogramaLido(abas, principal, dotacoes, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(texto.ToString())))[..16]);
        }
        finally
        {
            File.Delete(copia); // copia de trabalho criada aqui mesmo
        }
    }

    /// <summary>
    /// Gera um cronograma novo a partir do modelo: copia, preenche as entradas de ate 15 obras e limpa as
    /// linhas que sobrarem. O modelo original nao e alterado e nada e sobrescrito.
    /// </summary>
    /// <param name="remuneracao">Percentual de remuneracao (0,10 = 10%, o do modelo).</param>
    public static CronogramaGerado Gerar(string modelo, string aba, IReadOnlyList<ObraCronograma> obras, string saida, decimal remuneracao = PadraoRemuneracao)
    {
        if (obras.Count > ValidadorCronograma.ObrasPorAba)
        {
            throw new ArgumentException($"O modelo comporta {ValidadorCronograma.ObrasPorAba} obras por cronograma; divida a lista antes de gerar.");
        }

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
            File.Copy(modelo, parcial, overwrite: true);
            File.SetAttributes(parcial, FileAttributes.Normal);
            using (var editor = new EditorXlsx(parcial))
            {
                for (int i = 0; i < ValidadorCronograma.ObrasPorAba; i++)
                {
                    int linha = PrimeiraLinha + i;
                    var o = i < obras.Count ? obras[i] : null;
                    editor.Escrever(aba, "B" + linha, NumeroOuTexto(o?.Portaria));
                    editor.Escrever(aba, "C" + linha, NumeroOuTexto(o?.Lote));
                    editor.Escrever(aba, "D" + linha, NumeroOuTexto(o?.Dotacao));
                    editor.Escrever(aba, "E" + linha, TextoOuNada(o?.Nome));
                    editor.Escrever(aba, "F" + linha, TextoOuNada(o?.Escopo));
                    editor.Escrever(aba, "G" + linha, o?.Valor);
                    editor.Escrever(aba, "J" + linha, o?.Inicio);
                    editor.Escrever(aba, "K" + linha, o?.Fim);
                }

                if (remuneracao != PadraoRemuneracao)
                {
                    AjustarRemuneracao(editor, aba, remuneracao);
                }

                editor.MarcarRecalculo();
                editor.Salvar();
            }

            File.Move(parcial, saida, overwrite: false);
            return new CronogramaGerado(saida, aba, obras.Count);
        }
        catch
        {
            File.Delete(parcial);
            throw;
        }
    }

    public const decimal PadraoRemuneracao = 0.10m;

    /// <summary>
    /// O modelo calcula a remuneracao com 10% escrito na formula (H = G*0,1). Para outro percentual, a
    /// formula de cada linha e trocada na copia, mas so se for exatamente a esperada; se o modelo mudou,
    /// nada e trocado e a geracao para com a explicacao.
    /// </summary>
    private static void AjustarRemuneracao(EditorXlsx editor, string aba, decimal percentual)
    {
        string fator = percentual.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
        for (int i = 0; i < ValidadorCronograma.ObrasPorAba; i++)
        {
            int l = PrimeiraLinha + i;
            string esperada = $"IF($G{l}=\"\",\"\",$G{l}*0.1)";
            if (editor.Formula(aba, "H" + l) != esperada)
            {
                throw new PlanilhaInvalidaException(
                    $"A fórmula de remuneração da linha {l} não é a esperada ({esperada}). O percentual não foi aplicado; gere com 10% ou confira o modelo.");
            }
        }

        for (int i = 0; i < ValidadorCronograma.ObrasPorAba; i++)
        {
            int l = PrimeiraLinha + i;
            editor.TrocarFormula(aba, "H" + l, $"IF($G{l}=\"\",\"\",$G{l}*{fator})");
        }

        // O percentual da aba Parametros acompanha, para quem abrir o arquivo ver o mesmo valor.
        if (editor.Abas.Contains("Parametros") && !editor.TemFormula("Parametros", "B2"))
        {
            editor.Escrever("Parametros", "B2", percentual);
        }
    }

    /// <summary>"Cronograma - Portaria 3 - Lote 12" ou "Cronograma - Nome da obra", conforme o que houver.</summary>
    public static string NomeSugerido(IReadOnlyList<ObraCronograma> obras, int parte = 0, int partes = 1)
    {
        var validas = obras.Where(o => !o.Vazia).ToList();
        string meio = partes == 1 && validas.Count == 1 && validas[0].Nome.Length > 0
            ? validas[0].Nome
            : string.Join(" - ", new[]
            {
                validas.Select(o => o.Portaria).FirstOrDefault(p => p.Length > 0) is { } p ? (p.StartsWith("Portaria", StringComparison.OrdinalIgnoreCase) ? p : "Portaria " + p) : null,
                validas.Select(o => o.Lote).FirstOrDefault(l => l.Length > 0) is { } l ? (l.StartsWith("Lote", StringComparison.OrdinalIgnoreCase) ? l : "Lote " + l) : null,
            }.Where(t => t is not null));
        string nome = "Cronograma" + (meio.Length > 0 ? " - " + meio : "") + (partes > 1 ? $" - parte {parte + 1} de {partes}" : "");
        return NomeArquivo.Corrigir(nome);
    }

    private static object? TextoOuNada(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();

    /// <summary>
    /// Igual a digitar no Excel: so algarismos vira numero (a aba Dotacoes guarda a dotacao resumida como
    /// numero e o resumo do modelo a procura assim); o resto fica como texto.
    /// </summary>
    private static object? NumeroOuTexto(string? texto)
    {
        if (TextoOuNada(texto) is not string t)
        {
            return null;
        }

        return t.Length <= 15 && t.All(char.IsAsciiDigit) && !(t.Length > 1 && t[0] == '0')
            ? decimal.Parse(t, System.Globalization.CultureInfo.InvariantCulture)
            : t;
    }

    private static string? Texto(object? valor) => valor switch
    {
        null => null,
        string s => string.IsNullOrWhiteSpace(s) ? null : s.Trim(),
        decimal d => d.ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => valor.ToString(),
    };

    private static bool Igual(object?[] linha, int coluna, string texto) =>
        coluna < linha.Length && linha[coluna] is string s && string.Equals(s.Trim(), texto, StringComparison.OrdinalIgnoreCase);

    private static bool Comeca(object?[] linha, int coluna, string texto) =>
        coluna < linha.Length && linha[coluna] is string s && s.Trim().StartsWith(texto, StringComparison.OrdinalIgnoreCase);
}
