using Spobras.Dados.Configuracao;

namespace Spobras.App.Servicos;

/// <summary>Configuracoes do usuario. Tudo aqui e configuracao, nunca constante no codigo.</summary>
public sealed record Preferencias
{
    /// <summary>Onde o usuario organiza, renomeia, compacta e onde os arquivos gerados vao por padrao.</summary>
    public string PastaTrabalho { get; init; } = "";

    /// <summary>Onde ficam a planilha mestra e as outras fontes. O app so le desta pasta.</summary>
    public string PastaPlanilhasBase { get; init; } = "";
    public bool ModoAutomatico { get; init; } = true;

    /// <summary>Ultimo destino usado em cada modulo ("compactar", "organizar"...).</summary>
    public Dictionary<string, string> UltimoDestino { get; init; } = [];

    /// <summary>Ate quando as novidades da pasta de trabalho ja foram vistas (para achar o que chegou com o app fechado).</summary>
    public DateTimeOffset? NovidadesVistasAte { get; init; }

    /// <summary>O aviso "o app continua na bandeja" ja foi mostrado uma vez.</summary>
    public bool AvisouBandeja { get; init; }

    /// <summary>Quanto da largura a ficha da obra ocupa na Base de dados (0,6 = 60%), como o usuario deixou.</summary>
    public double ProporcaoDaFichaBase { get; init; } = 0.6;
}

public sealed class ServicoPreferencias
{
    private const string Chave = "preferencias";
    private readonly ConfigRepositorio _config;

    public ServicoPreferencias(ConfigRepositorio config, string pastaTrabalhoPadrao)
    {
        _config = config;
        var lidas = config.Ler<Preferencias>(Chave) ?? new Preferencias();
        Atual = string.IsNullOrWhiteSpace(lidas.PastaTrabalho) ? lidas with { PastaTrabalho = pastaTrabalhoPadrao } : lidas;
    }

    public Preferencias Atual { get; private set; }

    public event Action? Mudou;

    public void Alterar(Func<Preferencias, Preferencias> mudanca)
    {
        var nova = mudanca(Atual);
        if (nova == Atual)
        {
            return;
        }

        Atual = nova;
        _config.Gravar(Chave, nova);
        Mudou?.Invoke();
    }

    public string DestinoDe(string modulo) =>
        Atual.UltimoDestino.TryGetValue(modulo, out string? pasta) && !string.IsNullOrWhiteSpace(pasta) ? pasta : Atual.PastaTrabalho;

    public void LembrarDestino(string modulo, string pasta) =>
        Alterar(p => p with { UltimoDestino = new Dictionary<string, string>(p.UltimoDestino) { [modulo] = pasta } });

    /// <summary>Caminhos que o organizador nunca toca (especificacao 3.3).</summary>
    public IReadOnlyList<string> Protegidos(string pastaDadosDoApp) =>
        [.. new[] { Atual.PastaPlanilhasBase, pastaDadosDoApp }.Where(p => !string.IsNullOrWhiteSpace(p))];
}
