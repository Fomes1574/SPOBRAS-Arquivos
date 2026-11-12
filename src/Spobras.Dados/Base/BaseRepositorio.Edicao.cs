using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Spobras.Core.Base;

namespace Spobras.Dados.Base;

/// <summary>
/// Edicao no app por cima da base (especificacao 4.6, "Dados editaveis e dados importados"). A planilha
/// nunca e alterada: cada mudanca fica guardada aqui, com o valor da planilha, e e reaplicada a cada
/// nova leitura. Se a planilha mudar um campo editado, o app avisa em vez de sobrescrever em silencio.
/// </summary>
public sealed partial class BaseRepositorio
{
    public const string OrigemEditado = "editado no app";
    public const string OrigemIncluido = "incluído no app";

    // ---- Obras ----

    /// <summary>Campos desta obra mudados no app.</summary>
    public IReadOnlyList<AjusteObra> Ajustes(string contrato, string empreendimento)
    {
        using var conexao = banco.Abrir();
        using var cmd = conexao.CreateCommand();
        cmd.CommandText = "select campo, valor, valor_planilha, planilha_mudou, quando from AjusteObra where contrato = $c and empreendimento = $e";
        cmd.Parameters.AddWithValue("$c", contrato);
        cmd.Parameters.AddWithValue("$e", empreendimento);
        using var r = cmd.ExecuteReader();
        var lista = new List<AjusteObra>();
        while (r.Read())
        {
            lista.Add(new AjusteObra(contrato, empreendimento, r.GetString(0), Txt(r, 1), Txt(r, 2), r.GetInt64(3) != 0, Instante(r, 4)!.Value));
        }

        return lista;
    }

    /// <summary>
    /// Grava os campos mudados (valores ja convertidos por <see cref="CamposObra.Converter"/>). Campo que volta
    /// ao valor da planilha deixa de ser uma edicao. Obra incluida no app e alterada direto.
    /// </summary>
    public void GravarEdicao(ObraLida obra, IReadOnlyDictionary<string, object?> novos, DateTimeOffset quando)
    {
        using var conexao = banco.Abrir();
        using var tx = conexao.BeginTransaction();
        if (obra.IncluidaNoApp)
        {
            var atual = LerObraPorId(conexao, tx, obra.Id) ?? throw new InvalidOperationException("A obra incluída no app não foi encontrada.");
            foreach (var (campo, valor) in novos)
            {
                Atualizar(conexao, tx, obra.Id, CamposObra.Por(campo)!.Coluna, valor);
            }

            var gravada = LerObraPorId(conexao, tx, obra.Id)!;
            Comando(conexao, tx, "update ObraLocal set json = $j, quando = $q where id = $id",
                ("$j", JsonSerializer.Serialize(gravada)), ("$q", Iso(quando)), ("$id", -obra.Id));
            AtualizarBusca(conexao, tx, obra.Id);
            tx.Commit();
            return;
        }

        foreach (var (campo, valor) in novos)
        {
            if (CamposObra.Chave(campo))
            {
                continue; // contrato e escola identificam a obra da planilha
            }

            string coluna = CamposObra.Por(campo)!.Coluna;
            string? novo = Guardado(valor);
            string? atual = ValorGuardado(conexao, tx, obra.Id, coluna);
            var existente = AjusteDe(conexao, tx, obra.Contrato, obra.Empreendimento, campo);
            string? daPlanilha = existente is null ? atual : existente.Value.Planilha;
            if (novo == daPlanilha)
            {
                Comando(conexao, tx, "delete from AjusteObra where contrato = $c and empreendimento = $e and campo = $k",
                    ("$c", obra.Contrato), ("$e", obra.Empreendimento), ("$k", campo));
            }
            else if (novo != atual || existente is null)
            {
                Comando(conexao, tx, """
                    insert into AjusteObra (contrato, empreendimento, campo, valor, valor_planilha, planilha_mudou, quando) values ($c, $e, $k, $v, $p, 0, $q)
                    on conflict(contrato, empreendimento, campo) do update set valor = excluded.valor, planilha_mudou = 0, quando = excluded.quando
                    """, ("$c", obra.Contrato), ("$e", obra.Empreendimento), ("$k", campo), ("$v", novo), ("$p", daPlanilha), ("$q", Iso(quando)));
            }

            Atualizar(conexao, tx, obra.Id, coluna, valor);
        }

        ContarEditados(conexao, tx, obra);
        AtualizarBusca(conexao, tx, obra.Id);
        tx.Commit();
    }

