using System.IO.Compression;
using Spobras.Core;
using Spobras.Core.Abstracoes;
using Spobras.Core.Compactar;
using Spobras.Documentos.Zip;

namespace Spobras.Documentos.Tests;

public class ZipTests : IDisposable
{
    private readonly string _pasta = Directory.CreateTempSubdirectory("spobras-zip-").FullName;
    private readonly PlanejadorZip _planejador = new(new ArquivosDisco());

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    private string Em(params string[] partes) => Path.Combine([_pasta, .. partes]);

    private string Criar(string relativo, int bytes, bool aleatorio = true)
    {
        string caminho = Em(relativo);
        Directory.CreateDirectory(Path.GetDirectoryName(caminho)!);
        var dados = new byte[bytes];
        if (aleatorio)
        {
            new Random(relativo.GetHashCode(StringComparison.Ordinal)).NextBytes(dados); // nao comprime, como foto e PDF
        }

        File.WriteAllBytes(caminho, dados);
        return caminho;
    }

    private static List<string> NomesNoZip(string caminho)
    {
        using var zip = ZipFile.OpenRead(caminho);
        return [.. zip.Entries.Select(e => e.FullName)];
    }

    // ---- T07 ----

    [Fact]
    public void T07_um_zip_por_pasta_com_o_nome_da_pasta()
    {
        Criar(Path.Combine("Pautas", "p1.pdf"), 2_000);
        Criar(Path.Combine("Pautas", "sub", "p2.pdf"), 3_000);
        Criar(Path.Combine("Pautas", "~$aberto.xlsx"), 10);
        Criar(Path.Combine("Empenhos", "e1.pdf"), 1_000);
        Directory.CreateDirectory(Em("Vazia"));
        Directory.CreateDirectory(Em("Saida"));
        File.WriteAllText(Em("Saida", "Empenhos.zip"), "ja existia");

        var plano = _planejador.UmPorPasta([Em("Pautas"), Em("Empenhos"), Em("Vazia")], Em("Saida"));

        Assert.True(plano.PodeExecutar);
        Assert.Equal(["Pautas.zip", "Empenhos (2).zip"], plano.Zips.Select(z => z.NomeZip));
        Assert.Contains(plano.Avisos, a => a.Mensagem.Contains("\"Vazia\" está vazia"));
        Assert.Contains(plano.Avisos, a => a.Mensagem.Contains("Já existe \"Empenhos.zip\""));

        var criados = plano.Zips.Select(z => Compactador.Criar(z)).ToList();

        Assert.Equal(["p1.pdf", "sub/p2.pdf"], NomesNoZip(criados[0].Caminho)); // temporario do Office fica de fora
        Assert.Equal("ja existia", File.ReadAllText(Em("Saida", "Empenhos.zip")));
        Assert.Equal(2_000, new FileInfo(Em("Pautas", "p1.pdf")).Length); // originais preservados
        Assert.DoesNotContain(Directory.GetFiles(Em("Saida")), f => f.EndsWith(Compactador.SufixoParcial));
    }

    [Fact]
    public void T07_tudo_num_zip_so_sem_sobrepor_nomes_iguais()
    {
        string a = Criar(Path.Combine("a", "foto.jpg"), 500);
        string b = Criar(Path.Combine("b", "foto.jpg"), 700);
        Criar(Path.Combine("Fotos", "1.jpg"), 100);

        var plano = _planejador.Unico([a, b, Em("Fotos")], "Envio", Em("Saida"));
        var criado = Compactador.Criar(plano.Zips[0]);

        Assert.Equal(Em("Saida", "Envio.zip"), criado.Caminho);
        Assert.Equal(["foto.jpg", "foto (2).jpg", "Fotos/1.jpg"], NomesNoZip(criado.Caminho));
        using var zip = ZipFile.OpenRead(criado.Caminho);
        Assert.Equal(700, zip.GetEntry("foto (2).jpg")!.Length);
    }

