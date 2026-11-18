using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Spobras.Core.Relatorio;

public enum CantoCarimbo
{
    InferiorEsquerdo,
    InferiorDireito,
    SuperiorEsquerdo,
    SuperiorDireito,
}

/// <summary>
/// Como o carimbo aparece nas fotos (celular): o que entra, o canto em que fica grudado, a distancia ate esse canto e o
/// tamanho da letra. Distancias e tamanho sao fracoes do lado menor da foto, entao o carimbo fica no mesmo lugar visual
/// em foto em pe, deitada ou girada.
/// </summary>
public sealed record AjusteCarimbo
{
    public const double TamanhoMinimo = 0.03, TamanhoMaximo = 0.08, TamanhoPadrao = 0.06;

    /// <summary>Distancia minima ate a borda da foto (fracao do lado menor).</summary>
    public const double Margem = 0.02;

    public const double DistanciaPadrao = 0.03;

    public bool Ligado { get; init; }
    public bool Escola { get; init; } = true;
    public bool Endereco { get; init; } = true;
    public bool Data { get; init; } = true;
    public bool Hora { get; init; } = true;
    public CantoCarimbo Canto { get; init; } = CantoCarimbo.InferiorEsquerdo;
    public double DistanciaX { get; init; } = DistanciaPadrao;
    public double DistanciaY { get; init; } = DistanciaPadrao;
    public double Tamanho { get; init; } = TamanhoPadrao;

    [System.Text.Json.Serialization.JsonIgnore]
    public bool TemItem => Escola || Endereco || Data || Hora;

    /// <summary>Valores fora do alcance (arquivo antigo ou editado) voltam para dentro dos limites.</summary>
    public AjusteCarimbo Normalizado() => this with
    {
        Canto = Enum.IsDefined(Canto) ? Canto : CantoCarimbo.InferiorEsquerdo,
        DistanciaX = Limite(DistanciaX, Margem, 0.9, DistanciaPadrao),
        DistanciaY = Limite(DistanciaY, Margem, 0.9, DistanciaPadrao),
        Tamanho = Limite(Tamanho, TamanhoMinimo, TamanhoMaximo, TamanhoPadrao),
    };

    /// <summary>Volta posicao e tamanho para o padrao (canto inferior esquerdo, tamanho medio), sem mudar o que entra.</summary>
    public AjusteCarimbo Padrao() => this with
    {
        Canto = CantoCarimbo.InferiorEsquerdo, DistanciaX = DistanciaPadrao, DistanciaY = DistanciaPadrao, Tamanho = TamanhoPadrao,
    };

    private static double Limite(double v, double min, double max, double padrao) => double.IsFinite(v) ? Math.Clamp(v, min, max) : padrao;

    /// <summary>
    /// O carimbo foi solto em (x, y) com o tamanho (largura x altura), tudo em pixels da foto: gruda no canto mais
    /// proximo do centro dele e guarda a distancia ate esse canto.
    /// </summary>
    public AjusteCarimbo Soltar(double x, double y, double largura, double altura, int larguraDaFoto, int alturaDaFoto)
    {
        double menor = Math.Max(1, Math.Min(larguraDaFoto, alturaDaFoto));
        bool esquerda = x + largura / 2 <= larguraDaFoto / 2.0, cima = y + altura / 2 < alturaDaFoto / 2.0;
        double dx = esquerda ? x : larguraDaFoto - x - largura, dy = cima ? y : alturaDaFoto - y - altura;
        var canto = (esquerda, cima) switch
        {
            (true, false) => CantoCarimbo.InferiorEsquerdo,
            (false, false) => CantoCarimbo.InferiorDireito,
            (true, true) => CantoCarimbo.SuperiorEsquerdo,
            _ => CantoCarimbo.SuperiorDireito,
        };
        return (this with { Canto = canto, DistanciaX = dx / menor, DistanciaY = dy / menor }).Normalizado();
    }
}

/// <summary>Os textos do carimbo de uma foto (ja limpos) e o ajuste. Nulo quando nao ha nada para carimbar.</summary>
public sealed record CarimboFoto(string Escola, string Endereco, string DataHora, AjusteCarimbo Ajuste)
{
    public static CarimboFoto? Montar(AjusteCarimbo? ajuste, string? escola, string? endereco, DateTime? capturadaEm)
    {
        if (ajuste is not { Ligado: true } a)
        {
            return null;
        }

        a = a.Normalizado();
        string data = a.Data && capturadaEm is { } c1 ? c1.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) : "";
        string hora = a.Hora && capturadaEm is { } c2 ? c2.ToString("HH:mm", CultureInfo.InvariantCulture) : "";
        var c = new CarimboFoto(
            a.Escola ? TextoMedido.Limpar(escola).Trim() : "",
            a.Endereco ? TextoMedido.Limpar(endereco).Trim() : "",
            string.Join(" ", new[] { data, hora }.Where(t => t.Length > 0)),
            a);
        return c.Escola.Length + c.Endereco.Length + c.DataHora.Length == 0 ? null : c;
    }

    /// <summary>Marca curta do conteudo e da posicao: muda quando qualquer coisa do carimbo muda (nome da copia carimbada).</summary>
    public string Assinatura
    {
        get
        {
            var a = Ajuste;
            string texto = string.Join("|", Escola, Endereco, DataHora, a.Canto, F(a.DistanciaX), F(a.DistanciaY), F(a.Tamanho), "v1");
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(texto)))[..10].ToLowerInvariant();
        }
    }

    private static string F(double v) => v.ToString("0.0000", CultureInfo.InvariantCulture);
}

