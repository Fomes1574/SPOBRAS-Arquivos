using System.IO.Compression;
using Spobras.App.Servicos;
using Spobras.App.ViewModels;
using Spobras.Core.Organizar;
using Spobras.Core.Renomear;
using Spobras.Dados.Configuracao;

namespace Spobras.App.Tests;

public class RenomearVmTests : ComApp
{
    [Fact]
    public async Task Escolhe_pasta_mostra_previa_renomeia_e_desfaz()
    {
        foreach (string n in new[] { "scan 10.pdf", "scan 2.pdf", "scan 1.pdf", "~$aberto.xlsx" })
        {
            Criar(Path.Combine("Downloads", n), n);
        }

        var vm = NovoRenomear();
        Dialogos.Pastas.Enqueue(Trabalho);
        vm.EscolherPastaCommand.Execute(null);
        vm.NomeBase = "Relatório";

        Assert.Equal("3 arquivos escolhidos", vm.TextoEntrada); // temporario do Office fica de fora
        Assert.Equal($"De: {Trabalho}", vm.PastaEntrada);
        Assert.Equal(["scan 1.pdf", "scan 2.pdf", "scan 10.pdf"], vm.Linhas.Select(l => l.NomeAtual));
        Assert.Equal(["Relatório 01.pdf", "Relatório 02.pdf", "Relatório 03.pdf"], vm.Linhas.Select(l => l.NomeNovo));
        Assert.Equal("Renomear 3 arquivos", vm.TextoBotao);
        Assert.Equal("3 válidos", vm.Contagem);

        await vm.RenomearCommand.ExecuteAsync(null);

        Assert.Equal("3 arquivos renomeados.", vm.Resultado);
        Assert.Empty(vm.Linhas); // novo trabalho comeca vazio
        Assert.Equal(["Relatório 01.pdf", "Relatório 02.pdf", "Relatório 03.pdf", "~$aberto.xlsx"], Nomes("Downloads"));
        Assert.Equal("scan 10.pdf", File.ReadAllText(Em("Downloads", "Relatório 03.pdf")));
        Assert.True(vm.PodeDesfazerUltima);

        await vm.DesfazerUltimaCommand.ExecuteAsync(null);

        Assert.Equal("Desfeito: 3 arquivos voltaram ao nome anterior.", vm.Resultado);
        Assert.Equal(["scan 1.pdf", "scan 10.pdf", "scan 2.pdf", "~$aberto.xlsx"], Nomes("Downloads"));
        Assert.False(vm.PodeDesfazerUltima);
    }

    [Fact]
    public void Nome_editado_na_previa_complemento_e_troca_de_modo()
    {
        var vm = NovoRenomear();
        vm.Adicionar([Criar(Path.Combine("Downloads", "a.pdf")), Criar(Path.Combine("Downloads", "b.pdf"))]);
        vm.NomeBase = "Muros";

        vm.Linhas[0].NomeNovo = "Capa: final"; // digitado na previa, com caractere proibido
        Assert.True(vm.Linhas[0].Impedido);
        Assert.Contains("Sugestão: \"Capa- final.pdf\"", vm.Linhas[0].Motivo);
        Assert.Equal("1 válido · 1 impedido", vm.Contagem);
        Assert.Equal("Renomear 1 arquivo", vm.TextoBotao);

        vm.VoltarAoSugeridoCommand.Execute(vm.Linhas[0]);
        Assert.Equal("Muros 01.pdf", vm.Linhas[0].NomeNovo);

        vm.Modo = ModoRenomear.NomeComplemento;
        Assert.True(vm.ModoComplemento);
        vm.Linhas[0].Complemento = "Lote 3";
        Assert.Equal(["Muros Lote 3.pdf", "Muros.pdf"], vm.Linhas.Select(l => l.NomeNovo));

        vm.Modo = ModoRenomear.NomesLivres;
        Dialogos.AreaDeTransferencia = "Pauta de novembro\r\nAta\r\nSobrando\r\n";
        vm.ColarNomesCommand.Execute(null);
        Assert.Equal(["Pauta de novembro.pdf", "Ata.pdf"], vm.Linhas.Select(l => l.NomeNovo));
        Assert.Contains(vm.Avisos, a => a.Texto.Contains("3 nomes e há 2 arquivos"));
    }

