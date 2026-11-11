using System.Globalization;
using System.Text.RegularExpressions;
using Spobras.Core.Abstracoes;
using Spobras.Core.Nomes;

namespace Spobras.Core.Renomear;

/// <summary>
/// Monta a previa de uma renomeacao em lote (especificacao 4.2). Nao toca no disco alem de consultar
/// o que ja existe nas pastas.
/// </summary>
public sealed class PlanejadorRenomear(IArquivos arquivos)
{
    public PlanoRenomear Planejar(IReadOnlyList<ItemRenomear> itens, OpcoesRenomear opcoes)
    {
        var avisos = new List<Aviso>();
        if (itens.Count == 0)
        {
            avisos.Add(Aviso.Impede("Escolha pelo menos um arquivo."));
            return new PlanoRenomear([], avisos);
        }

        bool precisaBase = opcoes.Modo != ModoRenomear.NomesLivres;
        if (precisaBase && string.IsNullOrWhiteSpace(opcoes.NomeBase) && itens.Any(i => i.NomeEditado is null))
        {
            avisos.Add(Aviso.Impede("Digite o nome."));
        }

        var ordenados = Ordenar(itens, opcoes);
        var pedidos = CalcularNomes(ordenados, opcoes, avisos);

        // Nomes ja tomados em cada pasta, tirando os arquivos do proprio lote
        // (eles vao sair do nome atual, o que permite trocas A <-> B).
        var doLote = new HashSet<string>(ordenados.Select(i => i.Caminho), StringComparer.OrdinalIgnoreCase);
        var ocupadosPorPasta = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        HashSet<string> Ocupados(string pasta)
        {
            if (!ocupadosPorPasta.TryGetValue(pasta, out var nomes))
            {
                nomes = new HashSet<string>(
                    arquivos.ListarArquivos(pasta).Where(n => !doLote.Contains(Path.Combine(pasta, n))),
                    StringComparer.OrdinalIgnoreCase);
                ocupadosPorPasta[pasta] = nomes;
            }

            return nomes;
        }

        var linhas = new List<LinhaRenomear>(ordenados.Count);
        var impedidos = new List<int>();
        for (int i = 0; i < ordenados.Count; i++)
        {
            var item = ordenados[i];
            string pasta = Path.GetDirectoryName(item.Caminho) ?? "";
            string atual = Path.GetFileName(item.Caminho);
            string pedido = pedidos[i];

            if (!arquivos.ArquivoExiste(item.Caminho))
            {
                linhas.Add(new LinhaRenomear(item.Caminho, atual, pedido, SituacaoRenomear.Impedido, "O arquivo não está mais na pasta."));
                continue;
            }

            string? problema = NomeArquivo.Problema(pedido, pasta);
            if (problema is not null)
            {
                string sugestao = NomeArquivo.Corrigir(pedido);
                string dica = sugestao.Length > 0 && NomeArquivo.Problema(sugestao, pasta) is null && sugestao != pedido
                    ? $" Sugestão: \"{sugestao}\"."
                    : "";
                linhas.Add(new LinhaRenomear(item.Caminho, atual, pedido, SituacaoRenomear.Impedido, problema + dica));
                continue;
            }

            if (string.Equals(pedido, atual, StringComparison.Ordinal))
            {
                // Continua no lugar: o nome dele fica ocupado para os outros.
                Ocupados(pasta).Add(atual);
                linhas.Add(new LinhaRenomear(item.Caminho, atual, pedido, SituacaoRenomear.SemMudanca, "O nome já é este."));
                continue;
            }

            linhas.Add(new LinhaRenomear(item.Caminho, atual, pedido, SituacaoRenomear.Ok, null));
        }

        // Um arquivo impedido continua onde esta: o nome dele tambem fica ocupado.
        foreach (var linha in linhas.Where(l => l.Situacao == SituacaoRenomear.Impedido))
        {
            Ocupados(Path.GetDirectoryName(linha.Caminho) ?? "").Add(linha.NomeAtual);
        }

        // Colisoes: com o que ja existe e dentro do proprio lote. Nunca sobrescreve; sugere (2), (3).
        for (int i = 0; i < linhas.Count; i++)
        {
            var linha = linhas[i];
            if (linha.Situacao != SituacaoRenomear.Ok)
            {
                continue;
            }

            var ocupados = Ocupados(Path.GetDirectoryName(linha.Caminho) ?? "");
            string livre = NomeArquivo.Livre(linha.NomeNovo, ocupados.Contains);
            ocupados.Add(livre);
            if (!string.Equals(livre, linha.NomeNovo, StringComparison.Ordinal))
            {
                linhas[i] = string.Equals(livre, linha.NomeAtual, StringComparison.Ordinal)
                    ? linha with { NomeNovo = livre, Situacao = SituacaoRenomear.SemMudanca, Motivo = "O nome já é este." }
                    : linha with
                    {
                        NomeNovo = livre,
                        Situacao = SituacaoRenomear.Ajustado,
                        Motivo = $"Já existe \"{linha.NomeNovo}\"; sugerido \"{livre}\" para não sobrescrever.",
                    };
            }
        }

        if (linhas.Any(l => l.Situacao == SituacaoRenomear.Impedido))
        {
            avisos.Add(Aviso.Conferir(
                $"{linhas.Count(l => l.Situacao == SituacaoRenomear.Impedido)} arquivo(s) não podem ser renomeados; corrija o nome ou execute só os válidos."));
        }

        return new PlanoRenomear(linhas, avisos);
    }

