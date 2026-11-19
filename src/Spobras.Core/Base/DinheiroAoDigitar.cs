namespace Spobras.Core.Base;

/// <summary>
/// Formata um valor em reais enquanto a pessoa digita: "2500000" vira "2.500.000" na hora, para nao confundir
/// 250.000 com 2.500.000. A virgula separa os centavos (no maximo dois). Pontos digitados sao ignorados: os
/// separadores de milhar sao sempre os do app. Devolve tambem onde o cursor deve ficar.
/// </summary>
public static class DinheiroAoDigitar
{
    public static (string Texto, int Cursor) Formatar(string texto, int cursor)
    {
        texto ??= "";
        cursor = Math.Clamp(cursor, 0, texto.Length);

        // Quantos digitos (e a virgula) havia antes do cursor: o cursor fica depois do mesmo digito no texto novo.
        int antes = 0;
        bool virgulaAntes = false;
        bool achouVirgula = false;
        for (int i = 0; i < cursor; i++)
        {
            char c = texto[i];
            if (char.IsAsciiDigit(c))
            {
                antes++;
            }
            else if (c == ',' && !achouVirgula)
            {
                achouVirgula = true;
                virgulaAntes = true;
            }
        }

        int virgula = texto.IndexOf(',');
        string inteiros = new([.. (virgula < 0 ? texto : texto[..virgula]).Where(char.IsAsciiDigit)]);
        string? centavos = virgula < 0 ? null : new string([.. texto[(virgula + 1)..].Where(char.IsAsciiDigit)]);
        if (centavos is { Length: > 2 })
        {
            centavos = centavos[..2];
        }

        // Zeros a esquerda saem (mas "0,5" continua "0,5").
        string limpos = inteiros.TrimStart('0');
        int tirados = inteiros.Length - limpos.Length;
        if (limpos.Length == 0 && (inteiros.Length > 0 || centavos is not null))
        {
            limpos = "0";
            tirados = Math.Max(0, inteiros.Length - 1);
        }

        var grupos = new System.Text.StringBuilder();
        for (int i = 0; i < limpos.Length; i++)
        {
            if (i > 0 && (limpos.Length - i) % 3 == 0)
            {
                grupos.Append('.');
            }

            grupos.Append(limpos[i]);
        }

        string novo = grupos + (centavos is null ? "" : "," + centavos);

        // Posicao do cursor: depois do mesmo numero de digitos (descontando os zeros tirados), e depois da virgula se estava.
        int alvo = Math.Max(0, antes - Math.Min(antes, tirados));

        int pos = 0, vistos = 0;
        while (pos < novo.Length && vistos < alvo)
        {
            if (char.IsAsciiDigit(novo[pos]))
            {
                vistos++;
            }

            pos++;
        }

        if (virgulaAntes)
        {
            int v = novo.IndexOf(',');
            if (v >= 0 && pos <= v)
            {
                pos = Math.Min(novo.Length, v + 1 + Math.Max(0, alvo - limpos.Length)); // depois da virgula e dos centavos ja digitados
            }
        }

        return (novo, Math.Min(pos, novo.Length));
    }
}