    [Fact]
    public void Ordem_manual_ao_mover_linha_e_remover()
    {
        var vm = NovoRenomear();
        vm.Adicionar([Criar(Path.Combine("Downloads", "a.pdf")), Criar(Path.Combine("Downloads", "b.pdf")), Criar(Path.Combine("Downloads", "c.pdf"))]);
        vm.NomeBase = "Foto";

        vm.SubirCommand.Execute(vm.Linhas[2]);

        Assert.Equal(OrdemRenomear.Manual, vm.Ordem);
        Assert.Equal(["a.pdf", "c.pdf", "b.pdf"], vm.Linhas.Select(l => l.NomeAtual));
        Assert.Equal("Foto 02.pdf", vm.Linhas[1].NomeNovo);

        vm.RemoverCommand.Execute(vm.Linhas[0]);
        Assert.Equal(["Foto 01.pdf", "Foto 02.pdf"], vm.Linhas.Select(l => l.NomeNovo));
        vm.LimparCommand.Execute(null);
        Assert.False(vm.TemLinhas);
        Assert.False(vm.RenomearCommand.CanExecute(null) && vm.TextoBotao != "Renomear");
    }

    [Fact]
    public void Perfil_salvo_e_aplicado_em_outra_abertura_da_tela()
    {
        var vm = NovoRenomear();
        vm.NomeBase = "Medição";
        vm.Separador = " - ";
        vm.NumeroInicial = "7";
        vm.ZeroEsquerda = false;
        Dialogos.Textos.Enqueue("Medições mensais");
        vm.SalvarPerfilCommand.Execute(null);

        var outra = NovoRenomear();
        Assert.Equal("Medições mensais", Assert.Single(outra.Perfis).Nome);
        outra.Adicionar([Criar(Path.Combine("Downloads", "x.pdf"))]);
        outra.PerfilSelecionado = outra.Perfis[0];

        Assert.Equal("Medição", outra.NomeBase);
        Assert.Equal("Medição - 7.pdf", outra.Linhas[0].NomeNovo);

        Dialogos.Confirmacoes.Enqueue(true);
        outra.RemoverPerfilCommand.Execute(null);
        Assert.Empty(NovoRenomear().Perfis);
    }
}

public class OrganizarVmTests : ComApp
{
    private void GravarRegra(string nome, string extensao, string pasta) =>
        Novidades.GravarRegras([.. Novidades.Regras, new Regra { Nome = nome, Extensoes = [extensao], Destino = Em(pasta) }]);

    [Fact]
    public async Task Procurar_de_novo_sugere_move_os_confirmados_e_desfaz()
    {
        GravarRegra("Planilhas", "xlsx", "Medições");
        Criar(Path.Combine("Downloads", "medicao.xlsx"), "m");
        Criar(Path.Combine("Downloads", "outra.xlsx"), "o");
        Criar(Path.Combine("Downloads", "arquivo.zip"), "z");
        Criar(Path.Combine("Downloads", "baixando.crdownload"));
        var vm = NovoOrganizar();

        vm.AoEntrar();
        Assert.False(vm.TemLinhas); // sem novidades do modo automatico
        Assert.Contains("Procurar de novo", vm.TextoVazio);

        vm.ProcurarDeNovoCommand.Execute(null);

        Assert.Equal("Todos os arquivos de Downloads", vm.TituloLista);
        Assert.Equal(["medicao.xlsx", "outra.xlsx", "arquivo.zip"], vm.Linhas.Select(l => l.Nome)); // sugeridos primeiro
        Assert.Equal(["Medições", "Medições", "Você decide"], vm.Linhas.Select(l => l.Destino));
        Assert.True(vm.TemVoceDecide);
        Assert.Equal("Mover 2 arquivos", vm.TextoBotaoMover);

        vm.Linhas[1].Selecionado = false; // o usuario tira um da confirmacao
        Assert.Equal("Mover 1 arquivo", vm.TextoBotaoMover);

        await vm.MoverSugeridosCommand.ExecuteAsync(null);

        Assert.Equal("1 arquivo movido.", vm.Resultado);
        Assert.Equal(["medicao.xlsx"], Nomes("Medições"));
        Assert.Equal(["arquivo.zip", "baixando.crdownload", "outra.xlsx"], Nomes("Downloads"));

        await vm.DesfazerUltimaCommand.ExecuteAsync(null);
        Assert.Equal(["arquivo.zip", "baixando.crdownload", "medicao.xlsx", "outra.xlsx"], Nomes("Downloads"));
    }

