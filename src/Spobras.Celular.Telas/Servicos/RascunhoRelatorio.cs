using System.Text.Json;
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
}

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
}

public sealed record RelatorioPronto(string Nome, IReadOnlyList<Salvo> Arquivos, int Paginas, int Fotos, string Onde, IReadOnlyList<string> Locais);

public sealed class RascunhoRelatorio
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly string _pasta;
    private readonly string _arquivo;
    private readonly Func<DateTimeOffset> _agora;
    private readonly Dictionary<string, string> _miniaturas = [];

    public RascunhoRelatorio(string pastaDoApp, Func<DateTimeOffset>? agora = null)
    {
        _pasta = Path.Combine(pastaDoApp, "rascunho");
        _arquivo = Path.Combine(_pasta, "rascunho.json");
        _agora = agora ?? (() => DateTimeOffset.Now);
        PastaDasFotos = Path.Combine(_pasta, "fotos");
        Directory.CreateDirectory(PastaDasFotos);
        Dados = Carregar();
    }

    public string PastaDasFotos { get; }
    public DadosRascunho Dados { get; private set; }
    public bool Vazio => Dados.Fotos.Count == 0 && string.IsNullOrWhiteSpace(Dados.Unidade) && string.IsNullOrWhiteSpace(Dados.Contrato);

    private DadosRascunho Carregar()
    {
        try
        {
            if (File.Exists(_arquivo))
            {
                var d = JsonSerializer.Deserialize<DadosRascunho>(File.ReadAllText(_arquivo), Json) ?? new();
                // Foto cuja copia sumiu (limpeza do sistema) sai do rascunho em vez de quebrar a geracao.
                return d with { Fotos = [.. d.Fotos.Where(f => File.Exists(Caminho(f)))] };
            }
        }
        catch (JsonException)
        {
        }

        return Novo();
    }

    private DadosRascunho Novo() => new() { Data = Formato.Data(DateOnly.FromDateTime(_agora().LocalDateTime)) };

    public string Caminho(FotoRascunho foto) => Path.Combine(PastaDasFotos, foto.Arquivo);

    public void Alterar(Func<DadosRascunho, DadosRascunho> mudar)
    {
        Dados = mudar(Dados) with { Alterado = _agora() };
        string temp = _arquivo + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(Dados, Json));
        File.Move(temp, _arquivo, overwrite: true);
    }

    /// <summary>Foto tirada pela camera do app.</summary>
    public string AdicionarFoto(byte[] jpeg)
    {
        DateTimeOffset agora = _agora();
        string nome = $"{agora:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"[..24] + ".jpg";
        string caminho = Path.Combine(PastaDasFotos, nome);
        File.WriteAllBytes(caminho, jpeg);
        Alterar(d => d with { Fotos = [.. d.Fotos, new FotoRascunho(nome) { NomeOriginal = nome, CapturadaEm = agora.DateTime }] });
        return caminho;
    }

    /// <summary>Fotos ja copiadas para a pasta do rascunho (pela galeria). As que nao sao imagem ficam de fora.</summary>
    public IReadOnlyList<string> AdicionarCopiadas(IEnumerable<string> caminhos) =>
        AdicionarCopiadas(caminhos.Select(c => new Escolhido("", Path.GetFileName(c), c)));

    public IReadOnlyList<string> AdicionarCopiadas(IEnumerable<Escolhido> escolhidas)
    {
        var recusadas = new List<string>();
        var novas = new List<FotoRascunho>();
        foreach (var escolhida in escolhidas)
        {
            if (escolhida.Local is not { } c) continue;
            try
            {
                var info = Fotos.Ler(c);
                novas.Add(new FotoRascunho(Path.GetFileName(c)) { NomeOriginal = escolhida.Nome, CapturadaEm = info.CapturadaEm });
            }
            catch (FotoInvalidaException ex)
            {
                recusadas.Add($"{Path.GetFileName(c)}: {ex.Message}");
                File.Delete(c); // copia do app, nao a original
            }
        }

        Alterar(d => d with { Fotos = [.. d.Fotos, .. novas] });
        return recusadas;
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

        var fotos = Dados.Fotos.Select(Completar);
        var ordem = porData
            ? fotos.OrderBy(f => f.CapturadaEm is null).ThenBy(f => f.CapturadaEm).ThenBy(f => f.NomeOriginal!, OrdemNatural.Instancia)
            : fotos.OrderBy(f => f.NomeOriginal!, OrdemNatural.Instancia);
        Alterar(d => d with { Fotos = [.. ordem] });
    }

    public void Legendar(int i, string legenda) => Trocar(i, f => f with { Legenda = legenda });

    public int LegendarVarias(IEnumerable<int> indices, string legenda)
    {
        var marcadas = indices.Where(i => i >= 0 && i < Dados.Fotos.Count).ToHashSet();
        if (marcadas.Count == 0)
            marcadas = Dados.Fotos.Select((foto, i) => (foto, i)).Where(x => x.foto.Legenda.Length == 0).Select(x => x.i).ToHashSet();
        if (marcadas.Count > 0)
            Alterar(d => d with
            {
                Fotos = d.Fotos.Select((foto, i) => marcadas.Contains(i) ? foto with { Legenda = legenda.Trim() } : foto).ToList(),
            });
        return marcadas.Count;
    }

    public void Girar(int i) => Trocar(i, f => f with { Rotacao = (f.Rotacao + 90) % 360 });

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

    /// <summary>Tira a foto do relatorio e apaga a copia que o app guardou (a da galeria fica).</summary>
    public void Remover(int i)
    {
        if (i < 0 || i >= Dados.Fotos.Count)
        {
            return;
        }

        string caminho = Caminho(Dados.Fotos[i]);
        Alterar(d => d with { Fotos = [.. d.Fotos.Where((_, n) => n != i)] });
        if (File.Exists(caminho))
        {
            File.Delete(caminho);
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
        foreach (var f in Directory.GetFiles(PastaDasFotos))
        {
            File.Delete(f);
        }

        _miniaturas.Clear();
        Dados = Novo();
        if (File.Exists(_arquivo))
        {
            File.Delete(_arquivo);
        }
    }

    /// <summary>Miniatura em data URL para a tela (guardada em memoria).</summary>
    public string Miniatura(FotoRascunho foto)
    {
        string chave = foto.Arquivo + "|" + foto.Rotacao;
        if (!_miniaturas.TryGetValue(chave, out string? url))
        {
            try
            {
                url = "data:image/jpeg;base64," + Convert.ToBase64String(Fotos.Miniatura(Caminho(foto), 360, foto.Rotacao));
            }
            catch (FotoInvalidaException)
            {
                url = "";
            }

            _miniaturas[chave] = url;
        }

        return url;
    }

    // ---- Dados da base ----

    /// <summary>
    /// Atualiza os campos da obra e devolve o que mudou. Data do relatorio, fiscal, descricao e fotos sao preservados.
    /// </summary>
    public IReadOnlyList<string> AplicarObra(ObraLida obra)
    {
        var mudou = new List<string>();
        var d = Dados;
        string Trocar(string rotulo, string atual, string? novo)
        {
            novo ??= "";
            if (!string.Equals(atual.Trim(), novo.Trim(), StringComparison.Ordinal))
            {
                mudou.Add(rotulo);
            }

            return novo;
        }

        d = d with
        {
            Unidade = Trocar("Unidade", d.Unidade, obra.Empreendimento),
            Contrato = Trocar("Contrato", d.Contrato, obra.Contrato),
            Lote = Trocar("Lote", d.Lote, obra.Lote),
            Portaria = Trocar("Portaria", d.Portaria, obra.Portaria),
            Contratada = Trocar("Contratada", d.Contratada, obra.Contratada),
            DataOs = Trocar("Data da OS", d.DataOs, Formato.Data(obra.DataOs)),
            Objeto = Trocar("Objeto", d.Objeto, obra.Escopo),
            ProcessoSei = Trocar("Processo SEI", d.ProcessoSei, obra.ProcessoSei),
        };
        Alterar(_ => d);
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
            var info = Fotos.Ler(c, f.Rotacao);
            return new FotoRelatorio(c, info.Largura, info.Altura) { Legenda = f.Legenda.Trim(), Descricao = f.Descricao.Trim(), Rotacao = f.Rotacao };
        }).ToList();
        return PaginadorRelatorio.Paginar(fotos, new OpcoesRelatorio { FotosPorPagina = Dados.FotosPorPagina }, Cabecalho());
    }

    /// <summary>
    /// Gera o Excel e/ou o PDF numa pasta temporaria do app e salva copias na pasta escolhida pelo usuario
    /// (numa subpasta com o nome da escola, se pedido). Nunca sobrescreve: o nome ganha (2), (3)...
    /// </summary>
    public async Task<RelatorioPronto> GerarAsync(IPlataforma plataforma, Preferencias p, IProgress<int>? progresso = null)
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
        string nome = CabecalhoRelatorio.NomeSugerido(plano.Dados);
        string temp = Path.Combine(_pasta, "gerado");
        if (Directory.Exists(temp))
        {
            Directory.Delete(temp, recursive: true); // pasta temporaria do proprio app
        }

        Directory.CreateDirectory(temp);
        string? logo = p.Logo is { } l && File.Exists(l) ? l : null;
        var locais = new List<string>();
        await Task.Run(() =>
        {
            if (p.Formato != FormatoGerado.Pdf)
            {
                string xlsx = Path.Combine(temp, nome + ".xlsx");
                GeradorRelatorio.Gerar(plano, xlsx, logo, progresso);
                locais.Add(xlsx);
            }

            if (p.Formato != FormatoGerado.Excel)
            {
                string pdf = Path.Combine(temp, nome + ".pdf");
                GeradorRelatorioPdf.Gerar(plano, pdf, logo, progresso);
                locais.Add(pdf);
            }
        });

        string? subpasta = p.PastaPorEscola && plano.Dados.Unidade.Length > 0 ? Spobras.Core.Nomes.NomeArquivo.Corrigir(plano.Dados.Unidade) : null;
        var salvos = new List<Salvo>();
        foreach (string local in locais)
        {
            salvos.Add(await plataforma.SalvarAsync(p.Pasta.Id, subpasta, local));
        }

        string onde = p.Pasta.Nome + (subpasta is null ? "" : " › " + subpasta);
        return new RelatorioPronto(nome, salvos, plano.Paginas.Count, plano.TotalDeFotos, onde, locais);
    }
}
