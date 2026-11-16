using System.Globalization;

namespace Spobras.Core.Relatorio;

/// <summary>
/// Distribui as fotos nas paginas (especificacao 4.4, "Layout e paginacao"). A capacidade escolhida e o
/// maximo por pagina; texto longo ocupa mais linhas e empurra fotos para a pagina seguinte, sem nunca
/// reduzir a fonte nem cortar ou distorcer a foto.
/// </summary>
public static class PaginadorRelatorio
{
    /// <summary>Menor altura aceitavel da foto, em linhas, quando o texto precisa de mais espaco.</summary>
    private const int MinimoDeLinhasDaFoto = 5;

    public static PlanoRelatorio Paginar(IReadOnlyList<FotoRelatorio> fotos, OpcoesRelatorio opcoes, DadosRelatorio? dados = null)
    {
        var avisos = new List<Aviso>();
        dados ??= new DadosRelatorio();

        // Cada campo preenchido do cabecalho toma linhas do corpo de todas as paginas (nada fica escondido).
        var cabecalho = CabecalhoRelatorio.Montar(dados);
        int corpo = cabecalho.LinhasDoCorpo;
        double topo = cabecalho.Altura;
        if (fotos.Count == 0)
        {
            avisos.Add(Aviso.Impede("Escolha pelo menos uma foto."));
            return new PlanoRelatorio([], avisos, opcoes, dados, cabecalho);
        }

        if (!OpcoesRelatorio.Capacidades.Contains(opcoes.FotosPorPagina))
        {
            avisos.Add(Aviso.Impede("Fotos por página deve ser 1, 2, 4, 6 ou 8."));
            return new PlanoRelatorio([], avisos, opcoes, dados, cabecalho);
        }

        // Cabecalho enorme (descricao ou objeto muito longos) nao deixa espaco nem para uma foto: avisa em vez de quebrar.
        int minimoDoCorpo = MinimoDeLinhasDaFoto + 4 + (opcoes.Assinaturas ? GradeRelatorio.LinhasDasAssinaturas + 1 : 0);
        if (corpo < minimoDoCorpo)
        {
            avisos.Add(Aviso.Impede("O cabeçalho ficou grande demais e não sobra espaço para as fotos. Encurte a descrição ou o objeto do relatório."));
            return new PlanoRelatorio([], avisos, opcoes, dados, cabecalho);
        }

        int colunas = opcoes.FotosPorPagina <= 2 ? 1 : 2;
        int faixasPorPagina = opcoes.FotosPorPagina / colunas;
        int linhasDaFaixa = corpo / faixasPorPagina;

        // Todas as fotos do relatorio no mesmo quadro (como no modelo de referencia): o quadro desconta o
        // maior texto, mas nunca fica menor que 60% da faixa; faixa com texto maior so cresce para baixo.
        int maiorTexto = 0;
        for (int i = 0; i < fotos.Count; i += colunas)
        {
            maiorTexto = Math.Max(maiorTexto, fotos.Skip(i).Take(colunas)
                .Select((f, c) => Texto(f, i + c + 1, fotos.Count, opcoes, GradeRelatorio.LarguraDoEspaco(c, colunas)).Linhas).Max());
        }

        int linhasDoQuadro = Math.Max(MinimoDeLinhasDaFoto,
            Math.Max(linhasDaFaixa - maiorTexto, (int)Math.Ceiling(linhasDaFaixa * 0.6)));

        var paginas = new List<PaginaRelatorio>();
        var fotosDaPagina = new List<FotoNaPagina>();
        var continuacoes = new List<Continuacao>();
        int linhaLivre = 0; // proxima linha livre do corpo (0 a corpo)
        int faixasNaPagina = 0;
        int paginasComMenosFotos = 0;
        int textosContinuados = 0;

        void FecharPagina(BlocoAssinaturas? assinaturas = null)
        {
            paginas.Add(new PaginaRelatorio(paginas.Count + 1, [.. fotosDaPagina], [.. continuacoes], assinaturas));
            fotosDaPagina.Clear();
            continuacoes.Clear();
            linhaLivre = 0;
            faixasNaPagina = 0;
        }

        for (int i = 0; i < fotos.Count; i += colunas)
        {
            var daFaixa = fotos.Skip(i).Take(colunas).ToList();
            var textos = daFaixa.Select((f, c) => Texto(f, i + c + 1, fotos.Count, opcoes, GradeRelatorio.LarguraDoEspaco(c, colunas))).ToList();
            int linhasDoTexto = textos.Max(t => t.Linhas);

            int linhasDaFoto = Math.Min(linhasDoQuadro, Math.Max(MinimoDeLinhasDaFoto, corpo - linhasDoTexto));
            int necessario = linhasDaFoto + linhasDoTexto;

            // Texto maior que a pagina inteira: fica o que cabe e o resto continua na pagina seguinte.
            List<(string Titulo, string Resto, int Coluna)> restos = [];
            if (necessario > corpo)
            {
                int cabe = corpo - MinimoDeLinhasDaFoto;
                for (int c = 0; c < textos.Count; c++)
                {
                    if (textos[c].Linhas > cabe)
                    {
                        double largura = GradeRelatorio.LarguraDoEspaco(c, colunas);
                        int livres = cabe - 2; // uma linha de respiro e uma de folga para o "(continua...)"
                        var t = textos[c];
                        string rotulo = t.Titulo.Length > 0 ? Encurtar(t.Titulo, 60) : Rotulo(i + c + 1, fotos.Count);
                        if (t.LinhasDoTitulo < livres)
                        {
                            // a legenda cabe: a descricao continua na pagina seguinte
                            var (fica, resto) = DividirEmLinhas(t.Descricao, livres - t.LinhasDoTitulo, largura);
                            restos.Add((rotulo, resto, c));
                            textos[c] = t with { Descricao = fica + " (continua na próxima página)", Linhas = cabe };
                        }
                        else
                        {
                            // a propria legenda e maior que o espaco: ela continua, e a descricao vai junto
                            var (fica, resto) = DividirEmLinhas(t.Titulo, livres, largura);
                            string titulo = fica + " (continua na próxima página)";
                            restos.Add((rotulo, t.Descricao.Length > 0 ? resto + "\n" + t.Descricao : resto, c));
                            textos[c] = new TextoDaFoto(titulo, "", LinhasDe(titulo, largura), cabe);
                        }

                        textosContinuados++;
                    }
                }

                linhasDoTexto = textos.Max(t => t.Linhas);
                linhasDaFoto = MinimoDeLinhasDaFoto;
                necessario = linhasDaFoto + linhasDoTexto;
            }

            bool cheia = faixasNaPagina >= faixasPorPagina || linhaLivre + necessario > corpo;
            if (cheia && (fotosDaPagina.Count > 0 || continuacoes.Count > 0))
            {
                if (faixasNaPagina < faixasPorPagina)
                {
                    paginasComMenosFotos++;
                }

                FecharPagina();
            }

            for (int c = 0; c < daFaixa.Count; c++)
            {
                fotosDaPagina.Add(Posicionar(daFaixa[c], i + c + 1, c, colunas, linhaLivre, linhasDaFoto, linhasDoTexto, textos[c], topo));
            }

            linhaLivre += necessario;
            faixasNaPagina++;

            if (restos.Count > 0)
            {
                FecharPagina();
                foreach (var (titulo, restoInicial, _) in restos)
                {
                    // Texto maior que uma pagina inteira segue pagina a pagina (nada e cortado).
                    string resto = restoInicial;
                    while (resto.Length > 0)
                    {
                        int livres = corpo - linhaLivre - 1; // a primeira linha e o titulo da continuacao
                        if (livres < 2)
                        {
                            FecharPagina();
                            continue;
                        }

                        string parte = resto;
                        if (LinhasDe(resto, GradeRelatorio.Largura) > livres)
                        {
                            (parte, resto) = DividirEmLinhas(resto, livres - 1, GradeRelatorio.Largura);
                            if (parte.Length == 0)
                            {
                                (parte, resto) = (resto, "");
                            }
                        }
                        else
                        {
                            resto = "";
                        }

                        int linhas = Math.Min(corpo - linhaLivre, LinhasDe(parte, GradeRelatorio.Largura) + 1);
                        var area = new Area(0, topo + linhaLivre * GradeRelatorio.AlturaDaLinha,
                            GradeRelatorio.Largura, linhas * GradeRelatorio.AlturaDaLinha);
                        continuacoes.Add(new Continuacao(titulo + " (continuação)", parte, linhaLivre, linhas, area));
                        linhaLivre += linhas + 1;
                        if (resto.Length > 0)
                        {
                            FecharPagina();
                        }
                    }
                }
            }
        }

        BlocoAssinaturas? assinaturas = null;
        if (opcoes.Assinaturas)
        {
            if (linhaLivre + GradeRelatorio.LinhasDasAssinaturas + 1 > corpo)
            {
                FecharPagina();
                avisos.Add(Aviso.Info("As assinaturas não couberam junto das últimas fotos e foram para uma página própria."));
            }

            // Sempre no pe da pagina, sem encostar nas fotos.
            int inicio = corpo - GradeRelatorio.LinhasDasAssinaturas;
            assinaturas = new BlocoAssinaturas(inicio, GradeRelatorio.LinhasDasAssinaturas,
                new Area(0, topo + inicio * GradeRelatorio.AlturaDaLinha,
                    GradeRelatorio.Largura, GradeRelatorio.LinhasDasAssinaturas * GradeRelatorio.AlturaDaLinha));
        }

        FecharPagina(assinaturas);

        if (paginasComMenosFotos > 0)
        {
            avisos.Add(Aviso.Info(
                $"{paginasComMenosFotos} página(s) ficaram com menos de {opcoes.FotosPorPagina} fotos para o texto caber inteiro, sem diminuir a letra."));
        }

        if (textosContinuados > 0)
        {
            avisos.Add(Aviso.Conferir($"{textosContinuados} texto(s) de foto são maiores que o espaço da página e continuam na página seguinte."));
        }

        return new PlanoRelatorio(paginas, avisos, opcoes, dados, cabecalho);
    }