    [Fact]
    public void T07_zips_independentes_respeitam_o_limite_no_tamanho_final()
    {
        var arquivos = Enumerable.Range(1, 10).Select(n => Criar(Path.Combine("Fotos", $"foto {n}.jpg"), 300_000)).ToList();
        const long limite = 1_000_000;

        var plano = _planejador.PorLimite(arquivos, "Fotos da obra", limite, Em("Saida"));

        Assert.True(plano.PodeExecutar);
        Assert.Equal(4, plano.Zips.Count); // 3 + 3 + 3 + 1 fotos de 300 KB em partes de 1 MB
        Assert.Equal("Fotos da obra - parte 01.zip", plano.Zips[0].NomeZip);
        Assert.Equal("foto 1.jpg", plano.Zips[0].Entradas[0].NomeNoZip); // ordem preservada
        Assert.Equal("foto 10.jpg", plano.Zips[3].Entradas[0].NomeNoZip);

        foreach (var zip in plano.Zips)
        {
            var criado = Compactador.Criar(zip, limite);
            Assert.True(criado.Tamanho <= limite, $"{zip.NomeZip} ficou com {criado.Tamanho} bytes");
            Assert.True(criado.Tamanho <= zip.TamanhoMaximoEstimado);
            using var aberto = ZipFile.OpenRead(criado.Caminho); // cada zip abre sozinho
            Assert.Equal(zip.Entradas.Count, aberto.Entries.Count);
        }

        Assert.Equal(10, plano.Zips.Sum(z => z.Entradas.Count));
    }

    [Fact]
    public void T07_arquivo_que_nao_cabe_no_limite_impede_e_e_apontado()
    {
        string[] arquivos = [Criar("pequeno.pdf", 1_000), Criar("video grande.mp4", 2_000_000)];

        var plano = _planejador.PorLimite(arquivos, "Envio", 1_000_000, Em("Saida"));

        Assert.False(plano.PodeExecutar);
        Assert.Empty(plano.Zips);
        Assert.Equal("video grande.mp4", Assert.Single(plano.AcimaDoLimite).NomeNoZip);
        Assert.Contains(plano.Avisos, a => a.Gravidade == Gravidade.ImpedeGerar && a.Mensagem.Contains("não cabe sozinho"));
        Assert.False(Directory.Exists(Em("Saida")));
    }

    [Fact]
    public void T07_nao_sobrescreve_zip_que_apareceu_depois_e_limite_estourado_nao_publica()
    {
        string a = Criar("a.bin", 50_000);
        var plano = _planejador.Unico([a], "Envio.zip", Em("Saida"));
        Directory.CreateDirectory(Em("Saida"));

        var erroLimite = Assert.Throws<InvalidOperationException>(() => Compactador.Criar(plano.Zips[0], limiteBytes: 10_000));
        Assert.Contains("acima do limite", erroLimite.Message);
        Assert.Empty(Directory.GetFiles(Em("Saida")));

        File.WriteAllText(Em("Saida", "Envio.zip"), "apareceu depois");
        Assert.Throws<IOException>(() => Compactador.Criar(plano.Zips[0]));
        Assert.Equal("apareceu depois", File.ReadAllText(Em("Saida", "Envio.zip")));
    }

    [Fact]
    public void T07_cancelar_nao_deixa_zip_com_nome_final()
    {
        var arquivos = Enumerable.Range(1, 3).Select(n => Criar($"f{n}.bin", 1_000)).ToList();
        var plano = _planejador.Unico(arquivos, "Envio", Em("Saida"));
        using var cancelamento = new CancellationTokenSource();
        var cancelaNoPrimeiro = new Aviso1(() => cancelamento.Cancel());

        Assert.Throws<OperationCanceledException>(() => Compactador.Criar(plano.Zips[0], null, cancelamento.Token, cancelaNoPrimeiro));

        Assert.Empty(Directory.GetFiles(Em("Saida")));
    }

    [Fact]
    public void Plano_sem_entradas_ou_sem_limite_impede()
    {
        Assert.False(_planejador.Unico([], "x", _pasta).PodeExecutar);
        Assert.False(_planejador.UmPorPasta([], _pasta).PodeExecutar);
        Assert.False(_planejador.PorLimite([Criar("a.bin", 10)], "x", 0, _pasta).PodeExecutar);
        Assert.False(_planejador.Unico([Criar("b.bin", 10)], "nome:ruim", _pasta).PodeExecutar);
    }

    // ---- T08 ----

