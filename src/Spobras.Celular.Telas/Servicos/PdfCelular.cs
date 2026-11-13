using Spobras.Core.Pdf;
using Spobras.Documentos.Pdf;

namespace Spobras.Celular.Servicos;

public sealed record PdfPronto(string Mensagem, IReadOnlyList<Salvo> Arquivos, IReadOnlyList<string> Locais);

/// <summary>
/// Ferramentas de PDF no celular, as mesmas do computador (Documentos). Os arquivos escolhidos sao copiados para o
/// app; o resultado e gerado no app e salvo na pasta do usuario sem sobrescrever nada.
/// </summary>
public sealed class PdfCelular(IPlataforma plataforma, ArquivoPreferencias preferencias)
{
    private string Trabalho()
    {
        string pasta = Path.Combine(plataforma.PastaDoApp, "pdf");
        if (Directory.Exists(pasta))
        {
            Directory.Delete(pasta, recursive: true); // pasta temporaria do proprio app
        }

        Directory.CreateDirectory(pasta);
        return pasta;
    }

    /// <summary>Pasta temporaria onde os arquivos escolhidos sao copiados.</summary>
    public string Entrada
    {
        get
        {
            string pasta = Path.Combine(plataforma.PastaDoApp, "pdf-entrada");
            Directory.CreateDirectory(pasta);
            return pasta;
        }
    }

    public void LimparEntrada()
    {
        string pasta = Path.Combine(plataforma.PastaDoApp, "pdf-entrada");
        if (Directory.Exists(pasta))
        {
            Directory.Delete(pasta, recursive: true);
        }
    }

    public Task<IReadOnlyList<Escolhido>> EscolherPdfsAsync(bool varios) => plataforma.EscolherArquivosAsync("application/pdf", varios, Entrada);

    public Task<IReadOnlyList<Escolhido>> EscolherFotosAsync() => plataforma.EscolherArquivosAsync("image/*", true, Entrada);

    public static int Paginas(string pdf) => FerramentasPdf.Ler(pdf).Paginas;

    private async Task<PdfPronto> Salvar(string mensagem, IReadOnlyList<string> locais)
    {
        var p = preferencias.Ler();
        if (p.Pasta is null)
        {
            throw new InvalidOperationException("Escolha antes a pasta onde salvar os arquivos.");
        }

        var salvos = new List<Salvo>();
        foreach (string l in locais)
        {
            salvos.Add(await plataforma.SalvarAsync(p.Pasta.Id, null, l));
        }

        return new PdfPronto(mensagem, salvos, locais);
    }

    private static string Nome(string original, string sufixo) => $"{Path.GetFileNameWithoutExtension(original)} - {sufixo}";

    public async Task<PdfPronto> JuntarAsync(IReadOnlyList<Escolhido> pdfs)
    {
        if (pdfs.Count < 2)
        {
            throw new InvalidOperationException("Escolha pelo menos dois PDFs para juntar.");
        }

        string saida = Path.Combine(Trabalho(), Nome(pdfs[0].Nome, "juntado") + ".pdf");
        int paginas = await Task.Run(() => FerramentasPdf.Juntar([.. pdfs.Select(p => p.Local!)], saida));
        return await Salvar($"{pdfs.Count} PDFs juntados num só, com {Formato.Plural(paginas, "página", "páginas")}.", [saida]);
    }

    public async Task<PdfPronto> SepararAsync(Escolhido pdf, string paginas)
    {
        int total = Paginas(pdf.Local!);
        var lidos = IntervalosPdf.Interpretar(paginas, total);
        if (!lidos.Valido)
        {
            throw new InvalidOperationException(lidos.Erro ?? "Diga as páginas, por exemplo 1-3, 5.");
        }

        string pasta = Trabalho();
        string copia = Path.Combine(pasta, pdf.Nome);
        File.Copy(pdf.Local!, copia);
        var criados = await Task.Run(() => FerramentasPdf.Separar(copia, lidos.Intervalos, pasta));
        return await Salvar($"{Formato.Plural(criados.Count, "arquivo criado", "arquivos criados")} com as páginas {paginas.Trim()}.", criados);
    }

    public async Task<PdfPronto> ReduzirAsync(Escolhido pdf)
    {
        string saida = Path.Combine(Trabalho(), Nome(pdf.Nome, "reduzido") + ".pdf");
        var r = await Task.Run(() => FerramentasPdf.Reduzir(pdf.Local!, saida, 1600, 70));
        if (!r.Reduziu)
        {
            return new PdfPronto("Este PDF já está no menor tamanho possível sem perder qualidade. Nada foi salvo.", [], []);
        }

        return await Salvar($"De {Formato.Tamanho(r.TamanhoOriginal)} para {Formato.Tamanho(r.TamanhoNovo)}.", [r.Caminho!]);
    }

    public async Task<PdfPronto> FotosEmPdfAsync(IReadOnlyList<Escolhido> fotos, string nome)
    {
        if (fotos.Count == 0)
        {
            throw new InvalidOperationException("Escolha pelo menos uma foto.");
        }

        string limpo = Spobras.Core.Nomes.NomeArquivo.Corrigir(string.IsNullOrWhiteSpace(nome) ? "Fotos" : nome.Trim());
        string saida = Path.Combine(Trabalho(), limpo + ".pdf");
        int paginas = await Task.Run(() => FerramentasPdf.FotosEmPdf([.. fotos.Select(f => f.Local!)], saida));
        return await Salvar($"{Formato.Plural(paginas, "foto", "fotos")} em PDF, uma por página.", [saida]);
    }

    public async Task<PdfPronto> NumerarAsync(Escolhido pdf, bool numerar, bool logo, int aPartirDa)
    {
        var p = preferencias.Ler();
        if (logo && (p.Logo is null || !File.Exists(p.Logo)))
        {
            throw new InvalidOperationException("Escolha o logo nas Configurações antes.");
        }

        if (!numerar && !logo)
        {
            throw new InvalidOperationException("Escolha numeração, logo ou os dois.");
        }

        string saida = Path.Combine(Trabalho(), Nome(pdf.Nome, numerar ? "numerado" : "com logo") + ".pdf");
        var opcoes = new OpcoesCarimbo { Numerar = numerar, Logo = logo ? p.Logo : null, APartirDa = Math.Max(1, aPartirDa) };
        int paginas = await Task.Run(() => CarimboPdf.Carimbar(pdf.Local!, saida, opcoes));
        return await Salvar($"{Formato.Plural(paginas, "página marcada", "páginas marcadas")}.", [saida]);
    }

    public async Task<PdfPronto> ParaExcelAsync(Escolhido pdf)
    {
        string saida = Path.Combine(Trabalho(), Path.GetFileNameWithoutExtension(pdf.Nome) + ".xlsx");
        // No celular nao ha leitura de imagem (OCR): PDF sem texto e avisado antes, sem gerar nada.
        if (await Task.Run(() => TextoPdf.Extrair(pdf.Local!).SemTextoNenhum))
        {
            return new PdfPronto("Este PDF não tem texto (parece digitalizado). No celular não há leitura de imagem; use o computador.", [], []);
        }

        var texto = await Task.Run(() => TextoPdf.ParaExcel(pdf.Local!, saida));
        int linhas = texto.Tabela?.Linhas.Count ?? texto.Linhas.Count;
        return await Salvar($"Tabela com {Formato.Plural(linhas, "linha", "linhas")}, de {Formato.Plural(texto.Paginas, "página", "páginas")}.", [saida]);
    }
}