    /// <summary>"Foto 01", "Foto 02"... com a largura do maior numero.</summary>
    public static string Rotulo(int numero, int total) =>
        "Foto " + numero.ToString(CultureInfo.InvariantCulture).PadLeft(Math.Max(2, total.ToString(CultureInfo.InvariantCulture).Length), '0');

    private sealed record TextoDaFoto(string Titulo, string Descricao, int LinhasDoTitulo, int Linhas);

    /// <summary>
    /// Texto embaixo da foto: so a legenda que o usuario escreveu (sem "Foto 01" automatico) e, no modo descricao,
    /// a descricao do servico. Sem nenhum dos dois, fica so a linha de respiro e a foto ganha o espaco.
    /// </summary>
    private static TextoDaFoto Texto(FotoRelatorio foto, int numero, int total, OpcoesRelatorio opcoes, double largura)
    {
        string titulo = foto.Legenda.Trim();
        int linhasDoTitulo = titulo.Length == 0 ? 0 : LinhasDe(titulo, largura);
        string descricao = opcoes.Modo == ModoLegenda.DescricaoServico ? foto.Descricao.Trim() : "";
        int linhasDaDescricao = descricao.Length == 0 ? 0 : LinhasDe(descricao, largura);

        // Uma linha de respiro depois do texto separa esta faixa da seguinte.
        return new TextoDaFoto(titulo, descricao, linhasDoTitulo, linhasDoTitulo + linhasDaDescricao + 1);
    }

