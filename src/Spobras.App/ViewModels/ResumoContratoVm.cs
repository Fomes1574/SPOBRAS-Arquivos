using Spobras.Core.Base;
using Spobras.Documentos.Relatorio;

namespace Spobras.App.ViewModels;

public sealed class ResumoContratoVm(ResumoContrato resumo, DateOnly hoje)
{
    public string Titulo => "Contrato " + resumo.Identificacao.Contrato;
    public SeloContrato Status => PrazoContrato.Status(resumo.Identificacao.StatusContrato);
    public SeloContrato Prazo => PrazoContrato.Prazo(resumo.Identificacao, hoje);
    public string Dados
    {
        get
        {
            var o = resumo.Identificacao;
            return string.Join(" · ", new[] { OpcaoContratoResumo.NomeLote(o.Lote), o.Modalidade, o.Contratada,
                "OS " + GeradorResumoContratoPdf.Data(o.DataOs), "término " + GeradorResumoContratoPdf.Data(o.Termino), o.Portaria,
                string.IsNullOrWhiteSpace(o.Situacao) ? null : "Prazo (planilha): " + o.Situacao }
                .Where(s => !string.IsNullOrWhiteSpace(s)));
        }
    }

    public IReadOnlyList<ItemCronograma> Cartoes
    {
        get
        {
            var v = resumo.Valores;
            return [
                new("Valor original", FormatoBase.Reais(v.Original)),
                new("Aditado", FormatoBase.Reais(v.Aditado)),
                new("Contratado c/ adit.", FormatoBase.Reais(v.Contratado)),
                new("Pago", ComPercentual(v.Pago, v)),
                new("Em trâmite", ComPercentual(v.Tramite, v)),
                new("Saldo a pagar", FormatoBase.Reais(v.SaldoAPagar)),
            ];
        }
    }

    private static string ComPercentual(decimal valor, ValoresResumo v) =>
        $"{FormatoBase.Reais(valor)} ({GeradorResumoContratoPdf.Percentual(v.Percentual(valor), 0)})";

    public IReadOnlyList<EscolaResumoVm> Escolas => [
        .. resumo.Escolas.Select(e => new EscolaResumoVm(e.Obra, e.Obra.Empreendimento, e.Valores)),
        new(null, $"Total do contrato ({resumo.Escolas.Count} {(resumo.Escolas.Count == 1 ? "escola" : "escolas")})", resumo.Valores),
    ];
}

public sealed record EscolaResumoVm(ObraLida? Obra, string Nome, ValoresResumo Valores)
{
    public string Contratado => FormatoBase.Reais(Valores.Contratado);
    public string Pago => FormatoBase.Reais(Valores.Pago);
    public string Tramite => FormatoBase.Reais(Valores.Tramite);
    public string Saldo => FormatoBase.Reais(Valores.SaldoAPagar);
    public string Percentual => GeradorResumoContratoPdf.Percentual(Valores.Percentual(Valores.Pago), 0);
    public bool EhObra => Obra is not null;
}
