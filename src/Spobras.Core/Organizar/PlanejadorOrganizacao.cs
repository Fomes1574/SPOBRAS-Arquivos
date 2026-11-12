using System.Globalization;
using Spobras.Core.Abstracoes;
using Spobras.Core.Nomes;
using Spobras.Core.Operacoes;

namespace Spobras.Core.Organizar;

/// <summary>
/// Monta as sugestoes do organizador (especificacao 4.1, entrega inicial: nome, extensao,
/// pasta de origem e regras explicitas). So sugere: mover depende de confirmacao na previa.
/// </summary>
/// <param name="lerConteudo">Texto de dentro do arquivo (ou o motivo de nao ter lido); null = nao le conteudo.</param>
/// <param name="reconhecedor">Contratos e escolas da base, para {contrato}, {escola}, {lote} e {portaria}.</param>
public sealed class PlanejadorOrganizacao(
    IArquivos arquivos, Func<string, Spobras.Core.Ferramentas.TextoIndexado>? lerConteudo = null, ReconhecedorObras? reconhecedor = null)
{
    private static readonly string[] MarcadoresDaBase = ["{contrato}", "{escola}", "{lote}", "{portaria}"];
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

            var analise = new AnaliseArquivo(entrada.Caminho, lerConteudo, reconhecedor);
            var servem = ativas.Where(r => Serve(r, entrada.Caminho) && ServeConteudo(r, analise)).ToList();
            if (servem.Count == 0)
            {
                string porque = analise.MotivoSemConteudo is { } m && ativas.Any(r => r.ConteudoContem.Count > 0 && Serve(r, entrada.Caminho))
                    ? $"Nenhuma regra serve. O conteúdo não foi lido ({m}); o arquivo continua podendo ser organizado pelo nome."
                    : "Nenhuma regra serve para este arquivo";
                sugestoes.Add(new Sugestao(entrada.Caminho, tipo, porque, OrigemSugestao.VoceDecide,
                    null, null, "Você decide. O app pode guardar a sua escolha, se você pedir.", null));
                continue;
            }

            int maior = servem.Max(r => r.Prioridade);
            var vencedoras = servem.Where(r => r.Prioridade == maior).ToList();
            var resolvidos = vencedoras.Select(r => (Regra: r, Destino: ResolverDestino(r.Destino, entrada, analise, out string? falta), Falta: falta)).ToList();
            if (resolvidos.FirstOrDefault(r => r.Falta is not null) is { Regra: not null } pendente)
            {
                sugestoes.Add(new Sugestao(entrada.Caminho, tipo, pendente.Falta!, OrigemSugestao.VoceDecide, null, null,
                    $"Você decide. A regra \"{pendente.Regra.Nome}\" precisa saber a obra do arquivo.", null));
                continue;
            }

            var destinos = resolvidos.Select(r => r.Destino).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
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

            string achou = DescreverCondicao(regra, entrada.Caminho, analise);
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

    private static string DescreverCondicao(Regra regra, string caminho, AnaliseArquivo analise)
    {
        string basico = DescreverCondicao(regra, caminho);
        var extras = new List<string>();
        if (regra.ConteudoContem.FirstOrDefault(t => analise.Conteudo is { } c && Contem(c, t)) is { } dentro)
        {
            extras.Add($"conteúdo tem \"{dentro}\"");
        }

        if (MarcadoresDaBase.Any(m => regra.Destino.Contains(m, StringComparison.OrdinalIgnoreCase)) && analise.Obras.Count > 0)
        {
            extras.Add("achou " + analise.Obras[0].Evidencia);
        }

        return extras.Count == 0 ? basico : (basico == "Regra sem condição" ? "Pelo " : basico + "; ") + string.Join("; ", extras);
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

    private static string ResolverDestino(string modelo, ArquivoParaOrganizar entrada) => ResolverDestino(modelo, entrada, null, out _);

    private static string ResolverDestino(string modelo, ArquivoParaOrganizar entrada, AnaliseArquivo? analise, out string? falta)
    {
        falta = null;
        var data = entrada.ChegouEm ?? entrada.ModificadoEm;
        string ano = data?.ToString("yyyy", CultureInfo.InvariantCulture) ?? "sem data";
        string mes = data?.ToString("MM", CultureInfo.InvariantCulture) ?? "sem data";
        string destino = modelo
            .Replace("{ano}", ano, StringComparison.OrdinalIgnoreCase)
            .Replace("{mes}", mes, StringComparison.OrdinalIgnoreCase);
        var usados = MarcadoresDaBase.Where(m => destino.Contains(m, StringComparison.OrdinalIgnoreCase)).ToList();
        if (usados.Count == 0 || analise is null)
        {
            return destino;
        }

        var obras = analise.Obras;
        if (obras.Count == 0)
        {
            falta = analise.SemBase
                ? "A regra usa dados da obra, mas a Base de dados ainda está vazia"
                : "Não achou contrato nem escola da Base de dados no nome" + (analise.Conteudo is null ? "" : " nem no conteúdo");
            return destino;
        }

        foreach (string marcador in usados)
        {
            var valores = obras.Select(o => marcador switch
            {
                "{contrato}" => o.Contrato,
                "{escola}" => o.Escola,
                "{lote}" => o.Lote,
                _ => o.Portaria,
            }).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            string nome = marcador.Trim('{', '}');
            if (valores.Count != 1 || string.IsNullOrWhiteSpace(valores[0]))
            {
                falta = valores.Count > 1
                    ? $"Mais de uma obra combina ({string.Join("; ", obras.Select(o => o.Evidencia).Distinct().Take(3))}): {nome} não é único"
                    : $"Achou {obras[0].Evidencia}, mas falta {nome} para montar a pasta";
                return destino;
            }

            destino = destino.Replace(marcador, NomeArquivo.Corrigir(valores[0]!.Replace('/', '-')).Trim(), StringComparison.OrdinalIgnoreCase);
        }

        return destino;
    }

    private static bool ServeConteudo(Regra regra, AnaliseArquivo analise) =>
        regra.ConteudoContem.Count == 0 || (analise.Conteudo is { } texto && regra.ConteudoContem.Any(t => Contem(texto, t)));

    /// <summary>O que o app sabe do arquivo alem do nome: conteudo (lido so se alguma regra precisar) e obras reconhecidas.</summary>
    private sealed class AnaliseArquivo(string caminho, Func<string, Spobras.Core.Ferramentas.TextoIndexado>? ler, ReconhecedorObras? reconhecedor)
    {
        private Spobras.Core.Ferramentas.TextoIndexado? _lido;
        private IReadOnlyList<ObraReconhecida>? _obras;

        private Spobras.Core.Ferramentas.TextoIndexado Lido => _lido ??= ler?.Invoke(caminho) ?? new(null, "leitura de conteúdo desligada");

        public string? Conteudo => Lido.Texto;
        public string? MotivoSemConteudo => Lido.Texto is null ? Lido.Motivo : null;
        public bool SemBase => reconhecedor is null || reconhecedor.Vazio;

        /// <summary>Primeiro pelo nome; se o nome nao identifica uma obra so, olha o conteudo.</summary>
        public IReadOnlyList<ObraReconhecida> Obras => _obras ??= Reconhecer();

        private IReadOnlyList<ObraReconhecida> Reconhecer()
        {
            if (SemBase)
            {
                return [];
            }

            var peloNome = reconhecedor!.Reconhecer(Path.GetFileNameWithoutExtension(caminho), "no nome");
            if (peloNome.Count == 1 || ler is null)
            {
                return peloNome;
            }

            if (Conteudo is not { } texto)
            {
                return peloNome;
            }

            var peloConteudo = reconhecedor.Reconhecer(texto, "no conteúdo");
            if (peloNome.Count == 0)
            {
                return peloConteudo;
            }

            // Nome ambiguo: o conteudo pode desempatar entre as candidatas do nome.
            var juntas = peloNome.Where(n => peloConteudo.Any(c => c.Contrato == n.Contrato && c.Escola == n.Escola)).ToList();
            return juntas.Count > 0 ? juntas : peloNome;
        }
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
