using Spobras.Celular.Servicos;
using Spobras.Testes;

namespace Spobras.Celular.Navegador;

/// <summary>Estados iniciais para os testes visuais (dados inventados).</summary>
public static class Cenarios
{
    public static AppCelular? App { get; private set; }

    public static async Task PrepararAsync(PlataformaDeTeste p, string cenario, Func<DateTimeOffset> agora)
    {
        if (!File.Exists(p.Mestra))
        {
            var mestra = AmostraCelular.Mestra();
            if (cenario == "pronto")
            {
                var obras = mestra.TabelasPorNome["contratos_VW_ESCOLAS_CONTRATOS_MEDIDOS_PAGOS"];
                var secoes = obras.Colunas.ToList();
                var dados = obras.Linhas.Select(l => l.ToArray()).ToArray();
                var vencida = dados.Single(l => Equals(l[secoes.IndexOf("CONTRATO")], "052/EX/2026"));
                vencida[secoes.IndexOf("CONTRATADA")] = "Engenharia Fictícia de Obras, Manutenção e Desenvolvimento de Infraestrutura Escolar";
                vencida[secoes.IndexOf("EMPREENDIMENTO")] = "EMEF Escola Fictícia do Jardim das Flores e do Parque Municipal da Vila Nova";
                vencida[secoes.IndexOf("VALOR EMPREENDIMENTO")] = 99_999_999.99m;
                vencida[secoes.IndexOf("SALDO A PAGAR")] = 99_999_999.99m;
                vencida[secoes.IndexOf("TÉRMINO EXECUÇÃO")] = new DateTime(2026, 9, 1);
                vencida[secoes.IndexOf("STATUS CONTRATO")] = "VENCIDO";
                mestra.Tabela(obras.Nome, [.. obras.Colunas], dados);
            }

            XlsxSintetico.Gravar(mestra, p.Mestra);
            int[][] tamanhos = [[1600, 1200], [1600, 900], [1200, 1600], [1600, 1200], [1200, 1200], [1600, 1200], [1600, 900], [1200, 1600]];
            for (int i = 0; i < tamanhos.Length; i++)
            {
                AmostraCelular.Foto(p.Entrada, $"foto-{i + 1:00}.jpg", tamanhos[i][0], tamanhos[i][1], i + 1);
            }

            AmostraCelular.Logo(Path.Combine(p.Raiz, "logo"));
            string fotos = Path.Combine(p.Raiz, "para-pdf");
            Spobras.Documentos.Pdf.FerramentasPdf.FotosEmPdf([AmostraCelular.Foto(fotos, "a.jpg", 800, 600, 1), AmostraCelular.Foto(fotos, "b.jpg", 800, 600, 2)], Path.Combine(p.Entrada, "Medicao 05.pdf"));
            Spobras.Documentos.Pdf.FerramentasPdf.FotosEmPdf([AmostraCelular.Foto(fotos, "c.jpg", 600, 800, 3)], Path.Combine(p.Entrada, "Planilha orcamentaria.pdf"));
        }

        if (cenario == "grande" && !File.Exists(Path.Combine(p.Raiz, "grande.ok")))
        {
            // Uso intenso: 2.000 obras e 60 fotos (dados inventados).
            XlsxSintetico.Gravar(AmostraCelular.MestraGrande(2000), p.Mestra);
            for (int i = 9; i <= 60; i++)
            {
                AmostraCelular.Foto(p.Entrada, $"foto-{i:00}.jpg", i % 3 == 0 ? 1200 : 1600, i % 3 == 0 ? 1600 : 1200, i);
            }

            File.WriteAllText(Path.Combine(p.Raiz, "grande.ok"), "");
        }

        // GPS de mentira: uma leitura precisa, com um instante de espera para aparecer o "Procurando sinal".
        p.Pontos.Add(new Spobras.Core.Relatorio.PontoGps(-23.5505, -46.6333, 12, agora()));
        p.AtrasoDoGps = TimeSpan.FromMilliseconds(1200);
        App = new AppCelular(p, agora);
        if (cenario == "novo")
        {
            return;
        }

        await App.Base.EscolherPlanilhaAsync();
        if (App.Config.Logo is null)
        {
            p.ProximaEscolha = [Path.Combine(p.Raiz, "logo", "logo-exemplo.png")];
            await App.EscolherLogoAsync();
            App.Configurar(c => c with { Pasta = new Escolhido("pasta:" + p.Saida, "Relatórios") });
        }

        if (App.Rascunho.Vazio)
        {
            var obra = App.Base.Buscar(new Spobras.Core.Base.FiltroObras { Contrato = cenario == "grande" ? [] : ["044/EX/2026"] }, 1).Obras[0];
            App.Rascunho.AplicarObra(obra);
            p.ProximaEscolha = [.. Directory.GetFiles(p.Entrada, "foto-*.jpg").Order(StringComparer.Ordinal)];
            var escolhidas = await p.EscolherArquivosAsync("image/*", true, App.Rascunho.PastaDasFotos);
            App.Rascunho.AdicionarCopiadas(escolhidas);
            string[] legendas = ["Execução do piso cerâmico da sala 3", "Fachada principal após a pintura", "", "Cobertura: troca das telhas", "Rampa de acesso", "", "Pátio coberto", "Sanitário adaptado"];
            for (int i = 0; i < legendas.Length; i++)
            {
                App.Rascunho.Legendar(i, legendas[i]);
            }
        }

        if (cenario == "movido")
        {
            File.Move(p.Mestra, p.Mestra + ".movida", overwrite: true);
            await App.Base.AtualizarAsync();
        }
        else if (cenario == "colunamudou")
        {
            // A planilha mudou: "INÍCIO EXECUÇÃO" virou "DATA INÍCIO OBRA" (a leitura falha ate ajustar a coluna).
            var mestra = AmostraCelular.Mestra();
            var obras = mestra.TabelasPorNome["contratos_VW_ESCOLAS_CONTRATOS_MEDIDOS_PAGOS"];
            mestra.Tabela(obras.Nome, [.. obras.Colunas.Select(c => c == "INÍCIO EXECUÇÃO" ? "DATA INÍCIO OBRA" : c)], [.. obras.Linhas]);
            XlsxSintetico.Gravar(mestra, p.Mestra);
            await App.Base.AtualizarAsync(forcar: true);
        }
        else if (cenario == "semrede")
        {
            p.SemConexao = true;
            await App.Base.AtualizarAsync();
        }
    }
}
