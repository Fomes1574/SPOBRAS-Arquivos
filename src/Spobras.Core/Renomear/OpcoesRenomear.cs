namespace Spobras.Core.Renomear;

public enum ModoRenomear
{
    /// <summary>Relatorio 01, Relatorio 02...</summary>
    NomeNumero,

    /// <summary>Nome base + complemento digitado em cada linha, sem sequencia.</summary>
    NomeComplemento,

    /// <summary>Um nome livre por arquivo, sem numeracao.</summary>
    NomesLivres,
}

public enum OrdemRenomear
{
    /// <summary>Pelo nome atual, em ordem natural (2 antes de 10).</summary>
    NomeNatural,

    /// <summary>Pela data informada em cada item (a tela diz qual data e).</summary>
    Data,

    /// <summary>Na ordem em que os itens foram entregues (arraste manual).</summary>
    Manual,
}

public enum PosicaoData
{
    Nenhuma,
    Inicio,
    Fim,
}

public sealed record OpcoesRenomear
{
    public ModoRenomear Modo { get; init; } = ModoRenomear.NomeNumero;
    public string NomeBase { get; init; } = "";
    public string Separador { get; init; } = " ";

    /// <summary>No modo Nome + numero: false deixa so o nome (o numero e opcional).</summary>
    public bool Numerar { get; init; } = true;
    public int NumeroInicial { get; init; } = 1;
    public bool ZeroEsquerda { get; init; } = true;

    /// <summary>Continua depois do maior numero que a pasta ja tem neste mesmo padrao.</summary>
    public bool ContinuarNumeracao { get; init; }
    public OrdemRenomear Ordem { get; init; } = OrdemRenomear.NomeNatural;
    public bool PreservarExtensao { get; init; } = true;

    // Opcoes de perfil (desligadas por padrao).
    public PosicaoData PosicaoData { get; init; } = PosicaoData.Nenhuma;
    public string? SubstituirDe { get; init; }
    public string SubstituirPor { get; init; } = "";
    public bool RemoverEspacosExcedentes { get; init; }
    public bool RemoverAcentos { get; init; }
}

/// <summary>Um arquivo do lote a renomear.</summary>
public sealed record ItemRenomear(string Caminho)
{
    /// <summary>Data usada na ordem "pela data" e na opcao de colocar a data no nome.</summary>
    public DateTimeOffset? Data { get; init; }

    /// <summary>Modo Nome + complemento: o texto digitado nesta linha.</summary>
    public string? Complemento { get; init; }

    /// <summary>Modo Nomes livres: o nome desta linha (sem extensao, se a extensao for preservada).</summary>
    public string? NomeLivre { get; init; }

    /// <summary>Nome digitado direto na previa; vale em qualquer modo e passa na frente do calculado.</summary>
    public string? NomeEditado { get; init; }
}
