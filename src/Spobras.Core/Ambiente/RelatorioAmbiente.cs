using System.Globalization;
using System.Text;

namespace Spobras.Core.Ambiente;

/// <summary>Monta o texto do relatorio-ambiente.txt.</summary>
public static class RelatorioAmbiente
{
    public const string NomeArquivo = "relatorio-ambiente.txt";

    public static string Gerar(
        IReadOnlyList<ResultadoVerificacao> resultados,
        DateTimeOffset quando,
        string versaoPrograma,
        MascaraCaminhos mascara)
    {
        var sb = new StringBuilder();
        sb.Append("Arquivos SPOBRAS - Teste de Ambiente\r\n");
        sb.Append("Versão do teste: ").Append(versaoPrograma).Append("\r\n");
        sb.Append("Data: ").Append(quando.ToString("dd/MM/yyyy HH:mm:ss zzz", CultureInfo.InvariantCulture)).Append("\r\n");
        sb.Append("\r\n");

        int passou = resultados.Count(r => r.Estado == EstadoVerificacao.Passou);
        int falhou = resultados.Count(r => r.Estado == EstadoVerificacao.Falhou);
        int naoVerificado = resultados.Count(r => r.Estado == EstadoVerificacao.NaoVerificado);
        sb.Append("Resumo: ").Append(passou).Append(" passou, ")
          .Append(falhou).Append(" falhou, ")
          .Append(naoVerificado).Append(" não verificado\r\n");

        foreach (var r in resultados.OrderBy(r => r.Numero))
        {
            sb.Append("\r\n");
            sb.Append('[').Append(r.EstadoTexto).Append("] ")
              .Append(r.Numero).Append(". ").Append(mascara.Aplicar(r.Titulo)).Append("\r\n");
            foreach (var detalhe in r.Detalhes)
            {
                foreach (var linha in detalhe.Split('\n'))
                {
                    sb.Append("    ").Append(mascara.Aplicar(linha.TrimEnd('\r'))).Append("\r\n");
                }
            }
        }

        return sb.ToString();
    }
}