    [Fact]
    public async Task Voce_decide_escolhe_pasta_e_so_vira_regra_se_pedir()
    {
        Criar(Path.Combine("Downloads", "arquivo.zip"));
        Criar(Path.Combine("Downloads", "outro.zip"));
        var vm = NovoOrganizar();
        vm.ProcurarDeNovoCommand.Execute(null);
        Assert.False(vm.MoverSugeridosCommand.CanExecute(null) && vm.TextoBotaoMover != "Mover sugeridos");

        Dialogos.Pastas.Enqueue(Em("Compactados"));
        vm.EscolherDestinoCommand.Execute(vm.Linhas[0]);

        Assert.Equal("Compactados", vm.Linhas[0].Destino);
        Assert.Equal("Você escolheu", vm.Linhas[0].Motivo);
        Assert.True(vm.Linhas[0].PodeLembrar);
        Assert.Equal("Lembrar para arquivos .zip", vm.Linhas[0].TextoLembrar);
        Assert.Empty(Novidades.Regras); // escolha isolada nao cria regra

        Dialogos.Confirmacoes.Enqueue(true);
        vm.LembrarEscolhaCommand.Execute(vm.Linhas[0]);
        Assert.Equal("Arquivos .zip", Assert.Single(Novidades.Regras).Nome);
        Assert.Contains("O app só vai sugerir", Dialogos.ConfirmacoesPedidas[^1]);

        await vm.MoverSugeridosCommand.ExecuteAsync(null);
        Assert.Equal(["arquivo.zip"], Nomes("Compactados"));
        // Com a regra criada, o outro zip agora tem sugestao.
        vm.ProcurarDeNovoCommand.Execute(null);
        Assert.Equal("Compactados", Assert.Single(vm.Linhas).Destino);
    }

    [Fact]
    public void Destino_em_lote_para_os_indecisos()
    {
        Criar(Path.Combine("Downloads", "a.dwg"));
        Criar(Path.Combine("Downloads", "b.txt"));
        var vm = NovoOrganizar();
        vm.ProcurarDeNovoCommand.Execute(null);

        Dialogos.Pastas.Enqueue(Em("Diversos"));
        vm.EscolherDestinoDosIndecisosCommand.Execute(null);

        Assert.All(vm.Linhas, l => Assert.Equal("Diversos", l.Destino));
        Assert.Equal("Mover 2 arquivos", vm.TextoBotaoMover);
        Assert.False(vm.TemVoceDecide);
    }

    [Fact]
    public void Regras_sao_validadas_antes_de_gravar()
    {
        var vm = NovoOrganizar();
        vm.NovaRegraCommand.Execute(null);
        vm.Regras[0].Nome = "Volta";
        vm.Regras[0].Extensoes = ".pdf; *.PDF";
        vm.Regras[0].Destino = Trabalho; // manda para a propria pasta vigiada

        vm.SalvarRegrasCommand.Execute(null);

        Assert.Contains(vm.AvisosRegras, a => a.Impede && a.Texto.Contains("pasta vigiada"));
        Assert.Empty(Novidades.Regras);

        vm.Regras[0].Destino = Em("PDFs");
        vm.Regras[0].Prioridade = "5";
        vm.SalvarRegrasCommand.Execute(null);

        var regra = Assert.Single(new RegraRepositorio(Banco).Listar());
        Assert.Equal(["pdf", "PDF"], regra.Extensoes);
        Assert.Equal(5, regra.Prioridade);
        Assert.Contains(vm.AvisosRegras, a => a.Texto == "1 regra salva.");
    }

