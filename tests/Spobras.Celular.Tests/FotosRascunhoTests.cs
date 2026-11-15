using Spobras.Celular.Servicos;
using Spobras.Testes;

namespace Spobras.Celular.Tests;

public sealed class FotosRascunhoTests : IDisposable
{
    private readonly string _raiz = Directory.CreateTempSubdirectory("spobras-fotos-").FullName;

    // As miniaturas sao feitas em segundo plano: a limpeza tenta de novo se ainda houver uma sendo gravada.
    public void Dispose()
    {
        for (int tentativa = 0; ; tentativa++)
        {
            try
            {
                Directory.Delete(_raiz, recursive: true);
                return;
            }
            catch (IOException) when (tentativa < 20)
            {
                Thread.Sleep(100);
            }
        }
    }

    private RascunhoRelatorio NovoComFotos(int n)
    {
        var r = new RascunhoRelatorio(Path.Combine(_raiz, "app"));
        var caminhos = new List<string>();
        for (int i = 1; i <= n; i++)
        {
            string foto = AmostraCelular.Foto(r.PastaDasFotos, $"f{i:00}.jpg", i % 2 == 0 ? 600 : 800, i % 2 == 0 ? 800 : 600, i);
            caminhos.Add(foto);
        }

        r.AdicionarCopiadas(caminhos);
        for (int i = 0; i < n; i++)
        {
            r.Legendar(i, $"L{i + 1}");
        }

        return r;
    }

    private static string Ordem(RascunhoRelatorio r) => string.Join(",", r.Dados.Fotos.Select(f => f.Legenda));

    [Fact]
    public void Desfazer_tres_remocoes_seguidas_devolve_cada_foto_para_onde_estava()
    {
        var r = NovoComFotos(8);
        Assert.Equal(1, r.Remover(2));          // L3
        Assert.Equal(2, r.Remover(4));          // L6 (agora na posicao 4)
        Assert.Equal(3, r.Remover(5));          // L8
        Assert.Equal("L1,L2,L4,L5,L7", Ordem(r));
        Assert.Equal(3, r.Desfazer());
        Assert.Equal("L1,L2,L3,L4,L5,L6,L7,L8", Ordem(r));
        Assert.Empty(r.Dados.Removidas);
    }

    [Fact]
    public void Remover_varias_marcadas_e_desfazer_devolve_todas_na_ordem()
    {
        var r = NovoComFotos(8);
        Assert.Equal(3, r.Remover([1, 3, 4]));
        Assert.Equal("L1,L3,L6,L7,L8", Ordem(r));
        r.Desfazer();
        Assert.Equal("L1,L2,L3,L4,L5,L6,L7,L8", Ordem(r));

        // legenda, giro e descricao voltam junto
        r.Girar(0);
        r.Remover(0);
        r.Desfazer();
        Assert.Equal(90, r.Dados.Fotos[0].Rotacao);
        Assert.Equal("L1", r.Dados.Fotos[0].Legenda);
    }

    [Fact]
    public void Tempo_acabado_apaga_so_as_copias_e_fechar_o_app_no_meio_conclui_ao_abrir()
    {
        var r = NovoComFotos(3);
        string a = r.Caminho(r.Dados.Fotos[0]), b = r.Caminho(r.Dados.Fotos[1]);
        r.Remover(0);
        Assert.True(File.Exists(a));
        r.FinalizarRemocoes();
        Assert.False(File.Exists(a));
        Assert.Equal(0, r.Desfazer()); // depois do tempo nao volta mais

        r.Remover(0); // L2, e o app "fecha" aqui
        var reaberto = new RascunhoRelatorio(Path.Combine(_raiz, "app"));
        Assert.False(File.Exists(b));
        Assert.Empty(reaberto.Dados.Removidas);
        Assert.Equal("L3", Ordem(reaberto));
    }

