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
            AmostraCelular.GravarMestra(p.Mestra);
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
            var obra = App.Base.Buscar(new Spobras.Core.Base.FiltroObras { Contrato = "044/EX/2026" }, 1).Obras[0];
            App.Rascunho.AplicarObra(obra);
            p.ProximaEscolha = [.. Directory.GetFiles(p.Entrada, "foto-*.jpg").Order(StringComparer.Ordinal)];
            var escolhidas = await p.EscolherArquivosAsync("image/*", true, App.Rascunho.PastaDasFotos);
            App.Rascunho.AdicionarCopiadas(escolhidas.Select(e => e.Local!));
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
        else if (cenario == "semrede")
        {
            p.SemConexao = true;
            await App.Base.AtualizarAsync();
        }
    }
}
