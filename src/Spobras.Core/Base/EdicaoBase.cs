using System.Globalization;

namespace Spobras.Core.Base;

public enum TipoCampoObra
{
    Texto,
    Data,
    Reais,
}

/// <summary>Um campo da obra que pode ser mudado no app: rotulo para a tela e coluna no banco.</summary>
public sealed record CampoObra(string Chave, string Rotulo, string Coluna, TipoCampoObra Tipo)
{
    /// <summary>Valor da obra como texto para a tela ("10/11/2026", "1.234,56").</summary>
    public string Ler(ObraLida o) => Chave switch
    {
        "contrato" => o.Contrato,
        "empreendimento" => o.Empreendimento,
        "contratada" => o.Contratada ?? "",
        "portaria" => o.Portaria ?? "",
        "contrato_receita" => o.ContratoReceita ?? "",
        "dotacao" => o.Dotacao ?? "",
        "atividade" => o.Atividade ?? "",
        "lote" => o.Lote ?? "",
        "modalidade" => o.Modalidade ?? "",
        "tipo" => o.Tipo ?? "",
        "tipo_obra" => o.TipoDeObra ?? "",
        "processo_sei" => o.ProcessoSei ?? "",
        "status" => o.Status ?? "",
        "status_contrato" => o.StatusContrato ?? "",
        "situacao" => o.Situacao ?? "",
        "escopo" => o.Escopo ?? "",
        "data_assinatura" => CamposObra.Data(o.DataAssinatura),
        "data_os" => CamposObra.Data(o.DataOs),
        "termino" => CamposObra.Data(o.Termino),
        _ => Tipo == TipoCampoObra.Reais && CamposObra.Valor(o, Chave) is { } v ? v.ToString("N2", CamposObra.PtBr) : "",
    };
}

/// <summary>Mudanca feita no app num campo de uma obra da planilha. O valor da planilha fica guardado.</summary>
public sealed record AjusteObra(
    string Contrato, string Empreendimento, string Campo, string? Valor, string? ValorDaPlanilha, bool PlanilhaMudou, DateTimeOffset Quando);

public static class CamposObra
{
    public static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static readonly IReadOnlyList<CampoObra> Todos =
    [
        new("contrato", "Contrato", "contrato", TipoCampoObra.Texto),
        new("empreendimento", "Escola / empreendimento", "empreendimento", TipoCampoObra.Texto),
        new("contratada", "Contratada", "contratada", TipoCampoObra.Texto),
        new("portaria", "Portaria", "portaria", TipoCampoObra.Texto),
        new("contrato_receita", "Contrato de receita", "contrato_receita", TipoCampoObra.Texto),
        new("dotacao", "Dotação completa", "dotacao", TipoCampoObra.Texto),
        new("atividade", "Dotação (atividade)", "atividade", TipoCampoObra.Texto),
        new("lote", "Lote", "lote", TipoCampoObra.Texto),
        new("modalidade", "Modalidade", "modalidade", TipoCampoObra.Texto),
        new("tipo", "Tipo", "tipo", TipoCampoObra.Texto),
        new("tipo_obra", "Tipo de obra", "tipo_obra", TipoCampoObra.Texto),
        new("processo_sei", "Processo SEI", "processo_sei", TipoCampoObra.Texto),
        new("status", "Status", "status", TipoCampoObra.Texto),
        new("status_contrato", "Status do contrato", "status_contrato", TipoCampoObra.Texto),
        new("situacao", "Situação", "situacao", TipoCampoObra.Texto),
        new("escopo", "Escopo", "escopo", TipoCampoObra.Texto),
        new("data_assinatura", "Data de assinatura", "data_assinatura", TipoCampoObra.Data),
        new("data_os", "Data da OS (INÍCIO EXECUÇÃO)", "data_os", TipoCampoObra.Data),
        new("termino", "Término", "termino", TipoCampoObra.Data),
        new("valor_empreendimento", CamposValor.Rotulo(CampoValor.ValorEmpreendimento), "valor_empreendimento", TipoCampoObra.Reais),
        new("valor_aditado", CamposValor.Rotulo(CampoValor.ValorAditado), "valor_aditado", TipoCampoObra.Reais),
        new("valor_contratado_aditamento", CamposValor.Rotulo(CampoValor.ValorContratadoComAditamento), "valor_contratado_aditamento", TipoCampoObra.Reais),
        new("valor_medido", CamposValor.Rotulo(CampoValor.ValorMedido), "valor_medido", TipoCampoObra.Reais),
        new("valor_pago", CamposValor.Rotulo(CampoValor.ValorPago), "valor_pago", TipoCampoObra.Reais),
        new("saldo_medir", CamposValor.Rotulo(CampoValor.SaldoAMedir), "saldo_medir", TipoCampoObra.Reais),
        new("saldo_pagar", CamposValor.Rotulo(CampoValor.SaldoAPagar), "saldo_pagar", TipoCampoObra.Reais),
    ];

    /// <summary>Campos que identificam a obra da planilha (contrato + escola): nao mudam numa obra importada.</summary>
    public static bool Chave(string campo) => campo is "contrato" or "empreendimento";

    public static CampoObra? Por(string chave) => Todos.FirstOrDefault(c => c.Chave == chave);

    public static string Data(DateOnly? d) => d?.ToString("dd/MM/yyyy", PtBr) ?? "";

    public static decimal? Valor(ObraLida o, string chave) => chave switch
    {
        "valor_empreendimento" => o.ValorEmpreendimento,
        "valor_aditado" => o.ValorAditado,
        "valor_contratado_aditamento" => o.ValorContratadoComAditamento,
        "valor_medido" => o.ValorMedido,
        "valor_pago" => o.ValorPago,
        "saldo_medir" => o.SaldoAMedir,
        "saldo_pagar" => o.SaldoAPagar,
        _ => null,
    };

    /// <summary>Texto digitado -> valor guardado no banco (texto, data ISO ou centavos). Erro explicado se nao entender.</summary>
    public static (object? Valor, string? Erro) Converter(CampoObra campo, string texto)
    {
        texto = texto.Trim();
        if (texto.Length == 0)
        {
            return CamposObra.Chave(campo.Chave) ? (null, $"{campo.Rotulo} não pode ficar vazio.") : (null, null);
        }

        switch (campo.Tipo)
        {
            case TipoCampoObra.Data:
                return DateOnly.TryParseExact(texto, ["d/M/yyyy", "d/M/yy"], PtBr, DateTimeStyles.None, out var d)
                    ? (d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), null)
                    : (null, $"{campo.Rotulo}: a data \"{texto}\" não foi entendida (use dia/mês/ano).");
            case TipoCampoObra.Reais:
                return Normalizacao.LerReais(texto) is { } v
                    ? (Normalizacao.Centavos(v), null)
                    : (null, $"{campo.Rotulo}: o valor \"{texto}\" não foi entendido (use 1.234,56).");
            default:
                return (campo.Chave == "dotacao" ? Normalizacao.Dotacao(texto).Completa : texto, null);
        }
    }

    /// <summary>Valor guardado (texto, ISO ou centavos) -> texto para a tela.</summary>
    public static string Mostrar(CampoObra campo, string? guardado)
    {
        if (string.IsNullOrEmpty(guardado))
        {
            return "(vazio)";
        }

        return campo.Tipo switch
        {
            TipoCampoObra.Data when DateOnly.TryParseExact(guardado, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) => Data(d),
            TipoCampoObra.Reais when long.TryParse(guardado, CultureInfo.InvariantCulture, out long c) => (c / 100m).ToString("N2", PtBr),
            _ => guardado,
        };
    }
}