    private static FotoNaPagina Posicionar(
        FotoRelatorio foto, int numero, int coluna, int colunas, int linha, int linhasDaFoto, int linhasDoTexto, TextoDaFoto texto, double topo)
    {
        var (primeira, _) = GradeRelatorio.ColunasDoEspaco(coluna, colunas);
        double x = GradeRelatorio.XDaColuna(primeira);
        double y = topo + linha * GradeRelatorio.AlturaDaLinha;
        double largura = GradeRelatorio.LarguraDoEspaco(coluna, colunas);
        double alturaDaFoto = linhasDaFoto * GradeRelatorio.AlturaDaLinha;

        var espaco = new Area(x, y, largura, (linhasDaFoto + linhasDoTexto) * GradeRelatorio.AlturaDaLinha);
        var caixa = new Area(x + GradeRelatorio.Folga / 2, y + GradeRelatorio.Folga / 2, largura - GradeRelatorio.Folga, alturaDaFoto - GradeRelatorio.Folga);
        var areaDoTexto = new Area(x, y + alturaDaFoto, largura, linhasDoTexto * GradeRelatorio.AlturaDaLinha);

        return new FotoNaPagina(foto, numero, espaco, Encaixar(foto.Largura, foto.Altura, caixa), areaDoTexto,
            linha, linhasDaFoto, linhasDoTexto, coluna, colunas, texto.Titulo, texto.Descricao);
    }

