using Microsoft.Data.Sqlite;
using Spobras.Core.Base;

namespace Spobras.Dados.Base;

public sealed partial class BaseRepositorio
{
    private static IReadOnlyList<OpcaoContratoResumo> LerOpcoesContrato(SqliteConnection conexao, FiltroObras filtro)
    {
        using var cmd = conexao.CreateCommand();
        string compativeis = Onde(filtro, cmd, "contrato");
        string visiveis = Onde(new FiltroObras { OcultarStatusContrato = filtro.OcultarStatusContrato }, cmd, null, "visivel");
        cmd.CommandText = $"""
            select trim(contrato), min(lote), min(modalidade), min(portaria), count(*)
            from Obra where trim(contrato) collate nocase in (
                select trim(contrato) from Obra where {compativeis}
            ) and trim(contrato) <> '' and {visiveis} group by trim(contrato) collate nocase
            order by trim(contrato) collate nocase
            """;
        using var r = cmd.ExecuteReader();
        var opcoes = new List<OpcaoContratoResumo>();
        while (r.Read()) opcoes.Add(new(r.GetString(0), Txt(r, 1), Txt(r, 2), Txt(r, 3), r.GetInt32(4)));
        return opcoes;
    }

    public IReadOnlyList<ResumoContrato> ResumosDosContratos(IReadOnlyList<string> contratos, IReadOnlyList<string>? ocultos = null)
    {
        if (contratos.Count == 0) return [];
        var obras = Buscar(new FiltroObras { Contrato = contratos, OcultarStatusContrato = ocultos ?? [] }, int.MaxValue).Obras;
        var medicoes = Medicoes(contratos.Select(c => c.Trim()));
        return [.. obras.GroupBy(o => o.Contrato.Trim(), StringComparer.OrdinalIgnoreCase).Select(g =>
            new ResumoContrato(g.First() with { Contrato = g.Key },
                g.OrderBy(o => o.Empreendimento, StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("pt-BR"), true))
                    .Select(o => new EscolaResumo(o, MedicoesEmTramite.DaObra(o, medicoes.GetValueOrDefault(g.Key) ?? []))).ToArray()))];
    }

    public EscolaResumo ResumoDaEscola(ObraLida obra) => new(obra with { Contrato = obra.Contrato.Trim() },
        MedicoesEmTramite.DaObra(obra, Medicoes(obra.Contrato.Trim())));
}
