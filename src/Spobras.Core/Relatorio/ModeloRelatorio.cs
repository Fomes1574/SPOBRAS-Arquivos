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
    /// <summary>Fiscal da obra (o nome antigo da propriedade fica para os rascunhos ja gravados).</summary>
    public string Responsavel { get; init; } = "";

    /// <summary>Descricao geral do relatorio, opcional, no fim do cabecalho.</summary>
    public string Descricao { get; init; } = "";
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
    /// <summary>Capacidade desejada por pagina: 1, 2, 4, 6 ou 8 (com descricao do servico, no maximo 4).</summary>
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

    /// <summary>Com legenda e descricao do servico, o texto ocupa mais espaco: cabem no maximo 4 fotos por pagina.</summary>
    public const int MaximoComDescricao = 4;

    /// <summary>Limites de caracteres: com eles o texto sempre cabe no espaco reservado embaixo da foto.</summary>
    public const int MaximoDaLegenda = 100, MaximoDaDescricao = 300, MaximoDaDescricaoGeral = 300;

    /// <summary>Capacidades que cabem no modo escolhido.</summary>
    public static IReadOnlyList<int> CapacidadesDoModo(ModoLegenda modo) =>
        modo == ModoLegenda.DescricaoServico ? [.. Capacidades.Where(c => c <= MaximoComDescricao)] : Capacidades;

    /// <summary>A capacidade que vale de fato: com descricao, 6 ou 8 viram 4.</summary>
    public int Capacidade => Modo == ModoLegenda.DescricaoServico ? Math.Min(FotosPorPagina, MaximoComDescricao) : FotosPorPagina;
}

/// <summary>Uma foto do relatorio. Largura e altura ja com a orientacao EXIF e a rotacao manual aplicadas.</summary>
public sealed record FotoRelatorio(string Caminho, int Largura, int Altura)
{
    public string Legenda { get; init; } = "";
    public string Descricao { get; init; } = "";

    /// <summary>Rotacao manual em graus (0, 90, 180, 270), alem da orientacao EXIF.</summary>
    public int Rotacao { get; init; }
}

/// <summary>Retangulo em pontos na folha A4 (origem no canto de cima a esquerda da folha).</summary>
public readonly record struct Area(double X, double Y, double Largura, double Altura)
{
    public double Direita => X + Largura;
    public double Base => Y + Altura;
}

/// <summary>
/// Uma foto posicionada: o espaco todo (quadro e textos), o quadro cinza claro, a foto encaixada nele sem cortar
/// nem distorcer, e as linhas ja quebradas da legenda e da descricao (o PDF e a previa desenham exatamente estas).
/// </summary>
public sealed record FotoNaPagina(
    FotoRelatorio Foto,
    int Numero,
    Area Espaco,
    Area Quadro,
    Area Imagem,
    Area Legenda,
    IReadOnlyList<string> LinhasDaLegenda,
    Area Descricao,
    IReadOnlyList<string> LinhasDaDescricao,
    int Coluna,
    int Colunas,
    int Faixa)
{
    public string Titulo => Foto.Legenda.Trim();
    public string TextoDaDescricao => string.Join(" ", LinhasDaDescricao);
}

/// <summary>Bloco das assinaturas, no pe da ultima pagina (nunca sozinho numa pagina).</summary>
public sealed record BlocoAssinaturas(Area Espaco);

public sealed record PaginaRelatorio(
    int Numero,
    IReadOnlyList<FotoNaPagina> Fotos,
    BlocoAssinaturas? Assinaturas,
    bool CabecalhoReduzido,
    double TopoDasFotos)
{
    /// <summary>Altura das faixas de fotos desta pagina, de cima para baixo (pontos).</summary>
    public double BaseDasFotos => Fotos.Count == 0 ? TopoDasFotos : Fotos.Max(f => f.Espaco.Base);
}

public sealed record PlanoRelatorio(
    IReadOnlyList<PaginaRelatorio> Paginas,
    IReadOnlyList<Aviso> Avisos,
    OpcoesRelatorio Opcoes,
    DadosRelatorio Dados,
    CabecalhoMontado Cabecalho)
{
    public bool PodeGerar => Paginas.Count > 0 && !Avisos.Any(a => a.Gravidade == Gravidade.ImpedeGerar);
    public int TotalDeFotos => Paginas.Sum(p => p.Fotos.Count);

    /// <summary>Altura do quadro das fotos (igual em todo o relatorio).</summary>
    public double AlturaDoQuadro => Paginas.SelectMany(p => p.Fotos).Select(f => f.Quadro.Altura).DefaultIfEmpty(0).First();
}
