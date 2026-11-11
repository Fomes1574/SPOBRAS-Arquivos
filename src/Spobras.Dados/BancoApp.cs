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
