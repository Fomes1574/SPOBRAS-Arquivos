using System.IO.Compression;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Spobras.App.ViewModels;
using Spobras.Core.Relatorio;

namespace Spobras.App.Tests;

public class RelatorioVmTests : ComApp
{
    private List<string> CriarFotos(int quantidade) =>
        [.. Enumerable.Range(1, quantidade).Select(n => CriarFoto(Path.Combine("Downloads", "Fotos", $"foto {n}.jpg"), n % 4 == 0 ? 300 : 400, n % 4 == 0 ? 400 : 300))];

    [Fact]
    public async Task Fluxo_completo_dados_fotos_conferencia_e_geracao_em_excel_e_pdf()
    {
        CriarFotos(8);
        File.WriteAllText(Em("Downloads", "Fotos", "anotacoes.txt"), "não é foto");
        var vm = NovoRelatorio();

        // Etapa 1: dados (todos opcionais)
        Assert.True(vm.EtapaDados);
        vm.Unidade = "EMEF Exemplo";
        vm.Contratada = "Construtora Exemplo";
        vm.DataTexto = "10/11/2026";
        Assert.Equal("Relatório Fotográfico - EMEF Exemplo - 2026-11-10", vm.NomeArquivo);

        // Etapa 2: fotos de uma pasta, em ordem natural; o .txt da pasta nao vira erro
        vm.AvancarCommand.Execute(null);
        Dialogos.Pastas.Enqueue(Em("Downloads", "Fotos"));
        vm.AdicionarPastaCommand.Execute(null);
        Assert.Equal("8 fotos escolhidas", vm.TextoFotos);
        Assert.Equal(["foto 1.jpg", "foto 2.jpg", "foto 3.jpg"], vm.Fotos.Take(3).Select(f => f.Nome));
        Assert.Equal(["Foto 01", "Foto 02"], vm.Fotos.Take(2).Select(f => f.Rotulo));
        Assert.Empty(vm.Problemas);
        Assert.Equal("Sem data de captura no arquivo", vm.Fotos[0].Detalhe);
        vm.Fotos[0].Legenda = "Fachada";

        // Etapa 3: conferencia
        vm.AvancarCommand.Execute(null);
        Assert.True(vm.EtapaConferencia);
        Assert.Equal("8 fotos em 2 páginas", vm.Resumo);
        Assert.Equal(["Página 1 de 2", "Página 2 de 2"], vm.Paginas.Select(p => p.Titulo));
        Assert.Contains(vm.Paginas[0].Elementos, e => e.Texto == "Fachada"); // so a legenda, sem "Foto 01"
        Assert.DoesNotContain(vm.Paginas[0].Elementos, e => e.Texto?.StartsWith("Foto ") == true);
        Assert.Contains(vm.Paginas[0].Elementos, e => e.Texto == "EMEF Exemplo");
        Assert.Contains(vm.Paginas[0].Elementos, e => e.Texto == "10/11/2026");
        await vm.Miniaturas; // miniaturas em segundo plano
        Assert.All(vm.Fotos, f => Assert.NotNull(f.Miniatura));

        vm.GerarPdf = true;
        Assert.True(vm.GerarCommand.CanExecute(null));
        await vm.GerarCommand.ExecuteAsync(null);

        string xlsx = Em("Downloads", "Relatório Fotográfico - EMEF Exemplo - 2026-11-10.xlsx");
        Assert.True(File.Exists(xlsx));
        Assert.True(File.Exists(Path.ChangeExtension(xlsx, ".pdf")));
        Assert.Equal((xlsx, Path.ChangeExtension(xlsx, ".pdf")), Assert.Single(Exportador.Chamadas));
        Assert.StartsWith("Relatório gerado em Excel e PDF", vm.Resultado);
        using (var zip = ZipFile.OpenRead(xlsx))
        {
            Assert.Equal(8, zip.Entries.Count(e => e.FullName.StartsWith("xl/media/")));
        }

        Assert.Equal("Gerou “Relatório Fotográfico - EMEF Exemplo - 2026-11-10.xlsx” e o PDF", Operacoes.Ultimos()[0].Descricao);

        // Gerar de novo nao sobrescreve: sai com (2).
        await vm.GerarCommand.ExecuteAsync(null);
        Assert.True(File.Exists(Em("Downloads", "Relatório Fotográfico - EMEF Exemplo - 2026-11-10 (2).xlsx")));
    }

