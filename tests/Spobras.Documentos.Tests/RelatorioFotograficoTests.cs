using System.IO.Compression;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using SkiaSharp;
using Spobras.Core.Relatorio;
using Spobras.Documentos.Imagens;
using Spobras.Documentos.Relatorio;

namespace Spobras.Documentos.Tests;

public class RelatorioFotograficoTests : IDisposable
{
    private readonly string _pasta = Directory.CreateTempSubdirectory("spobras-rel-").FullName;

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    private string Em(string nome) => Path.Combine(_pasta, nome);

    /// <summary>Foto de teste: metade esquerda vermelha, metade direita azul (da para conferir a orientacao).</summary>
    private string CriarFoto(string nome, int largura, int altura, int orientacaoExif = 1, DateTime? capturada = null, SKEncodedImageFormat formato = SKEncodedImageFormat.Jpeg)
    {
        using var bitmap = new SKBitmap(largura, altura);
        using (var tela = new SKCanvas(bitmap))
        {
            tela.Clear(SKColors.Red);
            using var azul = new SKPaint { Color = SKColors.Blue };
            tela.DrawRect(largura / 2f, 0, largura / 2f, altura, azul);
        }

        using var imagem = SKImage.FromBitmap(bitmap);
        byte[] bytes = imagem.Encode(formato, 90).ToArray();
        if (formato == SKEncodedImageFormat.Jpeg && (orientacaoExif != 1 || capturada is not null))
        {
            bytes = ComExif(bytes, orientacaoExif, capturada);
        }

        File.WriteAllBytes(Em(nome), bytes);
        return Em(nome);
    }

    /// <summary>Insere um bloco EXIF minimo (orientacao e data de captura) logo depois do inicio do JPEG.</summary>
    private static byte[] ComExif(byte[] jpeg, int orientacao, DateTime? capturada)
    {
        using var tiff = new MemoryStream();
        using var w = new BinaryWriter(tiff);
        w.Write("II"u8.ToArray());
        w.Write((ushort)42);
        w.Write(8u);                                   // IFD0 comeca no byte 8
        w.Write((ushort)(capturada is null ? 1 : 2));  // entradas
        w.Write((ushort)0x0112); w.Write((ushort)3); w.Write(1u); w.Write((ushort)orientacao); w.Write((ushort)0);
        if (capturada is { } data)
        {
            w.Write((ushort)0x8769); w.Write((ushort)4); w.Write(1u); w.Write(38u); // sub-IFD no byte 38
            w.Write(0u);                                // fim do IFD0 (bytes 34 a 37)
            w.Write((ushort)1);                         // sub-IFD: 1 entrada
            w.Write((ushort)0x9003); w.Write((ushort)2); w.Write(20u); w.Write(56u);
            w.Write(0u);
            w.Write(System.Text.Encoding.ASCII.GetBytes(data.ToString("yyyy:MM:dd HH:mm:ss") + "\0"));
        }
        else
        {
            w.Write(0u);
        }

        byte[] corpo = [.. "Exif\0\0"u8, .. tiff.ToArray()];
        int tamanho = corpo.Length + 2;
        return [0xFF, 0xD8, 0xFF, 0xE1, (byte)(tamanho >> 8), (byte)tamanho, .. corpo, .. jpeg.AsSpan(2)];
    }

    private static SKColor Cor(byte[] jpeg, float x, float y)
    {
        using var bitmap = SKBitmap.Decode(jpeg);
        return bitmap.GetPixel((int)(bitmap.Width * x), (int)(bitmap.Height * y));
    }

    private static bool Vermelho(SKColor c) => c.Red > 180 && c.Blue < 90;
    private static bool Azul(SKColor c) => c.Blue > 180 && c.Red < 90;

    // ---- Fotos ----

