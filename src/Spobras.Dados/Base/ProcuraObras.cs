using Spobras.Core.Base;

namespace Spobras.Dados.Base;

/// <summary>
/// Buscas por contrato, escola e lote digitados, usadas pelo relatorio e pelo cronograma no computador e pelo
/// app do celular. Ficam aqui para os dois darem o mesmo resultado.
/// </summary>
public static class ProcuraObras
{
    public sealed record Sugestao(string Valor, string Detalhe);

    public static IReadOnlyList<Sugestao> SugestoesDeEscola(BaseRepositorio base_, string texto, int max = 8)
    {
        string alvo = Normalizacao.Busca(texto.Trim());
        if (alvo.Length < 2) return [];
        var escolas = base_.Opcoes(new FiltroObras()).Escolas
            .Where(e => Normalizacao.Busca(e).Contains(alvo, StringComparison.Ordinal))
            .OrderByDescending(e => Normalizacao.Busca(e).StartsWith(alvo, StringComparison.Ordinal))
            .ThenBy(e => e, StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("pt-BR"), true)).Take(max).ToArray();
        if (escolas.Length == 0) return [];
        var obras = base_.Buscar(new FiltroObras { Escola = escolas }, int.MaxValue).Obras;
        return [.. escolas.Select(e =>
        {
            var contratos = obras.Where(o => MesmaEscola(e, o.Empreendimento)).Select(o => o.Contrato).Distinct().ToArray();
            return new Sugestao(e, contratos.Length == 1 ? contratos[0] : $"{contratos.Length} contratos");
        })];
    }

    public static IReadOnlyList<Sugestao> SugestoesDeContrato(BaseRepositorio base_, string texto, int max = 8)
    {
        string alvo = Normalizacao.Busca(texto.Trim().Replace('-', '/'));
        if (alvo.Length < 2) return [];
        static int Ano(string codigo) => int.TryParse(
            System.Text.RegularExpressions.Regex.Match(codigo, @"(?<![0-9])[0-9]{4}(?![0-9])",
                System.Text.RegularExpressions.RegexOptions.RightToLeft).Value, out int ano) ? ano : 0;
        var contratos = base_.Opcoes(new FiltroObras()).Contratos
            .Where(c => Normalizacao.Busca(c).Contains(alvo, StringComparison.Ordinal))
            .OrderByDescending(Ano).ThenBy(c => c, StringComparer.OrdinalIgnoreCase).Take(max).ToArray();
        if (contratos.Length == 0) return [];
        var obras = base_.Buscar(new FiltroObras { Contrato = contratos }, int.MaxValue).Obras;
        return [.. contratos.Select(c =>
        {
            var escolas = obras.Where(o => MesmoContrato(o.Contrato, c)).DistinctBy(o => Normalizacao.Busca(o.Empreendimento)).ToArray();
            string detalhe = escolas.Length == 1
                ? string.Join(" · ", new[] { escolas[0].Lote, escolas[0].Empreendimento }.Where(v => !string.IsNullOrWhiteSpace(v)))
                : $"{escolas.Length} escolas";
            return new Sugestao(c, detalhe);
        })];
    }

    /// <summary>Obras do contrato digitado ("044-EX-2026" e "044/EX/2026" sao o mesmo).</summary>
    public static IReadOnlyList<ObraLida> DoContrato(BaseRepositorio base_, string digitado)
    {
        string alvo = digitado.Trim().Replace('-', '/').Replace(' ', '/');
        string? contrato = base_.Opcoes(new FiltroObras()).Contratos
            .FirstOrDefault(c => string.Equals(c.Trim(), alvo, StringComparison.OrdinalIgnoreCase));
        return contrato is null ? [] : base_.Buscar(new FiltroObras { Contrato = [contrato] }, 100).Obras;
    }

    /// <summary>
    /// Obras da escola digitada: primeiro o nome exato (sem acento e caixa); se nao houver, as escolas que
    /// contem o texto. Uma escola pode estar em varios contratos.
    /// </summary>
    public static IReadOnlyList<ObraLida> DaEscola(BaseRepositorio base_, string digitado)
    {
        string alvo = Normalizacao.Busca(digitado.Trim());
        if (alvo.Length < 4)
        {
            return [];
        }

        var escolas = base_.Opcoes(new FiltroObras()).Escolas;
        var exatas = escolas.Where(e => Normalizacao.Busca(e.Trim()) == alvo).ToList();
        var achadas = exatas.Count > 0 ? exatas : escolas.Where(e => Normalizacao.Busca(e).Contains(alvo, StringComparison.Ordinal)).Take(10).ToList();
        return [.. achadas.SelectMany(e => base_.Buscar(new FiltroObras { Escola = [e] }, 100).Obras).Take(20)];
    }

    /// <summary>Obras de um lote: "216", "lote 216" e "LOTE 216" sao o mesmo lote.</summary>
    public static IReadOnlyList<ObraLida> DoLote(BaseRepositorio base_, string digitado)
    {
        string alvo = ChaveDoLote(digitado);
        if (alvo.Length == 0)
        {
            return [];
        }

        var lotes = base_.Opcoes(new FiltroObras()).Lotes.Where(l => ChaveDoLote(l) == alvo).ToList();
        return [.. lotes.SelectMany(l => base_.Buscar(new FiltroObras { Lote = [l] }, 200).Obras)];
    }

    public static string ChaveDoLote(string lote)
    {
        string digitos = new([.. lote.Where(char.IsDigit)]);
        return digitos.Length > 0 ? digitos.TrimStart('0') : Normalizacao.Busca(lote.Trim());
    }

    /// <summary>Mesma escola: sem acento, caixa e espacos nas pontas.</summary>
    public static bool MesmaEscola(string a, string b) => Normalizacao.Busca(a.Trim()) == Normalizacao.Busca(b.Trim());

    public static bool MesmoContrato(string a, string b) =>
        string.Equals(a.Trim().Replace('-', '/'), b.Trim().Replace('-', '/'), StringComparison.OrdinalIgnoreCase);
}