    [Fact]
    public void Planilhas_base_ficam_fora_do_organizador()
    {
        string basePlanilhas = Em("Downloads", "Base");
        Directory.CreateDirectory(basePlanilhas);
        Preferencias.Alterar(p => p with { PastaPlanilhasBase = basePlanilhas });
        GravarRegra("Planilhas", "xlsx", "Medições");
        string mestra = Criar(Path.Combine("Downloads", "Base", "Escolas.xlsx"));

        var sugestoes = Novidades.SugestoesDaPasta(basePlanilhas);

        Assert.Empty(sugestoes);
        Assert.True(File.Exists(mestra));
    }

    [Fact]
    public void Modo_automatico_liga_desliga_e_fica_gravado()
    {
        var vm = NovoOrganizar();
        Assert.False(vm.ModoAutomatico);
        Assert.Contains("Nada acontece sozinho", vm.TextoModoAutomatico);

        vm.AlternarModoAutomaticoCommand.Execute(null);

        Assert.True(vm.ModoAutomatico);
        Assert.True(Novidades.Vigiando);
        Assert.Contains("Nada é movido sem a sua confirmação", vm.TextoModoAutomatico);
        Assert.True(new ServicoPreferencias(new ConfigRepositorio(Banco), "x").Atual.ModoAutomatico);

        vm.AlternarModoAutomaticoCommand.Execute(null);
        Assert.False(Novidades.Vigiando);
    }
}

public class CompactarVmTests : ComApp
{
    [Fact]
    public async Task Reduzir_fotos_mostra_o_tamanho_novo_e_poe_copias_no_zip_sem_mexer_nos_originais()
    {
        string grande = CriarFoto(Path.Combine("Downloads", "Obra", "foto 1.jpg"), 3000, 2000);
        string outra = CriarFoto(Path.Combine("Downloads", "Obra", "foto 2.jpg"), 3000, 2000);
        Criar(Path.Combine("Downloads", "Obra", "planilha.xlsx"), "não é foto");
        var antes = File.ReadAllBytes(grande);
        var vm = NovoCompactar();
        vm.Adicionar([Em("Downloads", "Obra")]);

        vm.ReduzirFotos = true;
        await vm.Preparando;

        var aviso = Assert.Single(vm.Avisos, a => a.Texto.StartsWith("Fotos: 2 fotos, de"));
        Assert.Contains("as fotos originais não mudam", aviso.Texto);
        await vm.CriarCommand.ExecuteAsync(null);

        Assert.False(vm.ResultadoComFalha, vm.Resultado);
        string zip = Em("Downloads", "Obra.zip");
        using (var arquivo = System.IO.Compression.ZipFile.OpenRead(zip))
        {
            Assert.Equal(["foto 1.jpg", "foto 2.jpg", "planilha.xlsx"], arquivo.Entries.Select(e => e.FullName).Order());
            var foto = arquivo.GetEntry("foto 1.jpg")!;
            Assert.True(foto.Length < antes.Length);
            using var lida = SkiaSharp.SKBitmap.Decode(foto.Open());
            Assert.Equal(1600, Math.Max(lida.Width, lida.Height)); // qualidade media
        }

        Assert.Equal(antes, File.ReadAllBytes(grande)); // original intacto
        Assert.True(File.Exists(outra));
    }