    /// <summary>Desfaz a mudanca de um campo: volta ao valor que a planilha tem.</summary>
    public void VoltarAoDaPlanilha(ObraLida obra, string campo)
    {
        using var conexao = banco.Abrir();
        using var tx = conexao.BeginTransaction();
        if (AjusteDe(conexao, tx, obra.Contrato, obra.Empreendimento, campo) is { } ajuste)
        {
            Atualizar(conexao, tx, obra.Id, CamposObra.Por(campo)!.Coluna, ParaBanco(CamposObra.Por(campo)!, ajuste.Planilha));
            Comando(conexao, tx, "delete from AjusteObra where contrato = $c and empreendimento = $e and campo = $k",
                ("$c", obra.Contrato), ("$e", obra.Empreendimento), ("$k", campo));
            ContarEditados(conexao, tx, obra);
            AtualizarBusca(conexao, tx, obra.Id);
        }

        tx.Commit();
    }

    /// <summary>Inclui uma obra que nao esta na planilha. Devolve a obra gravada (com Id negativo).</summary>
    public ObraLida IncluirObra(ObraLida obra, DateTimeOffset quando)
    {
        using var conexao = banco.Abrir();
        using var tx = conexao.BeginTransaction();
        if (Existe(conexao, tx, obra.Contrato, obra.Empreendimento))
        {
            throw new InvalidOperationException($"Já existe a obra {obra.Empreendimento} no contrato {obra.Contrato}. Edite a que existe.");
        }

        long id;
        using (var cmd = conexao.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "insert into ObraLocal (json, quando) values ($j, $q); select last_insert_rowid();";
            cmd.Parameters.AddWithValue("$j", JsonSerializer.Serialize(obra with { IncluidaNoApp = true }));
            cmd.Parameters.AddWithValue("$q", Iso(quando));
            id = (long)cmd.ExecuteScalar()!;
        }

        var gravada = obra with { Id = -id, IncluidaNoApp = true };
        InserirObraLocal(conexao, tx, gravada);
        tx.Commit();
        return gravada;
    }

    /// <summary>Tira do app uma obra incluida nele. Obras da planilha nao podem ser removidas por aqui.</summary>
    public void RemoverObraIncluida(ObraLida obra)
    {
        if (!obra.IncluidaNoApp)
        {
            throw new InvalidOperationException("Só obras incluídas no app podem ser tiradas. As da planilha mestra vêm dela.");
        }

        using var conexao = banco.Abrir();
        using var tx = conexao.BeginTransaction();
        Comando(conexao, tx, "delete from ObraLocal where id = $id", ("$id", -obra.Id));
        Comando(conexao, tx, "delete from Obra where id = $id", ("$id", obra.Id));
        tx.Commit();
    }

    private void AplicarEdicoesDeObras(SqliteConnection conexao, SqliteTransaction tx, bool releitura)
    {
        // Obras incluidas no app voltam depois de cada leitura da planilha.
        using (var cmd = conexao.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "select id, json from ObraLocal";
            using var r = cmd.ExecuteReader();
            var locais = new List<ObraLida>();
            while (r.Read())
            {
                if (JsonSerializer.Deserialize<ObraLida>(r.GetString(1)) is { } o)
                {
                    locais.Add(o with { Id = -r.GetInt64(0), IncluidaNoApp = true });
                }
            }

            r.Close();
            foreach (var o in locais.Where(o => !Existe(conexao, tx, o.Contrato, o.Empreendimento)))
            {
                InserirObraLocal(conexao, tx, o);
            }
        }

        var ajustes = new List<(string Contrato, string Emp, string Campo, string? Valor, string? Planilha)>();
        using (var cmd = conexao.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "select contrato, empreendimento, campo, valor, valor_planilha from AjusteObra";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                ajustes.Add((r.GetString(0), r.GetString(1), r.GetString(2), Txt(r, 3), Txt(r, 4)));
            }
        }

