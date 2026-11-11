using System.Globalization;
using Microsoft.Data.Sqlite;
using Spobras.Core.Ambiente;

namespace Spobras.Dados.Ambiente;

/// <summary>Item 7: grava e le um banco SQLite de teste na pasta de dados do app.</summary>
public static class VerificacaoSqlite
{
    public const int Numero = 7;
    public const string Titulo = "Gravar e ler o SQLite em %LOCALAPPDATA%";

    public static ResultadoVerificacao Executar(string pastaDados)
    {
        string caminho = Path.Combine(pastaDados, "teste-ambiente-" + Guid.NewGuid().ToString("N")[..8] + ".db");
        try
        {
            Directory.CreateDirectory(pastaDados);

            const string texto = "Dotação 3390.39.00 - ação nº 1";
            const decimal valor = 1234567.89m;
            string versao;

            var cs = new SqliteConnectionStringBuilder { DataSource = caminho, Pooling = false }.ToString();
            using (var conexao = new SqliteConnection(cs))
            {
                conexao.Open();
                using var cmd = conexao.CreateCommand();
                cmd.CommandText = "select sqlite_version()";
                versao = (string)cmd.ExecuteScalar()!;

                cmd.CommandText = "create table teste (id integer primary key, texto text not null, valor text not null)";
                cmd.ExecuteNonQuery();

                using var transacao = conexao.BeginTransaction();
                cmd.Transaction = transacao;
                cmd.CommandText = "insert into teste (texto, valor) values ($texto, $valor)";
                cmd.Parameters.AddWithValue("$texto", texto);
                cmd.Parameters.AddWithValue("$valor", valor.ToString(CultureInfo.InvariantCulture));
                cmd.ExecuteNonQuery();
                transacao.Commit();
            }

            // Reabre o arquivo para garantir que o dado foi mesmo para o disco.
            using (var conexao = new SqliteConnection(cs))
            {
                conexao.Open();
                using var cmd = conexao.CreateCommand();
                cmd.CommandText = "select texto, valor from teste";
                using var leitor = cmd.ExecuteReader();
                if (!leitor.Read())
                {
                    return ResultadoVerificacao.Falhou(Numero, Titulo, $"Pasta: {pastaDados}", "A linha gravada não foi encontrada.");
                }

                string textoLido = leitor.GetString(0);
                decimal valorLido = decimal.Parse(leitor.GetString(1), CultureInfo.InvariantCulture);
                if (textoLido != texto || valorLido != valor)
                {
                    return ResultadoVerificacao.Falhou(Numero, Titulo, $"Pasta: {pastaDados}",
                        $"Valor lido diferente do gravado: \"{textoLido}\" / {valorLido}.");
                }
            }

            return ResultadoVerificacao.Passou(Numero, Titulo,
                $"Pasta: {pastaDados}",
                $"SQLite {versao}: criar, gravar, reabrir e ler: ok");
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException or DllNotFoundException)
        {
            return ResultadoVerificacao.Falhou(Numero, Titulo, $"Pasta: {pastaDados}", $"Falha: {ex.Message}");
        }
        finally
        {
            try
            {
                File.Delete(caminho);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Banco de teste nosso.
            }
        }
    }
}