    [Fact]
    public async Task Um_zip_por_pasta_mostra_previa_cria_e_registra_no_historico()
    {
        Criar(Path.Combine("Downloads", "Pautas", "p1.pdf"), new string('a', 3000));
        Criar(Path.Combine("Downloads", "Empenhos", "e1.pdf"), "e");
        string solto = Criar(Path.Combine("Downloads", "solto.pdf"));
        var vm = NovoCompactar();

        vm.Adicionar([Em("Downloads", "Pautas"), Em("Downloads", "Empenhos"), solto]);

        Assert.Equal(["Pautas.zip", "Empenhos.zip"], vm.Previa.Select(p => p.Nome));
        Assert.StartsWith("1 arquivo · até ", vm.Previa[0].Detalhe);
        Assert.Equal("Criar 2 arquivos .zip", vm.TextoBotao);
        Assert.Contains(vm.Avisos, a => a.Conferir && a.Texto.Contains("1 arquivo solto fica de fora"));
        Assert.Equal(Trabalho, vm.PastaDestino);

        await vm.CriarCommand.ExecuteAsync(null);

        Assert.StartsWith("2 arquivos .zip criados (", vm.Resultado);
        Assert.True(File.Exists(Em("Downloads", "Pautas.zip")));
        Assert.True(File.Exists(Em("Downloads", "Pautas", "p1.pdf"))); // originais preservados
        Assert.False(vm.TemEntradas);

        var lote = Assert.Single(Operacoes.Ultimos());
        Assert.Equal("Gerou 2 arquivos .zip em Downloads", lote.Descricao);
        var loteVm = new LoteVm(lote);
        Assert.False(loteVm.PodeDesfazer); // arquivo gerado nao e apagado pelo Desfazer
        Assert.True(loteVm.PodeAbrirPasta);

        vm.AbrirResultadoCommand.Execute(null);
        Assert.Equal(Em("Downloads", "Pautas.zip"), Dialogos.Abertos[^1]);
    }

    [Fact]
    public async Task Por_limite_e_destino_escolhido()
    {
        var arquivos = Enumerable.Range(1, 4).Select(n =>
        {
            string caminho = Em("Downloads", $"foto {n}.jpg");
            var dados = new byte[400_000];
            new Random(n).NextBytes(dados);
            File.WriteAllBytes(caminho, dados);
            return caminho;
        }).ToList();
        var vm = NovoCompactar();
        vm.Adicionar(arquivos);
        vm.Modo = ModoCompactar.PorLimite;
        vm.NomeZip = "Fotos da obra";
        vm.LimiteMb = "1";
        Dialogos.Pastas.Enqueue(Em("Envio"));
        vm.EscolherDestinoCommand.Execute(null);

        Assert.Equal(["Fotos da obra - parte 01.zip", "Fotos da obra - parte 02.zip"], vm.Previa.Select(p => p.Nome));

        vm.LimiteMb = "0,3"; // nenhuma foto cabe
        Assert.Empty(vm.Previa);
        Assert.Contains(vm.Avisos, a => a.Impede && a.Texto.Contains("não cabe sozinho"));
        Assert.Equal("Criar .zip", vm.TextoBotao);

        vm.LimiteMb = "1";
        await vm.CriarCommand.ExecuteAsync(null);

        Assert.Equal(["Fotos da obra - parte 01.zip", "Fotos da obra - parte 02.zip"], Nomes("Envio"));
        Assert.All(Directory.GetFiles(Em("Envio")), f => Assert.True(new FileInfo(f).Length <= 1024 * 1024));
        Assert.Equal(Em("Envio"), NovoCompactar().PastaDestino); // ultimo destino lembrado
    }

    [Fact]
    public async Task Descompactar_varios_cada_um_na_sua_pasta()
    {
        string zip1 = Em("Downloads", "A.zip"), zip2 = Em("Downloads", "B.zip"), ruim = Criar(Path.Combine("Downloads", "quebrado.zip"), "nao e zip");
        foreach (string z in new[] { zip1, zip2 })
        {
            using var zip = ZipFile.Open(z, ZipArchiveMode.Create);
            using var escritor = new StreamWriter(zip.CreateEntry("doc.txt").Open());
            escritor.Write(z);
        }

        var vm = NovoCompactar();
        Dialogos.Arquivos.Enqueue([zip1, zip2, ruim]);

        await vm.DescompactarVariosCommand.ExecuteAsync(null);

        Assert.True(File.Exists(Em("Downloads", "A", "doc.txt")));
        Assert.True(File.Exists(Em("Downloads", "B", "doc.txt")));
        Assert.StartsWith("2 arquivos .zip abertos, cada um na sua pasta (2 arquivos). 1 falhou: quebrado.zip:", vm.Resultado);
        Assert.True(vm.ResultadoComFalha);
    }
}

