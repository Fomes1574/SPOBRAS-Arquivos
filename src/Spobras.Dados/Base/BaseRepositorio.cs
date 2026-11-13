using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Spobras.Core.Base;

namespace Spobras.Dados.Base;

/// <summary>
/// Base local: copia da ultima importacao valida de cada fonte. Cada importacao entra inteira numa
/// transacao ou nao entra (uma falha nunca deixa metade dos contratos atualizados).
/// </summary>
public sealed partial class BaseRepositorio(BancoApp banco)
{
    // ---- Gravacao ----

    public void GravarMestra(MestraImportada dados, FonteInfo fonte)
    {
        using var conexao = banco.Abrir();
        using var tx = conexao.BeginTransaction();
        Executar(conexao, tx, "delete from Obra; delete from Medicao; delete from Empenho;");

        using (var cmd = conexao.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                insert into Obra (id, contrato, empreendimento, contratada, portaria, contrato_receita, dotacao, dotacao_original, atividade, lote,
                    modalidade, tipo, tipo_obra, processo_sei, status, status_contrato, situacao, escopo, data_assinatura, data_os, termino,
                    valor_empreendimento, valor_aditado, valor_contratado_aditamento, valor_medido, valor_pago, saldo_medir, saldo_pagar, busca)
                values ($id, $contrato, $emp, $contratada, $portaria, $receita, $dotacao, $dotorig, $atividade, $lote, $modalidade, $tipo, $tipoobra,
                    $sei, $status, $statusct, $situacao, $escopo, $assinatura, $os, $termino, $vemp, $vadit, $vtotal, $vmedido, $vpago, $smedir, $spagar, $busca)
                """;
            var p = Parametros(cmd, "$id", "$contrato", "$emp", "$contratada", "$portaria", "$receita", "$dotacao", "$dotorig", "$atividade", "$lote",
                "$modalidade", "$tipo", "$tipoobra", "$sei", "$status", "$statusct", "$situacao", "$escopo", "$assinatura", "$os", "$termino",
                "$vemp", "$vadit", "$vtotal", "$vmedido", "$vpago", "$smedir", "$spagar", "$busca");
            foreach (var o in dados.Obras)
            {
                Valores(p, o.Id, o.Contrato, o.Empreendimento, o.Contratada, o.Portaria, o.ContratoReceita, o.Dotacao,
                    dados.DotacoesOriginais.GetValueOrDefault(o.Id), o.Atividade, o.Lote, o.Modalidade, o.Tipo, o.TipoDeObra, o.ProcessoSei, o.Status,
                    o.StatusContrato, o.Situacao, o.Escopo, Iso(o.DataAssinatura), Iso(o.DataOs), Iso(o.Termino),
                    Normalizacao.Centavos(o.ValorEmpreendimento), Normalizacao.Centavos(o.ValorAditado), Normalizacao.Centavos(o.ValorContratadoComAditamento),
                    Normalizacao.Centavos(o.ValorMedido), Normalizacao.Centavos(o.ValorPago), Normalizacao.Centavos(o.SaldoAMedir), Normalizacao.Centavos(o.SaldoAPagar),
                    Normalizacao.Busca(string.Join(' ', o.Contrato, o.Empreendimento, o.Contratada, o.Lote, o.ProcessoSei, o.Escopo, o.Modalidade, o.Portaria)));
                cmd.ExecuteNonQuery();
            }
        }

        using (var cmd = conexao.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                insert into Medicao (contrato, numero, empreendimento, inicio, fim, valor_protocolado, valor_liberado, valor_considerado, valor_pago,
                    status, contrato_receita, pagamento, valor_reajuste, status_contrato)
                values ($contrato, $numero, $emp, $inicio, $fim, $prot, $lib, $cons, $pago, $status, $receita, $pagamento, $reaj, $stc)
                """;
            var p = Parametros(cmd, "$contrato", "$numero", "$emp", "$inicio", "$fim", "$prot", "$lib", "$cons", "$pago", "$status", "$receita", "$pagamento", "$reaj", "$stc");
            foreach (var m in dados.Medicoes)
            {
                Valores(p, m.Contrato, m.Numero, m.Empreendimento, Iso(m.Inicio), Iso(m.Fim), Normalizacao.Centavos(m.ValorProtocolado),
                    Normalizacao.Centavos(m.ValorLiberado), Normalizacao.Centavos(m.ValorConsiderado), Normalizacao.Centavos(m.ValorPago), m.Status,
                    m.ContratoReceita, Iso(m.Pagamento), Normalizacao.Centavos(m.ValorReajuste), m.StatusContrato);
                cmd.ExecuteNonQuery();
            }
        }

        using (var cmd = conexao.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                insert into Empenho (contrato, numero, data, ano, processo_sei, total_empenhado, liquido, liquidado, anulado, pago_exercicio,
                    pago_restos, credor, portaria, saldo_final)
                values ($contrato, $numero, $data, $ano, $sei, $total, $liquido, $liquidado, $anulado, $pagoex, $pagorestos, $credor, $portaria, $saldo)
                """;
            var p = Parametros(cmd, "$contrato", "$numero", "$data", "$ano", "$sei", "$total", "$liquido", "$liquidado", "$anulado", "$pagoex",
                "$pagorestos", "$credor", "$portaria", "$saldo");
            foreach (var e in dados.Empenhos)
            {
                Valores(p, e.Contrato, e.Numero, Iso(e.Data), e.Ano, e.ProcessoSei, Normalizacao.Centavos(e.TotalEmpenhado), Normalizacao.Centavos(e.Liquido),
                    Normalizacao.Centavos(e.Liquidado), Normalizacao.Centavos(e.Anulado), Normalizacao.Centavos(e.PagoNoExercicio),
                    Normalizacao.Centavos(e.PagoRestos), e.Credor, e.Portaria, Normalizacao.Centavos(e.SaldoFinal));
                cmd.ExecuteNonQuery();
            }
        }

        GravarFonte(conexao, tx, fonte);
        AplicarEdicoesDeObras(conexao, tx, releitura: true);
        tx.Commit();
    }

    public void GravarDotacoes(DotacoesImportadas dados, FonteInfo fonte)
    {
        using var conexao = banco.Abrir();
        using var tx = conexao.BeginTransaction();
        Executar(conexao, tx, "delete from Vinculo; delete from Portaria;");
        var ids = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in dados.Vinculos)
        {
            if (!ids.TryGetValue(v.Portaria, out long portariaId))
            {
                using var nova = conexao.CreateCommand();
                nova.Transaction = tx;
                nova.CommandText = "insert into Portaria (rotulo, numero, ano) values ($rotulo, $numero, $ano); select last_insert_rowid();";
                Valores(Parametros(nova, "$rotulo", "$numero", "$ano"), v.Portaria, v.Numero, v.Ano);
                portariaId = (long)nova.ExecuteScalar()!;
                ids[v.Portaria] = portariaId;
            }

            using var cmd = conexao.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                insert into Vinculo (portaria_id, contrato_receita, finalidade, dotacao, dotacao_original, resumida, descricao)
                values ($portaria, $receita, $finalidade, $dotacao, $original, $resumida, $descricao)
                """;
            Valores(Parametros(cmd, "$portaria", "$receita", "$finalidade", "$dotacao", "$original", "$resumida", "$descricao"),
                portariaId, v.ContratoReceita, v.Finalidade, v.Dotacao, v.DotacaoOriginal, v.Resumida, v.Descricao);
            cmd.ExecuteNonQuery();
        }

        GravarFonte(conexao, tx, fonte);
        AplicarEdicoesDeVinculos(conexao, tx);
        tx.Commit();
    }

