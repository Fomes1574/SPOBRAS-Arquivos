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

    [Fact]
    public void Preparo_de_fotos_preserva_a_copia_que_esta_sendo_lida()
    {
        string cache = Em("cache.jpg");
        PreparoDeFotos.Gravar(cache, [1, 2, 3]);
        using var leitura = File.Open(cache, FileMode.Open, FileAccess.Read, FileShare.Read);

        PreparoDeFotos.Gravar(cache, [4, 5, 6]);

        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(cache));
        Assert.Equal(cache, Assert.Single(Directory.GetFiles(_pasta)));
    }

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

    /// <summary>
    /// As 8 orientacoes de camera (EXIF) com os 4 giros manuais: cada canto colorido vai para onde deve, em foto
    /// grande (aberta ja reduzida) e em miniatura. Protege a preparacao das fotos, que faz tudo num desenho so.
    /// </summary>
    [Fact]
    public void Todas_as_orientacoes_com_todos_os_giros_ficam_no_lugar_certo()
    {
        SKColor[] cores = [SKColors.Red, SKColors.Lime, SKColors.Blue, SKColors.Yellow]; // canto: sup-esq, sup-dir, inf-esq, inf-dir
        (float X, float Y)[] centros = [(0.25f, 0.25f), (0.75f, 0.25f), (0.25f, 0.75f), (0.75f, 0.75f)];
        using var bitmap = new SKBitmap(1600, 1200);
        using (var tela = new SKCanvas(bitmap))
        {
            for (int q = 0; q < 4; q++)
            {
                using var tinta = new SKPaint { Color = cores[q] };
                tela.DrawRect(q % 2 * 800, q / 2 * 600, 800, 600, tinta);
            }
        }

        byte[] jpeg = SKImage.FromBitmap(bitmap).Encode(SKEncodedImageFormat.Jpeg, 92).ToArray();
        // onde um ponto (x, y) da foto gravada aparece depois de aplicar a orientacao EXIF e o giro (horario)
        static (float, float) Exibir(int o, float x, float y) => o switch
        {
            2 => (1 - x, y), 3 => (1 - x, 1 - y), 4 => (x, 1 - y), 5 => (y, x), 6 => (1 - y, x), 7 => (1 - y, 1 - x), 8 => (y, 1 - x), _ => (x, y),
        };
        static (float, float) Girar(int g, float x, float y) => g switch { 90 => (1 - y, x), 180 => (1 - x, 1 - y), 270 => (y, 1 - x), _ => (x, y) };

        foreach (int lado in new[] { 1600, 900, 320 })
        {
            for (int o = 1; o <= 8; o++)
            {
                string arquivo = Em($"o{o}.jpg");
                File.WriteAllBytes(arquivo, o == 1 ? jpeg : ComExif(jpeg, o, null));
                foreach (int g in new[] { 0, 90, 180, 270 })
                {
                    byte[] pronta = Fotos.Preparar(arquivo, lado, 90, g);
                    var info = Fotos.Ler(arquivo, g);
                    using var b = SKBitmap.Decode(pronta);
                    Assert.Equal(info.Largura >= info.Altura, b.Width >= b.Height);
                    Assert.True(Math.Max(b.Width, b.Height) <= lado);
                    for (int q = 0; q < 4; q++)
                    {
                        var (x1, y1) = Exibir(o, centros[q].X, centros[q].Y);
                        var (x2, y2) = Girar(g, x1, y1);
                        var c = b.GetPixel((int)(x2 * b.Width), (int)(y2 * b.Height));
                        int diferenca = Math.Abs(c.Red - cores[q].Red) + Math.Abs(c.Green - cores[q].Green) + Math.Abs(c.Blue - cores[q].Blue);
                        Assert.True(diferenca < 90, $"lado {lado}, orientação {o}, giro {g}, canto {q}: {c}");
                    }
                }
            }
        }
    }

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

    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";

    /// <summary>Alturas (pt) das linhas da planilha e o fim de cada pagina (linha da quebra).</summary>
    private static (List<double> Alturas, List<int> Fins) Paginas(XDocument planilha)
    {
        var alturas = planilha.Descendants(S + "row").Select(r => double.Parse((string)r.Attribute("ht")!, System.Globalization.CultureInfo.InvariantCulture)).ToList();
        var fins = planilha.Descendants(S + "brk").Select(b => (int)b.Attribute("id")!).Append(alturas.Count).ToList();
        return (alturas, fins);
    }

    [Fact]
    public void Gera_xlsx_valido_com_o_layout_do_pdf_quebra_por_pagina_e_ajuste_a_largura()
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
        Assert.Equal(17, desenho.Descendants(Xdr + "pic").Count()); // 14 fotos + o logo em cada pagina
        Assert.Equal(14, desenho.Descendants(Xdr + "sp").Count());  // um quadro cinza por foto
        using (var zip = ZipFile.OpenRead(caminho))
        {
            Assert.Equal(15, zip.Entries.Count(e => e.FullName.StartsWith("xl/media/"))); // logo guardado uma vez so
        }

        Assert.Equal(3, textos.Count(t => t == "RELATÓRIO FOTOGRÁFICO")); // um titulo por pagina
        Assert.Contains("\n10 de novembro de 2026", textos);
        Assert.Equal(3, textos.Count(t => t == "EMEF Exemplo")); // cabecalho completo em todas as paginas
        Assert.Equal(3, textos.Count(t => t == "CONTRATO"));
        Assert.Equal(3, textos.Count(t => t == "LOTE"));
        Assert.DoesNotContain("PORTARIA", textos); // campo vazio nao aparece
        Assert.Contains("Serviço 1 & <teste>", textos); // caracteres especiais preservados
        Assert.Contains("Serviço 14 & <teste>", textos);
        Assert.DoesNotContain(textos, t => t.StartsWith("Foto ")); // sem numeracao automatica
        Assert.DoesNotContain("Descrição do serviço executado.", textos); // modo so legenda

        // Uma quebra no fim de cada pagina; cada pagina cabe na altura util da folha com as margens do arquivo.
        var (alturas, fins) = Paginas(planilha);
        Assert.Equal(3, fins.Count);
        var margens = planilha.Descendants(S + "pageMargins").Single();
        double M(string nome) => double.Parse((string)margens.Attribute(nome)!, System.Globalization.CultureInfo.InvariantCulture) * 72;
        double util = GradeRelatorio.AlturaDaFolha - M("top") - M("bottom");
        int inicio = 0;
        foreach (int fim in fins)
        {
            double pagina = alturas.Skip(inicio).Take(fim - inicio).Sum();
            Assert.True(pagina <= GradeRelatorio.FimDoConteudo - GradeRelatorio.MargemDeCima + 0.01, $"página até a linha {fim}: {pagina} pt");
            Assert.True(pagina <= util, $"página até a linha {fim} não cabe na folha: {pagina} > {util}");
            inicio = fim;
        }

        // Toda linha com altura em pixels inteiros (o Excel arredondaria e a soma passaria).
        Assert.All(alturas, h => Assert.Equal(0, h / 0.75 - Math.Round(h / 0.75), 6));
        Assert.Contains($"'Relatório'!$C$1:$M${alturas.Count}", Parte(caminho, "xl/workbook.xml").Descendants(S + "definedName").Single().Value);
        string rodape = planilha.Descendants(S + "oddFooter").Single().Value;
        Assert.Contains("Página &P de &N", rodape);
        Assert.Contains("EMEF Exemplo · Relatório fotográfico de 10/11/2026", rodape);
        var pagina1 = planilha.Descendants(S + "pageSetup").Single();
        Assert.Equal("portrait", (string)pagina1.Attribute("orientation")!);
        Assert.Equal("9", (string)pagina1.Attribute("paperSize")!); // A4
        Assert.Equal("1", (string)pagina1.Attribute("fitToWidth")!);
        Assert.Equal("0", (string)pagina1.Attribute("fitToHeight")!);
        Assert.Null(pagina1.Attribute("scale"));
        Assert.Equal("1", (string)planilha.Descendants(S + "pageSetUpPr").Single().Attribute("fitToPage")!);
        Assert.Equal("pageBreakPreview", (string)planilha.Descendants(S + "sheetView").Single().Attribute("view")!);
    }

    [Fact]
    public void Imagem_e_quadro_no_arquivo_seguem_o_plano()
    {
        var (plano, caminho) = Gerar(3, new OpcoesRelatorio(), logo: false);
        var desenho = Parte(caminho, "xl/drawings/drawing1.xml");
        long Emu(double pt) => (long)Math.Round(pt * 12700);

        var fotos = desenho.Descendants(Xdr + "pic").Select(p => p.Parent!.Element(Xdr + "ext")!).ToList();
        var quadros = desenho.Descendants(Xdr + "sp").Select(p => p.Parent!.Element(Xdr + "ext")!).ToList();
        Assert.Equal(3, fotos.Count);
        var planejadas = plano.Paginas[0].Fotos;
        for (int i = 0; i < 3; i++)
        {
            Assert.Equal(Emu(planejadas[i].Imagem.Largura), (long)fotos[i].Attribute("cx")!);
            Assert.Equal(Emu(planejadas[i].Imagem.Altura), (long)fotos[i].Attribute("cy")!);
            Assert.Equal(Emu(plano.AlturaDoQuadro), (long)quadros[i].Attribute("cy")!); // todos os quadros iguais
        }

        Assert.Equal(4.0 / 3, (double)(long)fotos[0].Attribute("cx")! / (long)fotos[0].Attribute("cy")!, 2); // deitada
        Assert.Equal(3.0 / 4, (double)(long)fotos[2].Attribute("cx")! / (long)fotos[2].Attribute("cy")!, 2); // em pe: nao e esticada
        Assert.Equal("F2F2F0", desenho.Descendants(A + "srgbClr").First().Attribute("val")!.Value);
    }

    [Fact]
    public void Colunas_casam_com_as_bordas_do_cabecalho_e_dos_quadros()
    {
        var colunas = GradeRelatorio.Colunas;
        Assert.Equal(11, colunas.Count);
        Assert.Equal(GradeRelatorio.Largura, colunas.Sum(), 3);
        Assert.Equal(GradeRelatorio.LarguraDoQuadro(2), colunas.Take(5).Sum(), 3);       // C-G: quadro da esquerda
        Assert.Equal(GradeRelatorio.EspacoEntreColunas, colunas[5], 3);                 // H: espaco entre as fotos
        Assert.Equal(GradeRelatorio.LarguraDoQuadro(2), colunas.Skip(6).Sum(), 3);       // I-M: quadro da direita
        Assert.Equal(GradeRelatorio.LarguraDoRotulo, colunas.Take(2).Sum(), 3);          // rotulo
        Assert.Equal(GradeRelatorio.LarguraDoLogo, colunas.Take(3).Sum(), 3);            // logo
        Assert.Equal(GradeRelatorio.LarguraDoOrgao, colunas.Skip(9).Sum(), 3);           // SPOBRAS
        Assert.All(colunas, c => Assert.True(c > 5));

        var (_, caminho) = Gerar(2, new OpcoesRelatorio(), logo: false);
        var larguras = Parte(caminho, "xl/worksheets/sheet1.xml").Descendants(S + "col").Skip(2)
            .Select(c => double.Parse((string)c.Attribute("width")!, System.Globalization.CultureInfo.InvariantCulture)).ToList();
        Assert.Equal(colunas.Select(c => (int)Math.Round(c / 0.75)), larguras.Select(GradeRelatorio.PixelsNoExcel));
    }

    [Fact]
    public void Campos_vazios_nao_deixam_rotulo_e_descricao_e_assinaturas_aparecem_quando_pedidas()
    {
        var (_, semDados) = Gerar(2, new OpcoesRelatorio(), logo: false);
        var vazio = Textos(Parte(semDados, "xl/worksheets/sheet1.xml"));
        Assert.DoesNotContain("CONTRATADA", vazio);
        Assert.DoesNotContain("UNIDADE EDUCACIONAL", vazio);
        Assert.DoesNotContain("Fiscalização SPOBRAS", vazio);
        Assert.Equal(2, Parte(semDados, "xl/drawings/drawing1.xml").Descendants(Xdr + "pic").Count()); // sem logo

        var opcoes = new OpcoesRelatorio { Modo = ModoLegenda.DescricaoServico, Assinaturas = true, AssinaturaFiscal = "Fulano de Tal", FotosPorPagina = 4 };
        var (_, completo) = Gerar(2, opcoes, new DadosRelatorio { Unidade = "CEI Exemplo", Descricao = "Visita de rotina." });
        var textos = Textos(Parte(completo, "xl/worksheets/sheet1.xml"));
        Assert.Contains("UNIDADE EDUCACIONAL", textos);
        Assert.Contains("DESCRIÇÃO", textos);
        Assert.Contains("Visita de rotina.", textos);
        Assert.Contains("Descrição do serviço executado.", textos);
        Assert.Contains("Fiscalização SPOBRAS", textos);
        Assert.Contains("Contratada", textos);
        Assert.Contains("Fulano de Tal", textos);
    }

    [Fact]
    public void Dados_longos_no_cabecalho_diminuem_os_quadros_sem_esconder_texto()
    {
        string objeto = string.Join(" ", Enumerable.Repeat("reforma e ampliação da unidade educacional", 12));
        var (plano, caminho) = Gerar(6, new OpcoesRelatorio(), new DadosRelatorio { Objeto = objeto, Contrato = "001/EX/2026" }, logo: false);
        var curto = PaginadorRelatorio.Paginar(plano.Paginas[0].Fotos.Select(f => f.Foto).ToList(), new OpcoesRelatorio(), new DadosRelatorio { Contrato = "001/EX/2026" });

        Assert.True(plano.Cabecalho.Campos.Single(c => c.Rotulo == "OBJETO").LinhasDoValor.Count > 1);
        Assert.True(plano.AlturaDoQuadro < curto.AlturaDoQuadro);
        Assert.Equal(6, plano.Paginas[0].Fotos.Count);
        Assert.Contains(objeto, Textos(Parte(caminho, "xl/worksheets/sheet1.xml")));
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
