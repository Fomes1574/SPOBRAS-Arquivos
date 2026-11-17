using System.Globalization;

namespace Spobras.Core.Relatorio;

/// <summary>
/// Distribui as fotos nas paginas A4, como no modelo aprovado em 17/11/2026:
/// - todas as fotos no mesmo quadro, alinhadas em linhas e colunas, inteiras e centralizadas;
/// - a legenda colada embaixo do quadro, com espaco de pelo menos 2 linhas (foto sem legenda guarda o mesmo espaco);
/// - com os limites de 100 (legenda) e 300 caracteres (descricao), o texto sempre cabe: nao existe continuacao;
/// - da pagina 2 em diante, cabecalho reduzido;
/// - as assinaturas ficam na pagina da ultima foto, nunca sozinhas numa pagina.
/// </summary>
public static class PaginadorRelatorio
{
    /// <summary>Quadro deitado 4:3 no maximo: com 1, 2 ou 4 por pagina as fotos nao viram faixas altas e vazias.</summary>
    private const double ProporcaoMaxima = 0.75;

    /// <summary>Para as assinaturas caberem numa pagina unica, o quadro diminui no maximo ate 75% do normal.</summary>
    private const double MenorEncolhimento = 0.75;

    private sealed record Textos(IReadOnlyList<string> Legenda, IReadOnlyList<string> Descricao);

    public static PlanoRelatorio Paginar(IReadOnlyList<FotoRelatorio> fotos, OpcoesRelatorio opcoes, DadosRelatorio? dados = null)
    {
        var avisos = new List<Aviso>();
        dados ??= new DadosRelatorio();
        var cabecalho = CabecalhoRelatorio.Montar(dados);
        PlanoRelatorio Vazio() => new([], avisos, opcoes, dados, cabecalho);

        if (fotos.Count == 0)
        {
            avisos.Add(Aviso.Impede("Escolha pelo menos uma foto."));
            return Vazio();
        }

        if (!OpcoesRelatorio.Capacidades.Contains(opcoes.FotosPorPagina))
        {
            avisos.Add(Aviso.Impede("Fotos por página deve ser 1, 2, 4, 6 ou 8."));
            return Vazio();
        }

        bool comDescricao = opcoes.Modo == ModoLegenda.DescricaoServico;
        for (int i = 0; i < fotos.Count; i++)
        {
            int legenda = fotos[i].Legenda.Trim().Length, descricao = fotos[i].Descricao.Trim().Length;
            if (legenda > OpcoesRelatorio.MaximoDaLegenda)
            {
                avisos.Add(Aviso.Impede($"{Rotulo(i + 1, fotos.Count)}: a legenda tem {legenda} caracteres; o limite é {OpcoesRelatorio.MaximoDaLegenda}. Encurte para gerar."));
            }

            if (comDescricao && descricao > OpcoesRelatorio.MaximoDaDescricao)
            {
                avisos.Add(Aviso.Impede($"{Rotulo(i + 1, fotos.Count)}: a descrição do serviço tem {descricao} caracteres; o limite é {OpcoesRelatorio.MaximoDaDescricao}. Encurte para gerar."));
            }
        }

        if (dados.Descricao.Trim().Length > OpcoesRelatorio.MaximoDaDescricaoGeral)
        {
            avisos.Add(Aviso.Impede($"A descrição do relatório tem {dados.Descricao.Trim().Length} caracteres; o limite é {OpcoesRelatorio.MaximoDaDescricaoGeral}. Encurte para gerar."));
        }

        if (avisos.Count > 0)
        {
            return Vazio();
        }

        int capacidade = opcoes.Capacidade;
        int colunas = capacidade <= 2 ? 1 : 2;
        int faixasPorPagina = capacidade / colunas;
        double largura = GradeRelatorio.LarguraDoQuadro(colunas);

        var textos = fotos.Select(f => new Textos(
            f.Legenda.Trim().Length == 0 ? [] : TextoMedido.Quebrar(f.Legenda.Trim(), largura, GradeRelatorio.LetraDaLegenda, negrito: true),
            !comDescricao || f.Descricao.Trim().Length == 0 ? [] : TextoMedido.Quebrar(f.Descricao.Trim(), largura, GradeRelatorio.LetraDaDescricao, negrito: false))).ToList();

        // Altura do texto de cada faixa (o maior das fotos lado a lado) e a maior de todas.
        var alturaDoTexto = new List<double>();
        for (int i = 0; i < fotos.Count; i += colunas)
        {
            alturaDoTexto.Add(textos.Skip(i).Take(colunas).Max(AlturaDoTexto));
        }

        double maiorTexto = alturaDoTexto.Max();
        double corpo1 = GradeRelatorio.FimDoConteudo - cabecalho.TopoDasFotos(reduzido: false);

        // O quadro e o mesmo em todo o relatorio: o que cabe na primeira pagina (a de cabecalho maior) com o maior texto.
        double quadro = (corpo1 - faixasPorPagina * maiorTexto - (faixasPorPagina - 1) * GradeRelatorio.EspacoEntreLinhas) / faixasPorPagina;
        quadro = Math.Min(quadro, largura * ProporcaoMaxima);
        if (quadro < GradeRelatorio.MenorQuadro)
        {
            avisos.Add(Aviso.Impede("O cabeçalho ficou grande demais e não sobra espaço para as fotos. Encurte a descrição ou o objeto do relatório."));
            return Vazio();
        }

        var paginas = Montar(fotos, textos, alturaDoTexto, colunas, faixasPorPagina, quadro, cabecalho, moverUltimaFaixa: false);
        if (opcoes.Assinaturas && !CabemAssinaturas(paginas[^1]))
        {
            // Pagina unica: as fotos diminuem so o necessario. Mais paginas: a ultima faixa desce junto com as assinaturas.
            int faixasNaUltima = paginas[^1].Fotos.Select(f => f.Faixa).Distinct().Count();
            double falta = paginas[^1].BaseDasFotos + GradeRelatorio.BlocoDasAssinaturas - GradeRelatorio.FimDoConteudo;
            double menor = Math.Floor((quadro - falta / faixasNaUltima - 0.5) * 100) / 100;
            if (paginas.Count == 1 && menor >= Math.Max(GradeRelatorio.MenorQuadro, quadro * MenorEncolhimento))
            {
                quadro = menor;
                paginas = Montar(fotos, textos, alturaDoTexto, colunas, faixasPorPagina, quadro, cabecalho, moverUltimaFaixa: false);
                avisos.Add(Aviso.Info("As fotos ficaram um pouco menores para as assinaturas caberem na mesma página."));
            }
            else if (faixasNaUltima > 1 || paginas.Count > 1)
            {
                paginas = Montar(fotos, textos, alturaDoTexto, colunas, faixasPorPagina, quadro, cabecalho, moverUltimaFaixa: true);
                avisos.Add(Aviso.Info("A última linha de fotos foi para a página seguinte, junto com as assinaturas."));
            }
        }

        if (opcoes.Assinaturas)
        {
            var ultima = paginas[^1];
            var espaco = new Area(GradeRelatorio.MargemLateral, GradeRelatorio.FimDoConteudo - GradeRelatorio.AlturaDasAssinaturas,
                GradeRelatorio.Largura, GradeRelatorio.AlturaDasAssinaturas);
            paginas[^1] = ultima with { Assinaturas = new BlocoAssinaturas(espaco) };
            if (!CabemAssinaturas(ultima))
            {
                // Nao deveria acontecer (cada caso acima foi medido); se acontecer, nao gera nada torto.
                avisos.Add(Aviso.Impede("As assinaturas não couberam na página da última foto. Diminua as fotos por página."));
            }
        }

        avisos.AddRange(FaltaTexto(fotos, comDescricao));
        return new PlanoRelatorio(paginas, avisos, opcoes, dados, cabecalho);
    }

