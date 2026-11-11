using Spobras.Core.Abstracoes;
using Spobras.Core.Operacoes;
using Spobras.Core.Organizar;

namespace Spobras.Core.Tests;

public class OrganizarTests : ComPastaTemporaria
{
    private readonly ArquivosDisco _disco = new();
    private readonly RelogioFixo _relogio = new();

    private IReadOnlyList<Sugestao> Planejar(IEnumerable<string> caminhos, IReadOnlyList<Regra> regras, params string[] protegidos) =>
        new PlanejadorOrganizacao(_disco).Planejar([.. caminhos.Select(c => new ArquivoParaOrganizar(c))], regras, protegidos);

    [Fact]
    public void Sugere_pela_regra_e_mostra_o_motivo()
    {
        var regras = new[]
        {
            new Regra { Nome = "Medições", NomeContem = ["medicao"], Extensoes = ["xlsx", ".xlsm"], Destino = Em("Medições") },
            new Regra { Nome = "Fotos", Extensoes = ["jpg"], Destino = Em("Fotos de obra") },
        };
        string[] arquivos = [Criar("Medição out final (2).xlsx"), Criar("IMG-20261110-WA0007.jpg"), Criar("arquivo.zip"), Criar("medicao.pdf")];

        var s = Planejar(arquivos, regras);

        Assert.Equal(OrigemSugestao.Regra, s[0].Origem);
        Assert.Equal(Em("Medições", "Medição out final (2).xlsx"), s[0].CaminhoDestino);
        Assert.Equal("Planilha Excel", s[0].Tipo);
        Assert.Contains("nome contém \"medicao\"", s[0].Achou);
        Assert.Equal("Regra \"Medições\"", s[0].Motivo);

        Assert.Equal("Foto (WhatsApp)", s[1].Tipo);
        Assert.Equal(Em("Fotos de obra"), s[1].PastaDestino);

        Assert.Equal(OrigemSugestao.VoceDecide, s[2].Origem);   // nenhuma regra
        Assert.Equal(OrigemSugestao.VoceDecide, s[3].Origem);   // nome bate, extensao nao
        Assert.False(s[2].PodeMover);
    }

    [Fact]
    public void Prioridade_decide_e_empate_com_destinos_diferentes_vira_voce_decide()
    {
        string arquivo = Criar("cronograma lote 4.xlsx");
        var geral = new Regra { Nome = "Planilhas", Extensoes = ["xlsx"], Destino = Em("Planilhas") };
        var especifica = new Regra { Nome = "Cronogramas", NomeContem = ["cronograma"], Destino = Em("Cronogramas"), Prioridade = 10 };
        var empatada = new Regra { Nome = "Lotes", NomeContem = ["lote"], Destino = Em("Lotes"), Prioridade = 10 };
        var desligada = new Regra { Nome = "Desligada", NomeContem = ["lote"], Destino = Em("X"), Prioridade = 99, Ativa = false };

        var comPrioridade = Planejar([arquivo], [geral, especifica, desligada]);
        var comEmpate = Planejar([arquivo], [geral, especifica, empatada]);

        Assert.Equal(Em("Cronogramas"), comPrioridade[0].PastaDestino);
        Assert.Equal(OrigemSugestao.VoceDecide, comEmpate[0].Origem);
        Assert.Contains("\"Cronogramas\", \"Lotes\"", comEmpate[0].Achou);
    }

    [Fact]
    public void Protegidos_temporarios_e_ja_no_destino_ficam_de_fora()
    {
        var regra = new Regra { Nome = "Tudo", Extensoes = ["xlsx", "crdownload", "tmp"], Destino = Em("Destino") };
        string mestra = Criar(Path.Combine("Base", "Escolas_Contratos_Com_Relatorio.xlsx"));
        string[] arquivos =
        [
            mestra,
            Criar(Path.Combine("Base", "sub", "outra.xlsx")),
            Criar("baixando.xlsx.crdownload"),
            Criar("~$aberta.xlsx"),
            Criar(Path.Combine("Destino", "ja-esta.xlsx")),
            Criar("Basement.xlsx"),
        ];

        var s = Planejar(arquivos, [regra], Em("Base"));

        Assert.All(s.Take(5), x => Assert.Equal(OrigemSugestao.Ignorado, x.Origem));
        Assert.Contains("protegido", s[0].Motivo);
        Assert.Contains("protegido", s[1].Motivo);
        Assert.Contains("temporário", s[2].Motivo);
        Assert.Contains("temporário", s[3].Motivo);
        Assert.Contains("Já está na pasta", s[4].Motivo);
        Assert.Equal(OrigemSugestao.Regra, s[5].Origem); // "Basement" nao esta dentro de "Base"
    }