    [Fact]
    public void T09_arquivos_invalidos_e_repetidos_sao_avisados_e_nao_somem_em_silencio()
    {
        var fotos = CriarFotos(2);
        string falsa = Criar(Path.Combine("Downloads", "quebrada.jpg"), "não é imagem");
        string heic = Criar(Path.Combine("Downloads", "celular.heic"));
        var vm = NovoRelatorio();

        vm.Adicionar([fotos[0], falsa, heic, fotos[1], fotos[0]]);

        Assert.Equal(2, vm.Fotos.Count);
        Assert.Contains(vm.Problemas, p => p.Texto.StartsWith("quebrada.jpg: O arquivo não é uma imagem válida"));
        Assert.Contains(vm.Problemas, p => p.Texto.Contains("celular.heic") && p.Texto.Contains("HEIC"));
        Assert.Contains(vm.Problemas, p => p.Texto == "1 foto já estava na lista e não foi repetida.");
    }

    [Fact]
    public async Task Ordenar_mover_girar_legenda_em_lote_e_opcoes_de_layout()
    {
        var vm = NovoRelatorio();
        vm.Adicionar(CriarFotos(5).AsEnumerable().Reverse());
        await vm.Miniaturas.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal("foto 5.jpg", vm.Fotos[0].Nome);

        vm.OrdenarPorNomeCommand.Execute(null);
        Assert.Equal(["foto 1.jpg", "foto 2.jpg", "foto 3.jpg", "foto 4.jpg", "foto 5.jpg"], vm.Fotos.Select(f => f.Nome));

        vm.DescerCommand.Execute(vm.Fotos[0]);
        Assert.Equal(["foto 2.jpg", "foto 1.jpg"], vm.Fotos.Take(2).Select(f => f.Nome));
        Assert.Equal("Foto 02", vm.Fotos[1].Rotulo); // o numero acompanha a ordem

        // Girar troca largura e altura e a previa continua sem distorcer.
        var foto = vm.Fotos[0];
        Assert.Equal((400, 300), (foto.Largura, foto.Altura));
        vm.GirarDireitaCommand.Execute(foto);
        await vm.Miniaturas.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal((300, 400, 90), (foto.Largura, foto.Altura, foto.Rotacao));
        vm.GirarEsquerdaCommand.Execute(foto);
        await vm.Miniaturas.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(0, foto.Rotacao);

        vm.Fotos[2].Legenda = "Já tinha";
        vm.LegendaEmLote = "Vista geral";
        vm.AplicarLegendaEmLoteCommand.Execute(null); // sem selecao: so as vazias
        Assert.Equal(["Vista geral", "Vista geral", "Já tinha", "Vista geral", "Vista geral"], vm.Fotos.Select(f => f.Legenda));
        vm.Fotos[2].Selecionada = true;
        vm.LegendaEmLote = "Trocada";
        vm.AplicarLegendaEmLoteCommand.Execute(null); // com selecao: so as marcadas
        Assert.Equal("Trocada", vm.Fotos[2].Legenda);
        Assert.Equal("Vista geral", vm.Fotos[0].Legenda);

        vm.FotosPorPagina = 2;
        Assert.Equal("5 fotos em 3 páginas", vm.Resumo);
        vm.Assinaturas = true;
        vm.AssinaturaFiscal = "Fulano";
        Assert.Contains(vm.Paginas[^1].Elementos, e => e.Texto == "Fiscalização SPOBRAS" && e.LinhaEmCima);
        vm.Modo = ModoLegenda.DescricaoServico;
        vm.Fotos[0].Descricao = "Serviço executado conforme projeto.";
        Assert.Contains(vm.Paginas[0].Elementos, e => e.Texto == "Serviço executado conforme projeto.");

        vm.RemoverCommand.Execute(vm.Fotos[0]);
        Assert.Equal("4 fotos escolhidas", vm.TextoFotos);
    }