public class HistoricoETelasTests : ComApp
{
    [Fact]
    public async Task Historico_lista_filtra_mostra_itens_e_desfaz_com_confirmacao()
    {
        var renomear = NovoRenomear();
        renomear.Adicionar([Criar(Path.Combine("Downloads", "a.pdf")), Criar(Path.Combine("Downloads", "b.pdf"))]);
        renomear.NomeBase = "Relatório";
        await renomear.RenomearCommand.ExecuteAsync(null);
        Relogio.Agora = Relogio.Agora.AddMinutes(5);
        Operacoes.RegistrarCriados("Gerou “Pautas.zip”", [Criar(Path.Combine("Downloads", "Pautas.zip"))]);

        var vm = NovoHistorico();
        vm.AoEntrar();

        Assert.Equal(["Gerou “Pautas.zip”", "Renomeou 2 arquivos, de “Relatório 01” a “Relatório 02”"], vm.Lotes.Select(l => l.Descricao));
        Assert.Equal("2 concluídos", vm.Lotes[1].Resumo);
        Assert.Matches(@"^11/11/2026 às \d\d:00$", vm.Lotes[1].Quando); // hora no fuso do computador
        Assert.Single(vm.Tarefas);
        Assert.Equal("Concluída", vm.Tarefas[0].Estado);

        vm.Filtro = "a.pdf";
        var lote = Assert.Single(vm.Lotes);
        vm.Selecionado = lote;
        Assert.Equal(("a.pdf", "Relatório 01.pdf", "Concluído"), (lote.Itens[0].De, lote.Itens[0].Para, lote.Itens[0].Estado));

        // Um dos arquivos foi editado depois: o Desfazer avisa e so volta o outro.
        File.WriteAllText(Em("Downloads", "Relatório 02.pdf"), "editado depois");
        Dialogos.Confirmacoes.Enqueue(true);
        await vm.DesfazerCommand.ExecuteAsync(lote);

        Assert.Contains("1 arquivo volta ao estado anterior.", Dialogos.ConfirmacoesPedidas[^1]);
        Assert.Contains("alterado depois", Dialogos.ConfirmacoesPedidas[^1]);
        Assert.Equal(["Pautas.zip", "Relatório 02.pdf", "a.pdf"], Nomes("Downloads"));
        vm.Filtro = "";
        Assert.Contains(vm.Lotes, l => l.Resumo.Contains("desfeito"));

        // Arquivo gerado: explica por que nao desfaz.
        await vm.DesfazerCommand.ExecuteAsync(vm.Lotes.First(l => l.Descricao.StartsWith("Gerou")));
        Assert.Contains("não é apagado", Dialogos.AvisosMostrados[^1]);
        Assert.True(File.Exists(Em("Downloads", "Pautas.zip")));
    }