    [Fact]
    public void T09_le_tamanho_e_data_de_captura_e_aplica_a_orientacao_exif()
    {
        var quando = new DateTime(2026, 11, 10, 14, 30, 5);
        string normal = CriarFoto("normal.jpg", 400, 300);
        string deitada = CriarFoto("celular.jpg", 400, 300, orientacaoExif: 6, capturada: quando); // precisa girar 90 graus
        string deCabeca = CriarFoto("cabeca.jpg", 400, 300, orientacaoExif: 3);

        Assert.Equal(new InfoFoto(400, 300, null), Fotos.Ler(normal));
        Assert.Equal(new InfoFoto(300, 400, quando), Fotos.Ler(deitada));
        Assert.Equal(new InfoFoto(300, 400, null), Fotos.Ler(normal, rotacaoManual: 90));
        Assert.Equal(new InfoFoto(400, 300, quando), Fotos.Ler(deitada, rotacaoManual: 270));

        // Orientacao 6: o lado esquerdo do arquivo (vermelho) vai para cima.
        byte[] girada = Fotos.Preparar(deitada, 400, 90);
        Assert.True(Vermelho(Cor(girada, 0.5f, 0.2f)));
        Assert.True(Azul(Cor(girada, 0.5f, 0.8f)));

        // Orientacao 3: 180 graus, o vermelho passa para a direita.
        byte[] virada = Fotos.Preparar(deCabeca, 400, 90);
        Assert.True(Azul(Cor(virada, 0.2f, 0.5f)));
        Assert.True(Vermelho(Cor(virada, 0.8f, 0.5f)));

        // Rotacao manual de 90 graus no sentido horario: o vermelho vai para cima.
        byte[] manual = Fotos.Preparar(normal, 400, 90, rotacaoManual: 90);
        Assert.True(Vermelho(Cor(manual, 0.5f, 0.2f)));
    }

    [Fact]
    public void Copia_reduzida_mantem_a_proporcao_e_nao_altera_o_original()
    {
        string grande = CriarFoto("grande.jpg", 4000, 3000);
        long tamanhoAntes = new FileInfo(grande).Length;
        var modificadoAntes = File.GetLastWriteTimeUtc(grande);

        using var copia = SKBitmap.Decode(Fotos.Preparar(grande, 1600, 82));
        using var miniatura = SKBitmap.Decode(Fotos.Miniatura(grande, 320));
        using var pequena = SKBitmap.Decode(Fotos.Preparar(CriarFoto("pequena.png", 200, 100, formato: SKEncodedImageFormat.Png), 1600, 82));

        Assert.Equal((1600, 1200), (copia.Width, copia.Height));
        Assert.Equal((320, 240), (miniatura.Width, miniatura.Height));
        Assert.Equal((200, 100), (pequena.Width, pequena.Height)); // foto pequena nao e ampliada
        Assert.Equal(tamanhoAntes, new FileInfo(grande).Length);
        Assert.Equal(modificadoAntes, File.GetLastWriteTimeUtc(grande));
    }

    [Fact]
    public void Arquivo_invalido_ou_formato_nao_aceito_tem_mensagem_propria()
    {
        File.WriteAllText(Em("falsa.jpg"), "isto não é uma foto");
        File.WriteAllText(Em("celular.HEIC"), "x");
        File.WriteAllText(Em("planilha.xlsx"), "x");

        Assert.Contains("não é uma imagem válida", Assert.Throws<FotoInvalidaException>(() => Fotos.Ler(Em("falsa.jpg"))).Message);
        Assert.Contains("HEIC", Assert.Throws<FotoInvalidaException>(() => Fotos.Ler(Em("celular.HEIC"))).Message);
        Assert.Contains(".xlsx não é uma foto aceita", Assert.Throws<FotoInvalidaException>(() => Fotos.Preparar(Em("planilha.xlsx"), 100, 80)).Message);
        Assert.Equal(new Exif.Dados(1, null), Exif.Ler(Em("falsa.jpg")));
        Assert.True(Fotos.Suportada("a.JPEG"));
        Assert.False(Fotos.Suportada("a.heic"));
    }

    // ---- Gerador ----

    private (PlanoRelatorio Plano, string Caminho) Gerar(int fotos, OpcoesRelatorio opcoes, DadosRelatorio? dados = null, bool logo = true)
    {
        var lista = Enumerable.Range(1, fotos).Select(n =>
        {
            string caminho = CriarFoto($"foto {n}.jpg", n % 3 == 0 ? 300 : 400, n % 3 == 0 ? 400 : 300);
            var info = Fotos.Ler(caminho);
            return new FotoRelatorio(caminho, info.Largura, info.Altura) { Legenda = $"Serviço {n} & <teste>", Descricao = n == 2 ? "Descrição do serviço executado." : "" };
        }).ToList();
        var plano = PaginadorRelatorio.Paginar(lista, opcoes, dados);
        string caminho = Em($"relatorio-{Guid.NewGuid():N}.xlsx");
        GeradorRelatorio.Gerar(plano, caminho, logo ? CriarFoto("logo.png", 195, 138, formato: SKEncodedImageFormat.Png) : null);
        return (plano, caminho);
    }

