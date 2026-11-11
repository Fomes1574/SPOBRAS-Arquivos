namespace Spobras.Core.Relatorio;

/// <summary>Dados do cabecalho. Todos opcionais: campo vazio nao aparece no documento.</summary>
public sealed record DadosRelatorio
{
    public string Unidade { get; init; } = "";
    public string Contratada { get; init; } = "";
    public string Contrato { get; init; } = "";
    public string Lote { get; init; } = "";
    public string Portaria { get; init; } = "";
    public string ProcessoSei { get; init; } = "";
    public string Objeto { get; init; } = "";
    public string Responsavel { get; init; } = "";
    public DateOnly? Data { get; init; }

    /// <summary>Data da Ordem de Servico (inicio da obra), quando conhecida.</summary>
    public DateOnly? DataOs { get; init; }
}

public enum ModoLegenda
{
    /// <summary>"Foto 01 — legenda".</summary>
    LegendaCurta,

    /// <summary>"Foto 01 — legenda" e, abaixo, a descricao do servico.</summary>
    DescricaoServico,
}

public sealed record OpcoesRelatorio
{
    /// <summary>Capacidade desejada por pagina: 1, 2, 4, 6 ou 8.</summary>
    public int FotosPorPagina { get; init; } = 6;
    public ModoLegenda Modo { get; init; } = ModoLegenda.LegendaCurta;
    public bool Assinaturas { get; init; }
    public string AssinaturaFiscal { get; init; } = "";
    public string AssinaturaContratada { get; init; } = "";

    /// <summary>Maior lado, em pixels, da copia da foto que vai para o documento.</summary>
    public int LadoMaximoFoto { get; init; } = 1600;

    /// <summary>Qualidade JPEG da copia (1 a 100).</summary>
    public int QualidadeFoto { get; init; } = 82;

    public static readonly IReadOnlyList<int> Capacidades = [1, 2, 4, 6, 8];
}

/// <summary>Uma foto do relatorio. Largura e altura ja com a orientacao EXIF e a rotacao manual aplicadas.</summary>
public sealed record FotoRelatorio(string Caminho, int Largura, int Altura)
{
    public string Legenda { get; init; } = "";
    public string Descricao { get; init; } = "";

    /// <summary>Rotacao manual em graus (0, 90, 180, 270), alem da orientacao EXIF.</summary>
    public int Rotacao { get; init; }
}

/// <summary>Retangulo em pixels dentro da pagina (origem no canto superior esquerdo da area impressa).</summary>
public readonly record struct Area(double X, double Y, double Largura, double Altura)
{
    public double Direita => X + Largura;
    public double Base => Y + Altura;
}

public sealed record FotoNaPagina(
    FotoRelatorio Foto,
    int Numero,
    Area Espaco,
    Area Imagem,
    Area Texto,
    int LinhaInicial,
    int LinhasDaFoto,
    int LinhasDoTexto,
    int Coluna,
    int Colunas,
    string Titulo,
    string Descricao);

/// <summary>Texto que nao coube junto da foto e continua no alto da pagina seguinte.</summary>
public sealed record Continuacao(string Titulo, string Texto, int LinhaInicial, int Linhas, Area Texto_);

public sealed record BlocoAssinaturas(int LinhaInicial, int Linhas, Area Espaco);

public sealed record PaginaRelatorio(
    int Numero,
    IReadOnlyList<FotoNaPagina> Fotos,
    IReadOnlyList<Continuacao> Continuacoes,
    BlocoAssinaturas? Assinaturas);

public sealed record PlanoRelatorio(
    IReadOnlyList<PaginaRelatorio> Paginas,
    IReadOnlyList<Aviso> Avisos,
    OpcoesRelatorio Opcoes,
    DadosRelatorio Dados,
    int LinhasExtrasDoCabecalho)
{
    public bool PodeGerar => Paginas.Count > 0 && !Avisos.Any(a => a.Gravidade == Gravidade.ImpedeGerar);
    public int TotalDeFotos => Paginas.Sum(p => p.Fotos.Count);
}