    /// <summary>Foto sem legenda (ou sem descricao, no modo com descricao) nao impede gerar, mas vale conferir.</summary>
    public static IEnumerable<Aviso> FaltaTexto(IReadOnlyList<FotoRelatorio> fotos, bool comDescricao)
    {
        static string Lista(List<int> numeros, int total)
        {
            var nomes = numeros.Take(6).Select(n => n.ToString(CultureInfo.InvariantCulture).PadLeft(Math.Max(2, total.ToString(CultureInfo.InvariantCulture).Length), '0')).ToList();
            string texto = nomes.Count == 1 ? nomes[0] : string.Join(", ", nomes.Take(nomes.Count - 1)) + " e " + nomes[^1];
            return numeros.Count > 6 ? string.Join(", ", nomes) + $" e mais {numeros.Count - 6}" : texto;
        }

        var semLegenda = fotos.Select((f, i) => (f, i)).Where(x => x.f.Legenda.Trim().Length == 0).Select(x => x.i + 1).ToList();
        if (semLegenda.Count > 0)
        {
            yield return Aviso.Conferir(semLegenda.Count == 1 ? $"A foto {Lista(semLegenda, fotos.Count)} está sem legenda." : $"{semLegenda.Count} fotos estão sem legenda: {Lista(semLegenda, fotos.Count)}.");
        }

        if (comDescricao)
        {
            var semDescricao = fotos.Select((f, i) => (f, i)).Where(x => x.f.Descricao.Trim().Length == 0).Select(x => x.i + 1).ToList();
            if (semDescricao.Count > 0)
            {
                yield return Aviso.Conferir(semDescricao.Count == 1 ? $"A foto {Lista(semDescricao, fotos.Count)} está sem descrição do serviço." : $"{semDescricao.Count} fotos estão sem descrição do serviço: {Lista(semDescricao, fotos.Count)}.");
            }
        }
    }

