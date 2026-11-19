using Microsoft.Data.Sqlite;
using Spobras.Core.Base;

namespace Spobras.Dados.Base;

/// <summary>Bases Em licitacao e A licitar: cada uma na sua tabela, lidas inteiras (poucas centenas de linhas).</summary>
public sealed partial class BaseRepositorio
{
    private readonly Dictionary<BaseDeObras, IReadOnlyList<ObraPrevista>> _previstas = [];

    private static string TabelaDa(BaseDeObras qual) => qual switch
    {
        BaseDeObras.EmLicitacao => "ObraLicitacao",
        BaseDeObras.ALicitar => "ObraALicitar",
        _ => throw new ArgumentOutOfRangeException(nameof(qual), "Em andamento fica na tabela Obra."),
    };

    /// <summary>Grava uma base prevista dentro da transacao da mestra. Lista null = aba nao lida: nada muda.</summary>
    private static void GravarPrevistas(SqliteConnection conexao, SqliteTransaction tx, BaseDeObras qual, IReadOnlyList<ObraPrevista>? obras)
    {
        if (obras is null)
        {
            return;
        }

        string tabela = TabelaDa(qual);
        Executar(conexao, tx, $"delete from {tabela};");
        using var cmd = conexao.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"""
            insert into {tabela} (id, linha, nome, portaria, contrato_receita, dotacao, lote, modalidade, empresa, status, processo, valor_obra, valor_com_remuneracao)
            values ($id, $linha, $nome, $portaria, $receita, $dotacao, $lote, $modalidade, $empresa, $status, $processo, $valor, $com10)
            """;
        var p = Parametros(cmd, "$id", "$linha", "$nome", "$portaria", "$receita", "$dotacao", "$lote", "$modalidade", "$empresa", "$status", "$processo", "$valor", "$com10");
        foreach (var o in obras)
        {
            Valores(p, o.Id, o.Linha, o.Nome, o.Portaria, o.ContratoReceita, o.Dotacao, o.Lote, o.Modalidade, o.Empresa, o.Status, o.Processo,
                Normalizacao.Centavos(o.ValorObra), Normalizacao.Centavos(o.ValorComRemuneracao));
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>Todas as obras de uma base sem contrato (guardadas ate a proxima leitura da mestra).</summary>
    public IReadOnlyList<ObraPrevista> Previstas(BaseDeObras qual)
    {
        lock (_trava)
        {
            if (_previstas.TryGetValue(qual, out var guardadas))
            {
                return guardadas;
            }
        }

        using var conexao = banco.Abrir();
        using var cmd = conexao.CreateCommand();
        cmd.CommandText = $"""
            select id, linha, nome, portaria, contrato_receita, dotacao, lote, modalidade, empresa, status, processo, valor_obra, valor_com_remuneracao
            from {TabelaDa(qual)} order by linha
            """;
        using var r = cmd.ExecuteReader();
        var lista = new List<ObraPrevista>();
        while (r.Read())
        {
            lista.Add(new ObraPrevista
            {
                Id = r.GetInt64(0), Base = qual, Linha = r.GetInt32(1), Nome = r.GetString(2), Portaria = Txt(r, 3), ContratoReceita = Txt(r, 4),
                Dotacao = Txt(r, 5), Lote = Txt(r, 6), Modalidade = Txt(r, 7), Empresa = Txt(r, 8), Status = Txt(r, 9), Processo = Txt(r, 10),
                ValorObra = r.IsDBNull(11) ? null : Normalizacao.Reais(r.GetInt64(11)),
                ValorComRemuneracao = r.IsDBNull(12) ? null : Normalizacao.Reais(r.GetInt64(12)),
            });
        }

        lock (_trava)
        {
            _previstas[qual] = lista;
        }

        return lista;
    }

    public ObraPrevista? Prevista(BaseDeObras qual, long id) => Previstas(qual).FirstOrDefault(o => o.Id == id);

    /// <summary>
    /// Onde mais a escola aparece, sem juntar dados: obras em andamento (por contrato) e as outras bases sem contrato.
    /// A escola e a mesma quando o nome e igual (sem acento, caixa e espacos nas pontas): nomes diferentes, como
    /// "EMEF José Olympio" e "EMEF - José Olympio", sao escolas diferentes.
    /// </summary>
    public IReadOnlyList<OutraBase> TambemEm(string escola, BaseDeObras naoIncluir, long? idAtual = null)
    {
        var lista = new List<OutraBase>();
        if (naoIncluir != BaseDeObras.EmAndamento)
        {
            foreach (var o in Buscar(new FiltroObras { Escola = [escola] }, 50).Obras.Where(o => ProcuraObras.MesmaEscola(o.Empreendimento, escola)))
            {
                lista.Add(new OutraBase(BaseDeObras.EmAndamento, o.Id, $"contrato {o.Contrato}" + (string.IsNullOrWhiteSpace(o.Modalidade) ? "" : $" · {o.Modalidade}")));
            }
        }

        foreach (var qual in new[] { BaseDeObras.EmLicitacao, BaseDeObras.ALicitar })
        {
            foreach (var o in Previstas(qual).Where(o => ProcuraObras.MesmaEscola(o.Nome, escola) && !(qual == naoIncluir && o.Id == idAtual)))
            {
                lista.Add(new OutraBase(qual, o.Id, string.Join(" · ", new[] { o.Modalidade, o.Lote }.Where(v => !string.IsNullOrWhiteSpace(v)))));
            }
        }

        return lista;
    }
}

/// <summary>A mesma escola em outra base (ou outra obra da mesma base): so um atalho, os dados nao se juntam.</summary>
public sealed record OutraBase(BaseDeObras Base, long Id, string Detalhe)
{
    public string Texto => $"{BasesDeObras.Nome(Base)}" + (Detalhe.Length > 0 ? $" ({Detalhe})" : "");
}
