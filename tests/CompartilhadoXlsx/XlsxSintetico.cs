using System.Globalization;
using System.Security;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;

namespace Spobras.Testes;

/// <summary>Celula com formula (o valor guardado e um 0 antigo, como num modelo salvo antes).</summary>
public sealed record Formula(string Texto);

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

    /// <summary>
    /// Modelo de cronograma sintetico com a mesma estrutura do oficial: duas abas de cronograma, Parametros
    /// (que usa a segunda aba no resumo) e Dotacoes.
    /// </summary>
    public static void GravarModeloCronograma(string caminho)
    {
        List<object?[]> Aba()
        {
            var g = Enumerable.Range(0, 18).Select(_ => new object?[33]).ToList();
            g[0][1] = new Formula("IF(COUNTA(E3:E17)=0,\"Cronograma SPOBRAS\",\"Cronograma SPOBRAS - \"&COUNTA(E3:E17)&\" unidades\")");
            string[] cab = ["Portaria ", "Lote ", "Dotação ", "Nome da Obra", "Breve escopo", "Valores para Obras", "Valores de Remuneração", "Valores Total", "Início", "Fim"];
            for (int i = 0; i < cab.Length; i++)
            {
                g[1][1 + i] = cab[i];
            }

            g[1][11] = new Formula("IF(COUNT($J$3:$J$17)=0,\"\",MIN($J$3:$J$17))");
            g[1][12] = new Formula("IF(L2=\"\",\"\",EDATE(L2,1))");
            for (int l = 3; l <= 17; l++)
            {
                g[l - 1][7] = new Formula($"IF($G{l}=\"\",\"\",$G{l}*0.1)");
                g[l - 1][8] = new Formula($"IF($G{l}=\"\",\"\",SUM($G{l}:$H{l}))");
                g[l - 1][11] = new Formula($"IF($E{l}=\"\",\"\",$I{l})");
                g[l - 1][31] = new Formula($"IF($E{l}=\"\",\"\",$I{l})");
                g[l - 1][32] = new Formula($"IF($E{l}=\"\",\"\",0)");
            }

            g[17][1] = "TOTAL";
            g[17][6] = new Formula("SUM(G3:G17)");
            g[17][7] = new Formula("SUM(H3:H17)");
            g[17][8] = new Formula("SUM(I3:I17)");
            g[17][11] = new Formula("SUM(L3:L17)");
            return g;
        }

        var planilha = new PlanilhaFalsa();
        planilha.AbasPorNome["Cronograma Um"] = Aba();
        planilha.AbasPorNome["Cronograma Dois"] = Aba();
        var parametros = Enumerable.Range(0, 20).Select(_ => new object?[2]).ToList();
        parametros[0] = ["Parâmetro", "Valor"];
        parametros[1] = ["Percentual de remuneração", 0.1m];
        parametros[12] = ["Dotação única 1", new Formula("IFERROR(INDEX('Cronograma Dois'!$D$3:$D$17,1),\"\")")];
        parametros[19] = [null, new Formula("IF(ABS('Cronograma Dois'!I18-'Cronograma Dois'!I18)<0.01,\"RESUMO OK\",\"VERIFICAR RESUMO\")")];
        planilha.AbasPorNome["Parametros"] = parametros;
        planilha.AbasPorNome["Dotacoes"] =
        [
            ["Dotação Completa Remuneração", "Dotação Completa", "Descrição", "Dotação Resumida"],
            ["16.10.12.365.3010.2878.3390.3900.00", "16.10.12.365.3010.2878.3390.3900.00", "2878 - Conservação", "2878"],
            ["16.10.12.365.3025.3362.4490.3900.00", "16.10.12.365.3025.3362.4490.5100.00", "3362 - Ampliação", "3362"],
        ];
        Gravar(planilha, caminho);
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
                    case Formula f:
                        xml.Append($"""<c r="{r}"><f>{SecurityElement.Escape(f.Texto)}</f><v>0</v></c>""");
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
