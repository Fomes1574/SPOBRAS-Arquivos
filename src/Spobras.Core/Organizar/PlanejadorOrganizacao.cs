using System.Globalization;
using Spobras.Core.Abstracoes;
using Spobras.Core.Nomes;
using Spobras.Core.Operacoes;

namespace Spobras.Core.Organizar;

/// <summary>
/// Monta as sugestoes do organizador (especificacao 4.1, entrega inicial: nome, extensao,
/// pasta de origem e regras explicitas). So sugere: mover depende de confirmacao na previa.
/// </summary>
public sealed class PlanejadorOrganizacao(IArquivos arquivos)
{
    private static readonly CompareInfo Comparador = CultureInfo.InvariantCulture.CompareInfo;
    private const CompareOptions SemAcentoNemCaixa = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;

    /// <param name="protegidos">
    /// Arquivos e pastas que o organizador nunca toca: planilha mestra, pasta das planilhas base,
    /// modelos ativos e arquivos internos do app.
    /// </param>
    public IReadOnlyList<Sugestao> Planejar(
        IReadOnlyList<ArquivoParaOrganizar> entradas,
        IReadOnlyList<Regra> regras,
        IReadOnlyList<string> protegidos)
    {
        var ativas = regras.Where(r => r.Ativa).ToList();
        var reservados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sugestoes = new List<Sugestao>(entradas.Count);

        foreach (var entrada in entradas)
        {
            string tipo = TipoArquivo.Descrever(entrada.Caminho);

            if (protegidos.Any(p => DentroDe(entrada.Caminho, p)))
            {
                sugestoes.Add(Ignorar(entrada, tipo, "Arquivo protegido: o organizador não mexe na planilha mestra, nos modelos nem nos arquivos do app."));
                continue;
            }

            if (TipoArquivo.Temporario(entrada.Caminho))
            {
                sugestoes.Add(Ignorar(entrada, tipo, "Arquivo temporário ou download ainda em andamento."));
                continue;
            }

            var servem = ativas.Where(r => Serve(r, entrada.Caminho)).ToList();
            if (servem.Count == 0)
            {
                sugestoes.Add(new Sugestao(entrada.Caminho, tipo, "Nenhuma regra serve para este arquivo", OrigemSugestao.VoceDecide,
                    null, null, "Você decide. O app pode guardar a sua escolha, se você pedir.", null));
                continue;
            }

            int maior = servem.Max(r => r.Prioridade);
            var vencedoras = servem.Where(r => r.Prioridade == maior).ToList();
            var destinos = vencedoras
                .Select(r => ResolverDestino(r.Destino, entrada))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (destinos.Count > 1)
            {
                string nomes = string.Join(", ", vencedoras.Select(r => $"\"{r.Nome}\""));
                sugestoes.Add(new Sugestao(entrada.Caminho, tipo, $"Regras com a mesma prioridade apontam pastas diferentes: {nomes}",
                    OrigemSugestao.VoceDecide, null, null, "Você decide. Ajuste a prioridade das regras para o app escolher sozinho.", null));
                continue;
            }

            var regra = vencedoras[0];
            string pasta = destinos[0];
            if (string.Equals(Normalizar(pasta), Normalizar(Path.GetDirectoryName(entrada.Caminho) ?? ""), StringComparison.OrdinalIgnoreCase))
            {
                sugestoes.Add(Ignorar(entrada, tipo, $"Já está na pasta da regra \"{regra.Nome}\"."));
                continue;
            }

            // Nome repetido no destino (ou dentro deste mesmo lote) ganha (2), (3). Nada e sobrescrito.
            string nome = Path.GetFileName(entrada.Caminho);
            string livre = NomeArquivo.Livre(nome, n =>
                arquivos.ArquivoExiste(Path.Combine(pasta, n)) || reservados.Contains(Path.Combine(pasta, n)));
            string destino = Path.Combine(pasta, livre);
            reservados.Add(destino);

            string achou = DescreverCondicao(regra, entrada.Caminho);
            string motivo = $"Regra \"{regra.Nome}\"" + (livre != nome ? $". Já existe \"{nome}\" lá; vai como \"{livre}\"." : "");
            sugestoes.Add(new Sugestao(entrada.Caminho, tipo, achou, OrigemSugestao.Regra, pasta, destino, motivo, regra.Id));
        }

        return sugestoes;
    }

    /// <summary>Lote de mover a partir das sugestoes que o usuario confirmou na previa.</summary>
    public static Lote CriarLote(IEnumerable<Sugestao> confirmadas, IRelogio relogio)
    {
        var itens = confirmadas
            .Where(s => s.CaminhoDestino is not null)
            .Select((s, ordem) => new ItemLote { Ordem = ordem, Origem = s.Caminho, Destino = s.CaminhoDestino! })
            .ToList();
        var pastas = itens.Select(i => Path.GetFileName(Path.GetDirectoryName(i.Destino))).Distinct().ToList();
        string para = pastas.Count == 1 ? $"para a pasta {pastas[0]}" : $"para {pastas.Count} pastas";
        return new Lote
        {
            Tipo = TipoLote.Mover,
            Descricao = $"Moveu {itens.Count} arquivo(s) {para}",
            CriadoEm = relogio.Agora,
            Itens = itens,
        };
    }

