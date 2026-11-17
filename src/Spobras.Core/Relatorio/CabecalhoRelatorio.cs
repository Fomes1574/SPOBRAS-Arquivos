using System.Globalization;

namespace Spobras.Core.Relatorio;

/// <summary>
/// Uma linha da grade de dados do cabecalho. Larga: um rotulo e um valor ocupando a linha inteira. Dupla: dois
/// pares lado a lado (Contrato e Lote, Contratada e Data da OS...). As linhas ja vem quebradas na largura certa.
/// </summary>
public sealed record CampoCabecalho(
    string Rotulo,
    string Valor,
    IReadOnlyList<string> LinhasDoRotulo,
    IReadOnlyList<string> LinhasDoValor,
    double Altura,
    string? Rotulo2 = null,
    string? Valor2 = null,
    IReadOnlyList<string>? LinhasDoRotulo2 = null,
    IReadOnlyList<string>? LinhasDoValor2 = null)
{
    public bool Dupla => Rotulo2 is not null;
}

/// <summary>Cabecalho da primeira pagina: a faixa de cima e as linhas de campos (so os preenchidos).</summary>
public sealed record CabecalhoMontado(
    IReadOnlyList<CampoCabecalho> Campos,
    IReadOnlyList<string> LinhasDoSubtitulo,
    string DataPorExtenso,
    string Rodape)
{
    public double AlturaDosCampos => Campos.Sum(c => c.Altura);

    /// <summary>Altura do cabecalho da primeira pagina (faixa de cima e campos).</summary>
    public double Altura => GradeRelatorio.AlturaDoTopo + AlturaDosCampos;

    /// <summary>Y (na folha) do primeiro campo.</summary>
    public static double TopoDosCampos => GradeRelatorio.MargemDeCima + GradeRelatorio.AlturaDoTopo;

    /// <summary>Onde as fotos comecam na primeira pagina e nas seguintes (cabecalho reduzido).</summary>
    public double TopoDasFotos(bool reduzido) =>
        GradeRelatorio.MargemDeCima + (reduzido ? GradeRelatorio.AlturaDoTopoReduzido : Altura) + GradeRelatorio.RespiroAntesDasFotos;
}