    private static XDocument Parte(string xlsx, string nome)
    {
        using var zip = ZipFile.OpenRead(xlsx);
        using var fluxo = zip.GetEntry(nome)!.Open();
        return XDocument.Load(fluxo);
    }

    private static readonly XNamespace S = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace Xdr = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";

    private static List<string> Textos(XDocument planilha) => [.. planilha.Descendants(S + "t").Select(t => t.Value)];

    [Fact]
    public void T09_gera_xlsx_valido_com_todas_as_fotos_paginas_e_cabecalho_repetido()
    {
        var dados = new DadosRelatorio
        {
            Unidade = "EMEF Exemplo", Contratada = "Construtora Exemplo Ltda", Contrato = "000/EXEMPLO/2026", Lote = "12",
            Data = new DateOnly(2026, 11, 10),
        };
        var (plano, caminho) = Gerar(14, new OpcoesRelatorio(), dados);

        using (var doc = SpreadsheetDocument.Open(caminho, false))
        {
            Assert.Empty(new OpenXmlValidator().Validate(doc).Select(e => e.Description));
        }

        var planilha = Parte(caminho, "xl/worksheets/sheet1.xml");
        var desenho = Parte(caminho, "xl/drawings/drawing1.xml");
        var textos = Textos(planilha);

        Assert.Equal(3, plano.Paginas.Count);
        // 14 fotos + o logo em cada uma das 3 paginas.
        Assert.Equal(17, desenho.Descendants(Xdr + "oneCellAnchor").Count());
        using (var zip = ZipFile.OpenRead(caminho))
        {
            Assert.Equal(15, zip.Entries.Count(e => e.FullName.StartsWith("xl/media/"))); // logo guardado uma vez so
        }

        Assert.Equal(6, textos.Count(t => t == "RELATÓRIO FOTOGRÁFICO")); // titulo + faixa, por pagina
        Assert.Equal(3, textos.Count(t => t == "EMEF Exemplo"));
        Assert.Equal(3, textos.Count(t => t == "Construtora Exemplo Ltda"));
        Assert.Equal(3, textos.Count(t => t == "Contrato: 000/EXEMPLO/2026   ·   Lote: 12"));
        Assert.Contains("Foto 01 — Serviço 1 & <teste>", textos); // caracteres especiais preservados
        Assert.Contains("Foto 14 — Serviço 14 & <teste>", textos);
        Assert.DoesNotContain("Descrição do serviço executado.", textos); // modo legenda curta

        // Data como data de verdade (editavel no Excel), nao como texto.
        double dataExcel = new DateTime(2026, 11, 10).ToOADate();
        Assert.Equal(3, planilha.Descendants(S + "v").Count(v => v.Value == dataExcel.ToString(System.Globalization.CultureInfo.InvariantCulture)));

        // Quebras de pagina manuais, area de impressao e rodape.
        var quebras = planilha.Descendants(S + "brk").Select(b => (int)b.Attribute("id")!).ToList();
        Assert.Equal([55, 110], quebras);
        Assert.Contains("'Relatório'!$C$1:$L$165", Parte(caminho, "xl/workbook.xml").Descendants(S + "definedName").Single().Value);
        Assert.Contains("Página &P de &N", planilha.Descendants(S + "oddFooter").Single().Value);
        Assert.Equal("portrait", (string)planilha.Descendants(S + "pageSetup").Single().Attribute("orientation")!);
        Assert.Equal("9", (string)planilha.Descendants(S + "pageSetup").Single().Attribute("paperSize")!); // A4
    }

    [Fact]
    public void T09_imagem_no_arquivo_tem_a_mesma_proporcao_da_foto()
    {
        var (plano, caminho) = Gerar(3, new OpcoesRelatorio(), logo: false);

        var tamanhos = Parte(caminho, "xl/drawings/drawing1.xml").Descendants(Xdr + "ext")
            .Select(e => ((double)(long)e.Attribute("cx")!, (double)(long)e.Attribute("cy")!)).ToList();

        Assert.Equal(3, tamanhos.Count);
        Assert.Equal(4.0 / 3, tamanhos[0].Item1 / tamanhos[0].Item2, 2); // horizontal
        Assert.Equal(3.0 / 4, tamanhos[2].Item1 / tamanhos[2].Item2, 2); // vertical: nao e esticada
        Assert.All(plano.Paginas[0].Fotos, f => Assert.True(f.Imagem.Base <= f.Texto.Y));
    }