    private static bool CabemAssinaturas(PaginaRelatorio pagina) =>
        pagina.BaseDasFotos + GradeRelatorio.BlocoDasAssinaturas <= GradeRelatorio.FimDoConteudo + 0.01;

    private static double AlturaDaLegenda(int linhas) =>
        Math.Max(GradeRelatorio.LinhasMinimasDaLegenda, linhas) * GradeRelatorio.LetraDaLegenda * GradeRelatorio.AlturaDaLinhaDaLegenda;

    private static double AlturaDaDescricao(int linhas) =>
        linhas == 0 ? 0 : GradeRelatorio.RespiroDaDescricao + linhas * GradeRelatorio.LetraDaDescricao * GradeRelatorio.AlturaDaLinhaDaDescricao;

    private static double AlturaDoTexto(Textos t) =>
        GradeRelatorio.RespiroDaLegenda + AlturaDaLegenda(t.Legenda.Count) + AlturaDaDescricao(t.Descricao.Count);

    private static List<PaginaRelatorio> Montar(
        IReadOnlyList<FotoRelatorio> fotos, List<Textos> textos, List<double> alturaDoTexto, int colunas, int faixasPorPagina,
        double quadro, CabecalhoMontado cabecalho, bool moverUltimaFaixa)
    {
        var paginas = new List<PaginaRelatorio>();
        var naPagina = new List<FotoNaPagina>();
        double largura = GradeRelatorio.LarguraDoQuadro(colunas);
        int totalDeFaixas = alturaDoTexto.Count;
        double topo = cabecalho.TopoDasFotos(reduzido: false), y = topo;
        int faixasNaPagina = 0;

        void Fechar()
        {
            paginas.Add(new PaginaRelatorio(paginas.Count + 1, [.. naPagina], null, paginas.Count > 0, topo));
            naPagina.Clear();
            faixasNaPagina = 0;
            topo = y = cabecalho.TopoDasFotos(reduzido: true);
        }

        for (int faixa = 0; faixa < totalDeFaixas; faixa++)
        {
            double altura = quadro + alturaDoTexto[faixa];
            bool ultimaQueDesce = moverUltimaFaixa && faixa == totalDeFaixas - 1;
            if (faixasNaPagina > 0 && (faixasNaPagina >= faixasPorPagina || y + altura > GradeRelatorio.FimDoConteudo + 0.01 || ultimaQueDesce))
            {
                Fechar();
            }

            for (int c = 0; c < colunas && faixa * colunas + c < fotos.Count; c++)
            {
                int i = faixa * colunas + c;
                double x = GradeRelatorio.MargemLateral + GradeRelatorio.XDoQuadro(c, colunas);
                var caixa = new Area(x, y, largura, quadro);
                var t = textos[i];
                double yLegenda = y + quadro + GradeRelatorio.RespiroDaLegenda;
                var legenda = new Area(x, yLegenda, largura, AlturaDaLegenda(t.Legenda.Count));
                var descricao = new Area(x, legenda.Base + (t.Descricao.Count > 0 ? GradeRelatorio.RespiroDaDescricao : 0), largura,
                    t.Descricao.Count * GradeRelatorio.LetraDaDescricao * GradeRelatorio.AlturaDaLinhaDaDescricao);

                // A foto encaixada por dentro da borda fina do quadro.
                var dentro = new Area(caixa.X + 0.5, caixa.Y + 0.5, caixa.Largura - 1, caixa.Altura - 1);
                naPagina.Add(new FotoNaPagina(fotos[i], i + 1, new Area(x, y, largura, altura), caixa,
                    Encaixar(fotos[i].Largura, fotos[i].Altura, dentro), legenda, t.Legenda, descricao, t.Descricao, c, colunas, faixa));
            }

            y += altura + GradeRelatorio.EspacoEntreLinhas;
            faixasNaPagina++;
        }

        Fechar();
        return paginas;
    }

    /// <summary>"Foto 01", "Foto 02"... com a largura do maior numero.</summary>
    public static string Rotulo(int numero, int total) =>
        "Foto " + numero.ToString(CultureInfo.InvariantCulture).PadLeft(Math.Max(2, total.ToString(CultureInfo.InvariantCulture).Length), '0');

    /// <summary>
    /// Maior tamanho que cabe na caixa mantendo a proporcao (sem cortar nem distorcer), centralizado.
    /// Com <paramref name="encostarEmbaixo"/>, a sobra de altura fica toda em cima.
    /// </summary>
    public static Area Encaixar(int largura, int altura, Area caixa, bool encostarEmbaixo = false)
    {
        if (largura <= 0 || altura <= 0)
        {
            return caixa;
        }

        double escala = Math.Min(caixa.Largura / largura, caixa.Altura / altura);
        double l = largura * escala, a = altura * escala;
        double y = encostarEmbaixo ? caixa.Base - a : caixa.Y + (caixa.Altura - a) / 2;
        return new Area(caixa.X + (caixa.Largura - l) / 2, y, l, a);
    }
}