    /// <summary>
    /// Sugestao para o usuario que escolheu a pasta na mao: mesmo arquivo, destino escolhido.
    /// Nao cria regra nenhuma (uma escolha isolada nao vira regra geral).
    /// </summary>
    public Sugestao ComDestinoEscolhido(Sugestao sugestao, string pasta)
    {
        string nome = Path.GetFileName(sugestao.Caminho);
        string livre = NomeArquivo.Livre(nome, n => arquivos.ArquivoExiste(Path.Combine(pasta, n)));
        return sugestao with
        {
            Origem = OrigemSugestao.Regra,
            PastaDestino = pasta,
            CaminhoDestino = Path.Combine(pasta, livre),
            Motivo = "Você escolheu" + (livre != nome ? $". Já existe \"{nome}\" lá; vai como \"{livre}\"." : ""),
            RegraId = null,
        };
    }

    /// <summary>
    /// Problemas nas regras: destino que cai numa pasta vigiada (o arquivo voltaria para a fila) e
    /// regras de mesma prioridade que podem disputar o mesmo arquivo.
    /// </summary>
    public static IReadOnlyList<Aviso> ValidarRegras(IReadOnlyList<Regra> regras, IReadOnlyList<string> pastasVigiadas)
    {
        var avisos = new List<Aviso>();
        foreach (var regra in regras.Where(r => r.Ativa))
        {
            if (string.IsNullOrWhiteSpace(regra.Destino))
            {
                avisos.Add(Aviso.Impede($"A regra \"{regra.Nome}\" não tem pasta de destino."));
                continue;
            }

            if (regra.NomeContem.Count == 0 && regra.Extensoes.Count == 0 && string.IsNullOrWhiteSpace(regra.PastaOrigemContem))
            {
                avisos.Add(Aviso.Conferir($"A regra \"{regra.Nome}\" não tem condição: ela serve para qualquer arquivo."));
            }

            string fixo = Normalizar(regra.Destino.Split('{')[0]);
            if (pastasVigiadas.Any(v => string.Equals(Normalizar(v), fixo, StringComparison.OrdinalIgnoreCase)))
            {
                avisos.Add(Aviso.Impede($"A regra \"{regra.Nome}\" manda arquivos para uma pasta vigiada; eles voltariam para a fila."));
            }
        }

        return avisos;
    }

    private static Sugestao Ignorar(ArquivoParaOrganizar entrada, string tipo, string motivo) =>
        new(entrada.Caminho, tipo, motivo, OrigemSugestao.Ignorado, null, null, motivo, null);

    private static bool Serve(Regra regra, string caminho)
    {
        string nome = Path.GetFileName(caminho);
        if (regra.Extensoes.Count > 0)
        {
            string extensao = Path.GetExtension(nome).TrimStart('.');
            if (!regra.Extensoes.Any(e => string.Equals(e.TrimStart('.'), extensao, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }
        }

        if (regra.NomeContem.Count > 0 && !regra.NomeContem.Any(t => Contem(nome, t)))
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(regra.PastaOrigemContem)
               || Contem(Path.GetDirectoryName(caminho) ?? "", regra.PastaOrigemContem);
    }

    private static string DescreverCondicao(Regra regra, string caminho)
    {
        string nome = Path.GetFileName(caminho);
        var partes = new List<string>();
        string? trecho = regra.NomeContem.FirstOrDefault(t => Contem(nome, t));
        if (trecho is not null)
        {
            partes.Add($"nome contém \"{trecho}\"");
        }

        if (regra.Extensoes.Count > 0)
        {
            partes.Add($"extensão {Path.GetExtension(nome).ToLowerInvariant()}");
        }

        if (!string.IsNullOrWhiteSpace(regra.PastaOrigemContem))
        {
            partes.Add($"veio de pasta com \"{regra.PastaOrigemContem}\"");
        }

        return partes.Count == 0 ? "Regra sem condição" : "Pelo " + string.Join(" e ", partes).Replace("Pelo extensão", "Pela extensão");
    }

    private static string ResolverDestino(string modelo, ArquivoParaOrganizar entrada)
    {
        var data = entrada.ChegouEm ?? entrada.ModificadoEm;
        string ano = data?.ToString("yyyy", CultureInfo.InvariantCulture) ?? "sem data";
        string mes = data?.ToString("MM", CultureInfo.InvariantCulture) ?? "sem data";
        return modelo
            .Replace("{ano}", ano, StringComparison.OrdinalIgnoreCase)
            .Replace("{mes}", mes, StringComparison.OrdinalIgnoreCase);
    }

    private static bool Contem(string texto, string trecho) =>
        trecho.Length > 0 && Comparador.IndexOf(texto, trecho, SemAcentoNemCaixa) >= 0;

    private static string Normalizar(string caminho) => caminho.TrimEnd('\\', '/', ' ');

    private static bool DentroDe(string caminho, string protegido)
    {
        string a = Normalizar(caminho), b = Normalizar(protegido);
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase)
               || (a.Length > b.Length
                   && a.StartsWith(b, StringComparison.OrdinalIgnoreCase)
                   && a[b.Length] is '\\' or '/');
    }
}
