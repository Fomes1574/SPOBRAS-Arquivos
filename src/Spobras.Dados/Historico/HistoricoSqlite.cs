using System.Globalization;
using Microsoft.Data.Sqlite;
using Spobras.Core.Operacoes;

namespace Spobras.Dados.Historico;

/// <summary>Historico por arquivo gravado no SQLite a cada mudanca de estado.</summary>
public sealed class HistoricoSqlite(BancoApp banco) : IHistorico
{
    public void CriarLote(Lote lote)
    {
        using var conexao = banco.Abrir();
        using var transacao = conexao.BeginTransaction();
        using (var cmd = conexao.CreateCommand())
        {
            cmd.Transaction = transacao;
            cmd.CommandText = """
                insert into Lote (id, tipo, descricao, criado_em, desfaz_lote_id, desfeito_por_lote_id)
                values ($id, $tipo, $descricao, $criado, $desfaz, $desfeito)
                """;
            cmd.Parameters.AddWithValue("$id", lote.Id.ToString());
            cmd.Parameters.AddWithValue("$tipo", (int)lote.Tipo);
            cmd.Parameters.AddWithValue("$descricao", lote.Descricao);
            cmd.Parameters.AddWithValue("$criado", Texto(lote.CriadoEm));
            cmd.Parameters.AddWithValue("$desfaz", (object?)lote.DesfazLoteId?.ToString() ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$desfeito", (object?)lote.DesfeitoPorLoteId?.ToString() ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }

        foreach (var item in lote.Itens)
        {
            using var cmd = conexao.CreateCommand();
            cmd.Transaction = transacao;
            cmd.CommandText = """
                insert into LoteItem (id, lote_id, ordem, origem, destino, estado, mensagem, temporario, tamanho, modificado_em_ticks, concluido_em)
                values ($id, $lote, $ordem, $origem, $destino, $estado, $mensagem, $temporario, $tamanho, $modificado, $concluido)
                """;
            cmd.Parameters.AddWithValue("$id", item.Id.ToString());
            cmd.Parameters.AddWithValue("$lote", lote.Id.ToString());
            cmd.Parameters.AddWithValue("$ordem", item.Ordem);
            cmd.Parameters.AddWithValue("$origem", item.Origem);
            cmd.Parameters.AddWithValue("$destino", item.Destino);
            ParametrosDeEstado(cmd, item);
            cmd.ExecuteNonQuery();
        }

        transacao.Commit();
    }

    public void AtualizarItem(Guid loteId, ItemLote item)
    {
        using var conexao = banco.Abrir();
        using var cmd = conexao.CreateCommand();
        cmd.CommandText = """
            update LoteItem
            set estado = $estado, mensagem = $mensagem, temporario = $temporario,
                tamanho = $tamanho, modificado_em_ticks = $modificado, concluido_em = $concluido
            where id = $id
            """;
        cmd.Parameters.AddWithValue("$id", item.Id.ToString());
        ParametrosDeEstado(cmd, item);
        cmd.ExecuteNonQuery();
    }

    public void MarcarDesfeito(Guid loteId, Guid desfeitoPorLoteId)
    {
        using var conexao = banco.Abrir();
        using var cmd = conexao.CreateCommand();
        cmd.CommandText = "update Lote set desfeito_por_lote_id = $desfeito where id = $id";
        cmd.Parameters.AddWithValue("$id", loteId.ToString());
        cmd.Parameters.AddWithValue("$desfeito", desfeitoPorLoteId.ToString());
        cmd.ExecuteNonQuery();
    }

    public Lote? Obter(Guid loteId)
    {
        using var conexao = banco.Abrir();
        return Ler(conexao, "where id = $id", c => c.Parameters.AddWithValue("$id", loteId.ToString())).FirstOrDefault();
    }

    public IReadOnlyList<Lote> Listar(int maximo = 100)
    {
        using var conexao = banco.Abrir();
        return Ler(conexao, "order by criado_em desc limit $max", c => c.Parameters.AddWithValue("$max", maximo));
    }

    private static List<Lote> Ler(SqliteConnection conexao, string filtro, Action<SqliteCommand> parametros)
    {
        var lotes = new List<Lote>();
        using (var cmd = conexao.CreateCommand())
        {
            cmd.CommandText = "select id, tipo, descricao, criado_em, desfaz_lote_id, desfeito_por_lote_id from Lote " + filtro;
            parametros(cmd);
            using var leitor = cmd.ExecuteReader();
            while (leitor.Read())
            {
                lotes.Add(new Lote
                {
                    Id = Guid.Parse(leitor.GetString(0)),
                    Tipo = (TipoLote)leitor.GetInt32(1),
                    Descricao = leitor.GetString(2),
                    CriadoEm = Data(leitor.GetString(3)),
                    DesfazLoteId = leitor.IsDBNull(4) ? null : Guid.Parse(leitor.GetString(4)),
                    DesfeitoPorLoteId = leitor.IsDBNull(5) ? null : Guid.Parse(leitor.GetString(5)),
                });
            }
        }

        foreach (var lote in lotes)
        {
            using var cmd = conexao.CreateCommand();
            cmd.CommandText = """
                select id, ordem, origem, destino, estado, mensagem, temporario, tamanho, modificado_em_ticks, concluido_em
                from LoteItem where lote_id = $lote order by ordem
                """;
            cmd.Parameters.AddWithValue("$lote", lote.Id.ToString());
            using var leitor = cmd.ExecuteReader();
            while (leitor.Read())
            {
                lote.Itens.Add(new ItemLote
                {
                    Id = Guid.Parse(leitor.GetString(0)),
                    Ordem = leitor.GetInt32(1),
                    Origem = leitor.GetString(2),
                    Destino = leitor.GetString(3),
                    Estado = (EstadoItem)leitor.GetInt32(4),
                    Mensagem = leitor.IsDBNull(5) ? null : leitor.GetString(5),
                    Temporario = leitor.IsDBNull(6) ? null : leitor.GetString(6),
                    Tamanho = leitor.IsDBNull(7) ? null : leitor.GetInt64(7),
                    ModificadoEm = leitor.IsDBNull(8) ? null : new DateTimeOffset(leitor.GetInt64(8), TimeSpan.Zero),
                    ConcluidoEm = leitor.IsDBNull(9) ? null : Data(leitor.GetString(9)),
                });
            }
        }

        return lotes;
    }

    private static void ParametrosDeEstado(SqliteCommand cmd, ItemLote item)
    {
        cmd.Parameters.AddWithValue("$estado", (int)item.Estado);
        cmd.Parameters.AddWithValue("$mensagem", (object?)item.Mensagem ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$temporario", (object?)item.Temporario ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$tamanho", (object?)item.Tamanho ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$modificado", (object?)item.ModificadoEm?.UtcTicks ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$concluido", item.ConcluidoEm is { } c ? Texto(c) : DBNull.Value);
    }

    // Formato ISO em UTC: ordena certo como texto.
    private static string Texto(DateTimeOffset data) => data.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset Data(string texto) =>
        DateTimeOffset.Parse(texto, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
