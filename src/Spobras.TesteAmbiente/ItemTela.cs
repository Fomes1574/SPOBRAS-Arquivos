using System.Windows;
using System.Windows.Media;
using Spobras.Core.Ambiente;

namespace Spobras.TesteAmbiente;

/// <summary>Uma linha da lista da tela: item aguardando, em andamento ou com resultado.</summary>
public sealed class ItemTela
{
    private static readonly Brush Cinza = Pincel("#E3E7EC");
    private static readonly Brush Azul = Pincel("#D6E6F8");
    private static readonly Brush Verde = Pincel("#D5F0DC");
    private static readonly Brush Vermelho = Pincel("#F9D7D7");
    private static readonly Brush Amarelo = Pincel("#FBEFC8");

    private ItemTela(int numero, string titulo, string estadoTexto, Brush corFundo, Brush corTexto, string detalhes)
    {
        TituloNumerado = $"{numero}. {titulo}";
        EstadoTexto = estadoTexto;
        CorFundo = corFundo;
        CorTexto = corTexto;
        DetalhesTexto = detalhes;
    }

    public string TituloNumerado { get; }
    public string EstadoTexto { get; }
    public Brush CorFundo { get; }
    public Brush CorTexto { get; }
    public string DetalhesTexto { get; }
    public Visibility VisibilidadeDetalhes => DetalhesTexto.Length == 0 ? Visibility.Collapsed : Visibility.Visible;

    public static ItemTela Aguardando(int numero, string titulo) =>
        new(numero, titulo, "AGUARDANDO", Cinza, Pincel("#44505C"), "");

    public static ItemTela Testando(int numero, string titulo) =>
        new(numero, titulo, "TESTANDO…", Azul, Pincel("#174A84"), "");

    public static ItemTela De(ResultadoVerificacao r, MascaraCaminhos mascara) => new(
        r.Numero,
        r.Titulo,
        r.EstadoTexto,
        r.Estado switch
        {
            EstadoVerificacao.Passou => Verde,
            EstadoVerificacao.Falhou => Vermelho,
            _ => Amarelo,
        },
        r.Estado switch
        {
            EstadoVerificacao.Passou => Pincel("#14602B"),
            EstadoVerificacao.Falhou => Pincel("#8F1D1D"),
            _ => Pincel("#6B5200"),
        },
        mascara.Aplicar(string.Join(Environment.NewLine, r.Detalhes)));

    private static SolidColorBrush Pincel(string cor)
    {
        var pincel = new SolidColorBrush((Color)ColorConverter.ConvertFromString(cor));
        pincel.Freeze();
        return pincel;
    }
}
