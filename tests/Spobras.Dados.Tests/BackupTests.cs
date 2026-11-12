using Microsoft.Data.Sqlite;
using Spobras.Dados;
using Spobras.Dados.Configuracao;

namespace Spobras.Dados.Tests;

public class BackupTests : IDisposable
{
    private readonly string _pasta = Directory.CreateTempSubdirectory("spobras-backup-").FullName;

    private string Db => Path.Combine(_pasta, "arquivos-spobras.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_pasta, recursive: true);
    }

    private static void Sql(BancoApp banco, string sql)
    {
        using var c = banco.Abrir();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public void T23_atualizacao_guarda_copia_antes_de_migrar_e_preserva_os_dados()
    {
        var banco = new BancoApp(Db);
        new ConfigRepositorio(banco).Gravar("teste", "valor antigo");
        Assert.Empty(banco.Backups()); // banco novo: nada para guardar
        // Simula um banco da versao anterior do app
        Sql(banco, "drop table AjusteObra; drop table ObraLocal; drop table VinculoLocal; alter table Obra drop column local; "
                   + $"alter table Obra drop column editados; alter table Vinculo drop column origem; pragma user_version = {BancoApp.VersaoAtual - 1};");

        var atualizado = new BancoApp(Db);

        var copia = Assert.Single(atualizado.Backups());
        Assert.Contains($"antes de atualizar (versão {BancoApp.VersaoAtual - 1})", copia.Name);
        Assert.Equal("valor antigo", new ConfigRepositorio(atualizado).Ler<string>("teste"));
        Sql(atualizado, "select count(*) from AjusteObra"); // migracao aplicada

        using var c = new SqliteConnection($"Data Source={copia.FullName};Mode=ReadOnly;Pooling=False");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "pragma user_version";
        Assert.Equal(BancoApp.VersaoAtual - 1, Convert.ToInt32(cmd.ExecuteScalar()));
    }

    [Fact]
    public void Restaurar_vale_na_proxima_abertura_e_guarda_o_banco_atual_antes()
    {
        var banco = new BancoApp(Db);
        var config = new ConfigRepositorio(banco);
        config.Gravar("x", "antes");
        string copia = banco.FazerBackup("manual");
        config.Gravar("x", "depois");

        banco.PedirRestauracao(copia);
        Assert.True(banco.RestauracaoPendente);
        Assert.Equal("depois", config.Ler<string>("x")); // nada muda com o app aberto

        var reaberto = new BancoApp(Db);
        Assert.False(reaberto.RestauracaoPendente);
        Assert.Equal("antes", new ConfigRepositorio(reaberto).Ler<string>("x"));
        Assert.Contains(reaberto.Backups(), b => b.Name.Contains("antes de restaurar"));

        string texto = Path.Combine(_pasta, "nao-e-banco.db");
        File.WriteAllText(texto, "");
        Assert.Throws<InvalidOperationException>(() => reaberto.PedirRestauracao(texto));
        Assert.False(reaberto.RestauracaoPendente);
    }

    [Fact]
    public void Guarda_so_as_copias_mais_recentes()
    {
        var banco = new BancoApp(Db, copiasGuardadas: 2);
        var feitas = Enumerable.Range(0, 4).Select(i => banco.FazerBackup($"manual {i}")).ToList();

        Assert.Equal(2, banco.Backups().Count);
        Assert.True(File.Exists(feitas[^1])); // a mais nova fica
    }
}