    /// <summary>
    /// Maior tamanho que cabe na caixa mantendo a proporcao (sem cortar nem distorcer), centralizado.
    /// Com <paramref name="encostarEmbaixo"/>, a sobra de altura fica toda em cima (a foto fica junto da legenda).
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

    private static int CaracteresPorLinha(double largura) =>
        Math.Max(10, (int)((largura - 2 * GradeRelatorio.Folga) / GradeRelatorio.LarguraDoCaractere));

    /// <summary>Linhas que um texto ocupa com quebra automatica por palavra (estimativa para mais).</summary>
    public static int LinhasDe(string texto, double largura)
    {
        int porLinha = CaracteresPorLinha(largura);
        int linhas = 0;
        foreach (string paragrafo in texto.Replace("\r", "").Split('\n'))
        {
            int usados = 0;
            int linhasDoParagrafo = 1;
            foreach (string palavra in paragrafo.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                int tamanho = palavra.Length;
                if (usados > 0 && usados + 1 + tamanho > porLinha)
                {
                    linhasDoParagrafo++;
                    usados = 0;
                }

                // Palavra maior que a linha ocupa varias.
                while (tamanho > porLinha)
                {
                    linhasDoParagrafo++;
                    tamanho -= porLinha;
                }

                usados += (usados > 0 ? 1 : 0) + tamanho;
            }

            linhas += linhasDoParagrafo;
        }

        return Math.Max(1, linhas);
    }

    /// <summary>Maior comeco do texto (cortado em espaco) que ocupa ate <paramref name="linhas"/> linhas.</summary>
    private static (string Fica, string Resto) DividirEmLinhas(string texto, int linhas, double largura)
    {
        linhas = Math.Max(1, linhas);
        if (LinhasDe(texto, largura) <= linhas)
        {
            return (texto, "");
        }

        int caracteres = linhas * CaracteresPorLinha(largura);
        while (true)
        {
            var (fica, resto) = Dividir(texto, caracteres);
            if (caracteres <= 1 || LinhasDe(fica, largura) <= linhas)
            {
                return (fica, resto);
            }

            caracteres = Math.Max(1, caracteres * 9 / 10);
        }
    }

    private static string Encurtar(string texto, int maximo) => texto.Length <= maximo ? texto : texto[..maximo].TrimEnd() + "…";

    private static (string Fica, string Resto) Dividir(string texto, int caracteres)
    {
        caracteres = Math.Max(1, caracteres);
        if (texto.Length <= caracteres)
        {
            return (texto, "");
        }

        int corte = texto.LastIndexOf(' ', Math.Max(0, Math.Min(caracteres, texto.Length - 1)));
        if (corte <= 0)
        {
            corte = caracteres;
        }

        return (texto[..corte].TrimEnd(), texto[corte..].TrimStart());
    }
}
