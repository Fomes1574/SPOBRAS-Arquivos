using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Spobras.Core;
using Spobras.Core.Operacoes;

namespace Spobras.App.ViewModels;

public static class Formato
{
    public static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static string DataHora(DateTimeOffset quando) => quando.ToLocalTime().ToString("dd/MM/yyyy 'às' HH:mm", PtBr);

    public static string Plural(int n, string singular, string plural) => n == 1 ? $"1 {singular}" : $"{n} {plural}";
}

/// <summary>Mensagem da tela com gravidade (texto + icone, nunca so cor).</summary>
public sealed record AvisoVm(Gravidade Gravidade, string Texto)
{
    public AvisoVm(Aviso aviso) : this(aviso.Gravidade, aviso.Mensagem)
    {
    }

    public string Rotulo => Gravidade switch
    {
        Gravidade.ImpedeGerar => "Impede",
        Gravidade.PrecisaConferir => "Conferir",
        _ => "Informação",
    };

    public bool Impede => Gravidade == Gravidade.ImpedeGerar;
    public bool Conferir => Gravidade == Gravidade.PrecisaConferir;
}

/// <summary>Um lote do historico como a tela mostra.</summary>
public sealed class LoteVm(Lote lote)
{
    public Lote Lote { get; } = lote;
    public string Descricao => Lote.Descricao;
    public string Quando => Formato.DataHora(Lote.CriadoEm);

    public string Resumo
    {
        get
        {
            var partes = new List<string>();
            void Somar(EstadoItem estado, string singular, string plural)
            {
                int n = Lote.Contar(estado);
                if (n > 0)
                {
                    partes.Add(Formato.Plural(n, singular, plural));
                }
            }

            Somar(EstadoItem.Concluido, "concluído", "concluídos");
            Somar(EstadoItem.Falhou, "com falha", "com falha");
            int pendentes = Lote.Contar(EstadoItem.Pendente) + Lote.Contar(EstadoItem.Cancelado) + Lote.Contar(EstadoItem.EmExecucao);
            if (pendentes > 0)
            {
                partes.Add(Formato.Plural(pendentes, "pendente", "pendentes"));
            }

            Somar(EstadoItem.Ignorado, "ignorado", "ignorados");
            if (Lote.DesfeitoPorLoteId is not null)
            {
                partes.Add("desfeito");
            }

            return string.Join(" · ", partes);
        }
    }

    /// <summary>Mostra o botao Desfazer; a conferencia de seguranca e feita no clique.</summary>
    public bool PodeDesfazer =>
        Lote.Tipo != TipoLote.Criar && Lote.DesfeitoPorLoteId is null && Lote.Contar(EstadoItem.Concluido) > 0;

    public bool TemPendentes => Lote.Tipo != TipoLote.Criar && Lote.TemPendentes;
    public bool PodeAbrirPasta => Lote.Itens.Any(i => i.Estado == EstadoItem.Concluido);
    public string? PrimeiroResultado => Lote.Itens.FirstOrDefault(i => i.Estado == EstadoItem.Concluido)?.Destino;

    public IReadOnlyList<ItemLoteVm> Itens => [.. Lote.Itens.Select(i => new ItemLoteVm(i, Lote.Tipo))];
}

public sealed class ItemLoteVm(ItemLote item, TipoLote tipo)
{
    public string De => tipo == TipoLote.Criar ? "" : Path.GetFileName(item.Origem);

    public string Para => tipo switch
    {
        TipoLote.Renomear => Path.GetFileName(item.Destino),
        _ => item.Destino,
    };

    public string Estado => item.Estado switch
    {
        EstadoItem.Concluido => "Concluído",
        EstadoItem.Falhou => "Falhou",
        EstadoItem.Ignorado => "Ignorado",
        EstadoItem.Cancelado => "Pendente (cancelado)",
        EstadoItem.EmExecucao => "Interrompido",
        _ => "Pendente",
    };

    public string Mensagem => item.Mensagem ?? "";
    public bool Falhou => item.Estado == EstadoItem.Falhou;
}

/// <summary>Base das telas: mensagem de resultado da ultima acao e indicador de trabalho em andamento.</summary>
public abstract partial class TelaVm : ObservableObject
{
    [ObservableProperty]
    private string _resultado = "";

    [ObservableProperty]
    private bool _resultadoComFalha;

    [ObservableProperty]
    private bool _ocupado;

    public abstract string Titulo { get; }

    /// <summary>Chamado quando a tela aparece.</summary>
    public virtual void AoEntrar()
    {
    }
}