    [Fact]
    public void Nome_repetido_no_destino_ganha_sufixo_inclusive_dentro_do_lote()
    {
        var regra = new Regra { Nome = "PDFs", Extensoes = ["pdf"], Destino = Em("Empenhos") };
        Criar(Path.Combine("Empenhos", "Doc.pdf"), "ja estava");
        string[] arquivos = [Criar(Path.Combine("a", "Doc.pdf"), "um"), Criar(Path.Combine("b", "Doc.pdf"), "dois")];

        var s = Planejar(arquivos, [regra]);

        Assert.Equal(Em("Empenhos", "Doc (2).pdf"), s[0].CaminhoDestino);
        Assert.Equal(Em("Empenhos", "Doc (3).pdf"), s[1].CaminhoDestino);
        Assert.Contains("vai como \"Doc (2).pdf\"", s[0].Motivo);
    }

    [Fact]
    public void Destino_por_ano_e_mes_usa_a_chegada_e_depois_a_data_do_arquivo()
    {
        var regra = new Regra { Nome = "PDFs", Extensoes = ["pdf"], Destino = Path.Combine(Pasta, "Pautas", "{ano}", "{mes}") };
        var planejador = new PlanejadorOrganizacao(_disco);
        var chegada = new DateTimeOffset(2026, 3, 5, 9, 0, 0, TimeSpan.Zero);

        var s = planejador.Planejar(
        [
            new ArquivoParaOrganizar(Criar("a.pdf")) { ChegouEm = chegada, ModificadoEm = chegada.AddYears(-2) },
            new ArquivoParaOrganizar(Criar("b.pdf")) { ModificadoEm = chegada.AddYears(-2) },
            new ArquivoParaOrganizar(Criar("c.pdf")),
        ], [regra], []);

        Assert.Equal(Em("Pautas", "2026", "03"), s[0].PastaDestino);
        Assert.Equal(Em("Pautas", "2024", "03"), s[1].PastaDestino);
        Assert.Equal(Em("Pautas", "sem data", "sem data"), s[2].PastaDestino);
    }

    [Fact]
    public void Validar_regras_impede_destino_em_pasta_vigiada_e_avisa_regra_sem_condicao()
    {
        var avisos = PlanejadorOrganizacao.ValidarRegras(
        [
            new Regra { Nome = "Volta", Extensoes = ["pdf"], Destino = Em("Downloads") + Path.DirectorySeparatorChar },
            new Regra { Nome = "Aberta", Destino = Em("Tudo") },
            new Regra { Nome = "Certa", Extensoes = ["pdf"], Destino = Em("Downloads", "Organizados") },
        ], [Em("Downloads")]);

        Assert.Equal(2, avisos.Count);
        Assert.Contains(avisos, a => a.Gravidade == Gravidade.ImpedeGerar && a.Mensagem.Contains("\"Volta\""));
        Assert.Contains(avisos, a => a.Gravidade == Gravidade.PrecisaConferir && a.Mensagem.Contains("\"Aberta\""));
    }

    [Fact]
    public void Mover_os_confirmados_cria_a_pasta_registra_e_pode_ser_desfeito()
    {
        var regra = new Regra { Nome = "Medições", Extensoes = ["xlsx"], Destino = Em("Medições") };
        string[] arquivos = [Criar("m1.xlsx", "um"), Criar("m2.xlsx", "dois"), Criar("solto.zip")];
        var planejador = new PlanejadorOrganizacao(_disco);
        var sugestoes = Planejar(arquivos, [regra]);
        // O usuario escolhe a pasta do arquivo sem regra.
        var escolhida = planejador.ComDestinoEscolhido(sugestoes[2], Em("Diversos"));

        var historico = new HistoricoMemoria();
        var lote = PlanejadorOrganizacao.CriarLote([.. sugestoes.Where(s => s.PodeMover), escolhida], _relogio);
        historico.CriarLote(lote);
        var executor = new ExecutorLote(_disco, historico, _relogio);
        executor.Executar(lote);

        Assert.Equal("Moveu 3 arquivo(s) para 2 pastas", lote.Descricao);
        Assert.Equal(["m1.xlsx", "m2.xlsx"], Nomes("Medições"));
        Assert.Equal(["solto.zip"], Nomes("Diversos"));
        Assert.Empty(Nomes());
        Assert.Equal("Você escolheu", escolhida.Motivo);
        Assert.Null(escolhida.RegraId); // escolha isolada nao vira regra

        var desfazer = new Desfazer(_disco, _relogio);
        var volta = desfazer.CriarLote(desfazer.Planejar(lote));
        historico.CriarLote(volta);
        executor.Executar(volta);

        Assert.Equal(["m1.xlsx", "m2.xlsx", "solto.zip"], Nomes());
        Assert.Equal("um", File.ReadAllText(Em("m1.xlsx")));
    }

