using System.Text.Json;
using Spobras.Core;
using Spobras.Core.Base;
using Spobras.Core.Nomes;
using Spobras.Core.Relatorio;
using Spobras.Documentos.Imagens;
using Spobras.Documentos.Relatorio;

namespace Spobras.Celular.Servicos;

/// <summary>Uma foto do rascunho: a copia fica na pasta do app (a original do usuario nunca muda).</summary>
public sealed record FotoRascunho(string Arquivo, string Legenda = "", int Rotacao = 0, string Descricao = "")
{
    public string? NomeOriginal { get; init; }
    public DateTime? CapturadaEm { get; init; }

    /// <summary>Tamanho ja na orientacao da camera (sem o giro manual), guardado ao entrar: conferir nao rele a foto.</summary>
    public int? Largura { get; init; }
    public int? Altura { get; init; }

    /// <summary>Marca do conteudo (tamanho e trechos do arquivo): avisa quando a mesma foto entra de novo.</summary>
    public string? Impressao { get; init; }
}

/// <summary>
/// Resultado de colocar fotos da galeria: as que nao sao imagem ficam de fora (com o motivo); as repetidas entram
/// assim mesmo e viram um aviso so, com os nomes.
/// </summary>
public sealed record ResultadoFotos(IReadOnlyList<string> Recusadas, IReadOnlyList<string> Repetidas)
{
    public IReadOnlyList<string> Avisos => AvisoDasRepetidas is { } r ? [.. Recusadas, r] : Recusadas;

    public string? AvisoDasRepetidas => Repetidas.Count switch
    {
        0 => null,
        1 => $"“{Repetidas[0]}” já estava no relatório e foi colocada de novo. Se não queria repetir, toque nela e tire do relatório.",
        _ => $"{Repetidas.Count} fotos já estavam no relatório e foram colocadas de novo ({string.Join(", ", Repetidas.Take(3).Select(n => $"“{n}”"))}"
             + (Repetidas.Count > 3 ? $" e mais {Repetidas.Count - 3}" : "") + "). Se não queria repetir, selecione e tire do relatório.",
    };
}

/// <summary>Foto tirada do relatorio que ainda pode voltar (Desfazer): a posicao de onde saiu.</summary>
public sealed record FotoRemovida(FotoRascunho Foto, int Posicao);

/// <summary>Tudo que o usuario ja preencheu, guardado a cada mudanca: sair do app nao perde nada.</summary>
public sealed record DadosRascunho
{
    public string Unidade { get; init; } = "";
    public string Contrato { get; init; } = "";
    public string Lote { get; init; } = "";
    public string Portaria { get; init; } = "";
    public string Contratada { get; init; } = "";
    public string DataOs { get; init; } = "";
    public string Data { get; init; } = "";
    public string Fiscal { get; init; } = "";
    public string ProcessoSei { get; init; } = "";
    public string Objeto { get; init; } = "";
    public string Descricao { get; init; } = "";
    public int FotosPorPagina { get; init; } = 6;
    public List<FotoRascunho> Fotos { get; init; } = [];
    public DateTimeOffset? Alterado { get; init; }

    /// <summary>Removidas ha pouco, na ordem em que sairam. So sao apagadas de vez quando o tempo do Desfazer acaba.</summary>
    public List<FotoRemovida> Removidas { get; init; } = [];
}

public sealed record RelatorioPronto(string Nome, IReadOnlyList<Salvo> Arquivos, int Paginas, int Fotos, string Onde, IReadOnlyList<string> Locais);

