using System.Globalization;

namespace Spobras.Core.Relatorio;

/// <summary>
/// Uma linha da tabela de dados do cabecalho: rotulo a esquerda, valor a direita. Com <paramref name="Rotulo2"/>,
/// a linha tem um segundo par na metade direita (Contrato e Data da OS lado a lado).
/// </summary>
public sealed record CampoCabecalho(string Rotulo, string Valor, int Linhas, string? Rotulo2 = null, string? Valor2 = null)
{
    public bool Dupla => Rotulo2 is not null;
}

/// <summary>
/// Cabecalho montado para uma pagina: alturas (pixels) de cada linha fixa, na ordem da planilha:
/// 4 linhas do topo, uma por campo preenchido, e as 3 da faixa de titulo. O corpo usa o que sobra.
/// </summary>
public sealed record CabecalhoMontado(IReadOnlyList<CampoCabecalho> Campos, IReadOnlyList<int> Alturas, int LinhasDaContratada)
{
    public double Altura => Alturas.Sum();

    /// <summary>Linhas de 20 px que sobram para fotos, legendas e assinaturas.</summary>
    public int LinhasDoCorpo => (int)Math.Floor((GradeRelatorio.AlturaDaPagina - Altura) / GradeRelatorio.AlturaDaLinha);

    /// <summary>Indice (0..) da primeira linha de campo.</summary>
    public int PrimeiroCampo => GradeRelatorio.LinhasDoTopo.Count;

    public double YDoCampo(int indice) => Alturas.Take(PrimeiroCampo + indice).Sum();

    public double YDoTitulo => Alturas.Take(PrimeiroCampo + Campos.Count).Sum();
}

/// <summary>Textos e linhas do cabecalho do relatorio. Campo vazio nao gera rotulo nem espaco.</summary>
public static class CabecalhoRelatorio
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static string Data(DateOnly? data) => data?.ToString("dd/MM/yyyy", PtBr) ?? "";

    /// <summary>Largura do valor de um campo (colunas F a L).</summary>
    public static double LarguraDoValor => GradeRelatorio.Largura - GradeRelatorio.XDaColuna(GradeRelatorio.ColunaDoValor);

    /// <summary>Largura da caixa da direita (SPOBRAS, data, contratada).</summary>
    public static double LarguraDaCaixa => GradeRelatorio.Largura - GradeRelatorio.XDaColuna(GradeRelatorio.ColunaDaCaixa);

    /// <summary>Colunas (0 a 9) da linha dupla: valor 1 em F-G, rotulo 2 em H-J, valor 2 em K-L.</summary>
    public const int ColunaDoRotulo2 = 5, ColunaDoValor2 = 8;

    /// <summary>Largura do primeiro valor numa linha dupla (colunas F e G).</summary>
    public static double LarguraDoValorDuplo => GradeRelatorio.XDaColuna(ColunaDoRotulo2) - GradeRelatorio.XDaColuna(GradeRelatorio.ColunaDoValor);

    /// <summary>
    /// Campos preenchidos, na ordem do documento. Campo vazio nao aparece (nem o rotulo). Contrato e Data da OS
    /// dividem a mesma linha para o cabecalho ficar menor e sobrar espaco para as fotos.
    /// </summary>
    public static IReadOnlyList<CampoCabecalho> Campos(DadosRelatorio d)
    {
        var campos = new List<CampoCabecalho>();
        void Somar(string rotulo, string valor)
        {
            if (!string.IsNullOrWhiteSpace(valor))
            {
                string limpo = valor.Trim();
                campos.Add(new CampoCabecalho(rotulo, limpo, PaginadorRelatorio.LinhasDe(limpo, LarguraDoValor)));
            }
        }

        Somar("UNIDADE EDUCACIONAL", d.Unidade);
        Somar("LOTE", d.Lote);
        Somar("PORTARIA", d.Portaria);
        string contrato = d.Contrato.Trim(), dataOs = Data(d.DataOs);
        if (contrato.Length > 0 && dataOs.Length > 0)
        {
            campos.Add(new CampoCabecalho("CONTRATO", contrato, PaginadorRelatorio.LinhasDe(contrato, LarguraDoValorDuplo), "DATA DA OS", dataOs));
        }
        else
        {
            Somar("CONTRATO", contrato);
            Somar("DATA DA OS", dataOs);
        }

        Somar("PROCESSO SEI", d.ProcessoSei);
        Somar("OBJETO", d.Objeto);
        Somar("FISCAL", d.Responsavel);
        Somar("DESCRIÇÃO", d.Descricao);
        return campos;
    }

    /// <summary>Altura (px) de uma linha com <paramref name="linhas"/> linhas de texto.</summary>
    public static int AlturaDeTexto(int linhas) => (int)(Math.Max(1, linhas) * GradeRelatorio.AlturaDaLinha) + 4;

    public static CabecalhoMontado Montar(DadosRelatorio d)
    {
        var campos = Campos(d);
        var alturas = new List<int>(GradeRelatorio.LinhasDoTopo);
        int linhasContratada = string.IsNullOrWhiteSpace(d.Contratada) ? 1 : PaginadorRelatorio.LinhasDe(d.Contratada.Trim(), LarguraDaCaixa);
        alturas[^1] = Math.Max(alturas[^1], AlturaDeTexto(linhasContratada));
        alturas.AddRange(campos.Select(c => AlturaDeTexto(c.Linhas)));
        alturas.AddRange(GradeRelatorio.LinhasDoTitulo);
        return new CabecalhoMontado(campos, alturas, linhasContratada);
    }

    /// <summary>"Relatório Fotográfico - Escola - 2026-11-10", omitindo o que estiver vazio.</summary>
    public static string NomeSugerido(DadosRelatorio d)
    {
        var partes = new List<string> { "Relatório Fotográfico" };
        if (!string.IsNullOrWhiteSpace(d.Unidade))
        {
            partes.Add(Nomes.NomeArquivo.Corrigir(d.Unidade.Trim()));
        }

        if (d.Data is { } data)
        {
            partes.Add(data.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        return string.Join(" - ", partes);
    }
}