public sealed record LinhaCarimbo(string Texto, bool Negrito, double X, double Base);

/// <summary>
/// Onde o carimbo fica na foto, em pixels: a faixa (com cantos arredondados), o tamanho da letra e cada linha com a
/// posicao da linha de base. A gravacao (SkiaSharp), a previa do editor e a previa da pagina usam esta mesma conta,
/// entao o que aparece na tela e o que sai na foto.
/// </summary>
public sealed record GeometriaCarimbo(double X, double Y, double Largura, double Altura, double Raio, double Letra, IReadOnlyList<LinhaCarimbo> Linhas)
{
    /// <summary>Faixa no maximo com esta fracao da largura e da altura da foto.</summary>
    public const double LarguraMaxima = 0.6, AlturaMaxima = 0.35;

    /// <summary>Medidas da fonte do relatorio (Carlito): subida acima da linha de base e altura da linha, em em.</summary>
    public const double Subida = 1950.0 / 2048, AlturaDaLinha = 1.24;

    public const double Respiro = 0.45;

    /// <summary>Linhas do endereco no maximo; o que passar termina em reticencias.</summary>
    public const int LinhasDoEndereco = 2;

    public static GeometriaCarimbo? Calcular(int largura, int altura, CarimboFoto? carimbo)
    {
        if (carimbo is null || largura <= 0 || altura <= 0)
        {
            return null;
        }

        var a = carimbo.Ajuste.Normalizado();
        double menor = Math.Min(largura, altura);
        double letra = a.Tamanho * menor;
        double maxLargura = LarguraMaxima * largura, maxAltura = AlturaMaxima * altura;
        List<(string Texto, bool Negrito)> linhas = [];
        double caixaL = 0, caixaA = 0, respiro = 0;
        for (int tentativa = 0; tentativa < 6; tentativa++)
        {
            respiro = Respiro * letra;
            double util = Math.Max(1, maxLargura - 2 * respiro);
            linhas = Montar(carimbo, util, letra);
            if (linhas.Count == 0)
            {
                return null;
            }

            double maisLarga = linhas.Max(l => TextoMedido.Largura(l.Texto, letra, l.Negrito));
            caixaL = maisLarga + 2 * respiro;
            caixaA = linhas.Count * AlturaDaLinha * letra + 2 * respiro;
            double encolher = Math.Min(maxAltura / caixaA, maxLargura / caixaL);
            if (encolher >= 0.999)
            {
                break;
            }

            // muitas linhas numa foto baixa, ou data e hora largas numa foto estreita: a letra diminui ate caber
            letra *= encolher;
        }

        double margem = AjusteCarimbo.Margem * menor;
        caixaL = Math.Min(caixaL, largura - 2 * margem);
        caixaA = Math.Min(caixaA, altura - 2 * margem);
        double dx = Math.Clamp(a.DistanciaX * menor, margem, Math.Max(margem, largura - caixaL - margem));
        double dy = Math.Clamp(a.DistanciaY * menor, margem, Math.Max(margem, altura - caixaA - margem));
        bool esquerda = a.Canto is CantoCarimbo.InferiorEsquerdo or CantoCarimbo.SuperiorEsquerdo;
        bool cima = a.Canto is CantoCarimbo.SuperiorEsquerdo or CantoCarimbo.SuperiorDireito;
        double x = esquerda ? dx : largura - caixaL - dx;
        double y = cima ? dy : altura - caixaA - dy;
        var posicionadas = linhas.Select((l, i) => new LinhaCarimbo(l.Texto, l.Negrito, x + respiro, y + respiro + i * AlturaDaLinha * letra + Subida * letra)).ToList();
        return new GeometriaCarimbo(x, y, caixaL, caixaA, 0.35 * letra, letra, posicionadas);
    }

    private static List<(string, bool)> Montar(CarimboFoto c, double util, double letra)
    {
        var linhas = new List<(string, bool)>();
        if (c.Escola.Length > 0)
        {
            linhas.Add((DesenhoRelatorio.Encurtar(c.Escola, util, letra, true), true));
        }

        if (c.Endereco.Length > 0)
        {
            var quebradas = TextoMedido.Quebrar(c.Endereco, util, letra, false);
            for (int i = 0; i < Math.Min(LinhasDoEndereco, quebradas.Count); i++)
            {
                string linha = quebradas[i];
                if (i == LinhasDoEndereco - 1 && quebradas.Count > LinhasDoEndereco)
                {
                    linha = DesenhoRelatorio.Encurtar(string.Join(" ", quebradas.Skip(i)) + "…", util, letra, false);
                }

                linhas.Add((linha, false));
            }
        }

        if (c.DataHora.Length > 0)
        {
            linhas.Add((c.DataHora, false));
        }

        return linhas;
    }

    /// <summary>Letra do carimbo no papel, em pontos, para uma foto ja posicionada na pagina.</summary>
    public static double? LetraNoPapel(FotoNaPagina foto)
    {
        if (foto.Foto.Carimbo is not { } c || Calcular(foto.Foto.Largura, foto.Foto.Altura, c) is not { } g)
        {
            return null;
        }

        return g.Letra * foto.Imagem.Largura / Math.Max(1, foto.Foto.Largura);
    }

    /// <summary>Menor letra legivel do carimbo no papel (pontos).</summary>
    public const double MenorLetraNoPapel = 5.0;
}