    // ---- Fontes ----

    public IReadOnlyList<FonteInfo> Fontes()
    {
        using var conexao = banco.Abrir();
        using var cmd = conexao.CreateCommand();
        cmd.CommandText = "select id, nome, caminho, estado, mensagem, ultima_leitura, modificado_arquivo, tamanho_arquivo, linhas, versao, avisos_json from Fonte order by id desc";
        using var r = cmd.ExecuteReader();
        var fontes = new List<FonteInfo>();
        while (r.Read())
        {
            fontes.Add(new FonteInfo
            {
                Id = r.GetString(0),
                Nome = r.GetString(1),
                Caminho = r.GetString(2),
                Estado = (EstadoFonte)r.GetInt32(3),
                Mensagem = r.GetString(4),
                UltimaLeitura = Instante(r, 5),
                ModificadoArquivo = Instante(r, 6),
                TamanhoArquivo = r.GetInt64(7),
                Linhas = r.GetInt32(8),
                Versao = r.GetInt32(9),
                Avisos = JsonSerializer.Deserialize<List<string>>(r.GetString(10)) ?? [],
            });
        }

        return fontes;
    }

    public FonteInfo? Fonte(string id) => Fontes().FirstOrDefault(f => f.Id == id);

    /// <summary>Grava o cadastro ou o estado da fonte sem tocar nos dados importados (ex.: falha de leitura).</summary>
    public void GravarFonte(FonteInfo fonte)
    {
        using var conexao = banco.Abrir();
        GravarFonte(conexao, null, fonte);
    }

