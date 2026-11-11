using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Spobras.Core.Organizar;

namespace Spobras.Dados.Configuracao;

internal static class Json
{
    public static readonly JsonSerializerOptions Opcoes = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}

/// <summary>Configuracoes do app: um valor (JSON) por chave.</summary>
public sealed class ConfigRepositorio(BancoApp banco)
{
    public T? Ler<T>(string chave)
    {
        using var conexao = banco.Abrir();
        using var cmd = conexao.CreateCommand();
        cmd.CommandText = "select valor from Config where chave = $chave";
        cmd.Parameters.AddWithValue("$chave", chave);
        return cmd.ExecuteScalar() is string json ? JsonSerializer.Deserialize<T>(json, Json.Opcoes) : default;
    }

    public void Gravar<T>(string chave, T valor)
    {
        using var conexao = banco.Abrir();
        using var cmd = conexao.CreateCommand();
        cmd.CommandText = """
            insert into Config (chave, valor) values ($chave, $valor)
            on conflict(chave) do update set valor = excluded.valor
            """;
        cmd.Parameters.AddWithValue("$chave", chave);
        cmd.Parameters.AddWithValue("$valor", JsonSerializer.Serialize(valor, Json.Opcoes));
        cmd.ExecuteNonQuery();
    }
}

/// <summary>Regras do organizador, na ordem em que o usuario as ve.</summary>
public sealed class RegraRepositorio(BancoApp banco)
{
    public IReadOnlyList<Regra> Listar()
    {
        using var conexao = banco.Abrir();
        using var cmd = conexao.CreateCommand();
        cmd.CommandText = "select json from Regra order by ordem";
        using var leitor = cmd.ExecuteReader();
        var regras = new List<Regra>();
        while (leitor.Read())
        {
            if (JsonSerializer.Deserialize<Regra>(leitor.GetString(0), Json.Opcoes) is { } regra)
            {
                regras.Add(regra);
            }
        }

        return regras;
    }

    /// <summary>Substitui a lista inteira (as regras sao poucas e editadas juntas).</summary>
    public void GravarTodas(IReadOnlyList<Regra> regras)
    {
        using var conexao = banco.Abrir();
        using var transacao = conexao.BeginTransaction();
        Executar(conexao, transacao, "delete from Regra");
        for (int i = 0; i < regras.Count; i++)
        {
            Executar(conexao, transacao, "insert into Regra (id, ordem, json) values ($id, $ordem, $json)",
                ("$id", regras[i].Id.ToString()), ("$ordem", i), ("$json", JsonSerializer.Serialize(regras[i], Json.Opcoes)));
        }

        transacao.Commit();
    }

    private static void Executar(SqliteConnection conexao, SqliteTransaction transacao, string sql, params (string Nome, object Valor)[] parametros)
    {
        using var cmd = conexao.CreateCommand();
        cmd.Transaction = transacao;
        cmd.CommandText = sql;
        foreach (var (nome, valor) in parametros)
        {
            cmd.Parameters.AddWithValue(nome, valor);
        }

        cmd.ExecuteNonQuery();
    }
}

public sealed record PerfilGuardado(Guid Id, string Modulo, string Nome, string Json);

/// <summary>Perfis: conjuntos de opcoes salvos com nome, por modulo.</summary>
public sealed class PerfilRepositorio(BancoApp banco)
{
    public IReadOnlyList<PerfilGuardado> Listar(string modulo)
    {
        using var conexao = banco.Abrir();
        using var cmd = conexao.CreateCommand();
        cmd.CommandText = "select id, modulo, nome, json from Perfil where modulo = $modulo order by nome collate nocase";
        cmd.Parameters.AddWithValue("$modulo", modulo);
        using var leitor = cmd.ExecuteReader();
        var perfis = new List<PerfilGuardado>();
        while (leitor.Read())
        {
            perfis.Add(new PerfilGuardado(Guid.Parse(leitor.GetString(0)), leitor.GetString(1), leitor.GetString(2), leitor.GetString(3)));
        }

        return perfis;
    }

    /// <summary>Grava o perfil; outro perfil com o mesmo nome no mesmo modulo e substituido.</summary>
    public PerfilGuardado Salvar<T>(string modulo, string nome, T opcoes)
    {
        var perfil = new PerfilGuardado(Guid.NewGuid(), modulo, nome.Trim(), JsonSerializer.Serialize(opcoes, Json.Opcoes));
        using var conexao = banco.Abrir();
        using var cmd = conexao.CreateCommand();
        cmd.CommandText = """
            insert into Perfil (id, modulo, nome, json) values ($id, $modulo, $nome, $json)
            on conflict(modulo, nome) do update set json = excluded.json
            """;
        cmd.Parameters.AddWithValue("$id", perfil.Id.ToString());
        cmd.Parameters.AddWithValue("$modulo", perfil.Modulo);
        cmd.Parameters.AddWithValue("$nome", perfil.Nome);
        cmd.Parameters.AddWithValue("$json", perfil.Json);
        cmd.ExecuteNonQuery();
        return Listar(modulo).First(p => p.Nome.Equals(perfil.Nome, StringComparison.OrdinalIgnoreCase));
    }

    public void Remover(Guid id)
    {
        using var conexao = banco.Abrir();
        using var cmd = conexao.CreateCommand();
        cmd.CommandText = "delete from Perfil where id = $id";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        cmd.ExecuteNonQuery();
    }

    public static T? Abrir<T>(PerfilGuardado perfil) => JsonSerializer.Deserialize<T>(perfil.Json, Json.Opcoes);
}

/// <summary>Rascunhos: o trabalho em preparacao de cada modulo, gravado sozinho a cada mudanca.</summary>
public sealed class RascunhoRepositorio(BancoApp banco)
{
    public T? Ler<T>(string chave)
    {
        using var conexao = banco.Abrir();
        using var cmd = conexao.CreateCommand();
        cmd.CommandText = "select json from Rascunho where chave = $chave";
        cmd.Parameters.AddWithValue("$chave", chave);
        try
        {
            return cmd.ExecuteScalar() is string json ? JsonSerializer.Deserialize<T>(json, Json.Opcoes) : default;
        }
        catch (JsonException)
        {
            return default; // rascunho de uma versao antiga que nao da mais para ler: comeca vazio
        }
    }

    public void Gravar<T>(string chave, T valor, DateTimeOffset agora)
    {
        using var conexao = banco.Abrir();
        using var cmd = conexao.CreateCommand();
        cmd.CommandText = """
            insert into Rascunho (chave, json, atualizado_em) values ($chave, $json, $agora)
            on conflict(chave) do update set json = excluded.json, atualizado_em = excluded.atualizado_em
            """;
        cmd.Parameters.AddWithValue("$chave", chave);
        cmd.Parameters.AddWithValue("$json", JsonSerializer.Serialize(valor, Json.Opcoes));
        cmd.Parameters.AddWithValue("$agora", agora.UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        cmd.ExecuteNonQuery();
    }

    public void Remover(string chave)
    {
        using var conexao = banco.Abrir();
        using var cmd = conexao.CreateCommand();
        cmd.CommandText = "delete from Rascunho where chave = $chave";
        cmd.Parameters.AddWithValue("$chave", chave);
        cmd.ExecuteNonQuery();
    }
}
