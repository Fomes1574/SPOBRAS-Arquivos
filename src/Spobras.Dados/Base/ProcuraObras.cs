using Spobras.Core.Base;

namespace Spobras.Dados.Base;

/// <summary>
/// Buscas por contrato, escola e lote digitados, usadas pelo relatorio e pelo cronograma no computador e pelo
/// app do celular. Ficam aqui para os dois darem o mesmo resultado.
/// </summary>
public static class ProcuraObras
{
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