        var tocadas = new HashSet<long>();
        foreach (var a in ajustes)
        {
            var campo = CamposObra.Por(a.Campo);
            long? id = IdDaObra(conexao, tx, a.Contrato, a.Emp);
            if (campo is null || id is null)
            {
                continue; // a obra saiu da planilha: a edicao fica guardada para quando ela voltar
            }

            string? lidoAgora = ValorGuardado(conexao, tx, id.Value, campo.Coluna);
            if (releitura && lidoAgora != a.Planilha)
            {
                // A planilha mudou este campo depois da edicao: avisa e guarda o valor novo dela.
                Comando(conexao, tx, "update AjusteObra set planilha_mudou = 1, valor_planilha = $p where contrato = $c and empreendimento = $e and campo = $k",
                    ("$p", lidoAgora), ("$c", a.Contrato), ("$e", a.Emp), ("$k", a.Campo));
            }

            Atualizar(conexao, tx, id.Value, campo.Coluna, ParaBanco(campo, a.Valor));
            tocadas.Add(id.Value);
        }

        foreach (long id in tocadas)
        {
            Comando(conexao, tx, """
                update Obra set editados = (select count(*) from AjusteObra a where a.contrato = Obra.contrato and a.empreendimento = Obra.empreendimento)
                where id = $id
                """, ("$id", id));
            AtualizarBusca(conexao, tx, id);
        }
    }

    private void InserirObraLocal(SqliteConnection conexao, SqliteTransaction tx, ObraLida o)
    {
        Comando(conexao, tx, """
            insert into Obra (id, contrato, empreendimento, contratada, portaria, contrato_receita, dotacao, atividade, lote, modalidade, tipo, tipo_obra,
                processo_sei, status, status_contrato, situacao, escopo, data_assinatura, data_os, termino, valor_empreendimento, valor_aditado,
                valor_contratado_aditamento, valor_medido, valor_pago, saldo_medir, saldo_pagar, busca, local)
            values ($id, $c, $e, $ct, $p, $r, $d, $a, $l, $m, $t, $to, $sei, $s, $sc, $si, $es, $da, $os, $te, $v1, $v2, $v3, $v4, $v5, $v6, $v7, '', 1)
            """,
            ("$id", o.Id), ("$c", o.Contrato), ("$e", o.Empreendimento), ("$ct", o.Contratada), ("$p", o.Portaria), ("$r", o.ContratoReceita),
            ("$d", o.Dotacao), ("$a", o.Atividade), ("$l", o.Lote), ("$m", o.Modalidade), ("$t", o.Tipo), ("$to", o.TipoDeObra), ("$sei", o.ProcessoSei),
            ("$s", o.Status), ("$sc", o.StatusContrato), ("$si", o.Situacao), ("$es", o.Escopo), ("$da", Iso(o.DataAssinatura)), ("$os", Iso(o.DataOs)),
            ("$te", Iso(o.Termino)), ("$v1", Normalizacao.Centavos(o.ValorEmpreendimento)), ("$v2", Normalizacao.Centavos(o.ValorAditado)),
            ("$v3", Normalizacao.Centavos(o.ValorContratadoComAditamento)), ("$v4", Normalizacao.Centavos(o.ValorMedido)),
            ("$v5", Normalizacao.Centavos(o.ValorPago)), ("$v6", Normalizacao.Centavos(o.SaldoAMedir)), ("$v7", Normalizacao.Centavos(o.SaldoAPagar)));
        AtualizarBusca(conexao, tx, o.Id);
    }

    private static void ContarEditados(SqliteConnection conexao, SqliteTransaction tx, ObraLida obra) =>
        Comando(conexao, tx, "update Obra set editados = (select count(*) from AjusteObra where contrato = $c and empreendimento = $e) where id = $id",
            ("$c", obra.Contrato), ("$e", obra.Empreendimento), ("$id", obra.Id));

    private static (string? Valor, string? Planilha)? AjusteDe(SqliteConnection conexao, SqliteTransaction tx, string contrato, string emp, string campo)
    {
        using var cmd = conexao.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "select valor, valor_planilha from AjusteObra where contrato = $c and empreendimento = $e and campo = $k";
        cmd.Parameters.AddWithValue("$c", contrato);
        cmd.Parameters.AddWithValue("$e", emp);
        cmd.Parameters.AddWithValue("$k", campo);
        using var r = cmd.ExecuteReader();
        return r.Read() ? (Txt(r, 0), Txt(r, 1)) : null;
    }

    private static long? IdDaObra(SqliteConnection conexao, SqliteTransaction tx, string contrato, string emp)
    {
        using var cmd = conexao.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "select id from Obra where contrato = $c and empreendimento = $e and local = 0 limit 1";
        cmd.Parameters.AddWithValue("$c", contrato);
        cmd.Parameters.AddWithValue("$e", emp);
        return cmd.ExecuteScalar() is long id ? id : null;
    }

    private static bool Existe(SqliteConnection conexao, SqliteTransaction tx, string contrato, string emp)
    {
        using var cmd = conexao.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "select count(*) from Obra where contrato = $c collate nocase and empreendimento = $e collate nocase";
        cmd.Parameters.AddWithValue("$c", contrato.Trim());
        cmd.Parameters.AddWithValue("$e", emp.Trim());
        return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
    }

    private static ObraLida? LerObraPorId(SqliteConnection conexao, SqliteTransaction tx, long id)
    {
        using var cmd = conexao.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "select * from Obra where id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        using var r = cmd.ExecuteReader();
        return r.Read() ? LerObra(r) : null;
    }

    private static string? ValorGuardado(SqliteConnection conexao, SqliteTransaction tx, long id, string coluna)
    {
        using var cmd = conexao.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"select {coluna} from Obra where id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        return Guardado(cmd.ExecuteScalar());
    }

    private static string? Guardado(object? valor) => valor switch
    {
        null or DBNull => null,
        long l => l.ToString(CultureInfo.InvariantCulture),
        int i => i.ToString(CultureInfo.InvariantCulture),
        _ => Convert.ToString(valor, CultureInfo.InvariantCulture),
    };

    private static object? ParaBanco(CampoObra campo, string? guardado) =>
        guardado is null ? null : campo.Tipo == TipoCampoObra.Reais && long.TryParse(guardado, CultureInfo.InvariantCulture, out long c) ? c : guardado;

    private static void Atualizar(SqliteConnection conexao, SqliteTransaction tx, long id, string coluna, object? valor) =>
        Comando(conexao, tx, $"update Obra set {coluna} = $v where id = $id", ("$v", valor), ("$id", id));

    private static void AtualizarBusca(SqliteConnection conexao, SqliteTransaction tx, long id)
    {
        if (LerObraPorId(conexao, tx, id) is { } o)
        {
            Comando(conexao, tx, "update Obra set busca = $b where id = $id",
                ("$b", Normalizacao.Busca(string.Join(' ', o.Contrato, o.Empreendimento, o.Contratada, o.Lote, o.ProcessoSei, o.Escopo, o.Modalidade, o.Portaria))),
                ("$id", id));
        }
    }

    // ---- Portarias e dotacoes ----

    public void IncluirVinculo(VinculoBase novo, DateTimeOffset quando) => RegistrarVinculo("incluir", null, novo, quando);

    public void AlterarVinculo(VinculoBase antigo, VinculoBase novo, DateTimeOffset quando) => RegistrarVinculo("alterar", antigo.Chave, novo, quando);

    public void RemoverVinculo(VinculoBase antigo, DateTimeOffset quando) => RegistrarVinculo("remover", antigo.Chave, null, quando);

    /// <summary>Quantas mudancas em portarias e dotacoes foram feitas no app.</summary>
    public int EdicoesDeVinculos()
    {
        using var conexao = banco.Abrir();
        using var cmd = conexao.CreateCommand();
        cmd.CommandText = "select count(*) from VinculoLocal";
        return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private void RegistrarVinculo(string acao, string? chave, VinculoBase? novo, DateTimeOffset quando)
    {
        if (novo is not null && (string.IsNullOrWhiteSpace(novo.Portaria) || string.IsNullOrWhiteSpace(novo.DotacaoCompleta)))
        {
            throw new InvalidOperationException("Portaria e dotação completa são obrigatórias.");
        }

        var limpo = novo is null ? null : novo with
        {
            Portaria = novo.Portaria.Trim(),
            ContratoReceita = novo.ContratoReceita.Trim(),
            DotacaoCompleta = Normalizacao.Dotacao(novo.DotacaoCompleta).Completa,
        };
        using var conexao = banco.Abrir();
        using var tx = conexao.BeginTransaction();
        Comando(conexao, tx, "insert into VinculoLocal (acao, chave, json, quando) values ($a, $c, $j, $q)",
            ("$a", acao), ("$c", chave), ("$j", limpo is null ? null : JsonSerializer.Serialize(limpo)), ("$q", Iso(quando)));
        Aplicar(conexao, tx, acao, chave, limpo);
        tx.Commit();
    }

    private void AplicarEdicoesDeVinculos(SqliteConnection conexao, SqliteTransaction tx)
    {
        var acoes = new List<(string Acao, string? Chave, VinculoBase? Novo)>();
        using (var cmd = conexao.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "select acao, chave, json from VinculoLocal order by id";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                acoes.Add((r.GetString(0), Txt(r, 1), r.IsDBNull(2) ? null : JsonSerializer.Deserialize<VinculoBase>(r.GetString(2))));
            }
        }

        foreach (var (acao, chave, novo) in acoes)
        {
            Aplicar(conexao, tx, acao, chave, novo);
        }
    }

    private static void Aplicar(SqliteConnection conexao, SqliteTransaction tx, string acao, string? chave, VinculoBase? novo)
    {
        long? id = chave is null ? null : IdDoVinculo(conexao, tx, chave);
        switch (acao)
        {
            case "remover" when id is not null:
                Comando(conexao, tx, "delete from Vinculo where id = $id", ("$id", id));
                break;
            case "alterar" when id is not null && novo is not null:
                Comando(conexao, tx, """
                    update Vinculo set portaria_id = $p, contrato_receita = $r, finalidade = $f, dotacao = $d, resumida = $res, descricao = $desc,
                        origem = case when origem = $inc then origem else $ed end
                    where id = $id
                    """, ("$p", IdDaPortaria(conexao, tx, novo.Portaria)), ("$r", novo.ContratoReceita), ("$f", novo.Finalidade), ("$d", novo.DotacaoCompleta),
                    ("$res", novo.DotacaoResumida), ("$desc", novo.Descricao), ("$inc", OrigemIncluido), ("$ed", OrigemEditado), ("$id", id));
                break;
            case "incluir" when novo is not null && IdDoVinculo(conexao, tx, novo.Chave) is null:
                Comando(conexao, tx, """
                    insert into Vinculo (portaria_id, contrato_receita, finalidade, dotacao, resumida, descricao, origem)
                    values ($p, $r, $f, $d, $res, $desc, $o)
                    """, ("$p", IdDaPortaria(conexao, tx, novo.Portaria)), ("$r", novo.ContratoReceita), ("$f", novo.Finalidade), ("$d", novo.DotacaoCompleta),
                    ("$res", novo.DotacaoResumida), ("$desc", novo.Descricao), ("$o", OrigemIncluido));
                break;
        }
    }

    private static long? IdDoVinculo(SqliteConnection conexao, SqliteTransaction tx, string chave)
    {
        using var cmd = conexao.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "select v.id from Vinculo v join Portaria p on p.id = v.portaria_id where p.rotulo || '|' || v.contrato_receita || '|' || v.dotacao = $k limit 1";
        cmd.Parameters.AddWithValue("$k", chave);
        return cmd.ExecuteScalar() is long id ? id : null;
    }

    private static long IdDaPortaria(SqliteConnection conexao, SqliteTransaction tx, string rotulo)
    {
        using (var cmd = conexao.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "select id from Portaria where rotulo = $r";
            cmd.Parameters.AddWithValue("$r", rotulo);
            if (cmd.ExecuteScalar() is long id)
            {
                return id;
            }
        }

        var lida = Normalizacao.Portaria(rotulo);
        using var nova = conexao.CreateCommand();
        nova.Transaction = tx;
        nova.CommandText = "insert into Portaria (rotulo, numero, ano) values ($r, $n, $a); select last_insert_rowid();";
        nova.Parameters.AddWithValue("$r", rotulo);
        nova.Parameters.AddWithValue("$n", (object?)lida.Numero ?? DBNull.Value);
        nova.Parameters.AddWithValue("$a", (object?)lida.Ano ?? DBNull.Value);
        return (long)nova.ExecuteScalar()!;
    }

    private static void Comando(SqliteConnection conexao, SqliteTransaction tx, string sql, params (string Nome, object? Valor)[] parametros)
    {
        using var cmd = conexao.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (nome, valor) in parametros)
        {
            cmd.Parameters.AddWithValue(nome, valor ?? DBNull.Value);
        }

        cmd.ExecuteNonQuery();
    }
}