    [Fact]
    public async Task Data_invalida_impede_gerar_e_explica()
    {
        var vm = NovoRelatorio();
        vm.Adicionar(CriarFotos(1));
        await vm.Miniaturas.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(vm.GerarCommand.CanExecute(null));

        vm.DataTexto = "31/02/2026";

        Assert.False(vm.GerarCommand.CanExecute(null));
        Assert.Contains(vm.Avisos, a => a.Impede && a.Texto.Contains("data do relatório não existe"));
        vm.DataTexto = "1/3/2026";
        Assert.True(vm.GerarCommand.CanExecute(null));
        Assert.Contains(vm.Paginas[0].Elementos, e => e.Texto == "01/03/2026");
    }

    [Fact]
    public async Task T11_rascunho_volta_depois_de_fechar_e_foto_que_sumiu_e_indicada_e_localizavel()
    {
        var fotos = CriarFotos(3);
        var vm = NovoRelatorio();
        vm.Unidade = "CEI Exemplo";
        vm.Adicionar(fotos);
        await vm.Miniaturas;
        vm.Fotos[1].Legenda = "Cobertura";
        vm.Fotos[1].Descricao = "Troca de telhas";
        vm.SubirCommand.Execute(vm.Fotos[2]);
        vm.FotosPorPagina = 4;
        vm.Modo = ModoLegenda.DescricaoServico;
        vm.GirarDireitaCommand.Execute(vm.Fotos[0]);
        await vm.Miniaturas;

        // "Fecha o app" e a foto 2 muda de pasta.
        string novoLugar = Em("Downloads", "Movidas", "foto 2.jpg");
        Directory.CreateDirectory(Path.GetDirectoryName(novoLugar)!);
        File.Move(fotos[1], novoLugar);
        var reaberto = NovoRelatorio();
        await reaberto.Miniaturas;
        reaberto.AoEntrar();

        Assert.Equal("CEI Exemplo", reaberto.Unidade);
        Assert.Equal(["foto 1.jpg", "foto 3.jpg", "foto 2.jpg"], reaberto.Fotos.Select(f => f.Nome)); // ordem restaurada
        Assert.Equal(("Cobertura", "Troca de telhas"), (reaberto.Fotos[2].Legenda, reaberto.Fotos[2].Descricao));
        Assert.Equal(90, reaberto.Fotos[0].Rotacao);
        Assert.Equal((4, ModoLegenda.DescricaoServico), (reaberto.FotosPorPagina, reaberto.Modo));

        var sumida = reaberto.Fotos[2];
        Assert.True(sumida.Faltando);
        Assert.Contains("Use Localizar", sumida.Detalhe);
        Assert.False(reaberto.GerarCommand.CanExecute(null)); // nao gera relatorio incompleto em silencio
        Assert.Contains(reaberto.Avisos, a => a.Impede && a.Texto.Contains("1 foto não foi encontrada"));

        Dialogos.Arquivos.Enqueue([novoLugar]);
        reaberto.LocalizarCommand.Execute(sumida);
        await reaberto.Miniaturas;

        Assert.False(sumida.Faltando);
        Assert.Equal(novoLugar, sumida.Caminho);
        Assert.Equal("Cobertura", sumida.Legenda);
        Assert.True(reaberto.GerarCommand.CanExecute(null));
        await reaberto.GerarCommand.ExecuteAsync(null);
        Assert.False(reaberto.ResultadoComFalha);
    }