    [Fact]
    public async Task Lote_interrompido_e_reconciliado_ao_abrir_e_pode_ser_continuado()
    {
        string a = Criar(Path.Combine("Downloads", "a.pdf")), b = Criar(Path.Combine("Downloads", "b.pdf"));
        var lote = new Spobras.Core.Operacoes.Lote
        {
            Tipo = Spobras.Core.Operacoes.TipoLote.Renomear,
            Descricao = "Renomeou 2 arquivos",
            CriadoEm = Relogio.Agora,
            Itens =
            [
                new() { Ordem = 0, Origem = a, Destino = Em("Downloads", "1.pdf"), Estado = Spobras.Core.Operacoes.EstadoItem.EmExecucao },
                new() { Ordem = 1, Origem = b, Destino = Em("Downloads", "2.pdf") },
            ],
        };
        new Spobras.Dados.Historico.HistoricoSqlite(Banco).CriarLote(lote);
        File.Move(a, Em("Downloads", "1.pdf")); // o app fechou depois de mover e antes de gravar

        Assert.Equal(1, Operacoes.ReconciliarInterrompidos());

        var vm = NovoHistorico();
        vm.SoComPendencia = true;
        vm.AoEntrar();
        var pendente = Assert.Single(vm.Lotes);
        Assert.Equal("1 concluído · 1 pendente", pendente.Resumo);
        Assert.True(pendente.TemPendentes);

        await vm.ContinuarCommand.ExecuteAsync(pendente);

        Assert.Equal(["1.pdf", "2.pdf"], Nomes("Downloads"));
        Assert.Equal("Continuado: 2 concluído(s).", vm.Resultado);
    }

    [Fact]
    public void Preferencias_tem_padrao_lembram_destino_e_sobrevivem_a_reabertura()
    {
        Assert.Equal(Trabalho, Preferencias.Atual.PastaTrabalho);
        Assert.Equal(Trabalho, Preferencias.DestinoDe("compactar"));

        Preferencias.LembrarDestino("compactar", Em("Envio"));
        Preferencias.Alterar(p => p with { PastaPlanilhasBase = Em("Base") });

        var reaberta = new ServicoPreferencias(new ConfigRepositorio(Banco), "outro-padrao");
        Assert.Equal(Trabalho, reaberta.Atual.PastaTrabalho);
        Assert.Equal(Em("Envio"), reaberta.DestinoDe("compactar"));
        Assert.Equal([Em("Base"), Em("dados")], reaberta.Protegidos(Em("dados")));
    }

    [Fact]
    public void Navegacao_inicio_e_configuracoes()
    {
        ModuloVm[] modulos = [new("organizar", "Organizar arquivos", "", ""), new("renomear", "Renomear em lote", "", "")];
        string? pedido = null;
        var inicio = new InicioVm(modulos, Operacoes, Novidades, Preferencias, Dialogos, new InterfaceDireta(), id => pedido = id);
        var config = new ConfiguracoesVm(Preferencias, Novidades, Dialogos, Disco, Relogio, "0.1.0", Em("dados"));
        var telas = new Dictionary<string, TelaVm>
        {
            ["inicio"] = inicio, ["organizar"] = NovoOrganizar(), ["renomear"] = NovoRenomear(), ["configuracoes"] = config,
        };
        var principal = new PrincipalVm(modulos, telas, Novidades, Operacoes, new InterfaceDireta());

        Assert.Same(inicio, principal.PaginaAtual);
        Assert.Equal("Modo automático desligado", inicio.EstadoAutomatico);
        Assert.False(inicio.TemAcoes);
        Assert.Equal("Modo automático: desligado", principal.TextoAutomatico);

        inicio.AbrirCommand.Execute(modulos[1]);
        Assert.Equal("renomear", pedido);
        principal.NavegarCommand.Execute("renomear");
        Assert.Equal("Renomear em lote", principal.PaginaAtual!.Titulo);
        principal.NavegarCommand.Execute("nao-existe");
        Assert.Equal("renomear", principal.IdAtual);

        principal.NavegarCommand.Execute("configuracoes");
        Assert.Contains("Ainda não escolhida", config.EstadoPastaBase);
        Dialogos.Pastas.Enqueue(Em("Base"));
        config.EscolherPastaBaseCommand.Execute(null);
        Assert.Equal(Em("Base"), Preferencias.Atual.PastaPlanilhasBase);
        Assert.Contains("não encontrada", config.EstadoPastaBase);
        config.ModoAutomatico = true;
        Assert.True(Novidades.Vigiando);
        Assert.Equal("Modo automático: ligado", principal.TextoAutomatico);
    }
}