    [Fact]
    public async Task Miniaturas_sao_feitas_em_segundo_plano_e_guardadas_em_arquivo()
    {
        var r = NovoComFotos(6);
        await r.MiniaturasProntasAsync(TimeSpan.FromSeconds(20));
        Assert.All(r.Dados.Fotos, f => Assert.StartsWith("data:image/jpeg;base64,", r.Miniatura(f)));
        Assert.Equal(6, Directory.GetFiles(r.PastaDasMiniaturas, "*.jpg").Length);

        // outra abertura do app le do arquivo, na hora
        var reaberto = new RascunhoRelatorio(Path.Combine(_raiz, "app"));
        var relogio = System.Diagnostics.Stopwatch.StartNew();
        Assert.All(reaberto.Dados.Fotos, f => Assert.NotEmpty(reaberto.Miniatura(f)));
        Assert.True(relogio.ElapsedMilliseconds < 500, $"{relogio.ElapsedMilliseconds} ms");

        // girar pede outra miniatura; a antiga continua no disco
        reaberto.Girar(0);
        Assert.Equal("", reaberto.Miniatura(reaberto.Dados.Fotos[0]));
        await reaberto.MiniaturasProntasAsync(TimeSpan.FromSeconds(10));
        Assert.NotEmpty(reaberto.Miniatura(reaberto.Dados.Fotos[0]));
    }

    [Fact]
    public void Tamanho_guardado_ao_entrar_e_conferir_nao_rele_as_fotos()
    {
        var r = NovoComFotos(2);
        Assert.Equal((800, 600), (r.Dados.Fotos[0].Largura, r.Dados.Fotos[0].Altura));
        Assert.Equal((600, 800), (r.Dados.Fotos[1].Largura, r.Dados.Fotos[1].Altura));
        r.Girar(0);
        var plano = r.Plano();
        Assert.Equal((600, 800), (plano.Paginas[0].Fotos[0].Foto.Largura, plano.Paginas[0].Fotos[0].Foto.Altura));

        // foto da camera tambem guarda o tamanho
        string cam = AmostraCelular.Foto(Path.Combine(_raiz, "cam"), "c.jpg", 640, 480, 9);
        r.AdicionarFoto(File.ReadAllBytes(cam));
        Assert.Equal((640, 480), (r.Dados.Fotos[^1].Largura, r.Dados.Fotos[^1].Altura));
    }

    [Fact]
    public void Legenda_em_lotes_preserva_cada_grupo()
    {
        var r = NovoComFotos(6);
        r.LegendarVarias([0, 1, 2], "Troca de telha");
        r.LegendarVarias([3, 4, 5], "Montagem da estrutura metálica");
        Assert.Equal("Troca de telha,Troca de telha,Troca de telha,Montagem da estrutura metálica,Montagem da estrutura metálica,Montagem da estrutura metálica", Ordem(r));
    }

    [Fact]
    public async Task Fotos_seguidas_da_camera_ao_mesmo_tempo_nenhuma_se_perde()
    {
        var r = new RascunhoRelatorio(Path.Combine(_raiz, "app"));
        byte[] jpeg = File.ReadAllBytes(AmostraCelular.Foto(Path.Combine(_raiz, "cam"), "c.jpg", 640, 480, 1));
        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => r.AdicionarFoto(jpeg))));
        Assert.Equal(20, r.Dados.Fotos.Count);
        Assert.Equal(20, r.Dados.Fotos.Select(f => f.Arquivo).Distinct().Count());
        Assert.Equal(20, new RascunhoRelatorio(Path.Combine(_raiz, "app")).Dados.Fotos.Count);
    }

    [Fact]
    public void Sobras_de_copias_sao_limpas_ao_abrir_e_as_fotos_do_rascunho_ficam()
    {
        var r = NovoComFotos(2);
        string sobra = Path.Combine(r.PastaDasFotos, "sobra-de-remocao.jpg");
        File.WriteAllBytes(sobra, [1, 2, 3]);
        File.WriteAllBytes(Path.Combine(r.PastaDasMiniaturas, "sobra-de-remocao-0.jpg"), [1]);
        var reaberto = new RascunhoRelatorio(Path.Combine(_raiz, "app"));
        Assert.False(File.Exists(sobra));
        Assert.Empty(Directory.GetFiles(reaberto.PastaDasMiniaturas, "sobra*"));
        Assert.All(reaberto.Dados.Fotos, f => Assert.True(File.Exists(reaberto.Caminho(f))));
    }
}
