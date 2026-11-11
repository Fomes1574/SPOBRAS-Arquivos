using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Spobras.Core.Planilhas;

namespace Spobras.Documentos.Planilhas;

/// <summary>Planilha que nao pode ser lida; a mensagem explica o motivo para o usuario.</summary>
public sealed class PlanilhaInvalidaException(string mensagem) : Exception(mensagem);

/// <summary>
/// Le planilhas .xlsx direto do arquivo (Open XML), sem abrir o Excel e sem alterar nada.
/// Abre so para leitura e aceita que o arquivo esteja aberto no Excel do usuario.
/// </summary>
public sealed class LeitorXlsx : ILeitorPlanilha
{
    private readonly FileStream _arquivo;
    private readonly SpreadsheetDocument _doc;
    private readonly WorkbookPart _pasta;
    private List<string>? _textos;
    private HashSet<uint>? _estilosDeData;

    public LeitorXlsx(string caminho)
    {
        try
        {
            _arquivo = new FileStream(caminho, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new PlanilhaInvalidaException($"Não foi possível abrir \"{Path.GetFileName(caminho)}\": {ex.Message}");
        }

        try
        {
            _doc = SpreadsheetDocument.Open(_arquivo, isEditable: false);
            _pasta = _doc.WorkbookPart ?? throw new PlanilhaInvalidaException("O arquivo não tem pasta de trabalho.");
        }
        catch (Exception ex) when (ex is OpenXmlPackageException or InvalidDataException or FileFormatException or System.Xml.XmlException)
        {
            _arquivo.Dispose();
            throw new PlanilhaInvalidaException(
                $"\"{Path.GetFileName(caminho)}\" não pôde ser lido como planilha do Excel (pode estar sendo salvo ou sincronizado agora): {ex.Message}");
        }
    }

    public void Dispose()
    {
        _doc.Dispose();
        _arquivo.Dispose();
    }

    /// <summary>Todas as tabelas nomeadas do arquivo, com as colunas (sem ler os dados).</summary>
    public IReadOnlyList<DescricaoTabela> Tabelas()
    {
        var lista = new List<DescricaoTabela>();
        foreach (var (aba, parte) in Abas())
        {
            foreach (var tabela in parte.TableDefinitionParts)
            {
                var t = tabela.Table;
                if (t?.Name?.Value is not { } nome || t.Reference?.Value is not { } faixa)
                {
                    continue;
                }

                var (_, linha1, _, linha2) = Faixa(faixa);
                lista.Add(new DescricaoTabela(nome, aba, Math.Max(0, linha2 - linha1), [.. NomesDasColunas(t)]));
            }
        }

        return lista;
    }

    public IReadOnlyList<string> NomesDasAbas() => [.. Abas().Select(a => a.Nome)];

    /// <summary>Le uma tabela pelo nome (nao depende do nome nem da visibilidade da aba).</summary>
    public TabelaLida LerTabela(string nome)
    {
        foreach (var (aba, parte) in Abas())
        {
            foreach (var tabela in parte.TableDefinitionParts)
            {
                var t = tabela.Table;
                if (!string.Equals(t?.Name?.Value, nome, StringComparison.OrdinalIgnoreCase) || t!.Reference?.Value is not { } faixa)
                {
                    continue;
                }

                var (coluna1, linha1, coluna2, linha2) = Faixa(faixa);
                var colunas = NomesDasColunas(t).ToList();
                int totais = (int)(t.TotalsRowCount?.Value ?? 0);
                var linhas = LerFaixa(parte, coluna1, linha1 + 1, coluna2, linha2 - totais);
                return new TabelaLida(nome, aba, colunas, linhas);
            }
        }

        throw new PlanilhaInvalidaException($"A tabela \"{nome}\" não existe mais na planilha.");
    }

    /// <summary>Le uma aba inteira como grade (para planilhas sem tabelas nomeadas). Linhas e colunas comecam em 0.</summary>
    public IReadOnlyList<object?[]> LerAba(string nomeDaAba)
    {
        var aba = Abas().FirstOrDefault(a => string.Equals(a.Nome, nomeDaAba, StringComparison.OrdinalIgnoreCase));
        if (aba.Parte is null)
        {
            throw new PlanilhaInvalidaException($"A aba \"{nomeDaAba}\" não existe na planilha.");
        }

        return LerFaixa(aba.Parte, 1, 1, 60, 5000);
    }

    private IEnumerable<(string Nome, WorksheetPart Parte)> Abas()
    {
        foreach (var aba in _pasta.Workbook?.Sheets?.Elements<Sheet>() ?? [])
        {
            if (aba.Id?.Value is { } id && aba.Name?.Value is { } nome && _pasta.GetPartById(id) is WorksheetPart parte)
            {
                yield return (nome, parte);
            }
        }
    }

    private static IEnumerable<string> NomesDasColunas(Table tabela) =>
        tabela.TableColumns?.Elements<TableColumn>().Select(c => (c.Name?.Value ?? "").Replace("_x000a_", " ").Replace("\n", " ").Trim()) ?? [];

    private List<object?[]> LerFaixa(WorksheetPart parte, int coluna1, int linha1, int coluna2, int linha2)
    {
        int largura = coluna2 - coluna1 + 1;
        var linhas = new List<object?[]>();
        using var leitor = OpenXmlReader.Create(parte);
        object?[]? atual = null;
        int linhaAtual = 0;

        while (leitor.Read())
        {
            if (leitor.ElementType == typeof(Row) && leitor.IsStartElement)
            {
                linhaAtual = int.Parse(leitor.Attributes.First(a => a.LocalName == "r").Value!, CultureInfo.InvariantCulture);
                if (linhaAtual > linha2)
                {
                    break;
                }

                atual = linhaAtual >= linha1 ? new object?[largura] : null;
                if (atual is not null)
                {
                    // Linhas totalmente vazias no meio da faixa nao aparecem no arquivo: completa com vazias.
                    while (linhas.Count < linhaAtual - linha1)
                    {
                        linhas.Add(new object?[largura]);
                    }

                    linhas.Add(atual);
                }
            }
            else if (leitor.ElementType == typeof(Cell) && leitor.IsStartElement && atual is not null)
            {
                var celula = (Cell)leitor.LoadCurrentElement()!;
                if (celula.CellReference?.Value is not { } referencia)
                {
                    continue;
                }

                int coluna = ColunaDe(referencia);
                if (coluna >= coluna1 && coluna <= coluna2)
                {
                    atual[coluna - coluna1] = Valor(celula);
                }
            }
        }

        // Tira as linhas vazias do fim (abas lidas ate um limite fixo).
        while (linhas.Count > 0 && linhas[^1].All(v => v is null))
        {
            linhas.RemoveAt(linhas.Count - 1);
        }

        return linhas;
    }

    private object? Valor(Cell celula)
    {
        string? bruto = celula.CellValue?.Text;
        var tipo = celula.DataType?.Value;

        if (tipo == CellValues.SharedString)
        {
            return int.TryParse(bruto, NumberStyles.None, CultureInfo.InvariantCulture, out int i) && i < Textos().Count ? Vazio(Textos()[i]) : null;
        }

        if (tipo == CellValues.InlineString)
        {
            return Vazio(celula.InlineString?.InnerText);
        }

        if (tipo == CellValues.String)
        {
            return Vazio(bruto);
        }

        if (tipo == CellValues.Boolean)
        {
            return bruto == "1";
        }

        if (tipo == CellValues.Error || string.IsNullOrEmpty(bruto))
        {
            return null; // #REF!, #N/D etc. viram "sem informacao", nunca zero
        }

        if (tipo == CellValues.Date)
        {
            return DateTime.TryParse(bruto, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var iso) ? iso : null;
        }

        // Numero: data se o formato da celula for de data; senao decimal exato.
        if (!double.TryParse(bruto, NumberStyles.Float, CultureInfo.InvariantCulture, out double numero))
        {
            return null;
        }

        if (celula.StyleIndex?.Value is { } estilo && EstilosDeData().Contains(estilo))
        {
            return numero is > -657435 and < 2958466 ? DateTime.FromOADate(numero) : null;
        }

        return decimal.TryParse(bruto, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal exato)
            ? exato
            : Math.Abs(numero) < 7.9e27 ? (decimal)numero : null;
    }

    private static string? Vazio(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto;

    private List<string> Textos()
    {
        if (_textos is not null)
        {
            return _textos;
        }

        _textos = [];
        if (_pasta.SharedStringTablePart is { } parte)
        {
            using var leitor = OpenXmlReader.Create(parte);
            while (leitor.Read())
            {
                if (leitor.ElementType == typeof(SharedStringItem) && leitor.IsStartElement)
                {
                    var item = (SharedStringItem)leitor.LoadCurrentElement()!;
                    // Texto com formatacao vem em pedacos; a parte fonetica (rPh) nao entra.
                    _textos.Add(item.Text?.Text ?? string.Concat(item.Elements<Run>().Select(r => r.Text?.Text)));
                }
            }
        }

        return _textos;
    }

    /// <summary>Indices de estilo de celula cujo formato de numero e data ou hora.</summary>
    private HashSet<uint> EstilosDeData()
    {
        if (_estilosDeData is not null)
        {
            return _estilosDeData;
        }

        _estilosDeData = [];
        var folha = _pasta.WorkbookStylesPart?.Stylesheet;
        if (folha?.CellFormats is null)
        {
            return _estilosDeData;
        }

        var proprios = folha.NumberingFormats?.Elements<NumberingFormat>()
            .Where(f => f.NumberFormatId?.Value is not null)
            .ToDictionary(f => f.NumberFormatId!.Value, f => f.FormatCode?.Value ?? "") ?? [];

        uint indice = 0;
        foreach (var formato in folha.CellFormats.Elements<CellFormat>())
        {
            uint id = formato.NumberFormatId?.Value ?? 0;
            bool data = id is >= 14 and <= 22 or >= 27 and <= 36 or >= 45 and <= 47 or >= 50 and <= 58
                        || (proprios.TryGetValue(id, out string? codigo) && FormatoDeData(codigo));
            if (data)
            {
                _estilosDeData.Add(indice);
            }

            indice++;
        }

        return _estilosDeData;
    }

    /// <summary>Formato proprio e de data se tiver d, m, y ou h fora de textos entre aspas e colchetes.</summary>
    private static bool FormatoDeData(string codigo)
    {
        bool aspas = false, colchete = false;
        foreach (char c in codigo)
        {
            if (c == '"')
            {
                aspas = !aspas;
            }
            else if (!aspas && c == '[')
            {
                colchete = true;
            }
            else if (!aspas && c == ']')
            {
                colchete = false;
            }
            else if (!aspas && !colchete && "dmyhDMYH".Contains(c))
            {
                return true;
            }
        }

        return false;
    }

    private static (int Coluna1, int Linha1, int Coluna2, int Linha2) Faixa(string referencia)
    {
        string[] pontas = referencia.Replace("$", "").Split(':');
        string fim = pontas.Length > 1 ? pontas[1] : pontas[0];
        return (ColunaDe(pontas[0]), LinhaDe(pontas[0]), ColunaDe(fim), LinhaDe(fim));
    }

    private static int ColunaDe(string referencia)
    {
        int coluna = 0;
        foreach (char c in referencia)
        {
            if (!char.IsAsciiLetter(c))
            {
                break;
            }

            coluna = coluna * 26 + (char.ToUpperInvariant(c) - 'A' + 1);
        }

        return coluna;
    }

    private static int LinhaDe(string referencia) =>
        int.Parse(referencia.AsSpan(referencia.AsSpan().IndexOfAnyInRange('0', '9')), CultureInfo.InvariantCulture);
}