    [Fact]
    public async Task Novo_relatorio_limpa_o_contexto_e_o_rascunho()
    {
        var vm = NovoRelatorio();
        vm.Unidade = "EMEF Exemplo";
        vm.Adicionar(CriarFotos(2));
        await vm.Miniaturas.WaitAsync(TimeSpan.FromSeconds(30));
        vm.Etapa = 2;

        Dialogos.Confirmacoes.Enqueue(false);
        vm.NovoRelatorioCommand.Execute(null);
        Assert.Equal(2, vm.Fotos.Count); // cancelou: nada muda

        Dialogos.Confirmacoes.Enqueue(true);
        vm.NovoRelatorioCommand.Execute(null);

        Assert.Empty(vm.Fotos);
        Assert.Equal("", vm.Unidade);
        Assert.True(vm.EtapaDados);
        Assert.Equal("Relatório Fotográfico", vm.NomeArquivo);
        Assert.Empty(NovoRelatorio().Fotos);
        Assert.True(File.Exists(Em("Downloads", "Fotos", "foto 1.jpg"))); // as fotos nao sao apagadas
    }

    [Fact]
    public async Task Sem_excel_gera_so_a_planilha_e_falha_do_pdf_e_explicada()
    {
        Exportador.Disponivel = false;
        var vm = NovoRelatorio();
        vm.Adicionar(CriarFotos(1));
        await vm.Miniaturas;
        vm.GerarPdf = true;
        Assert.False(vm.PdfDisponivel);
        Assert.Contains(vm.Avisos, a => a.Conferir && a.Texto.Contains("Será gerado só o arquivo Excel"));
        await vm.GerarCommand.ExecuteAsync(null);
        Assert.Empty(Exportador.Chamadas);
        Assert.StartsWith("Relatório gerado: Relatório Fotográfico.xlsx (1 página, 1 foto).", vm.Resultado);

        Exportador.Disponivel = true;
        Exportador.Falhar = true;
        await vm.GerarCommand.ExecuteAsync(null);
        Assert.True(vm.ResultadoComFalha);
        Assert.Contains("mas o PDF não: O Excel não respondeu.", vm.Resultado);
        Assert.True(File.Exists(Em("Downloads", "Relatório Fotográfico (2).xlsx")));
    }

    [Fact]
    public async Task Logo_cadastrado_entra_na_previa_e_pode_ser_tirado()
    {
        string imagem = CriarFoto("logo-do-usuario.jpg", 195, 138);
        var config = new ConfiguracoesVm(Preferencias, Novidades, Dialogos, Disco, Relogio, "0.1.0", Em("dados"), Modelos);
        Assert.False(config.TemLogo);

        Dialogos.Arquivos.Enqueue([imagem]);
        config.EscolherLogoCommand.Execute(null);

        Assert.True(config.TemLogo);
        Assert.StartsWith(Em("dados", "modelos"), config.Logo); // copia interna, fora de Downloads
        var vm = NovoRelatorio();
        vm.Adicionar(CriarFotos(1));
        await vm.Miniaturas;
        Assert.Contains(vm.Paginas[0].Elementos, e => e.Imagem == config.Logo);

        Dialogos.Confirmacoes.Enqueue(true);
        config.RemoverLogoCommand.Execute(null);
        Assert.False(config.TemLogo);
        Assert.True(File.Exists(imagem)); // o arquivo do usuario fica

        Dialogos.Arquivos.Enqueue([Criar("nao-e-imagem.png", "x")]);
        config.EscolherLogoCommand.Execute(null);
        Assert.Contains("não é uma imagem válida", Dialogos.AvisosMostrados[^1]);
    }
}

public class PdfVmTests : ComApp
{
    private string CriarPdf(string nome, int paginas)
    {
        string caminho = Em("Downloads", nome);
        using var doc = new PdfDocument();
        for (int i = 0; i < paginas; i++)
        {
            doc.AddPage();
        }

        doc.Save(caminho);
        return caminho;
    }

    private static int Paginas(string pdf)
    {
        using var doc = PdfReader.Open(pdf, PdfDocumentOpenMode.Import);
        return doc.PageCount;
    }