public sealed class RascunhoRelatorio
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly string _pasta;
    private readonly string _arquivo;
    private readonly Func<DateTimeOffset> _agora;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _miniaturas = new();
    private readonly System.Collections.Concurrent.ConcurrentQueue<FotoRascunho> _fila = new();
    private readonly SemaphoreSlim _sinal = new(0);
    private readonly HashSet<string> _naFila = [];
    private readonly object _miniaturaTrava = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> _falhas = new();
    private const int TentativasDeMiniatura = 3;
    private int _trabalhando;

    public RascunhoRelatorio(string pastaDoApp, Func<DateTimeOffset>? agora = null)
    {
        _pasta = Path.Combine(pastaDoApp, "rascunho");
        _arquivo = Path.Combine(_pasta, "rascunho.json");
        _agora = agora ?? (() => DateTimeOffset.Now);
        PastaDasFotos = Path.Combine(_pasta, "fotos");
        PastaDasMiniaturas = Path.Combine(_pasta, "miniaturas");
        Directory.CreateDirectory(PastaDasFotos);
        Directory.CreateDirectory(PastaDasMiniaturas);
        Dados = Carregar();
        if (Dados.Removidas.Count > 0)
        {
            FinalizarRemocoes(); // o app fechou durante o Desfazer: a remocao vale
        }

        Conciliar();
    }

    /// <summary>
    /// O rascunho nao pode ser lido e as fotos foram recuperadas da pasta (sem as legendas que se perderam).
    /// A tela avisa uma vez.
    /// </summary>
    public string? AvisoDeRecuperacao { get; set; }

    public string PastaDasFotos { get; }
    public string PastaDasMiniaturas { get; }

    /// <summary>Uma miniatura ficou pronta em segundo plano (a tela atualiza).</summary>
    public event Action? MiniaturaPronta;

    /// <summary>O rascunho mudou (inclusive por uma foto da camera que terminou de gravar depois de sair dela).</summary>
    public event Action? Mudou;
    public DadosRascunho Dados { get; private set; }
    public bool Vazio => Dados.Fotos.Count == 0 && string.IsNullOrWhiteSpace(Dados.Unidade) && string.IsNullOrWhiteSpace(Dados.Contrato);

    private string Copia => _arquivo + ".bak";

    /// <summary>
    /// Le o rascunho; se estiver estragado, a copia anterior; se nenhum der, comeca um novo e as fotos da pasta sao
    /// recuperadas em <see cref="Conciliar"/>. Nunca apaga foto por nao conseguir ler o rascunho.
    /// </summary>
    private DadosRascunho Carregar()
    {
        bool existia = File.Exists(_arquivo) || File.Exists(Copia);
        foreach (string arquivo in new[] { _arquivo, Copia })
        {
            if (Ler(arquivo) is { } d)
            {
                if (arquivo == Copia)
                {
                    AvisoDeRecuperacao = "O rascunho estava danificado e foi recuperado da última cópia. Confira as legendas das últimas fotos.";
                }

                return d;
            }
        }

        if (existia)
        {
            AvisoDeRecuperacao = "O rascunho estava danificado. As fotos foram recuperadas, mas os dados e as legendas precisam ser preenchidos de novo.";
        }

        return Novo();
    }

    private DadosRascunho? Ler(string arquivo)
    {
        try
        {
            if (!File.Exists(arquivo))
            {
                return null;
            }

            var d = JsonSerializer.Deserialize<DadosRascunho>(File.ReadAllText(arquivo), Json);
            return d is null ? null : Sanear(d);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>Campos vazios no arquivo viram vazios de verdade; foto sem copia ou repetida sai da lista.</summary>
    private DadosRascunho Sanear(DadosRascunho d)
    {
        var vistas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fotos = (d.Fotos ?? [])
            .Where(f => f is not null && !string.IsNullOrWhiteSpace(f.Arquivo) && f.Arquivo == Path.GetFileName(f.Arquivo) && File.Exists(Caminho(f)) && vistas.Add(f.Arquivo))
            .Select(f => f with { Legenda = f.Legenda ?? "", Descricao = f.Descricao ?? "", Rotacao = ((f.Rotacao % 360) + 360) % 360 / 90 * 90 })
            .ToList();
        var removidas = (d.Removidas ?? [])
            .Where(r => r?.Foto is not null && !string.IsNullOrWhiteSpace(r.Foto.Arquivo) && r.Foto.Arquivo == Path.GetFileName(r.Foto.Arquivo) && !vistas.Contains(r.Foto.Arquivo))
            .ToList();
        return d with
        {
            Unidade = d.Unidade ?? "", Contrato = d.Contrato ?? "", Lote = d.Lote ?? "", Portaria = d.Portaria ?? "",
            Contratada = d.Contratada ?? "", DataOs = d.DataOs ?? "", Data = d.Data ?? "", Fiscal = d.Fiscal ?? "",
            ProcessoSei = d.ProcessoSei ?? "", Objeto = d.Objeto ?? "", Descricao = d.Descricao ?? "",
            FotosPorPagina = OpcoesRelatorio.Capacidades.Contains(d.FotosPorPagina) ? d.FotosPorPagina : 6,
            Fotos = fotos,
            Removidas = removidas,
        };
    }

    private DadosRascunho Novo() => new() { Data = Formato.Data(DateOnly.FromDateTime(_agora().LocalDateTime)) };

    public string Caminho(FotoRascunho foto) => Path.Combine(PastaDasFotos, foto.Arquivo);

    private readonly object _gravando = new();

    /// <summary>
    /// Muda e grava o rascunho. Uma mudanca por vez: fotos seguidas da camera chegam ao mesmo tempo e nenhuma pode
    /// se perder.
    /// </summary>
    public void Alterar(Func<DadosRascunho, DadosRascunho> mudar)
    {
        lock (_gravando)
        {
            Dados = mudar(Dados) with { Alterado = _agora() };
            Gravar();
        }

        Mudou?.Invoke();
    }

    /// <summary>Grava em arquivo temporario e troca de uma vez, guardando a versao anterior como copia.</summary>
    private void Gravar()
    {
        string temp = _arquivo + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(Dados, Json));
        if (File.Exists(_arquivo))
        {
            File.Copy(_arquivo, Copia, overwrite: true);
        }

        File.Move(temp, _arquivo, overwrite: true);
    }

    /// <summary>Foto tirada pela camera do app.</summary>
    public string AdicionarFoto(byte[] jpeg)
    {
        DateTimeOffset agora = _agora();
        string nome = $"{agora:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"[..24] + ".jpg";
        string caminho = Path.Combine(PastaDasFotos, nome);
        File.WriteAllBytes(caminho + ".tmp", jpeg);
        File.Move(caminho + ".tmp", caminho); // inteira ou nada: uma foto pela metade nunca entra no rascunho
        var foto = Medir(new FotoRascunho(nome) { NomeOriginal = nome, CapturadaEm = agora.DateTime, Impressao = Impressoes.De(jpeg) });
        Alterar(d => d with { Fotos = [.. d.Fotos, foto] });
        PedirMiniatura(foto);
        return caminho;
    }

    /// <summary>Fotos ja copiadas para a pasta do rascunho (pela galeria). As que nao sao imagem ficam de fora.</summary>
    public ResultadoFotos AdicionarCopiadas(IEnumerable<string> caminhos) =>
        AdicionarCopiadas(caminhos.Select(c => new Escolhido("", Path.GetFileName(c), c)));

    /// <summary>
    /// Fotos da galeria (ja copiadas para a pasta do rascunho). A que nao e imagem fica de fora; a que ja estava no
    /// relatorio entra assim mesmo, com aviso.
    /// </summary>
    public ResultadoFotos AdicionarCopiadas(IEnumerable<Escolhido> escolhidas)
    {
        var recusadas = new List<string>();
        var novas = new List<FotoRascunho>();
        foreach (var escolhida in escolhidas)
        {
            if (escolhida.Local is not { } c || !File.Exists(c)) continue;
            string nome = string.IsNullOrWhiteSpace(escolhida.Nome) ? Path.GetFileName(c) : escolhida.Nome;
            try
            {
                var info = Fotos.Ler(c);
                novas.Add(new FotoRascunho(Path.GetFileName(c))
                {
                    NomeOriginal = nome, CapturadaEm = info.CapturadaEm, Largura = info.Largura, Altura = info.Altura, Impressao = Impressoes.De(c),
                });
            }
            catch (Exception ex) when (ex is FotoInvalidaException or IOException or UnauthorizedAccessException)
            {
                recusadas.Add($"{nome}: {ex.Message}");
                Apagar(c); // copia do app, nao a original
            }
        }

        var repetidas = new List<string>();
        Alterar(d =>
        {
            var conhecidas = d.Fotos.Select(ImpressaoDe).Where(i => i is not null).ToHashSet();
            foreach (var f in novas)
            {
                if (f.Impressao is { } i && !conhecidas.Add(i))
                {
                    repetidas.Add(f.NomeOriginal ?? f.Arquivo);
                }
            }

            return d with { Fotos = [.. d.Fotos, .. novas] };
        });
        foreach (var f in novas)
        {
            PedirMiniatura(f);
        }

        return new ResultadoFotos(recusadas, repetidas);
    }

    /// <summary>Marca da foto; as de rascunhos antigos (sem marca) sao lidas na hora.</summary>
    private string? ImpressaoDe(FotoRascunho f)
    {
        if (f.Impressao is not null) return f.Impressao;
        try
        {
            return Impressoes.De(Caminho(f));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void OrdenarFotos(bool porData)
    {
        FotoRascunho Completar(FotoRascunho foto)
        {
            if (foto.NomeOriginal is not null) return foto;
            DateTime? captura = foto.CapturadaEm;
            try
            {
                captura ??= Fotos.Ler(Caminho(foto)).CapturadaEm;
            }
            catch (FotoInvalidaException) { }
            return foto with { NomeOriginal = foto.Arquivo, CapturadaEm = captura };
        }

        // Tudo dentro da mudanca: uma foto da camera que chegue agora nao e apagada por uma lista antiga.
        Alterar(d =>
        {
            var fotos = d.Fotos.Select(Completar).ToList();
            var ordem = porData
                ? fotos.OrderBy(f => f.CapturadaEm is null).ThenBy(f => f.CapturadaEm).ThenBy(f => f.NomeOriginal!, OrdemNatural.Instancia)
                : fotos.OrderBy(f => f.NomeOriginal!, OrdemNatural.Instancia);
            return d with { Fotos = [.. ordem] };
        });
    }

    /// <summary>Legenda curta: ate 300 caracteres (texto maior continua cabendo, mas deixa de ser legenda).</summary>
    public const int MaximoDaLegenda = 300;

    private static string Limitar(string? texto) => (texto ?? "") is { Length: > MaximoDaLegenda } t ? t[..MaximoDaLegenda] : texto ?? "";

    public void Legendar(int i, string legenda) => Trocar(i, f => f with { Legenda = Limitar(legenda) });

    public int LegendarVarias(IEnumerable<int> indices, string legenda)
    {
        var marcadas = indices.Where(i => i >= 0 && i < Dados.Fotos.Count).ToHashSet();
        if (marcadas.Count == 0)
            marcadas = Dados.Fotos.Select((foto, i) => (foto, i)).Where(x => x.foto.Legenda.Length == 0).Select(x => x.i).ToHashSet();
        if (marcadas.Count > 0)
            Alterar(d => d with
            {
                Fotos = d.Fotos.Select((foto, i) => marcadas.Contains(i) ? foto with { Legenda = Limitar(legenda.Trim()) } : foto).ToList(),
            });
        return marcadas.Count;
    }

    public void Girar(int i)
    {
        if (i < 0 || i >= Dados.Fotos.Count) return;
        var antes = Dados.Fotos[i];
        Trocar(i, f => f with { Rotacao = (f.Rotacao + 90) % 360 });
        EsquecerMiniaturas(antes.Arquivo, manter: antes.Rotacao); // so a do giro anterior fica (desfazer o giro e na hora)
    }

    /// <summary>Apaga as miniaturas de outros giros da foto (sobram ao girar varias vezes).</summary>
    private void EsquecerMiniaturas(string arquivo, int? manter = null)
    {
        foreach (var m in Directory.GetFiles(PastaDasMiniaturas, Path.GetFileNameWithoutExtension(arquivo) + "-*.jpg"))
        {
            var atual = Dados.Fotos.FirstOrDefault(f => f.Arquivo == arquivo);
            string nome = Path.GetFileNameWithoutExtension(m);
            int traco = nome.LastIndexOf('-');
            if (nome[..traco] != Path.GetFileNameWithoutExtension(arquivo) || !int.TryParse(nome[(traco + 1)..], out int giro))
            {
                continue; // miniatura de outra foto com nome parecido
            }

            if (giro == manter || giro == atual?.Rotacao)
            {
                continue;
            }

            Apagar(m);
            _miniaturas.TryRemove(arquivo + "|" + giro, out _);
        }
    }

    public void Mover(int de, int para)
    {
        if (de == para || de < 0 || para < 0 || de >= Dados.Fotos.Count || para >= Dados.Fotos.Count)
        {
            return;
        }

        Alterar(d =>
        {
            var lista = d.Fotos.ToList();
            var f = lista[de];
            lista.RemoveAt(de);
            lista.Insert(para, f);
            return d with { Fotos = lista };
        });
    }

    /// <summary>
    /// Tira fotos do relatorio na hora. A copia do app so e apagada em <see cref="FinalizarRemocoes"/> (fim do tempo
    /// do Desfazer). Removidas seguidas se juntam: o Desfazer devolve todas. Devolve quantas estao esperando.
    /// </summary>
    public int Remover(IEnumerable<int> indices)
    {
        // Da maior posicao para a menor, para cada posicao guardada ser a de onde a foto saiu de verdade.
        var alvo = indices.Where(i => i >= 0 && i < Dados.Fotos.Count).Distinct().OrderByDescending(i => i).ToList();
        if (alvo.Count > 0)
        {
            Alterar(d =>
            {
                var fotos = d.Fotos.ToList();
                var removidas = d.Removidas.ToList();
                foreach (int i in alvo)
                {
                    removidas.Add(new FotoRemovida(fotos[i], i));
                    fotos.RemoveAt(i);
                }

                return d with { Fotos = fotos, Removidas = removidas };
            });
        }

        return Dados.Removidas.Count;
    }

    public int Remover(int i) => Remover([i]);

    /// <summary>Devolve as fotos removidas ha pouco, cada uma para a posicao de onde saiu (na ordem inversa).</summary>
    public int Desfazer()
    {
        int n = Dados.Removidas.Count;
        if (n > 0)
        {
            Alterar(d =>
            {
                var fotos = d.Fotos.ToList();
                foreach (var r in Enumerable.Reverse(d.Removidas))
                {
                    if (!fotos.Any(f => f.Arquivo == r.Foto.Arquivo) && File.Exists(Caminho(r.Foto)))
                    {
                        fotos.Insert(Math.Min(r.Posicao, fotos.Count), r.Foto);
                    }
                }

                return d with { Fotos = fotos, Removidas = [] };
            });
        }

        return n;
    }

    /// <summary>Acabou o tempo do Desfazer: apaga as copias que o app guardou (as fotos da galeria ficam).</summary>
    public void FinalizarRemocoes()
    {
        var removidas = Dados.Removidas;
        if (removidas.Count == 0)
        {
            return;
        }

        // Primeiro apaga, depois tira da lista: se o app fechar no meio, a proxima abertura termina o servico
        // (e a foto nunca "ressuscita" como sobra adotada).
        foreach (var r in removidas)
        {
            if (Dados.Fotos.Any(f => f.Arquivo == r.Foto.Arquivo))
            {
                continue; // a mesma foto voltou por outro caminho
            }

            Apagar(Caminho(r.Foto));
            string baseDoNome = Path.GetFileNameWithoutExtension(r.Foto.Arquivo);
            foreach (var m in Directory.GetFiles(PastaDasMiniaturas, baseDoNome + "-*"))
            {
                string nome = Path.GetFileNameWithoutExtension(m);
                if (nome.LastIndexOf('-') is int t && t > 0 && nome[..t] == baseDoNome)
                {
                    Apagar(m); // so as desta foto (nao as de outra com nome parecido)
                }
            }

            foreach (var chave in _miniaturas.Keys.Where(k => k.StartsWith(r.Foto.Arquivo + "|", StringComparison.Ordinal)).ToList())
            {
                _miniaturas.TryRemove(chave, out _);
            }
        }

        var feitas = removidas.Select(r => r.Foto.Arquivo).ToHashSet();
        Alterar(d => d with { Removidas = [.. d.Removidas.Where(r => !feitas.Contains(r.Foto.Arquivo))] });
    }

    /// <summary>
    /// Apaga uma copia do proprio app. No Windows o arquivo pode estar aberto por um instante (miniatura sendo feita):
    /// tenta de novo; se ainda assim nao der, a limpeza da proxima abertura apaga.
    /// </summary>
    private static void Apagar(string caminho)
    {
        for (int tentativa = 0; tentativa < 10; tentativa++)
        {
            try
            {
                if (File.Exists(caminho))
                {
                    File.Delete(caminho);
                }

                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(30);
            }
        }
    }

    /// <summary>
    /// Ao abrir: foto que esta na pasta mas fora da lista (o app fechou no meio de colocar, ou o rascunho estragou)
    /// volta para o fim do relatorio, em vez de ser apagada. So o que nao e imagem e as miniaturas sem foto saem.
    /// </summary>
    private void Conciliar()
    {
        var usadas = Dados.Fotos.Select(f => f.Arquivo).Concat(Dados.Removidas.Select(r => r.Foto.Arquivo)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var adotadas = new List<FotoRascunho>();
        foreach (var f in Directory.GetFiles(PastaDasFotos).Where(f => !usadas.Contains(Path.GetFileName(f))).Order(StringComparer.Ordinal))
        {
            if (f.EndsWith(".tmp", StringComparison.Ordinal))
            {
                Apagar(f); // gravacao interrompida
                continue;
            }

            try
            {
                var info = Fotos.Ler(f);
                adotadas.Add(new FotoRascunho(Path.GetFileName(f)) { NomeOriginal = Path.GetFileName(f), CapturadaEm = info.CapturadaEm, Largura = info.Largura, Altura = info.Altura });
            }
            catch (Exception ex) when (ex is FotoInvalidaException or IOException or UnauthorizedAccessException)
            {
                Apagar(f);
            }
        }

        if (adotadas.Count > 0)
        {
            Alterar(d => d with { Fotos = [.. d.Fotos, .. adotadas] });
            usadas.UnionWith(adotadas.Select(a => a.Arquivo));
        }

        var nomes = usadas.Select(Path.GetFileNameWithoutExtension).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var m in Directory.GetFiles(PastaDasMiniaturas))
        {
            string nome = Path.GetFileNameWithoutExtension(m);
            int traco = nome.LastIndexOf('-');
            if (m.EndsWith(".tmp", StringComparison.Ordinal) || traco <= 0 || !nomes.Contains(nome[..traco]))
            {
                Apagar(m);
            }
        }
    }

    /// <summary>Le so o cabecalho da foto (rapido) para guardar o tamanho.</summary>
    private FotoRascunho Medir(FotoRascunho foto)
    {
        try
        {
            var info = Fotos.Ler(Caminho(foto));
            return foto with { Largura = info.Largura, Altura = info.Altura, CapturadaEm = foto.CapturadaEm ?? info.CapturadaEm };
        }
        catch (FotoInvalidaException)
        {
            return foto;
        }
    }

    private void Trocar(int i, Func<FotoRascunho, FotoRascunho> mudar)
    {
        if (i >= 0 && i < Dados.Fotos.Count)
        {
            Alterar(d => d with { Fotos = [.. d.Fotos.Select((f, n) => n == i ? mudar(f) : f)] });
        }
    }

    /// <summary>Comeca um relatorio novo: limpa o rascunho e as copias das fotos guardadas pelo app.</summary>
    public void Recomecar()
    {
        lock (_gravando)
        lock (_miniaturaTrava)
        {
            _fila.Clear();
            lock (_naFila)
            {
                _naFila.Clear();
            }

            foreach (var f in Directory.GetFiles(PastaDasFotos).Concat(Directory.GetFiles(PastaDasMiniaturas)))
            {
                Apagar(f);
            }

            _miniaturas.Clear();
            _falhas.Clear();
            Dados = Novo();
            Apagar(_arquivo);
            Apagar(Copia);
            AvisoDeRecuperacao = null;
        }

        Mudou?.Invoke();
    }

    private string ArquivoDaMiniatura(FotoRascunho foto) =>
        Path.Combine(PastaDasMiniaturas, $"{Path.GetFileNameWithoutExtension(foto.Arquivo)}-{foto.Rotacao}.jpg");

    /// <summary>
    /// Miniatura em data URL para a tela. Ja pronta: na hora (memoria ou arquivo). Ainda nao: devolve vazio e gera em
    /// segundo plano (a tela nao trava; <see cref="MiniaturaPronta"/> avisa quando ficar pronta).
    /// </summary>
    public string Miniatura(FotoRascunho foto)
    {
        string chave = foto.Arquivo + "|" + foto.Rotacao;
        if (_miniaturas.TryGetValue(chave, out string? url))
        {
            return url;
        }

        string arquivo = ArquivoDaMiniatura(foto);
        if (File.Exists(arquivo))
        {
            try
            {
                url = "data:image/jpeg;base64," + Convert.ToBase64String(File.ReadAllBytes(arquivo));
                _miniaturas[chave] = url;
                return url;
            }
            catch (IOException)
            {
            }
        }

        PedirMiniatura(foto);
        return "";
    }

    /// <summary>Gera a miniatura agora, no proprio fio (testes e a primeira geracao).</summary>
    public string MiniaturaAgora(FotoRascunho foto)
    {
        Gerar(foto);
        return Miniatura(foto);
    }

    /// <summary>Espera a fila de miniaturas esvaziar (testes).</summary>
    public async Task MiniaturasProntasAsync(TimeSpan limite)
    {
        var fim = DateTime.UtcNow + limite;
        while ((!_fila.IsEmpty || Volatile.Read(ref _trabalhando) > 0) && DateTime.UtcNow < fim)
        {
            await Task.Delay(20);
        }
    }

    private void PedirMiniatura(FotoRascunho foto)
    {
        lock (_naFila)
        {
            if (!_naFila.Add(foto.Arquivo + "|" + foto.Rotacao))
            {
                return;
            }
        }

        _fila.Enqueue(foto);
        if (Interlocked.CompareExchange(ref _trabalhando, 1, 0) == 0)
        {
            _ = Task.Run(Processar);
        }
    }

    /// <summary>Uma miniatura por vez (memoria baixa no celular), em segundo plano.</summary>
    private void Processar()
    {
        try
        {
            while (_fila.TryDequeue(out var foto))
            {
                Gerar(foto);
                lock (_naFila)
                {
                    _naFila.Remove(foto.Arquivo + "|" + foto.Rotacao);
                }

                MiniaturaPronta?.Invoke();
            }
        }
        finally
        {
            Volatile.Write(ref _trabalhando, 0);
            if (!_fila.IsEmpty && Interlocked.CompareExchange(ref _trabalhando, 1, 0) == 0)
            {
                _ = Task.Run(Processar);
            }
        }
    }

    private void Gerar(FotoRascunho foto)
    {
        string arquivo = ArquivoDaMiniatura(foto);
        try
        {
            if (!File.Exists(arquivo))
            {
                if (!File.Exists(Caminho(foto)))
                {
                    return;
                }

                byte[] jpeg = Fotos.Preparar(Caminho(foto), 320, 75, foto.Rotacao);
                lock (_miniaturaTrava) // descartar o rascunho espera esta gravacao terminar (e vice-versa)
                {
                    if (!File.Exists(Caminho(foto)))
                    {
                        return; // a foto saiu enquanto a miniatura era feita
                    }

                    string temp = arquivo + "." + Guid.NewGuid().ToString("N")[..6] + ".tmp";
                    try
                    {
                        File.WriteAllBytes(temp, jpeg);
                        File.Move(temp, arquivo, overwrite: true);
                    }
                    finally
                    {
                        Apagar(temp); // nao sobra temporario se a gravacao falhar
                    }
                }
            }

            _miniaturas[foto.Arquivo + "|" + foto.Rotacao] = "data:image/jpeg;base64," + Convert.ToBase64String(File.ReadAllBytes(arquivo));
        }
        catch (FotoInvalidaException)
        {
            _miniaturas[foto.Arquivo + "|" + foto.Rotacao] = ""; // nao e imagem: nao adianta tentar de novo
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Falha passageira (arquivo ocupado): tenta de novo nas proximas vezes que a tela pedir, ate 3 vezes.
            string chave = foto.Arquivo + "|" + foto.Rotacao;
            if (_falhas.AddOrUpdate(chave, 1, (_, n) => n + 1) >= TentativasDeMiniatura)
            {
                _miniaturas[chave] = "";
            }
        }

        if (!File.Exists(Caminho(foto)))
        {
            Apagar(arquivo); // a foto saiu (descartada) enquanto a miniatura era feita
            _miniaturas.TryRemove(foto.Arquivo + "|" + foto.Rotacao, out _);
        }
    }

    /// <summary>A foto nao tem previa (nao deu para ler): a tela mostra um aviso no lugar da miniatura.</summary>
    public bool SemPrevia(FotoRascunho foto) => _miniaturas.TryGetValue(foto.Arquivo + "|" + foto.Rotacao, out string? url) && url.Length == 0;

    // ---- Dados da base ----

    /// <summary>
    /// Atualiza os campos da obra e devolve o que mudou. Data do relatorio, fiscal, descricao e fotos sao preservados.
    /// </summary>
    public IReadOnlyList<string> AplicarObra(ObraLida obra)
    {
        var mudou = new List<string>();
        string Trocar(string rotulo, string atual, string? novo)
        {
            novo ??= "";
            if (!string.Equals(atual.Trim(), novo.Trim(), StringComparison.Ordinal))
            {
                mudou.Add(rotulo);
            }

            return novo;
        }

        // Dentro da mudanca: fotos que chegam ao mesmo tempo nao sao apagadas por uma copia antiga do rascunho.
        Alterar(d => d with
        {
            Unidade = Trocar("Unidade", d.Unidade, obra.Empreendimento),
            Contrato = Trocar("Contrato", d.Contrato, obra.Contrato),
            Lote = Trocar("Lote", d.Lote, obra.Lote),
            Portaria = Trocar("Portaria", d.Portaria, obra.Portaria),
            Contratada = Trocar("Contratada", d.Contratada, obra.Contratada),
            DataOs = Trocar("Data da OS", d.DataOs, Formato.Data(obra.DataOs)),
            Objeto = Trocar("Objeto", d.Objeto, obra.Escopo),
            ProcessoSei = Trocar("Processo SEI", d.ProcessoSei, obra.ProcessoSei),
        });
        return mudou;
    }

    // ---- Geracao ----

    public DadosRelatorio Cabecalho() => new()
    {
        Unidade = Dados.Unidade.Trim(),
        Contrato = Dados.Contrato.Trim(),
        Lote = Dados.Lote.Trim(),
        Portaria = Dados.Portaria.Trim(),
        Contratada = Dados.Contratada.Trim(),
        ProcessoSei = Dados.ProcessoSei.Trim(),
        Objeto = Dados.Objeto.Trim(),
        Responsavel = Dados.Fiscal.Trim(),
        Descricao = Dados.Descricao.Trim(),
        Data = Formato.LerData(Dados.Data).Data,
        DataOs = Formato.LerData(Dados.DataOs).Data,
    };

    /// <summary>Datas digitadas que nao sao data (o relatorio nao e gerado ate corrigir).</summary>
    public IReadOnlyList<string> Problemas()
    {
        var lista = new List<string>();
        if (Formato.LerData(Dados.Data).Invalida)
        {
            lista.Add("A data do relatório não é uma data válida (use dd/mm/aaaa).");
        }

        if (Formato.LerData(Dados.DataOs).Invalida)
        {
            lista.Add("A Data da OS não é uma data válida (use dd/mm/aaaa).");
        }

        if (Dados.Fotos.Count == 0)
        {
            lista.Add("Coloque pelo menos uma foto.");
        }

        return lista;
    }

    public PlanoRelatorio Plano()
    {
        var fotos = Dados.Fotos.Select(f =>
        {
            string c = Caminho(f);
            // Tamanho guardado ao entrar; so rascunhos antigos (sem ele) leem o cabecalho da foto.
            var (l, a) = f.Largura is { } lg && f.Altura is { } al ? (lg, al) : (Fotos.Ler(c).Largura, Fotos.Ler(c).Altura);
            if (f.Rotacao % 180 != 0)
            {
                (l, a) = (a, l);
            }

            return new FotoRelatorio(c, l, a) { Legenda = f.Legenda.Trim(), Descricao = f.Descricao.Trim(), Rotacao = f.Rotacao };
        }).ToList();
        return PaginadorRelatorio.Paginar(fotos, new OpcoesRelatorio { FotosPorPagina = Dados.FotosPorPagina }, Cabecalho());
    }

    /// <summary>
    /// Gera o Excel e/ou o PDF numa pasta temporaria do app e salva copias na pasta escolhida pelo usuario
    /// (numa subpasta com o nome da escola, se pedido). Nunca sobrescreve: o nome ganha (2), (3)...
    /// </summary>
    /// <summary>Pasta da escola: mesmo limite do nome do arquivo (nome enorme nao estoura o caminho).</summary>
    private static string NomeDaPasta(string unidade)
    {
        string nome = Spobras.Core.Nomes.NomeArquivo.Corrigir(unidade.Trim());
        return nome.Length <= CabecalhoRelatorio.MaximoDaUnidadeNoNome ? nome : nome[..CabecalhoRelatorio.MaximoDaUnidadeNoNome].TrimEnd(' ', '.', '-');
    }

    private readonly SemaphoreSlim _gerando = new(1, 1);

    public async Task<RelatorioPronto> GerarAsync(IPlataforma plataforma, Preferencias p, IProgress<int>? progresso = null)
    {
        if (!await _gerando.WaitAsync(0))
        {
            throw new InvalidOperationException("O relatório já está sendo gerado. Aguarde terminar.");
        }

        try
        {
            return await GerarUmAsync(plataforma, p, progresso);
        }
        finally
        {
            _gerando.Release();
        }
    }

    private async Task<RelatorioPronto> GerarUmAsync(IPlataforma plataforma, Preferencias p, IProgress<int>? progresso)
    {
        if (p.Pasta is null)
        {
            throw new InvalidOperationException("Escolha antes a pasta onde salvar os relatórios.");
        }

        var problemas = Problemas();
        if (problemas.Count > 0)
        {
            throw new InvalidOperationException(string.Join(" ", problemas));
        }

        var plano = Plano();
        if (plano.Avisos.FirstOrDefault(av => av.Gravidade == Gravidade.ImpedeGerar) is { } impede)
        {
            throw new InvalidOperationException(impede.Mensagem);
        }

        string nome = CabecalhoRelatorio.NomeSugerido(plano.Dados);
        string temp = Path.Combine(_pasta, "gerado");
        if (Directory.Exists(temp))
        {
            Directory.Delete(temp, recursive: true); // pasta temporaria do proprio app
        }

        Directory.CreateDirectory(temp);
        string? logo = p.Logo is { } l && File.Exists(l) ? l : null;
        var locais = new List<string>();
        // Progresso continuo: com Excel e PDF, o PDF continua a contagem de onde o Excel parou.
        int jaFeitas = 0;
        var passo = progresso is null ? null : new Progresso(n => progresso.Report(jaFeitas + n));
        await Task.Run(() =>
        {
            if (p.Formato != FormatoGerado.Pdf)
            {
                string xlsx = Path.Combine(temp, nome + ".xlsx");
                GeradorRelatorio.Gerar(plano, xlsx, logo, passo);
                locais.Add(xlsx);
                jaFeitas = plano.TotalDeFotos;
            }

            if (p.Formato != FormatoGerado.Excel)
            {
                string pdf = Path.Combine(temp, nome + ".pdf");
                GeradorRelatorioPdf.Gerar(plano, pdf, logo, passo);
                locais.Add(pdf);
            }
        });

        string? subpasta = p.PastaPorEscola && plano.Dados.Unidade.Length > 0 ? NomeDaPasta(plano.Dados.Unidade) : null;
        var salvos = new List<Salvo>();
        foreach (string local in locais)
        {
            salvos.Add(await plataforma.SalvarAsync(p.Pasta.Id, subpasta, local));
        }

        string onde = p.Pasta.Nome + (subpasta is null ? "" : " › " + subpasta);
        return new RelatorioPronto(nome, salvos, plano.Paginas.Count, plano.TotalDeFotos, onde, locais);
    }
}

/// <summary>Repassa o progresso na hora (sem esperar a tela), para somar as etapas na ordem certa.</summary>
internal sealed class Progresso(Action<int> aoAvancar) : IProgress<int>
{
    public void Report(int value) => aoAvancar(value);
}