/// <summary>Textos e medidas do cabecalho do relatorio. Campo vazio nao gera rotulo nem espaco.</summary>
public static class CabecalhoRelatorio
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static string Data(DateOnly? data) => data?.ToString("dd/MM/yyyy", PtBr) ?? "";

    /// <summary>"17 de novembro de 2026".</summary>
    public static string PorExtenso(DateOnly? data) => data?.ToString("d 'de' MMMM 'de' yyyy", PtBr) ?? "";

    private static double LarguraDoTextoDoRotulo => GradeRelatorio.LarguraDoRotulo - 2 * 2 * GradeRelatorio.Mm;

    private static double LarguraDoTextoDoValor(bool dupla) =>
        (dupla ? GradeRelatorio.Largura / 2 : GradeRelatorio.Largura) - GradeRelatorio.LarguraDoRotulo - 2 * GradeRelatorio.RecuoDoCampo;

    public static IReadOnlyList<string> LinhasDoRotulo(string rotulo) =>
        TextoMedido.Quebrar(rotulo, LarguraDoTextoDoRotulo, GradeRelatorio.LetraDoRotulo, negrito: true);

    public static IReadOnlyList<string> LinhasDoValor(string valor, bool dupla) =>
        TextoMedido.Quebrar(valor, LarguraDoTextoDoValor(dupla), GradeRelatorio.LetraDoValor, negrito: true);

    private static double AlturaDe(int linhasDoRotulo, int linhasDoValor)
    {
        double texto = Math.Max(linhasDoRotulo * GradeRelatorio.LetraDoRotulo, linhasDoValor * GradeRelatorio.LetraDoValor) * GradeRelatorio.AlturaDaLinhaDoCampo;
        return Math.Max(GradeRelatorio.AlturaMinimaDoCampo, texto + 2 * GradeRelatorio.RespiroDoCampo);
    }

    private static CampoCabecalho Larga(string rotulo, string valor)
    {
        var r = LinhasDoRotulo(rotulo);
        var v = LinhasDoValor(valor, dupla: false);
        return new CampoCabecalho(rotulo, valor, r, v, AlturaDe(r.Count, v.Count));
    }

    private static CampoCabecalho Dupla((string Rotulo, string Valor) a, (string Rotulo, string Valor) b)
    {
        var r1 = LinhasDoRotulo(a.Rotulo);
        var v1 = LinhasDoValor(a.Valor, dupla: true);
        var r2 = LinhasDoRotulo(b.Rotulo);
        var v2 = LinhasDoValor(b.Valor, dupla: true);
        return new CampoCabecalho(a.Rotulo, a.Valor, r1, v1, AlturaDe(Math.Max(r1.Count, r2.Count), Math.Max(v1.Count, v2.Count)), b.Rotulo, b.Valor, r2, v2);
    }

    /// <summary>
    /// Campos preenchidos, na ordem do modelo: a escola na linha inteira; Contrato, Lote, Contratada, Data da OS,
    /// Portaria, Fiscal e Processo SEI dois a dois; Objeto e Descricao na linha inteira. Campo vazio nao aparece.
    /// </summary>
    public static IReadOnlyList<CampoCabecalho> Campos(DadosRelatorio d)
    {
        var campos = new List<CampoCabecalho>();
        static string Limpo(string? v) => (v ?? "").Trim();
        if (Limpo(d.Unidade) is { Length: > 0 } unidade)
        {
            campos.Add(Larga("UNIDADE EDUCACIONAL", unidade));
        }

        var pares = new List<(string, string)>();
        void Par(string rotulo, string valor)
        {
            if (valor.Length > 0)
            {
                pares.Add((rotulo, valor));
            }
        }

        Par("CONTRATO", Limpo(d.Contrato));
        Par("LOTE", Limpo(d.Lote));
        Par("CONTRATADA", Limpo(d.Contratada));
        Par("DATA DA OS", Data(d.DataOs));
        Par("PORTARIA", Limpo(d.Portaria));
        Par("FISCAL", Limpo(d.Responsavel));
        Par("PROCESSO SEI", Limpo(d.ProcessoSei));
        for (int i = 0; i < pares.Count; i += 2)
        {
            campos.Add(i + 1 < pares.Count ? Dupla(pares[i], pares[i + 1]) : Larga(pares[i].Item1, pares[i].Item2));
        }

        if (Limpo(d.Objeto) is { Length: > 0 } objeto)
        {
            campos.Add(Larga("OBJETO", objeto));
        }

        if (Limpo(d.Descricao) is { Length: > 0 } descricao)
        {
            campos.Add(Larga("DESCRIÇÃO", descricao));
        }

        return campos;
    }

    /// <summary>Linha de baixo do cabecalho reduzido: "Escola · Contrato 017/EX/2026 · continuação", numa linha so.</summary>
    public static IReadOnlyList<string> Subtitulo(DadosRelatorio d)
    {
        double largura = GradeRelatorio.Largura - GradeRelatorio.LarguraDoLogoReduzido - 4 * GradeRelatorio.Mm;
        string contrato = d.Contrato.Trim().Length > 0 ? " · Contrato " + d.Contrato.Trim() : "";
        string fim = contrato + " · continuação";
        string escola = d.Unidade.Trim();
        string texto = escola.Length > 0 ? escola + fim : fim.TrimStart(' ', '·');
        while (escola.Length > 0 && TextoMedido.Largura(texto, GradeRelatorio.LetraDoSubtitulo, false) > largura)
        {
            escola = escola[..^1].TrimEnd();
            texto = escola + "…" + fim;
        }

        return [texto];
    }

    /// <summary>Texto do rodape, a esquerda: "Escola · Relatório fotográfico de 17/11/2026", encurtado se preciso.</summary>
    public static string Rodape(DadosRelatorio d)
    {
        string fim = "Relatório fotográfico" + (d.Data is { } data ? " de " + Data(data) : "");
        string escola = d.Unidade.Trim();
        double largura = GradeRelatorio.Largura * 0.78;
        string texto = escola.Length > 0 ? escola + " · " + fim : fim;
        while (escola.Length > 0 && TextoMedido.Largura(texto, GradeRelatorio.LetraDoRodape, false) > largura)
        {
            escola = escola[..^1].TrimEnd();
            texto = escola + "… · " + fim;
        }

        return texto;
    }

    public static CabecalhoMontado Montar(DadosRelatorio d) => new(Campos(d), Subtitulo(d), PorExtenso(d.Data), Rodape(d));

    /// <summary>Parte da escola no nome sugerido: com o resto, o nome fica com no maximo 120 caracteres.</summary>
    public const int MaximoDaUnidadeNoNome = 80;

    /// <summary>"Relatório Fotográfico - Escola - 2026-11-10", omitindo o que estiver vazio.</summary>
    public static string NomeSugerido(DadosRelatorio d)
    {
        var partes = new List<string> { "Relatório Fotográfico" };
        if (!string.IsNullOrWhiteSpace(d.Unidade))
        {
            // Nome de escola muito longo nao pode estourar o limite de caminho do Windows/Android.
            string unidade = Nomes.NomeArquivo.Corrigir(d.Unidade.Trim());
            partes.Add(unidade.Length <= MaximoDaUnidadeNoNome ? unidade : unidade[..MaximoDaUnidadeNoNome].TrimEnd(' ', '.', '-'));
        }

        if (d.Data is { } data)
        {
            partes.Add(data.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        return string.Join(" - ", partes);
    }
}