    [Fact]
    public async Task Logo_e_numeracao_mostra_as_paginas_atingidas_e_cria_copia()
    {
        string pdf = CriarPdf("relatorio.pdf", 3);
        var vm = NovoPdf();
        vm.Ferramenta = FerramentaPdf.LogoENumeracao;
        Assert.False(vm.TemLogo); // sem logo cadastrado
        vm.Adicionar([pdf]);
        Assert.Contains(vm.Avisos, a => a.Texto == "relatorio.pdf: páginas 1 a 3 (3) recebem a marca; a cópia sai com outro nome.");

        vm.APartirDa = "x";
        Assert.Contains(vm.Avisos, a => a.Impede && a.Texto.Contains("A partir da página"));
        vm.APartirDa = "5";
        Assert.Contains(vm.Avisos, a => a.Impede && a.Texto == "relatorio.pdf: não tem a página 5.");
        vm.APartirDa = "2";
        vm.Numerar = false;
        Assert.Contains(vm.Avisos, a => a.Impede && a.Texto.StartsWith("Escolha numerar"));
        Assert.False(vm.ExecutarCommand.CanExecute(null));
        vm.Numerar = true;

        await vm.ExecutarCommand.ExecuteAsync(null);

        Assert.False(vm.ResultadoComFalha, vm.Resultado);
        Assert.Equal("Pronto! relatorio.pdf: 2 páginas marcadas.", vm.Resultado);
        Assert.True(File.Exists(Em("Downloads", "relatorio (numerado).pdf")));
        Assert.Equal(3, Paginas(Em("Downloads", "relatorio (numerado).pdf")));
    }

    [Fact]
    public async Task Pdf_para_excel_gera_uma_planilha_por_pdf_e_explica_o_digitalizado()
    {
        string comTexto = Em("Downloads", "numerado.pdf");
        Spobras.Documentos.Pdf.CarimboPdf.Carimbar(CriarPdf("base.pdf", 2), comTexto, new Spobras.Documentos.Pdf.OpcoesCarimbo());
        string digitalizado = CriarPdf("digitalizado.pdf", 1);
        var vm = NovoPdf();
        vm.Ferramenta = FerramentaPdf.ParaExcel;
        vm.Adicionar([comTexto, digitalizado]);
        Assert.Equal("Gerar 2 planilhas", vm.TextoBotao);

        await vm.ExecutarCommand.ExecuteAsync(null);

        Assert.True(vm.ResultadoComFalha);
        Assert.Contains("numerado.pdf virou “numerado.xlsx” (2 linhas lidas)", vm.Resultado);
        Assert.Contains("digitalizado.pdf: \"digitalizado.pdf\" não tem texto", vm.Resultado);
        Assert.True(File.Exists(Em("Downloads", "numerado.xlsx")));
        Assert.False(File.Exists(Em("Downloads", "digitalizado.xlsx")));
        Assert.Equal("Gerou “numerado.xlsx”", Operacoes.Ultimos()[0].Descricao);
    }

    [Fact]
    public async Task Juntar_pede_dois_pdfs_respeita_a_ordem_e_registra()
    {
        string a = CriarPdf("a.pdf", 2), b = CriarPdf("b.pdf", 3);
        var vm = NovoPdf();
        Assert.True(vm.EhJuntar);
        Assert.Equal("Adicionar PDFs", vm.TextoAdicionar);

        vm.Adicionar([a]);
        Assert.Contains(vm.Avisos, x => x.Impede && x.Texto.Contains("pelo menos dois"));
        Assert.False(vm.ExecutarCommand.CanExecute(null));
        Assert.StartsWith("2 páginas · ", vm.Entradas[0].Detalhe);

        vm.Adicionar([b, Criar(Path.Combine("Downloads", "nota.txt"))]);
        Assert.Contains(vm.Avisos, x => x.Texto == "nota.txt: não é um PDF.");
        Assert.Equal("Juntar 2 PDFs", vm.TextoBotao);
        Assert.Equal("a (junto)", vm.NomeSaida);
        vm.SubirCommand.Execute(vm.Entradas[1]);
        Assert.Equal(["b.pdf", "a.pdf"], vm.Entradas.Select(e => e.Nome));

        await vm.ExecutarCommand.ExecuteAsync(null);

        Assert.Equal(5, Paginas(Em("Downloads", "a (junto).pdf")));
        Assert.Equal("Pronto! 1 arquivo criado: a (junto).pdf.", vm.Resultado);
        Assert.False(vm.TemEntradas);
        Assert.Equal("Gerou “a (junto).pdf”", Operacoes.Ultimos()[0].Descricao);
    }