    [Fact]
    public void T10_campos_vazios_nao_deixam_rotulo_e_descricao_e_assinaturas_aparecem_quando_pedidas()
    {
        var (_, semDados) = Gerar(2, new OpcoesRelatorio(), logo: false);
        var vazio = Textos(Parte(semDados, "xl/worksheets/sheet1.xml"));
        Assert.DoesNotContain("DATA", vazio);
        Assert.DoesNotContain("CONTRATADA", vazio);
        Assert.DoesNotContain("UNIDADE EDUCACIONAL", vazio);
        Assert.DoesNotContain("Fiscalização SPOBRAS", vazio);
        Assert.Empty(Parte(semDados, "xl/drawings/drawing1.xml").Descendants(Xdr + "oneCellAnchor").Skip(2)); // sem logo

        var opcoes = new OpcoesRelatorio { Modo = ModoLegenda.DescricaoServico, Assinaturas = true, AssinaturaFiscal = "Fulano de Tal", FotosPorPagina = 4 };
        var (_, completo) = Gerar(2, opcoes, new DadosRelatorio { Unidade = "CEI Exemplo" });
        var textos = Textos(Parte(completo, "xl/worksheets/sheet1.xml"));
        Assert.Contains("UNIDADE EDUCACIONAL", textos);
        Assert.Contains("Descrição do serviço executado.", textos);
        Assert.Contains("Fiscalização SPOBRAS", textos);
        Assert.Contains("Contratada", textos);
        Assert.Contains("Fulano de Tal", textos);
    }

    [Fact]
    public void Dados_longos_no_cabecalho_tomam_linhas_do_corpo_sem_esconder_texto()
    {
        string objeto = string.Join(" ", Enumerable.Repeat("reforma e ampliação da unidade educacional", 12));
        var dados = new DadosRelatorio { Objeto = objeto };

        var (plano, caminho) = Gerar(6, new OpcoesRelatorio(), dados, logo: false);

        Assert.True(plano.LinhasExtrasDoCabecalho > 0);
        Assert.Contains("Objeto: " + objeto, Textos(Parte(caminho, "xl/worksheets/sheet1.xml")));
        Assert.All(plano.Paginas.SelectMany(p => p.Fotos), f => Assert.True(f.Espaco.Base <= GradeRelatorio.AlturaDaPagina + 0.01));
        // A pagina continua com 55 linhas: a linha de dados cresce e o corpo encolhe.
        var linhas = Parte(caminho, "xl/worksheets/sheet1.xml").Descendants(S + "row").Count();
        Assert.Equal(plano.Paginas.Count * (55 - plano.LinhasExtrasDoCabecalho), linhas);
    }

    [Fact]
    public void Nao_sobrescreve_e_cancelamento_nao_deixa_arquivo_pela_metade()
    {
        var (plano, caminho) = Gerar(2, new OpcoesRelatorio(), logo: false);
        long tamanho = new FileInfo(caminho).Length;

        Assert.Throws<IOException>(() => GeradorRelatorio.Gerar(plano, caminho));
        Assert.Equal(tamanho, new FileInfo(caminho).Length);

        using var cancelamento = new CancellationTokenSource();
        cancelamento.Cancel();
        string outro = Em("cancelado.xlsx");
        Assert.Throws<OperationCanceledException>(() => GeradorRelatorio.Gerar(plano, outro, null, null, cancelamento.Token));
        Assert.Empty(Directory.GetFiles(_pasta, "cancelado*"));

        // Foto que sumiu depois da previa: erro claro e nada publicado.
        File.Delete(plano.Paginas[0].Fotos[0].Foto.Caminho);
        Assert.Throws<FotoInvalidaException>(() => GeradorRelatorio.Gerar(plano, Em("sem-foto.xlsx")));
        Assert.Empty(Directory.GetFiles(_pasta, "sem-foto*"));
    }

    [Fact]
    public void Nome_sugerido_omite_o_que_esta_vazio()
    {
        Assert.Equal("Relatório Fotográfico", CabecalhoRelatorio.NomeSugerido(new DadosRelatorio()));
        Assert.Equal("Relatório Fotográfico - CEU Exemplo - 2026-11-10",
            CabecalhoRelatorio.NomeSugerido(new DadosRelatorio { Unidade = " CEU Exemplo ", Data = new DateOnly(2026, 11, 10) }));
        Assert.Equal("Relatório Fotográfico - EMEF A-B", CabecalhoRelatorio.NomeSugerido(new DadosRelatorio { Unidade = "EMEF A/B" }));
    }
}
