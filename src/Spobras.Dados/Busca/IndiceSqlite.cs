using Spobras.Core.Ferramentas;

namespace Spobras.Dados.Busca;

public sealed class IndiceSqlite(BancoApp banco) : IIndiceTexto
{
    public TextoIndexado? Ler(string caminho, long tamanho, long modificado)
    {
        using var conexao = banco.Abrir();
        using var cmd = conexao.CreateCommand();
        cmd.CommandText = "select texto, motivo from IndiceArquivo where caminho = $c and tamanho = $t and modificado = $m";
        cmd.Parameters.AddWithValue("$c", caminho);
        cmd.Parameters.AddWithValue("$t", tamanho);
        cmd.Parameters.AddWithValue("$m", modificado);
        using var leitor = cmd.ExecuteReader();
        return leitor.Read() ? new TextoIndexado(leitor.IsDBNull(0) ? null : leitor.GetString(0), leitor.IsDBNull(1) ? null : leitor.GetString(1)) : null;
    }

    public void Gravar(string caminho, long tamanho, long modificado, TextoIndexado texto)
    {
        using var conexao = banco.Abrir();
        using var cmd = conexao.CreateCommand();
        cmd.CommandText = """
            insert into IndiceArquivo (caminho, tamanho, modificado, texto, motivo) values ($c, $t, $m, $x, $r)
            on conflict(caminho) do update set tamanho = excluded.tamanho, modificado = excluded.modificado, texto = excluded.texto, motivo = excluded.motivo
            """;
        cmd.Parameters.AddWithValue("$c", caminho);
        cmd.Parameters.AddWithValue("$t", tamanho);
        cmd.Parameters.AddWithValue("$m", modificado);
        cmd.Parameters.AddWithValue("$x", (object?)texto.Texto ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$r", (object?)texto.Motivo ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public int Total()
    {
        using var conexao = banco.Abrir();
        using var cmd = conexao.CreateCommand();
        cmd.CommandText = "select count(*) from IndiceArquivo";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }
}