    [Fact]
    public async Task Separar_por_pagina_e_por_intervalos_com_validacao()
    {
        string pdf = CriarPdf("Anexo.pdf", 6);
        var vm = NovoPdf();
        vm.Ferramenta = FerramentaPdf.Separar;
        vm.Adicionar([pdf, CriarPdf("outro.pdf", 1)]);
        Assert.Equal("outro.pdf", Assert.Single(vm.Entradas).Nome); // um PDF de cada vez
        vm.Adicionar([pdf]);

        vm.SepararCadaPagina = false;
        vm.Intervalos = "1-2, 9";
        Assert.Contains(vm.Avisos, a => a.Impede && a.Texto.Contains("o PDF tem 6 página(s)"));
        Assert.False(vm.ExecutarCommand.CanExecute(null));

        vm.Intervalos = "1-2, 5-";
        Assert.Contains(vm.Avisos, a => a.Texto == "Vão sair 2 arquivos: p 1-2, p 5-6.");
        Dialogos.Pastas.Enqueue(Em("Separados"));
        vm.EscolherDestinoCommand.Execute(null);
        await vm.ExecutarCommand.ExecuteAsync(null);

        Assert.Equal(["Anexo - p 1-2.pdf", "Anexo - p 5-6.pdf"], Nomes("Separados"));
        Assert.Equal("Pronto! 2 arquivos criados.", vm.Resultado);

        vm.Adicionar([pdf]);
        vm.SepararCadaPagina = true;
        await vm.ExecutarCommand.ExecuteAsync(null);
        Assert.Equal(8, Nomes("Separados").Length);
    }

    [Fact]
    public async Task Fotos_em_pdf_e_reduzir_com_pdf_ja_otimizado()
    {
        var vm = NovoPdf();
        vm.Ferramenta = FerramentaPdf.FotosEmPdf;
        vm.Adicionar([CriarFoto(Path.Combine("Downloads", "f1.jpg")), CriarFoto(Path.Combine("Downloads", "f2.jpg"), 300, 400), Criar(Path.Combine("Downloads", "x.heic"))]);
        Assert.Equal(2, vm.Entradas.Count);
        Assert.Contains(vm.Avisos, a => a.Texto.Contains("x.heic"));
        Assert.Equal("Criar PDF com 2 fotos", vm.TextoBotao);
        vm.NomeSaida = "Fotos da vistoria";

        await vm.ExecutarCommand.ExecuteAsync(null);
        Assert.Equal(2, Paginas(Em("Downloads", "Fotos da vistoria.pdf")));

        // Reduzir: PDF so com paginas em branco nao diminui; o app avisa e nao cria copia.
        vm.Ferramenta = FerramentaPdf.Reduzir;
        Assert.False(vm.TemEntradas); // trocar de ferramenta nao leva a lista junto
        vm.Adicionar([CriarPdf("leve.pdf", 2)]);
        await vm.ExecutarCommand.ExecuteAsync(null);

        Assert.Contains("leve.pdf: já está otimizado", vm.Resultado);
        Assert.False(File.Exists(Em("Downloads", "leve (reduzido).pdf")));
        Assert.False(vm.ResultadoComFalha);
    }

    [Fact]
    public void Pdf_protegido_e_recusado_com_mensagem()
    {
        string protegido = Em("Downloads", "protegido.pdf");
        using (var doc = new PdfDocument())
        {
            doc.AddPage();
            doc.SecuritySettings.UserPassword = "segredo";
            doc.Save(protegido);
        }

        var vm = NovoPdf();
        vm.Adicionar([protegido]);

        Assert.False(vm.TemEntradas);
        Assert.Contains(vm.Avisos, a => a.Texto.Contains("protegido por senha"));
    }
}