    [Fact]
    public void T08_extrai_em_pasta_propria_e_trata_colisao_de_pasta()
    {
        string a = Criar(Path.Combine("Fotos", "1.jpg"), 100);
        var zip = Compactador.Criar(_planejador.Unico([Em("Fotos"), a], "Pacote", _pasta).Zips[0]);
        Directory.CreateDirectory(Em("Saida", "Pacote"));
        File.WriteAllText(Em("Saida", "Pacote", "meu.txt"), "do usuario");

        var extraido = Descompactador.Extrair(zip.Caminho, Em("Saida"));

        Assert.Equal(Em("Saida", "Pacote (2)"), extraido.Pasta);
        Assert.Equal(2, extraido.Arquivos);
        Assert.True(File.Exists(Em("Saida", "Pacote (2)", "Fotos", "1.jpg")));
        Assert.True(File.Exists(Em("Saida", "Pacote (2)", "1.jpg")));
        Assert.Equal("do usuario", File.ReadAllText(Em("Saida", "Pacote", "meu.txt")));
    }

    [Theory]
    [InlineData("../fora.txt")]
    [InlineData("pasta/../../fora.txt")]
    [InlineData("/etc/fora.txt")]
    [InlineData("C:/Windows/fora.txt")]
    [InlineData("..\\fora.txt")]
    public void T08_caminho_que_sai_da_pasta_e_recusado_sem_extrair_nada(string nomeMalicioso)
    {
        string caminhoZip = Em("malicioso.zip");
        using (var zip = ZipFile.Open(caminhoZip, ZipArchiveMode.Create))
        {
            Escrever(zip, "ok.txt", "bom");
            Escrever(zip, nomeMalicioso, "ruim");
        }

        var erro = Assert.Throws<InvalidDataException>(() => Descompactador.Extrair(caminhoZip, Em("Saida")));

        Assert.Contains("sairia da pasta de destino", erro.Message);
        Assert.False(Directory.Exists(Em("Saida", "malicioso")));
        Assert.False(File.Exists(Em("fora.txt")));
        Assert.False(File.Exists(Em("Saida", "fora.txt")));
    }

    [Fact]
    public void T08_grande_expansao_e_barrada_pelo_declarado_e_pelo_real()
    {
        string caminhoZip = Em("bomba.zip");
        using (var zip = ZipFile.Open(caminhoZip, ZipArchiveMode.Create))
        {
            Escrever(zip, "zeros.bin", new string('0', 5_000_000)); // comprime para poucos KB
        }

        Assert.True(new FileInfo(caminhoZip).Length < 50_000);
        var erro = Assert.Throws<InvalidDataException>(() =>
            Descompactador.Extrair(caminhoZip, Em("Saida"), new LimitesExtracao { TamanhoTotal = 1_000_000 }));
        Assert.Contains("Nada foi extraído", erro.Message);
        Assert.False(Directory.Exists(Em("Saida", "bomba")));

        var muitos = Assert.Throws<InvalidDataException>(() =>
            Descompactador.Extrair(caminhoZip, Em("Saida"), new LimitesExtracao { Arquivos = 0 }));
        Assert.Contains("o limite é 0", muitos.Message);
    }

    [Fact]
    public void T08_nomes_repetidos_dentro_do_zip_nao_se_sobrescrevem()
    {
        string caminhoZip = Em("repetido.zip");
        using (var zip = ZipFile.Open(caminhoZip, ZipArchiveMode.Create))
        {
            Escrever(zip, "doc.txt", "primeiro");
            Escrever(zip, "doc.txt", "segundo");
        }

        var extraido = Descompactador.Extrair(caminhoZip, Em("Saida"));

        Assert.Equal("primeiro", File.ReadAllText(Path.Combine(extraido.Pasta, "doc.txt")));
        Assert.Equal("segundo", File.ReadAllText(Path.Combine(extraido.Pasta, "doc (2).txt")));
    }

    private static void Escrever(ZipArchive zip, string nome, string conteudo)
    {
        using var escritor = new StreamWriter(zip.CreateEntry(nome).Open());
        escritor.Write(conteudo);
    }

    private sealed class Aviso1(Action aoPrimeiro) : IProgress<int>
    {
        public void Report(int value)
        {
            if (value == 1)
            {
                aoPrimeiro();
            }
        }
    }
}
