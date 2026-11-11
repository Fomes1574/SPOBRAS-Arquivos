namespace Spobras.Core.Ambiente;

/// <summary>
/// Tira do texto o caminho do perfil e o nome do usuario, para o relatorio poder ser
/// enviado sem expor dados do PC do trabalho.
/// </summary>
public sealed class MascaraCaminhos
{
    private readonly List<(string De, string Para)> _trocas = [];

    public MascaraCaminhos(string? pastaPerfil, string? nomeUsuario)
    {
        if (!string.IsNullOrWhiteSpace(pastaPerfil))
        {
            _trocas.Add((pastaPerfil.TrimEnd('\\', '/'), "%USERPROFILE%"));
        }

        // Nomes muito curtos trocariam pedacos de palavras comuns.
        if (!string.IsNullOrWhiteSpace(nomeUsuario) && nomeUsuario.Length >= 3)
        {
            _trocas.Add((nomeUsuario, "<usuario>"));
        }
    }

    public string Aplicar(string texto)
    {
        foreach (var (de, para) in _trocas)
        {
            texto = texto.Replace(de, para, StringComparison.OrdinalIgnoreCase);
        }

        return texto;
    }
}