    // ---- T06 (parte de logica; a vigia real do Windows entra no passo 3) ----

    [Fact]
    public void T06_arquivo_em_gravacao_so_fica_pronto_quando_para_de_mudar()
    {
        var fila = new FilaChegada(TimeSpan.FromSeconds(3));
        var t0 = _relogio.Agora;
        string caminho = Em("relatorio.pdf");

        fila.Observar(caminho, 100, t0, t0, chegouAgora: true);
        fila.Observar(caminho, 5_000, t0.AddSeconds(2), t0.AddSeconds(2), chegouAgora: true); // ainda baixando
        Assert.Empty(fila.Prontos(t0.AddSeconds(4), _ => true));

        var prontos = fila.Prontos(t0.AddSeconds(5), _ => true);

        Assert.Single(prontos);
        Assert.Equal(t0, prontos[0].ChegouEm); // chegada observada pelo app, nao a data do arquivo
        Assert.Empty(fila.Prontos(t0.AddSeconds(60), _ => true)); // um registro por arquivo
    }

    [Fact]
    public void T06_eventos_repetidos_temporarios_e_arquivo_ocupado()
    {
        var fila = new FilaChegada(TimeSpan.FromSeconds(3));
        var t0 = _relogio.Agora;
        for (int i = 0; i < 5; i++)
        {
            fila.Observar(Em("a.xlsx"), 10, t0, t0.AddMilliseconds(i * 100), chegouAgora: true); // mesmo evento repetido
        }

        fila.Observar(Em("b.pdf.crdownload"), 10, t0, t0, chegouAgora: true);
        fila.Observar(Em("~$a.xlsx"), 10, t0, t0, chegouAgora: true);
        Assert.Equal(1, fila.Aguardando);

        // Ocupado pelo Excel: nao entrega, nao perde; entrega na rodada seguinte.
        Assert.Empty(fila.Prontos(t0.AddSeconds(10), _ => false));
        Assert.Single(fila.Prontos(t0.AddSeconds(11), _ => true));
    }

    [Fact]
    public void T06_varredura_ao_iniciar_recupera_pendencias_com_chegada_desconhecida_e_regravacao_volta_para_a_fila()
    {
        var fila = new FilaChegada(TimeSpan.FromSeconds(3));
        var t0 = _relogio.Agora;

        fila.Observar(Em("antigo.pdf"), 10, t0.AddDays(-30), t0, chegouAgora: false);
        var prontos = fila.Prontos(t0.AddSeconds(5), _ => true);
        Assert.Null(prontos[0].ChegouEm);

        // Uma nova varredura com o mesmo estado nao repete; arquivo salvo de novo volta.
        fila.Observar(Em("antigo.pdf"), 10, t0.AddDays(-30), t0.AddSeconds(6), chegouAgora: false);
        Assert.Empty(fila.Prontos(t0.AddSeconds(20), _ => true));
        fila.Observar(Em("antigo.pdf"), 99, t0.AddSeconds(21), t0.AddSeconds(21), chegouAgora: false);
        Assert.Single(fila.Prontos(t0.AddSeconds(30), _ => true));

        fila.Esquecer(Em("antigo.pdf"));
        Assert.Equal(0, fila.Aguardando);
    }

    [Theory]
    [InlineData("planilha.XLSX", "Planilha Excel")]
    [InlineData("doc.pdf", "PDF")]
    [InlineData("VID-20260101-WA0001.mp4", "Vídeo (WhatsApp)")]
    [InlineData("arquivo.xyz", "Arquivo .xyz")]
    [InlineData("LEIAME", "Arquivo sem extensão")]
    public void Descreve_o_tipo_pelo_nome(string nome, string esperado)
    {
        Assert.Equal(esperado, TipoArquivo.Descrever(nome));
    }
}

