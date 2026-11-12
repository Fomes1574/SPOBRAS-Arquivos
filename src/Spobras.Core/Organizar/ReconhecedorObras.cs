using System.Text.RegularExpressions;
using Spobras.Core.Base;

namespace Spobras.Core.Organizar;

/// <summary>Uma obra da base que combina com o arquivo, e onde estava a evidencia.</summary>
public sealed record ObraReconhecida(string Contrato, string? Escola, string? Lote, string? Portaria, string Evidencia);

/// <summary>
/// Reconhece contrato e escola num texto (nome do arquivo ou conteudo) pelos identificadores exatos da
/// base, nao por semelhanca. Contrato no nome de arquivo aceita "-", "_" ou "." no lugar da barra,
/// porque o Windows nao deixa usar "/" em nomes.
/// </summary>
public sealed class ReconhecedorObras
{
    private readonly List<(ObraLida Obra, string EscolaNormal)> _obras;
    private readonly List<(string Contrato, Regex Padrao)> _contratos;

    public ReconhecedorObras(IEnumerable<ObraLida> obras)
    {
        _obras = [.. obras.Select(o => (o, Normalizacao.Busca(o.Empreendimento).Trim()))];
        _contratos = [.. _obras.Select(o => o.Obra.Contrato).Where(c => c.Trim().Length >= 5).Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(c => (c, new Regex(@"(?<![0-9a-z])" + string.Join(@"[\/\-_. ]", c.Trim().Split('/').Select(p => Regex.Escape(Normalizacao.Busca(p)))) + @"(?![0-9a-z])",
                RegexOptions.CultureInvariant)))];
    }

    public bool Vazio => _obras.Count == 0;

    /// <summary>Obras que combinam com o texto. Nenhuma, uma ou varias (varias = o usuario decide).</summary>
    public IReadOnlyList<ObraReconhecida> Reconhecer(string texto, string onde)
    {
        string normal = Normalizacao.Busca(texto);
        var contratos = _contratos.Where(c => c.Padrao.IsMatch(normal)).Select(c => c.Contrato).ToHashSet(StringComparer.OrdinalIgnoreCase);
        // Escola so pelo nome completo (com pelo menos 8 letras), para "EMEF A" nao casar com tudo.
        var escolas = _obras.Where(o => o.EscolaNormal.Length >= 8 && normal.Contains(o.EscolaNormal, StringComparison.Ordinal))
            .Select(o => o.Obra.Empreendimento).ToHashSet(StringComparer.OrdinalIgnoreCase);

        IEnumerable<ObraLida> candidatas;
        if (contratos.Count > 0)
        {
            candidatas = _obras.Select(o => o.Obra).Where(o => contratos.Contains(o.Contrato));
            var comEscola = candidatas.Where(o => escolas.Contains(o.Empreendimento)).ToList();
            if (comEscola.Count > 0)
            {
                candidatas = comEscola;
            }
        }
        else if (escolas.Count > 0)
        {
            candidatas = _obras.Select(o => o.Obra).Where(o => escolas.Contains(o.Empreendimento));
        }
        else
        {
            return [];
        }

        var lista = candidatas.ToList();
        return [.. lista.Select(o =>
        {
            // Contrato com uma escola so: a escola tambem fica certa.
            bool escolaUnica = _obras.Count(x => string.Equals(x.Obra.Contrato, o.Contrato, StringComparison.OrdinalIgnoreCase)) == 1;
            bool achouEscola = escolas.Contains(o.Empreendimento);
            bool achouContrato = contratos.Contains(o.Contrato);
            string evidencia = (achouContrato, achouEscola) switch
            {
                (true, true) => $"contrato {o.Contrato} e escola {o.Empreendimento} {onde}",
                (true, false) => $"contrato {o.Contrato} {onde}",
                _ => $"escola {o.Empreendimento} {onde}",
            };
            return new ObraReconhecida(o.Contrato, achouEscola || !achouContrato || escolaUnica ? o.Empreendimento : null, o.Lote, o.Portaria, evidencia);
        })];
    }
}
