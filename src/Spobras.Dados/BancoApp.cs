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
        // 3: rascunhos (trabalho em preparacao, salvo sozinho)
        """
        create table Rascunho (
            chave text primary key,
            json text not null,
            atualizado_em text not null
        );
        """,
        // 4: base local (copia da ultima importacao valida de cada fonte)
        """
        create table Fonte (
            id text primary key,
            nome text not null,
            caminho text not null,
            estado integer not null,
            mensagem text not null,
            ultima_leitura text null,
            modificado_arquivo text null,
            tamanho_arquivo integer not null,
            linhas integer not null,
            versao integer not null,
            avisos_json text not null
        );
        create table ImportacaoLog (
            id integer primary key autoincrement,
            fonte_id text not null,
            quando text not null,
            ok integer not null,
            mensagem text not null,
            linhas integer not null,
            duracao_ms integer not null
        );
        create table Obra (
            id integer primary key,
            contrato text not null,
            empreendimento text not null,
            contratada text null, portaria text null, contrato_receita text null,
            dotacao text null, dotacao_original text null, atividade text null,
            lote text null, modalidade text null, tipo text null, tipo_obra text null,
            processo_sei text null, status text null, status_contrato text null, situacao text null, escopo text null,
            data_assinatura text null, data_os text null, termino text null,
            valor_empreendimento integer null, valor_aditado integer null, valor_contratado_aditamento integer null,
            valor_medido integer null, valor_pago integer null, saldo_medir integer null, saldo_pagar integer null,
            busca text not null
        );
        create index Obra_contrato on Obra(contrato);
        create index Obra_portaria on Obra(portaria);
        create index Obra_empreendimento on Obra(empreendimento);
        create index Obra_data_os on Obra(data_os);
        create table Medicao (
            id integer primary key,
            contrato text not null, numero text null, empreendimento text null,
            inicio text null, fim text null,
            valor_protocolado integer null, valor_liberado integer null, valor_considerado integer null, valor_pago integer null,
            status text null, contrato_receita text null, portaria text null, pagamento text null
        );
        create index Medicao_contrato on Medicao(contrato);
        create table Empenho (
            id integer primary key,
            contrato text null, numero text null, data text null, ano integer null, processo_sei text null,
            total_empenhado integer null, liquido integer null, liquidado integer null, anulado integer null,
            pago_exercicio integer null, pago_restos integer null, credor text null, portaria text null, saldo_final integer null
        );
        create index Empenho_contrato on Empenho(contrato);
        create table Portaria (
            id integer primary key,
            rotulo text not null unique,
            numero integer null,
            ano integer null
        );
        create table Vinculo (
            id integer primary key,
            portaria_id integer not null references Portaria(id),
            contrato_receita text not null,
            finalidade text null,
            dotacao text not null,
            dotacao_original text null,
            resumida text null,
            descricao text null
        );
        -- Mantido pelo usuario no app (nao vem de planilha): liga o texto de portaria de outra fonte a portaria da base.
        create table PortariaApelido (
            texto text primary key,
            portaria_rotulo text not null
        );
        """,

        // 5: indice local da busca dentro dos arquivos (refeito por arquivo quando tamanho ou data mudam)
        """
        create table IndiceArquivo (
            caminho text primary key,
            tamanho integer not null,
            modificado integer not null,
            texto text null,
            motivo text null
        );
        """,

        // 6: edicao no app por cima da base (a planilha nunca e alterada; cada mudanca guarda o valor da planilha)
        """
        alter table Obra add column local integer not null default 0;
        alter table Obra add column editados integer not null default 0;
        alter table Vinculo add column origem text not null default 'planilha';
        create table AjusteObra (
            contrato text not null,
            empreendimento text not null,
            campo text not null,
            valor text null,
            valor_planilha text null,
            planilha_mudou integer not null default 0,
            quando text not null,
            primary key (contrato, empreendimento, campo)
        );
        create table ObraLocal (
            id integer primary key,
            json text not null,
            quando text not null
        );
        create table VinculoLocal (
            id integer primary key,
            acao text not null,
            chave text null,
            json text null,
            quando text not null
        );
        """,

        // 7: medicoes guardam o reajuste e o status do contrato (para as medicoes em tramite da ficha)
        """
        alter table Medicao add column valor_reajuste integer null;
        alter table Medicao add column status_contrato text null;
        create index Medicao_contrato_emp on Medicao(contrato, empreendimento);
        """,
    ];

    private const string SufixoRestaurar = ".restaurar";
    private readonly string _conexao;

    public BancoApp(string caminho, int copiasGuardadas = 10)
    {
        string? pasta = Path.GetDirectoryName(caminho);
        if (!string.IsNullOrEmpty(pasta))
        {
            Directory.CreateDirectory(pasta);
        }

        Caminho = caminho;
        CopiasGuardadas = copiasGuardadas;
        AplicarRestauracaoPendente();
        _conexao = new SqliteConnectionStringBuilder { DataSource = caminho, Pooling = false }.ToString();
        Migrar();
    }

    public string Caminho { get; }
    public int CopiasGuardadas { get; }

    /// <summary>Copias de seguranca do banco: ao lado dele, na pasta "backups".</summary>
    public string PastaBackups => Path.Combine(Path.GetDirectoryName(Caminho) ?? ".", "backups");

    /// <summary>Restauracao pedida e ainda nao aplicada (vale quando o app abrir de novo).</summary>
    public bool RestauracaoPendente => File.Exists(Caminho + SufixoRestaurar);

    public IReadOnlyList<FileInfo> Backups() =>
        Directory.Exists(PastaBackups)
            ? [.. new DirectoryInfo(PastaBackups).EnumerateFiles("*.db").OrderByDescending(f => f.Name, StringComparer.Ordinal)]
            : [];

    /// <summary>
    /// Copia consistente do banco (API de backup do SQLite, que considera o WAL), guardando so as mais
    /// recentes. Devolve o caminho da copia.
    /// </summary>
    public string FazerBackup(string motivo)
    {
        Directory.CreateDirectory(PastaBackups);
        string nome = $"arquivos-spobras {DateTime.Now:yyyy-MM-dd HHmmss} {motivo}.db";
        string destino = Path.Combine(PastaBackups, string.Concat(nome.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c)));
        for (int n = 2; File.Exists(destino); n++)
        {
            destino = Path.Combine(PastaBackups, Path.GetFileNameWithoutExtension(nome) + $" ({n}).db");
        }

        using (var origem = new SqliteConnection(_conexao))
        using (var copia = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = destino, Pooling = false }.ToString()))
        {
            origem.Open();
            copia.Open();
            origem.BackupDatabase(copia);
        }

        foreach (var antigo in Backups().Skip(CopiasGuardadas))
        {
            antigo.Delete(); // so copias internas do app, nunca arquivos do usuario
        }

        return destino;
    }

    /// <summary>
    /// Pede para voltar a uma copia de seguranca. A troca acontece na proxima abertura do app (com o banco
    /// fechado); o banco atual vira mais uma copia antes da troca, para poder voltar atras.
    /// </summary>
    public void PedirRestauracao(string copia)
    {
        using (var teste = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = copia, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
        {
            teste.Open();
            using var cmd = teste.CreateCommand();
            cmd.CommandText = "pragma user_version";
            int versao = Convert.ToInt32(cmd.ExecuteScalar());
            if (versao == 0 || versao > Migracoes.Length)
            {
                throw new InvalidOperationException(versao == 0
                    ? "Este arquivo não é uma cópia de segurança do app."
                    : "Esta cópia foi feita por uma versão mais nova do app.");
            }
        }

        File.Copy(copia, Caminho + SufixoRestaurar, overwrite: true);
    }

    public void CancelarRestauracao() => File.Delete(Caminho + SufixoRestaurar);

    private void AplicarRestauracaoPendente()
    {
        string pendente = Caminho + SufixoRestaurar;
        if (!File.Exists(pendente))
        {
            return;
        }

        if (File.Exists(Caminho))
        {
            Directory.CreateDirectory(PastaBackups);
            string guarda = Path.Combine(PastaBackups, $"arquivos-spobras {DateTime.Now:yyyy-MM-dd HHmmss} antes de restaurar.db");
            // Banco fechado: junta o WAL antes de guardar, abrindo e fechando uma vez.
            using (var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Caminho, Pooling = false }.ToString()))
            {
                c.Open();
                using var cmd = c.CreateCommand();
                cmd.CommandText = "pragma wal_checkpoint(truncate);";
                cmd.ExecuteNonQuery();
            }

            File.Copy(Caminho, guarda, overwrite: false);
        }

        foreach (string auxiliar in new[] { Caminho + "-wal", Caminho + "-shm" })
        {
            File.Delete(auxiliar);
        }

        File.Copy(pendente, Caminho, overwrite: true);
        File.Delete(pendente);
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

        if (versao > 0 && versao < Migracoes.Length)
        {
            // Atualizacao do app: guarda o banco como estava antes de mudar a estrutura.
            FazerBackup($"antes de atualizar (versão {versao})");
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
