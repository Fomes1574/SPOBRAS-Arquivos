using System.Diagnostics;
using Spobras.Core.Base;
using Spobras.Core.Planilhas;
using Spobras.Dados.Base;
using Spobras.Testes;

namespace Spobras.Dados.Tests;

/// <summary>Peso: abas de licitação dez vezes maiores que as reais, lidas, gravadas, filtradas e buscadas sem demora.</summary>
public class LicitacoesPesoTests : IDisposable
{
    private readonly string _pasta = Directory.CreateTempSubdirectory("spobras-licpeso-").FullName;

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    private static List<object?[]> Aba(bool licitacao, int linhas)
    {
        var sorte = new Random(1574);
        string[] modalidades = ["Manutenção", "Manutenção CEU", "Reforma e Manutenção", "Muro de Arrimo", "Construção", "Pista de Skate"];
        var grade = new List<object?[]>
        {
            new object?[] { "Título", null, null, null, null, null, null, null, null, null, null },
            new object?[11],
            new object?[] { "UNIDADES", null, null, null, null, null, null, "TOTAIS", 0m, 0m, null },
            licitacao
                ? ["Portaria ", "Contrato Receita", "Dotação ", "Lote", "Modalidade", "Nome da Obra", "Empresa", "STATUS", "Valor do Contrato com ADM", "Recursos para 2027", "Processo "]
                : ["Portaria ", "Contrato Receita", "Dotação ", "Lote", "Modalidade", "Nome da Obra", "Processo", "STATUS", "Valor do Contrato", "Valor com ADM", null],
        };
        for (int i = 0; i < linhas; i++)
        {
            decimal valor = 100_000m + sorte.Next(0, 9_000_000);
            string escola = $"{(i % 3 == 0 ? "EMEF" : i % 3 == 1 ? "EMEI" : "CEI")} Exemplo Peso {i:0000}";
            grade.Add(licitacao
                ? [(decimal)(3 + i % 4), "015/EX/2025", 3360m + i % 6, $"LOTE {i % 120:000}", modalidades[i % modalidades.Length], escola,
                    i % 5 == 0 ? null : $"Empresa {i % 40}", i % 5 == 0 ? "Publicada" : "Homologação", valor, Math.Round(valor * 1.1m, 2), $"7910.2026/{i:0000000}-0"]
                : [(decimal)(3 + i % 4), "Não tem", 3360m + i % 6, i % 30 == 0 ? null : $"LOTE {i % 150:000}", modalidades[i % modalidades.Length], escola,
                    i % 2 == 0 ? "" : $"7910.2026/{i:0000000}-0", "A Licitar Obras", valor, Math.Round(valor * 1.1m, 2), null]);
        }

        return grade;
    }

    [Fact]
    public void Mil_e_duzentas_em_licitacao_e_duas_mil_a_licitar()
    {
        var p = BaseSintetica.Mestra();
        p.AbasPorNome["Em licitação"] = Aba(true, 1200);
        p.AbasPorNome["A Licitar"] = Aba(false, 2000);
        var base_ = new BaseRepositorio(new BancoApp(Path.Combine(_pasta, "app.db")));

        var relogio = Stopwatch.StartNew();
        var dados = ImportadorMestra.Importar(p);
        base_.GravarMestra(dados, new FonteInfo { Id = Fontes.Mestra, Nome = "m", Caminho = "m.xlsx", Estado = EstadoFonte.Atualizada, Versao = 1 });
        var lic = base_.Previstas(BaseDeObras.EmLicitacao);
        var aLicitar = base_.Previstas(BaseDeObras.ALicitar);
        long leitura = relogio.ElapsedMilliseconds;
        Assert.Equal(1200, lic.Count);
        Assert.Equal(2000, aLicitar.Count);
        Assert.DoesNotContain(dados.Avisos, a => a.Contains("não confere", StringComparison.Ordinal)); // 10% certos em todas
        Assert.True(leitura < 5000, $"ler e gravar levou {leitura} ms");

        relogio.Restart();
        for (int i = 0; i < 50; i++)
        {
            Previstas.Filtrar(lic, new FiltroPrevistas { Texto = $"peso {i:00}", Lote = [$"LOTE {i:000}"] });
            GrupoPrevisto.Montar(lic.Concat(aLicitar), (i % 120).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        long buscas = relogio.ElapsedMilliseconds;
        Assert.True(buscas < 3000, $"50 filtros e 50 buscas de grupo levaram {buscas} ms");

        // Grupos nunca juntam bases nem modalidades diferentes, mesmo com o mesmo número de lote.
        var grupos = GrupoPrevisto.Montar(lic.Concat(aLicitar), "lote 012", maximo: 100);
        Assert.All(grupos, g => Assert.All(g.Obras, o =>
        {
            Assert.Equal(g.Base, o.Base);
            Assert.Equal(Normalizacao.Busca(g.Modalidade ?? ""), Normalizacao.Busca(o.Modalidade ?? ""));
        }));

        // "também está em" com 3.200 obras sem contrato continua rápido
        relogio.Restart();
        for (int i = 0; i < 40; i++) base_.TambemEm($"EMEF Exemplo Peso {i * 3:0000}", BaseDeObras.EmLicitacao);
        Assert.True(relogio.ElapsedMilliseconds < 3000, $"40 consultas de também está em levaram {relogio.ElapsedMilliseconds} ms");
    }
}
