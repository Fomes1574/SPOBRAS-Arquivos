using Microsoft.Data.Sqlite;

namespace Spobras.Dados;

/// <summary>Banco local do app (SQLite) com migracoes numeradas por PRAGMA user_version.</summary>
public sealed class BancoApp
{
    private static readonly string[] Migracoes =
    [
        // 1: historico por arquivo
        """
        create table Lote (
            id text primary key,
            tipo integer not null,
            descricao text not null,
            criado_em text not null,
            desfaz_lote_id text null,
            desfeito_por_lote_id text null
        );
        create table LoteItem (
            id text primary key,
            lote_id text not null references Lote(id),
            ordem integer not null,
            origem text not null,
            destino text not null,
            estado integer not null,
            mensagem text null,
            temporario text null,
            tamanho integer null,
            modificado_em_ticks integer null,
            concluido_em text null
        );
        create index LoteItem_lote on LoteItem(lote_id, ordem);
        """,
        // 2: configuracoes, regras do organizador e perfis
        """
        create table Config (
            chave text primary key,
            valor text not null
        );
        create table Regra (
            id text primary key,
            ordem integer not null,
            json text not null
        );
        create table Perfil (
            id text primary key,
            modulo text not null,
            nome text not null,
            json text not null
        );
        create unique index Perfil_nome on Perfil(modulo, nome);
        """,
    ];

    private readonly string _conexao;

    public BancoApp(string caminho)
    {
        string? pasta = Path.GetDirectoryName(caminho);
        if (!string.IsNullOrEmpty(pasta))
        {
            Directory.CreateDirectory(pasta);
        }

        _conexao = new SqliteConnectionStringBuilder { DataSource = caminho, Pooling = false }.ToString();
        Migrar();
    }

    public static int VersaoAtual => Migracoes.Length;

    public SqliteConnection Abrir()
    {
        var conexao = new SqliteConnection(_conexao);
        conexao.Open();
        using var cmd = conexao.CreateCommand();
        cmd.CommandText = "pragma foreign_keys = on; pragma journal_mode = wal; pragma synchronous = normal;";
        cmd.ExecuteNonQuery();
        return conexao;
    }

    private void Migrar()
    {
        using var conexao = Abrir();
        using var cmd = conexao.CreateCommand();
        cmd.CommandText = "pragma user_version";
        int versao = Convert.ToInt32(cmd.ExecuteScalar());
        if (versao > Migracoes.Length)
        {
            throw new InvalidOperationException(
                "O banco de dados foi criado por uma versão mais nova do app. Atualize o app para abrir este banco.");
        }

        for (int i = versao; i < Migracoes.Length; i++)
        {
            // Cada migracao entra inteira ou nao entra.
            using var transacao = conexao.BeginTransaction();
            using var passo = conexao.CreateCommand();
            passo.Transaction = transacao;
            passo.CommandText = Migracoes[i] + $"; pragma user_version = {i + 1};";
            passo.ExecuteNonQuery();
            transacao.Commit();
        }
    }
}