    /// <summary>
    /// Maior numero que a pasta ja usa no padrao "base + separador + numero" (0 se nenhum).
    /// So contam nomes que seguem exatamente o padrao.
    /// </summary>
    public int MaiorNumeroNaPasta(string pasta, string nomeBase, string separador, IReadOnlySet<string>? ignorar = null)
    {
        var padrao = new Regex(
            "^" + Regex.Escape(nomeBase + separador) + @"(\d+)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        int maior = 0;
        foreach (string nome in arquivos.ListarArquivos(pasta))
        {
            if (ignorar is not null && ignorar.Contains(Path.Combine(pasta, nome)))
            {
                continue;
            }

            var m = padrao.Match(Path.GetFileNameWithoutExtension(nome));
            if (m.Success && int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int n))
            {
                maior = Math.Max(maior, n);
            }
        }

        return maior;
    }

    private static List<ItemRenomear> Ordenar(IReadOnlyList<ItemRenomear> itens, OpcoesRenomear opcoes) => opcoes.Ordem switch
    {
        OrdemRenomear.NomeNatural => [.. itens.OrderBy(i => Path.GetFileName(i.Caminho), OrdemNatural.Instancia)],
        // Sem data vai para o fim; empate decide pelo nome.
        OrdemRenomear.Data => [.. itens
            .OrderBy(i => i.Data is null)
            .ThenBy(i => i.Data)
            .ThenBy(i => Path.GetFileName(i.Caminho), OrdemNatural.Instancia)],
        _ => [.. itens],
    };

    private List<string> CalcularNomes(List<ItemRenomear> itens, OpcoesRenomear opcoes, List<Aviso> avisos)
    {
        int primeiro = opcoes.NumeroInicial;
        if (opcoes is { Modo: ModoRenomear.NomeNumero, Numerar: true, ContinuarNumeracao: true })
        {
            var doLote = new HashSet<string>(itens.Select(i => i.Caminho), StringComparer.OrdinalIgnoreCase);
            int maior = itens
                .Select(i => Path.GetDirectoryName(i.Caminho) ?? "")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Max(p => MaiorNumeroNaPasta(p, opcoes.NomeBase, opcoes.Separador, doLote));
            if (maior > 0)
            {
                primeiro = maior + 1;
                avisos.Add(Aviso.Info($"A pasta já tem \"{opcoes.NomeBase}\" até o número {maior}, então os novos começam no {primeiro}."));
            }
            else
            {
                avisos.Add(Aviso.Info($"A pasta ainda não tem \"{opcoes.NomeBase}\" numerado; a numeração começa no {primeiro}."));
            }
        }

        if (opcoes.Modo == ModoRenomear.NomeNumero && opcoes.Numerar && primeiro < 0)
        {
            avisos.Add(Aviso.Impede("O número inicial não pode ser negativo."));
            primeiro = 0;
        }

        // A largura acompanha o maior numero do lote (01..99, 001..120).
        int ultimo = primeiro + itens.Count - 1;
        int largura = opcoes.ZeroEsquerda ? Math.Max(2, ultimo.ToString(CultureInfo.InvariantCulture).Length) : 1;

        var nomes = new List<string>(itens.Count);
        for (int i = 0; i < itens.Count; i++)
        {
            var item = itens[i];
            string extensao = Path.GetExtension(item.Caminho);

            if (item.NomeEditado is not null)
            {
                nomes.Add(ComExtensao(item.NomeEditado.Trim(), extensao, opcoes.PreservarExtensao));
                continue;
            }

            string corpo = opcoes.Modo switch
            {
                ModoRenomear.NomeNumero => opcoes.Numerar
                    ? opcoes.NomeBase + opcoes.Separador + (primeiro + i).ToString(CultureInfo.InvariantCulture).PadLeft(largura, '0')
                    : opcoes.NomeBase,
                ModoRenomear.NomeComplemento => string.IsNullOrWhiteSpace(item.Complemento)
                    ? opcoes.NomeBase
                    : opcoes.NomeBase + opcoes.Separador + item.Complemento.Trim(),
                _ => item.NomeLivre?.Trim() ?? Path.GetFileNameWithoutExtension(item.Caminho),
            };

            corpo = AplicarOpcoesDePerfil(corpo, item, opcoes);
            nomes.Add(ComExtensao(corpo, extensao, opcoes.PreservarExtensao));
        }

        return nomes;
    }

    private static string AplicarOpcoesDePerfil(string corpo, ItemRenomear item, OpcoesRenomear opcoes)
    {
        if (!string.IsNullOrEmpty(opcoes.SubstituirDe))
        {
            corpo = corpo.Replace(opcoes.SubstituirDe, opcoes.SubstituirPor, StringComparison.OrdinalIgnoreCase);
        }

        if (opcoes.RemoverAcentos)
        {
            corpo = NomeArquivo.RemoverAcentos(corpo);
        }

        if (opcoes.PosicaoData != PosicaoData.Nenhuma && item.Data is { } data)
        {
            // Formato ordenavel (especificacao 3.2).
            string texto = data.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            corpo = opcoes.PosicaoData == PosicaoData.Inicio ? $"{texto} {corpo}" : $"{corpo} {texto}";
        }

        if (opcoes.RemoverEspacosExcedentes)
        {
            corpo = NomeArquivo.RemoverEspacosExcedentes(corpo);
        }

        return corpo;
    }

    private static string ComExtensao(string corpo, string extensao, bool preservar)
    {
        if (!preservar || extensao.Length == 0)
        {
            return corpo;
        }

        // Quem colou o nome ja com a extensao nao ganha a extensao duas vezes.
        return corpo.EndsWith(extensao, StringComparison.OrdinalIgnoreCase) ? corpo : corpo + extensao;
    }
}
