using System.Globalization;
using System.Text;

namespace Spobras.Core.Nomes;

/// <summary>Regras de nome de arquivo do Windows (valem em qualquer sistema onde o codigo rode).</summary>
public static class NomeArquivo
{
    public const int TamanhoMaximoNome = 255;
    public const int TamanhoMaximoCaminho = 259;

    private static readonly char[] Proibidos = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    private static readonly HashSet<string> Reservados = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>Motivo pelo qual o nome nao pode ser usado, ou null se for valido.</summary>
    public static string? Problema(string nome, string? pasta = null)
    {
        if (string.IsNullOrWhiteSpace(nome))
        {
            return "O nome está vazio.";
        }

        var ruins = nome.Where(c => Proibidos.Contains(c) || c < 32).Distinct().ToList();
        if (ruins.Count > 0)
        {
            string lista = string.Join(" ", ruins.Select(c => c < 32 ? "(caractere de controle)" : c.ToString()).Distinct());
            return $"O nome tem caractere que o Windows não aceita: {lista}";
        }

        if (nome.EndsWith(' ') || nome.EndsWith('.'))
        {
            return "O nome não pode terminar com espaço ou ponto.";
        }

        if (nome.StartsWith(' '))
        {
            return "O nome não pode começar com espaço.";
        }

        string semExtensao = nome.Split('.')[0].TrimEnd();
        if (Reservados.Contains(semExtensao))
        {
            return $"\"{semExtensao}\" é um nome reservado do Windows.";
        }

        if (nome.Length > TamanhoMaximoNome)
        {
            return $"O nome tem {nome.Length} caracteres; o máximo é {TamanhoMaximoNome}.";
        }

        if (pasta is not null && pasta.Length + 1 + nome.Length > TamanhoMaximoCaminho)
        {
            return $"O caminho completo passa de {TamanhoMaximoCaminho} caracteres; encurte o nome ou use uma pasta mais curta.";
        }

        return null;
    }

    /// <summary>Sugestao de nome valido: troca caracteres proibidos por "-" e apara as pontas.</summary>
    public static string Corrigir(string nome)
    {
        var sb = new StringBuilder(nome.Length);
        foreach (char c in nome)
        {
            sb.Append(Proibidos.Contains(c) || c < 32 ? '-' : c);
        }

        string corrigido = sb.ToString().Trim().TrimEnd('.', ' ');
        string semExtensao = corrigido.Split('.')[0].TrimEnd();
        if (Reservados.Contains(semExtensao))
        {
            corrigido = "_" + corrigido;
        }

        return corrigido;
    }

    public static string RemoverAcentos(string texto)
    {
        var sb = new StringBuilder(texto.Length);
        foreach (char c in texto.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
            }
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    public static string RemoverEspacosExcedentes(string texto) =>
        string.Join(' ', texto.Split(' ', StringSplitOptions.RemoveEmptyEntries));

    /// <summary>
    /// Primeiro nome livre no formato "nome (2).ext", "nome (3).ext"... O nome original volta
    /// sem mudanca se nao estiver ocupado.
    /// </summary>
    public static string Livre(string nome, Func<string, bool> ocupado)
    {
        if (!ocupado(nome))
        {
            return nome;
        }

        string extensao = Path.GetExtension(nome);
        string semExtensao = nome[..^extensao.Length];
        for (int n = 2; ; n++)
        {
            string candidato = $"{semExtensao} ({n}){extensao}";
            if (!ocupado(candidato))
            {
                return candidato;
            }
        }
    }
}
