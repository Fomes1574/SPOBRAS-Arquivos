using System.Globalization;
using System.Security;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;

namespace Spobras.Testes;

/// <summary>Grava uma planilha .xlsx de verdade (com tabelas nomeadas) a partir de dados sinteticos.</summary>
public static class XlsxSintetico
{
    public static void Gravar(PlanilhaFalsa planilha, string caminho)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(caminho)!);
        using var doc = SpreadsheetDocument.Create(caminho, SpreadsheetDocumentType.Workbook);
        var pasta = doc.AddWorkbookPart();
        var estilos = pasta.AddNewPart<WorkbookStylesPart>();
        Escrever(estilos,
            """<?xml version="1.0" encoding="UTF-8"?><styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><fonts count="1"><font><sz val="11"/><name val="Calibri"/></font></fonts><fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills><borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders><cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs><cellXfs count="2"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/><xf numFmtId="14" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/></cellXfs></styleSheet>""");

        var abas = new StringBuilder();
        uint idAba = 1, idTabela = 1;

        foreach (var tabela in planilha.TabelasPorNome.Values)
        {
            var parte = pasta.AddNewPart<WorksheetPart>();
            var grade = new List<object?[]> { tabela.Colunas.Cast<object?>().ToArray() };
            grade.AddRange(tabela.Linhas);
            var definicao = parte.AddNewPart<TableDefinitionPart>();
            string faixa = $"A1:{Coluna(tabela.Colunas.Count)}{grade.Count}";
            var colunas = string.Concat(tabela.Colunas.Select((c, i) => $"""<tableColumn id="{i + 1}" name="{SecurityElement.Escape(c)}"/>"""));
            Escrever(definicao,
                $"""<?xml version="1.0" encoding="UTF-8"?><table xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" id="{idTabela}" name="{tabela.Nome}" displayName="{tabela.Nome}" ref="{faixa}"><autoFilter ref="{faixa}"/><tableColumns count="{tabela.Colunas.Count}">{colunas}</tableColumns></table>""");
            Escrever(parte, Planilha(grade, $"""<tableParts count="1"><tablePart r:id="{parte.GetIdOfPart(definicao)}"/></tableParts>"""));
            // Nome de aba diferente do nome da tabela e com no maximo 31 caracteres, como no arquivo real.
            string nomeAba = (tabela.Nome.Replace('_', ' ') + " aba")[..Math.Min(31, tabela.Nome.Length + 4)];
            abas.Append($"""<sheet name="{SecurityElement.Escape(nomeAba)}" sheetId="{idAba++}" r:id="{pasta.GetIdOfPart(parte)}"/>""");
            idTabela++;
        }

        foreach (var (nome, grade) in planilha.AbasPorNome)
        {
            var parte = pasta.AddNewPart<WorksheetPart>();
            Escrever(parte, Planilha(grade, ""));
            abas.Append($"""<sheet name="{SecurityElement.Escape(nome)}" sheetId="{idAba++}" r:id="{pasta.GetIdOfPart(parte)}"/>""");
        }

        Escrever(pasta,
            $"""<?xml version="1.0" encoding="UTF-8"?><workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets>{abas}</sheets></workbook>""");
    }

    private static string Planilha(IReadOnlyList<object?[]> grade, string fim)
    {
        var xml = new StringBuilder("""<?xml version="1.0" encoding="UTF-8"?><worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheetData>""");
        for (int l = 0; l < grade.Count; l++)
        {
            if (grade[l].All(v => v is null))
            {
                continue; // linha vazia nao existe no arquivo, como no Excel
            }

            xml.Append($"""<row r="{l + 1}">""");
            for (int c = 0; c < grade[l].Length; c++)
            {
                string r = $"{Coluna(c + 1)}{l + 1}";
                switch (grade[l][c])
                {
                    case null:
                        break;
                    case string s:
                        xml.Append($"""<c r="{r}" t="inlineStr"><is><t xml:space="preserve">{SecurityElement.Escape(s)}</t></is></c>""");
                        break;
                    case DateTime d:
                        xml.Append(CultureInfo.InvariantCulture, $"""<c r="{r}" s="1"><v>{d.ToOADate()}</v></c>""");
                        break;
                    case bool b:
                        xml.Append($"""<c r="{r}" t="b"><v>{(b ? 1 : 0)}</v></c>""");
                        break;
                    case IFormattable n:
                        xml.Append($"""<c r="{r}"><v>{n.ToString(null, CultureInfo.InvariantCulture)}</v></c>""");
                        break;
                }
            }

            xml.Append("</row>");
        }

        return xml.Append("</sheetData>").Append(fim).Append("</worksheet>").ToString();
    }

    private static void Escrever(OpenXmlPart parte, string xml)
    {
        using var fluxo = parte.GetStream(FileMode.Create, FileAccess.Write);
        using var escritor = new StreamWriter(fluxo, new UTF8Encoding(false));
        escritor.Write(xml);
    }

    private static string Coluna(int numero)
    {
        string letras = "";
        for (int n = numero; n > 0; n = (n - 1) / 26)
        {
            letras = (char)('A' + (n - 1) % 26) + letras;
        }

        return letras;
    }
}