public class VigiaPastaTests : ComPastaTemporaria
{
    private readonly RelogioFixo _relogio = new();

    private static async Task<bool> Ate(Func<bool> condicao)
    {
        for (int i = 0; i < 100 && !condicao(); i++)
        {
            await Task.Delay(50);
        }

        return condicao();
    }

    [Fact]
    public void T06_so_avisa_do_que_chega_depois_e_nao_repete()
    {
        Criar("ja-estava.pdf");
        var vistos = new List<string>();
        using var vigia = new VigiaPasta(Pasta, _relogio, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(10));
        vigia.Novos += n => vistos.AddRange(n.Select(a => Path.GetFileName(a.Caminho)));

        vigia.Rodada(); // primeira varredura: cataloga o que ja existia
        Criar("novo.pdf", "conteudo");
        Criar("baixando.pdf.crdownload");
        _relogio.Agora = _relogio.Agora.AddSeconds(1);
        vigia.Rodada();
        Assert.Empty(vistos); // ainda nao ficou quieto pelo tempo pedido

        // Chega pelo evento do sistema ou, se ele se perder, pela varredura seguinte.
        _relogio.Agora = _relogio.Agora.AddSeconds(11);
        vigia.Rodada();
        _relogio.Agora = _relogio.Agora.AddSeconds(4);
        vigia.Rodada();
        Assert.Equal(["novo.pdf"], vistos);

        // Salvar de novo um arquivo ja avisado nao vira novidade outra vez.
        File.WriteAllText(Em("novo.pdf"), "salvo de novo, maior");
        _relogio.Agora = _relogio.Agora.AddSeconds(30);
        vigia.Rodada();
        _relogio.Agora = _relogio.Agora.AddSeconds(5);
        vigia.Rodada();
        Assert.Equal(["novo.pdf"], vistos);
    }

    [Fact]
    public void T06_ao_reiniciar_recupera_o_que_chegou_com_o_app_fechado()
    {
        Criar("antigo.pdf");
        var fechouEm = DateTimeOffset.UtcNow.AddSeconds(-1);
        File.SetCreationTimeUtc(Em("antigo.pdf"), fechouEm.UtcDateTime.AddDays(-3));
        File.SetLastWriteTimeUtc(Em("antigo.pdf"), fechouEm.UtcDateTime.AddDays(-3));
        Criar("chegou-com-o-app-fechado.pdf");
        var vistos = new List<ArquivoParaOrganizar>();
        using var vigia = new VigiaPasta(Pasta, _relogio, TimeSpan.FromSeconds(3)) { NovidadesDesde = fechouEm };
        vigia.Novos += vistos.AddRange;

        vigia.Rodada();
        _relogio.Agora = _relogio.Agora.AddSeconds(4);
        vigia.Rodada();

        // No Linux a data de criacao nao e ajustavel; la os dois contam como chegados depois.
        Assert.Contains(vistos, a => Path.GetFileName(a.Caminho) == "chegou-com-o-app-fechado.pdf" && a.ChegouEm is null);
        if (OperatingSystem.IsWindows())
        {
            Assert.Single(vistos);
        }
    }

    [Fact]
    public async Task T06_evento_do_sistema_e_download_que_troca_de_nome()
    {
        var relogio = new Spobras.Core.Abstracoes.RelogioSistema();
        var vistos = new List<string>();
        using var vigia = new VigiaPasta(Pasta, relogio, TimeSpan.FromMilliseconds(300), TimeSpan.FromHours(1));
        vigia.Novos += n =>
        {
            lock (vistos)
            {
                vistos.AddRange(n.Select(a => Path.GetFileName(a.Caminho)));
            }
        };
        vigia.Iniciar();
        await Task.Delay(300);

        Criar("relatorio.pdf.crdownload", "baixando");
        await Task.Delay(200);
        File.Move(Em("relatorio.pdf.crdownload"), Em("relatorio.pdf")); // o navegador terminou o download

        Assert.True(await Ate(() => { lock (vistos) { return vistos.Count > 0; } }));
        lock (vistos)
        {
            Assert.Equal(["relatorio.pdf"], vistos);
        }
    }

    [Fact]
    public void Pasta_inexistente_nao_derruba_a_vigia()
    {
        using var vigia = new VigiaPasta(Em("nao-existe"), _relogio);
        vigia.Rodada();
        vigia.Iniciar();
    }
}