    public void RegistrarImportacao(string fonteId, DateTimeOffset quando, bool ok, string mensagem, int linhas, long duracaoMs)
    {
        using var conexao = banco.Abrir();
        using var cmd = conexao.CreateCommand();
        cmd.CommandText = "insert into ImportacaoLog (fonte_id, quando, ok, mensagem, linhas, duracao_ms) values ($f, $q, $ok, $m, $l, $d)";
        Valores(Parametros(cmd, "$f", "$q", "$ok", "$m", "$l", "$d"), fonteId, Iso(quando), ok ? 1 : 0, mensagem, linhas, duracaoMs);
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<(DateTimeOffset Quando, bool Ok, string Mensagem, int Linhas)> HistoricoDeImportacoes(string fonteId, int maximo = 20)
    {
        using var conexao = banco.Abrir();
        using var cmd = conexao.CreateCommand();
        cmd.CommandText = "select quando, ok, mensagem, linhas from ImportacaoLog where fonte_id = $f order by id desc limit $max";
        Valores(Parametros(cmd, "$f", "$max"), fonteId, maximo);
        using var r = cmd.ExecuteReader();
        var lista = new List<(DateTimeOffset, bool, string, int)>();
        while (r.Read())
        {
            lista.Add((Instante(r, 0)!.Value, r.GetInt32(1) == 1, r.GetString(2), r.GetInt32(3)));
        }

        return lista;
    }

    // ---- Consulta de obras ----

    public ResultadoObras Buscar(FiltroObras filtro, int limite = 500, int pular = 0, OrdemObras ordem = OrdemObras.Contrato, bool decrescente = false)
    {
        using var conexao = banco.Abrir();
        using var contar = conexao.CreateCommand();
        contar.CommandText = "select count(*) from Obra where " + Onde(filtro, contar, null);
        int total = Convert.ToInt32(contar.ExecuteScalar(), CultureInfo.InvariantCulture);

        using var cmd = conexao.CreateCommand();
        string sentido = decrescente ? "desc" : "asc";
        string ordenar = ordem switch
        {
            OrdemObras.Escola => $"empreendimento collate nocase {sentido}, contrato",
            OrdemObras.DataOs => $"data_os is null, data_os {sentido}, contrato, empreendimento",
            OrdemObras.SaldoAPagar => $"saldo_pagar is null, saldo_pagar {sentido}, contrato, empreendimento",
            _ => $"substr(contrato, -4) {sentido}, contrato {sentido}, empreendimento collate nocase",
        };
        cmd.CommandText = $"select * from Obra where {Onde(filtro, cmd, null)} order by {ordenar} limit $limite offset $pular";
        cmd.Parameters.AddWithValue("$limite", limite);
        cmd.Parameters.AddWithValue("$pular", pular);
        using var r = cmd.ExecuteReader();
        var obras = new List<ObraLida>();
        while (r.Read())
        {
            obras.Add(LerObra(r));
        }

        return new ResultadoObras(obras, total);
    }

    /// <summary>
    /// Para cada campo, as opcoes que combinam com os OUTROS filtros ativos. Assim abrir um campo para
    /// trocar o valor mostra as alternativas compativeis, e limpar um campo amplia as opcoes dos demais.
    /// </summary>
    public OpcoesFiltro Opcoes(FiltroObras filtro)
    {
        using var conexao = banco.Abrir();
        List<string> Distintos(string coluna, string campo)
        {
            using var cmd = conexao.CreateCommand();
            cmd.CommandText = $"select distinct {coluna} from Obra where {coluna} is not null and {Onde(filtro, cmd, campo)} order by {coluna} collate nocase";
            using var r = cmd.ExecuteReader();
            var lista = new List<string>();
            while (r.Read())
            {
                lista.Add(r.GetString(0));
            }

            return lista;
        }

        // O contrato de receita pode vir combinado ("015/SIURB/2025 | 128/SIURB/2021"): cada codigo e uma opcao.
        var receitas = Distintos("contrato_receita", "receita")
            .SelectMany(t => t.Split([" | ", ", "], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();

        return new OpcoesFiltro(
            Distintos("portaria", "portaria"), Distintos("contrato", "contrato"), Distintos("empreendimento", "escola"),
            receitas, Distintos("atividade", "dotacao"), Distintos("status", "status"))
        {
            Empresas = Distintos("contratada", "empresa"),
            Lotes = [.. Distintos("lote", "lote").OrderBy(l => System.Text.RegularExpressions.Regex.Match(l, @"\d+") is { Success: true } m ? int.Parse(m.Value, System.Globalization.CultureInfo.InvariantCulture) : int.MaxValue)
                .ThenBy(l => l, StringComparer.OrdinalIgnoreCase)],
        };
    }

    public ObraLida? Obra(long id)
    {
        using var conexao = banco.Abrir();
        using var cmd = conexao.CreateCommand();
        cmd.CommandText = "select * from Obra where id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        using var r = cmd.ExecuteReader();
        return r.Read() ? LerObra(r) : null;
    }

    public int TotalDeObras()
    {
        using var conexao = banco.Abrir();
        using var cmd = conexao.CreateCommand();
        cmd.CommandText = "select count(*) from Obra";
        return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    /// <summary>Medicoes do contrato, cada uma com a sua identidade (nada e somado aqui).</summary>
    public IReadOnlyList<MedicaoLida> Medicoes(string contrato)
    {
        using var conexao = banco.Abrir();
        using var cmd = conexao.CreateCommand();
        cmd.CommandText = """
            select contrato, numero, empreendimento, inicio, fim, valor_protocolado, valor_liberado, valor_considerado, valor_pago, status, contrato_receita, pagamento,
                valor_reajuste, status_contrato
            from Medicao where contrato = $c collate nocase order by numero, inicio
            """;
        cmd.Parameters.AddWithValue("$c", contrato);
        using var r = cmd.ExecuteReader();
        var lista = new List<MedicaoLida>();
        while (r.Read())
        {
            lista.Add(new MedicaoLida(r.GetString(0), Txt(r, 1), Txt(r, 2), Dia(r, 3), Dia(r, 4), Reais(r, 5), Reais(r, 6), Reais(r, 7), Reais(r, 8),
                Txt(r, 9), Txt(r, 10), Dia(r, 11), Reais(r, 12), Txt(r, 13)));
        }

        return lista;
    }

    public IReadOnlyList<EmpenhoLido> Empenhos(string contrato)
    {
        using var conexao = banco.Abrir();
        using var cmd = conexao.CreateCommand();
        cmd.CommandText = """
            select contrato, numero, data, ano, processo_sei, total_empenhado, liquido, liquidado, anulado, pago_exercicio, pago_restos, credor, portaria, saldo_final
            from Empenho where contrato = $c collate nocase order by data, numero
            """;
        cmd.Parameters.AddWithValue("$c", contrato);
        using var r = cmd.ExecuteReader();
        var lista = new List<EmpenhoLido>();
        while (r.Read())
        {
            lista.Add(new EmpenhoLido(Txt(r, 0), Txt(r, 1), Dia(r, 2), r.IsDBNull(3) ? null : r.GetInt32(3), Txt(r, 4), Reais(r, 5), Reais(r, 6),
                Reais(r, 7), Reais(r, 8), Reais(r, 9), Reais(r, 10), Txt(r, 11), Txt(r, 12), Reais(r, 13)));
        }

        return lista;
    }

    // ---- Portarias e dotacoes ----

    public IReadOnlyList<VinculoBase> Vinculos()
    {
        using var conexao = banco.Abrir();
        using var cmd = conexao.CreateCommand();
        cmd.CommandText = """
            select v.id, p.rotulo, v.contrato_receita, v.finalidade, v.dotacao, v.dotacao_original, v.resumida, v.descricao, v.origem
            from Vinculo v join Portaria p on p.id = v.portaria_id
            order by p.numero, p.ano, p.rotulo, v.contrato_receita, v.resumida
            """;
        using var r = cmd.ExecuteReader();
        var lista = new List<VinculoBase>();
        while (r.Read())
        {
            lista.Add(new VinculoBase(r.GetInt64(0), r.GetString(1), r.GetString(2), Txt(r, 3), r.GetString(4), Txt(r, 5), Txt(r, 6), Txt(r, 7), r.GetString(8)));
        }

        return lista;
    }

    public IReadOnlyList<PortariaBase> Portarias()
    {
        using var conexao = banco.Abrir();
        using var cmd = conexao.CreateCommand();
        cmd.CommandText = "select id, rotulo, numero, ano from Portaria order by numero, ano, rotulo";
        using var r = cmd.ExecuteReader();
        var lista = new List<PortariaBase>();
        while (r.Read())
        {
            lista.Add(new PortariaBase(r.GetInt64(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetInt32(2), r.IsDBNull(3) ? null : r.GetInt32(3)));
        }

        return lista;
    }

    /// <summary>Apelidos mantidos pelo usuario: texto de portaria de outra fonte -> rotulo da portaria da base.</summary>
    public IReadOnlyDictionary<string, string> Apelidos()
    {
        using var conexao = banco.Abrir();
        using var cmd = conexao.CreateCommand();
        cmd.CommandText = "select texto, portaria_rotulo from PortariaApelido";
        using var r = cmd.ExecuteReader();
        var mapa = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        while (r.Read())
        {
            mapa[r.GetString(0)] = r.GetString(1);
        }

        return mapa;
    }

    public void GravarApelido(string texto, string? portariaRotulo)
    {
        using var conexao = banco.Abrir();
        using var cmd = conexao.CreateCommand();
        if (string.IsNullOrWhiteSpace(portariaRotulo))
        {
            cmd.CommandText = "delete from PortariaApelido where texto = $t";
            cmd.Parameters.AddWithValue("$t", texto);
        }
        else
        {
            cmd.CommandText = "insert into PortariaApelido (texto, portaria_rotulo) values ($t, $p) on conflict(texto) do update set portaria_rotulo = excluded.portaria_rotulo";
            Valores(Parametros(cmd, "$t", "$p"), texto, portariaRotulo);
        }

        cmd.ExecuteNonQuery();
    }

    /// <summary>Textos de portaria que aparecem nas obras (como a mestra escreve).</summary>
    public IReadOnlyList<string> PortariasDasObras()
    {
        using var conexao = banco.Abrir();
        using var cmd = conexao.CreateCommand();
        cmd.CommandText = "select distinct portaria from Obra where portaria is not null order by portaria collate nocase";
        using var r = cmd.ExecuteReader();
        var lista = new List<string>();
        while (r.Read())
        {
            lista.Add(r.GetString(0));
        }

        return lista;
    }

    // ---- Interno ----

    /// <summary>Monta o WHERE com todos os filtros, menos o do campo indicado (usado para montar as opcoes dele).</summary>
    private static string Onde(FiltroObras f, SqliteCommand cmd, string? menos)
    {
        var partes = new List<string> { "1 = 1" };
        int n = 0;
        string P(object valor)
        {
            string nome = "$f" + n++;
            cmd.Parameters.AddWithValue(nome, valor);
            return nome;
        }

        void Igual(string coluna, string? valor, string campo)
        {
            if (!string.IsNullOrWhiteSpace(valor) && menos != campo)
            {
                partes.Add($"{coluna} = {P(valor)} collate nocase");
            }
        }

        Igual("portaria", f.Portaria, "portaria");
        Igual("contrato", f.Contrato, "contrato");
        Igual("empreendimento", f.Escola, "escola");
        Igual("lote", f.Lote, "lote");
        Igual("contratada", f.Empresa, "empresa");
        Igual("atividade", f.Dotacao, "dotacao");
        Igual("status", f.Status, "status");
        if (!string.IsNullOrWhiteSpace(f.ContratoReceita) && menos != "receita")
        {
            partes.Add($"contrato_receita like {P("%" + f.ContratoReceita.Trim() + "%")}");
        }

        foreach (string termo in Normalizacao.Busca(f.Texto).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            partes.Add($"busca like {P("%" + termo.Replace("%", "").Replace("_", "") + "%")}");
        }

        // Datas no formato ISO comparam certo como texto; os limites entram na faixa.
        var datas = new List<string>();
        if (f.OsDe is not null || f.OsAte is not null)
        {
            var faixa = new List<string> { "data_os is not null" };
            if (f.OsDe is { } de)
            {
                faixa.Add($"data_os >= {P(Iso(de)!)}");
            }

            if (f.OsAte is { } ate)
            {
                faixa.Add($"data_os <= {P(Iso(ate)!)}");
            }

            datas.Add("(" + string.Join(" and ", faixa) + ")");
        }

        if (f.SemDataOs)
        {
            datas.Add("data_os is null");
        }

        if (datas.Count > 0)
        {
            partes.Add("(" + string.Join(" or ", datas) + ")");
        }

        foreach (var (campo, faixa) in f.Valores.Where(v => !v.Value.Vazia))
        {
            string coluna = Coluna(campo);
            var ou = new List<string>();
            if (faixa.Minimo is not null || faixa.Maximo is not null)
            {
                var e = new List<string> { $"{coluna} is not null" }; // ausente nao e zero
                if (faixa.Minimo is { } min)
                {
                    e.Add($"{coluna} >= {P(Normalizacao.Centavos(min)!)}");
                }

                if (faixa.Maximo is { } max)
                {
                    e.Add($"{coluna} <= {P(Normalizacao.Centavos(max)!)}");
                }

                ou.Add("(" + string.Join(" and ", e) + ")");
            }

            if (faixa.SemInformacao)
            {
                ou.Add($"{coluna} is null");
            }

            partes.Add("(" + string.Join(" or ", ou) + ")");
        }

        return string.Join(" and ", partes);
    }

    private static string Coluna(CampoValor campo) => campo switch
    {
        CampoValor.ValorEmpreendimento => "valor_empreendimento",
        CampoValor.ValorAditado => "valor_aditado",
        CampoValor.ValorContratadoComAditamento => "valor_contratado_aditamento",
        CampoValor.ValorMedido => "valor_medido",
        CampoValor.ValorPago => "valor_pago",
        CampoValor.SaldoAMedir => "saldo_medir",
        _ => "saldo_pagar",
    };

    private static ObraLida LerObra(SqliteDataReader r)
    {
        string? T(string coluna) => r.IsDBNull(r.GetOrdinal(coluna)) ? null : r.GetString(r.GetOrdinal(coluna));
        DateOnly? D(string coluna) => Dia(r, r.GetOrdinal(coluna));
        decimal? V(string coluna) => Reais(r, r.GetOrdinal(coluna));
        return new ObraLida
        {
            Id = r.GetInt64(r.GetOrdinal("id")),
            Contrato = T("contrato")!,
            Empreendimento = T("empreendimento")!,
            Contratada = T("contratada"),
            Portaria = T("portaria"),
            ContratoReceita = T("contrato_receita"),
            Dotacao = T("dotacao"),
            Atividade = T("atividade"),
            Lote = T("lote"),
            Modalidade = T("modalidade"),
            Tipo = T("tipo"),
            TipoDeObra = T("tipo_obra"),
            ProcessoSei = T("processo_sei"),
            Status = T("status"),
            StatusContrato = T("status_contrato"),
            Situacao = T("situacao"),
            Escopo = T("escopo"),
            DataAssinatura = D("data_assinatura"),
            DataOs = D("data_os"),
            Termino = D("termino"),
            ValorEmpreendimento = V("valor_empreendimento"),
            ValorAditado = V("valor_aditado"),
            ValorContratadoComAditamento = V("valor_contratado_aditamento"),
            ValorMedido = V("valor_medido"),
            ValorPago = V("valor_pago"),
            SaldoAMedir = V("saldo_medir"),
            SaldoAPagar = V("saldo_pagar"),
            IncluidaNoApp = r.GetInt64(r.GetOrdinal("local")) != 0,
            CamposEditados = r.GetInt32(r.GetOrdinal("editados")),
        };
    }

    private static void GravarFonte(SqliteConnection conexao, SqliteTransaction? tx, FonteInfo f)
    {
        using var cmd = conexao.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            insert into Fonte (id, nome, caminho, estado, mensagem, ultima_leitura, modificado_arquivo, tamanho_arquivo, linhas, versao, avisos_json)
            values ($id, $nome, $caminho, $estado, $mensagem, $leitura, $modificado, $tamanho, $linhas, $versao, $avisos)
            on conflict(id) do update set nome = excluded.nome, caminho = excluded.caminho, estado = excluded.estado, mensagem = excluded.mensagem,
                ultima_leitura = excluded.ultima_leitura, modificado_arquivo = excluded.modificado_arquivo, tamanho_arquivo = excluded.tamanho_arquivo,
                linhas = excluded.linhas, versao = excluded.versao, avisos_json = excluded.avisos_json
            """;
        Valores(Parametros(cmd, "$id", "$nome", "$caminho", "$estado", "$mensagem", "$leitura", "$modificado", "$tamanho", "$linhas", "$versao", "$avisos"),
            f.Id, f.Nome, f.Caminho, (int)f.Estado, f.Mensagem, Iso(f.UltimaLeitura), Iso(f.ModificadoArquivo), f.TamanhoArquivo, f.Linhas, f.Versao,
            JsonSerializer.Serialize(f.Avisos));
        cmd.ExecuteNonQuery();
    }

    private static void Executar(SqliteConnection conexao, SqliteTransaction tx, string sql)
    {
        using var cmd = conexao.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static SqliteParameter[] Parametros(SqliteCommand cmd, params string[] nomes) =>
        [.. nomes.Select(n => cmd.Parameters.Add(new SqliteParameter(n, DBNull.Value)))];

    private static void Valores(SqliteParameter[] parametros, params object?[] valores)
    {
        for (int i = 0; i < parametros.Length; i++)
        {
            parametros[i].Value = valores[i] ?? DBNull.Value;
        }
    }

    private static string? Iso(DateOnly? data) => data?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string? Iso(DateTimeOffset? instante) => instante?.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset? Instante(SqliteDataReader r, int i) =>
        r.IsDBNull(i) ? null : DateTimeOffset.Parse(r.GetString(i), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static string? Txt(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);

    private static DateOnly? Dia(SqliteDataReader r, int i) =>
        r.IsDBNull(i) ? null : DateOnly.ParseExact(r.GetString(i), "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static decimal? Reais(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : Normalizacao.Reais(r.GetInt64(i));
}
