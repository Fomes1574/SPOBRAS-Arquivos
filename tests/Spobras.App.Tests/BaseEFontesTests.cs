using Spobras.App.ViewModels;
using Spobras.Core.Base;
using Spobras.Core.Tarefas;
using Spobras.Testes;

namespace Spobras.App.Tests;

public class FontesTests : ComApp
{
    private FonteInfo Mestra => Fontes.Fontes().First(f => f.Id == Spobras.Core.Base.Fontes.Mestra);

    private async Task EsperarFila()
    {
        foreach (var tarefa in Operacoes.Fila.Tarefas)
        {
            await FilaTarefas.Aguardar(tarefa);
        }
    }

    [Fact]
    public async Task Primeira_execucao_acha_as_fontes_pelo_nome_na_pasta_das_planilhas_base_e_le()
    {
        string mestra = GravarMestra();
        GravarDotacoes();
        Assert.Equal(EstadoFonte.NuncaLida, Mestra.Estado);
        Assert.Equal("", Mestra.Caminho);

        Preferencias.Alterar(p => p with { PastaPlanilhasBase = Em("Base") });
        Fontes.Iniciar(vigiar: false);
        Assert.Equal(mestra, Mestra.Caminho); // achou pelo nome

        // Primeira conferencia so observa; depois de o arquivo ficar quieto, le sozinho.
        Fontes.ConferirMudancas();
        Assert.Equal(0, Base.TotalDeObras());
        Relogio.Agora = Relogio.Agora.AddSeconds(5);
        Fontes.ConferirMudancas();
        await EsperarFila();

        Assert.Equal(5, Base.TotalDeObras());
        Assert.Equal(5, Base.Vinculos().Count);
        Assert.Equal(EstadoFonte.Atualizada, Mestra.Estado);
        Assert.Equal(1, Mestra.Versao);
        Assert.Equal(5, Mestra.Linhas);
        Assert.Contains(Mestra.Avisos, a => a.Contains("espaço no lugar do ponto"));
        Assert.Equal(File.GetLastWriteTimeUtc(mestra), Mestra.ModificadoArquivo!.Value.UtcDateTime);

        // Nada mudou: outra conferencia nao le de novo.
        int tarefas = Operacoes.Fila.Tarefas.Count;
        Relogio.Agora = Relogio.Agora.AddMinutes(10);
        Fontes.ConferirMudancas();
        Fontes.ConferirMudancas();
        Assert.Equal(tarefas, Operacoes.Fila.Tarefas.Count);
    }

    [Fact]
    public async Task T30_planilha_salva_de_novo_e_relida_sozinha_e_a_tela_reaplica_os_filtros()
    {
        string caminho = GravarMestra();
        await Fontes.DefinirAsync(Spobras.Core.Base.Fontes.Mestra, caminho);
        var tela = new BaseVm(Base, Fontes, new Servicos.InterfaceDireta(), _ => { });
        tela.Portaria = BaseSintetica.P6;
        Assert.Equal(["CEU Delta"], tela.Obras.Select(o => o.Escola));

        // O usuario edita a planilha no Excel e salva: entra uma obra nova na Portaria 6.
        var nova = BaseSintetica.Mestra();
        var t = nova.TabelasPorNome["contratos_VW_ESCOLAS_CONTRATOS_MEDIDOS_PAGOS"];
        nova.Tabela(t.Nome, [.. t.Colunas], [.. t.Linhas, BaseSintetica.Obra("006/EX/2026", "EMEI Nova", BaseSintetica.P6, new DateTime(2026, 11, 1), 250m)]);
        GravarMestra(nova);
        File.SetLastWriteTimeUtc(caminho, DateTime.UtcNow.AddMinutes(1));

        Fontes.ConferirMudancas(); // viu a mudanca; ainda pode estar gravando
        Assert.Equal(5, Base.TotalDeObras());
        Relogio.Agora = Relogio.Agora.AddSeconds(2);
        Fontes.ConferirMudancas(); // menos que o tempo de espera
        Assert.Equal(5, Base.TotalDeObras());
        Relogio.Agora = Relogio.Agora.AddSeconds(3);
        Fontes.ConferirMudancas();
        await EsperarFila();

        Assert.Equal(6, Base.TotalDeObras());
        Assert.Equal(2, Mestra.Versao);
        Assert.Equal(BaseSintetica.P6, tela.Portaria); // filtro mantido
        Assert.Equal(["CEU Delta", "EMEI Nova"], tela.Obras.Select(o => o.Escola).Order()); // consulta atualizada sozinha
    }

    [Fact]
    public async Task T16_falha_de_leitura_mantem_a_ultima_base_valida_e_nao_insiste_em_laco()
    {
        string caminho = GravarMestra();
        await Fontes.DefinirAsync(Spobras.Core.Base.Fontes.Mestra, caminho);
        var lidaEm = Mestra.UltimaLeitura;

        // A planilha e salva com a estrutura mudada (coluna renomeada).
        var quebrada = BaseSintetica.Mestra();
        var t = quebrada.TabelasPorNome["contratos_VW_ESCOLAS_CONTRATOS_MEDIDOS_PAGOS"];
        quebrada.Tabela(t.Nome, [.. t.Colunas.Select(c => c == "INÍCIO EXECUÇÃO" ? "INICIO NOVO" : c)], [.. t.Linhas]);
        GravarMestra(quebrada);
        File.SetLastWriteTimeUtc(caminho, DateTime.UtcNow.AddMinutes(2));
        Relogio.Agora = Relogio.Agora.AddMinutes(1);
        Fontes.ConferirMudancas();
        Relogio.Agora = Relogio.Agora.AddSeconds(5);
        Fontes.ConferirMudancas();
        await EsperarFila();

        Assert.Equal(5, Base.TotalDeObras()); // base anterior intacta
        Assert.Equal(EstadoFonte.Desatualizada, Mestra.Estado);
        Assert.Contains("\"INÍCIO EXECUÇÃO\"", Mestra.Mensagem);
        Assert.Contains("Os dados mostrados são da leitura de", Mestra.Mensagem);
        Assert.Equal(lidaEm, Mestra.UltimaLeitura);
        Assert.False(Base.HistoricoDeImportacoes(Spobras.Core.Base.Fontes.Mestra)[0].Ok);

        // O mesmo arquivo com defeito nao e relido a cada conferencia.
        int tarefas = Operacoes.Fila.Tarefas.Count;
        for (int i = 0; i < 5; i++)
        {
            Relogio.Agora = Relogio.Agora.AddSeconds(10);
            Fontes.ConferirMudancas();
        }

        Assert.Equal(tarefas, Operacoes.Fila.Tarefas.Count);

        // Consertada e salva: volta a ler sozinha.
        GravarMestra();
        File.SetLastWriteTimeUtc(caminho, DateTime.UtcNow.AddMinutes(3));
        Fontes.ConferirMudancas();
        Relogio.Agora = Relogio.Agora.AddSeconds(5);
        Fontes.ConferirMudancas();
        await EsperarFila();
        Assert.Equal(EstadoFonte.Atualizada, Mestra.Estado);
        Assert.Equal(2, Mestra.Versao);
    }

    [Fact]
    public async Task T16_arquivo_que_sumiu_nao_e_trocado_sozinho_por_outro_com_o_mesmo_nome()
    {
        string caminho = GravarMestra(pasta: "Antiga");
        string outro = GravarMestra(pasta: "Base");
        Preferencias.Alterar(p => p with { PastaPlanilhasBase = Em("Base") });
        await Fontes.DefinirAsync(Spobras.Core.Base.Fontes.Mestra, caminho);
        File.Move(caminho, Em("Antiga", "renomeada.xlsx"));

        // Sumir por um instante (o Excel salvando) nao conta; so depois do tempo de espera.
        Fontes.ConferirMudancas();
        Assert.Equal(EstadoFonte.Atualizada, Mestra.Estado);
        Relogio.Agora = Relogio.Agora.AddSeconds(5);
        Fontes.ConferirMudancas();

        Assert.Equal(EstadoFonte.ArquivoNaoEncontrado, Mestra.Estado);
        Assert.Equal(caminho, Mestra.Caminho); // nao trocou sozinho
        Assert.Contains(outro, Mestra.Mensagem);
        Assert.Equal(5, Base.TotalDeObras());

        var tela = new FontesVm(Fontes, Base, Dialogos, new Servicos.InterfaceDireta(), Preferencias);
        var fonte = tela.Fontes.First(f => f.Info.Id == Spobras.Core.Base.Fontes.Mestra);
        Assert.Equal("Arquivo não encontrado", fonte.Estado);
        Assert.Equal(outro, fonte.Candidato);

        Dialogos.Confirmacoes.Enqueue(false);
        await tela.UsarCandidatoCommand.ExecuteAsync(fonte);
        Assert.Equal(caminho, Mestra.Caminho);

        Dialogos.Confirmacoes.Enqueue(true);
        await tela.UsarCandidatoCommand.ExecuteAsync(fonte);
        Assert.Equal(outro, Mestra.Caminho);
        Assert.Equal(EstadoFonte.Atualizada, Mestra.Estado);
    }

    [Fact]
    public async Task Tela_de_fontes_escolhe_arquivo_rele_mostra_avisos_e_guarda_apelidos()
    {
        string mestra = GravarMestra();
        string dotacoes = GravarDotacoes();
        var tela = new FontesVm(Fontes, Base, Dialogos, new Servicos.InterfaceDireta(), Preferencias);
        Assert.Equal(["Planilha mestra", "Portarias e dotações"], tela.Fontes.Select(f => f.Nome));
        Assert.Equal("Arquivo ainda não escolhido", tela.Fontes[0].Caminho);
        Assert.False(tela.TemApelidos);

        Dialogos.Arquivos.Enqueue([mestra]);
        await tela.EscolherArquivoCommand.ExecuteAsync(tela.Fontes[0]);
        Dialogos.Arquivos.Enqueue([dotacoes]);
        await tela.EscolherArquivoCommand.ExecuteAsync(tela.Fontes[1]);

        Assert.Equal("Portarias e dotações: leitura concluída, 5 linhas.", tela.Resultado);
        Assert.All(tela.Fontes, f => Assert.True(f.Ok));
        Assert.Equal("5", tela.Fontes[0].Linhas);
        Assert.True(tela.Fontes[1].TemAvisos);
        Assert.Contains(tela.Fontes[1].Avisos, a => a.Contains("corrigida para"));
        Assert.Equal(5, tela.Vinculos.Count);

        // Apelidos: um para cada texto de portaria da mestra, todos sem vinculo ate o usuario escolher.
        Assert.Equal([BaseSintetica.P3De2021, BaseSintetica.P3De2025, BaseSintetica.P6], tela.Apelidos.Select(a => a.Texto));
        Assert.All(tela.Apelidos, a => Assert.Equal(FontesVm.SemVinculo, a.Portaria));
        Assert.Equal([FontesVm.SemVinculo, "Portaria 2 (2024)", "Portaria 3", "Portaria 4"], tela.PortariasDaBase);

        tela.Apelidos[0].Portaria = "Portaria 3";
        Assert.Equal("Portaria 3", Base.Apelidos()[BaseSintetica.P3De2021]);
        await tela.RelerCommand.ExecuteAsync(tela.Fontes[0]);
        Assert.Equal("Portaria 3", tela.Apelidos[0].Portaria); // continua depois de reler
        tela.Apelidos[0].Portaria = FontesVm.SemVinculo;
        Assert.Empty(Base.Apelidos());

        // Arquivo que nao e a planilha certa: recusado com motivo, base intacta.
        string errado = Em("Base", "outra.xlsx");
        XlsxSintetico.Gravar(BaseSintetica.Dotacoes(), errado);
        Dialogos.Arquivos.Enqueue([errado]);
        await tela.EscolherArquivoCommand.ExecuteAsync(tela.Fontes[0]);
        Assert.True(tela.ResultadoComFalha);
        Assert.Contains("não foi encontrada na planilha", tela.Resultado);
        Assert.Equal(5, Base.TotalDeObras());
    }
}

public class BaseVmTests : ComApp
{
    private async Task<BaseVm> Abrir(Action<ObraLida>? usar = null)
    {
        await Fontes.DefinirAsync(Spobras.Core.Base.Fontes.Mestra, GravarMestra());
        return new BaseVm(Base, Fontes, new Servicos.InterfaceDireta(), _ => { }, usar);
    }

    [Fact]
    public void Base_vazia_explica_o_que_fazer()
    {
        string? destino = null;
        var tela = new BaseVm(Base, Fontes, new Servicos.InterfaceDireta(), id => destino = id);

        Assert.False(tela.TemBase);
        Assert.Contains("Aponte a planilha mestra em Fontes de dados", tela.EstadoDaBase);
        Assert.Empty(tela.Obras);
        tela.AbrirFontesCommand.Execute(null);
        Assert.Equal(FontesVm.Modulo, destino);
    }

    [Fact]
    public async Task T27_cada_escolha_atualiza_resultados_e_opcoes_sem_botao_buscar()
    {
        var tela = await Abrir();
        Assert.True(tela.TemBase);
        Assert.StartsWith("5 obras · planilha lida em", tela.EstadoDaBase);
        Assert.Equal("5 obras encontradas", tela.Resumo);
        Assert.Equal(3, tela.Portarias.Count);

        tela.Portaria = BaseSintetica.P3De2021;
        Assert.Equal("3 obras encontradas", tela.Resumo);
        Assert.Equal(["001/EX/2024", "002/EX/2024"], tela.Contratos);
        Assert.Equal(["CEI Beta", "EMEF Alfa", "EMEI Gama"], tela.Escolas);
        Assert.True(tela.TemFiltro);

        tela.Escola = "EMEF Alfa";
        Assert.Equal(["001/EX/2024"], tela.Obras.Select(o => o.Contrato));
        Assert.Equal(["001/EX/2024"], tela.Contratos);
        Assert.Equal(2, tela.Portarias.Count); // para trocar a portaria, so as que tem a EMEF Alfa

        tela.LimparCommand.Execute("portaria");
        Assert.Null(tela.Portaria);
        Assert.Equal(["001/EX/2024", "003/EX/2025"], tela.Contratos);
        Assert.Equal("2 obras encontradas", tela.Resumo);

        tela.LimparFiltrosCommand.Execute(null);
        Assert.Equal("5 obras encontradas", tela.Resumo);
        Assert.False(tela.TemFiltro);
    }

    [Fact]
    public async Task T29_T31_valores_em_reais_datas_e_avisos_de_digitacao()
    {
        var tela = await Abrir();

        tela.ValorDe = "1.000,00";
        Assert.Equal(["CEU Delta", "EMEF Alfa"], tela.Obras.Select(o => o.Escola).Order());
        Assert.Contains(tela.Obras, o => o.SaldoAPagar.Replace(' ', ' ') == "R$ 1.500,00");
        tela.ValorAte = "R$ 1.000";
        Assert.Equal(["CEU Delta"], tela.Obras.Select(o => o.Escola));

        tela.ValorDe = tela.ValorAte = "";
        tela.ValorSemInformacao = true;
        var semSaldo = Assert.Single(tela.Obras);
        Assert.Equal("Sem informação", semSaldo.SaldoAPagar);
        Assert.Equal("Sem data da OS", semSaldo.DataOs);
        tela.ValorSemInformacao = false;

        tela.ValorFiltrado = tela.CamposDeValor.First(c => c.Rotulo == "Valor pago");
        tela.ValorDe = "abc";
        Assert.Contains(tela.Avisos, a => a.Conferir && a.Texto.Contains("valor digitado não foi entendido"));
        Assert.Equal(5, tela.Obras.Count); // filtro invalido nao e aplicado
        tela.ValorDe = "";

        tela.OsDe = "10/01/2026";
        tela.OsAte = "1/3/2026";
        Assert.Equal(["CEI Beta", "EMEF Alfa"], tela.Obras.Select(o => o.Escola).Order());
        tela.SemDataOs = true;
        Assert.Equal(3, tela.Obras.Count);
        tela.OsAte = "31/02/2026";
        Assert.Contains(tela.Avisos, a => a.Texto.Contains("data digitada não existe"));

        tela.LimparFiltrosCommand.Execute(null);
        tela.Texto = "lote 07";
        Assert.Equal("1 obra encontrada", tela.Resumo);
        tela.Texto = "";
        tela.Ordem = OrdemObras.SaldoAPagar;
        Assert.Equal("EMEF Alfa", tela.Obras[0].Escola); // maior saldo primeiro
        Assert.Equal("EMEI Gama", tela.Obras[^1].Escola); // sem informacao no fim
    }

    [Fact]
    public async Task Ficha_da_obra_medicoes_no_nivel_do_contrato_e_uso_no_relatorio()
    {
        ObraLida? usada = null;
        var tela = await Abrir(o => usada = o);
        Assert.False(tela.PodeUsarNoRelatorio);

        tela.Selecionada = tela.Obras.First(o => o.Escola == "CEI Beta");

        Assert.True(tela.PodeUsarNoRelatorio);
        Assert.Contains(tela.Selecionada.Campos, c => c.Key == "Data da OS (início da execução)" && c.Value == "01/03/2026");
        Assert.Contains(tela.Selecionada.Campos, c => c.Key == "Saldo a pagar" && c.Value.Replace(' ', ' ') == "R$ 0,00");
        Assert.Contains(tela.Selecionada.Campos, c => c.Key.StartsWith("Contrato de receita") && c.Value == "015/EX/2025");
        Assert.Equal("Nenhuma medição em trâmite desta escola.", tela.ResumoDasMedicoes); // as tres do contrato estao pagas
        Assert.False(tela.TemMedicoesEmTramite);
        Assert.Equal(["Valor original (empreendimento)", "Aditado", "Contratado com aditamento", "Pago", "Saldo a pagar"], tela.Selecionada.Valores.Campos.Select(c => c.Key));
        Assert.DoesNotContain(tela.Selecionada.Grupos.SelectMany(g => g.Campos), c => c.Key is "Valor medido" or "Saldo a medir" or "Data de assinatura");
        Assert.Equal(["Data da OS (início da execução)", "Término da execução"], tela.Selecionada.Grupos.Single(g => g.Titulo == "Prazo").Campos.Select(c => c.Key));

        tela.UsarNoRelatorioCommand.Execute(null);
        Assert.Equal("CEI Beta", usada!.Empreendimento);

        var relatorio = NovoRelatorio();
        relatorio.PreencherDaObra(usada);
        Assert.Equal(("CEI Beta", "001/EX/2024", "01/03/2026", "LOTE 01"), (relatorio.Unidade, relatorio.Contrato, relatorio.DataOsTexto, relatorio.Lote));
        Assert.Equal(BaseSintetica.P3De2021, relatorio.Portaria);
        Assert.Contains("trazidos da Base de dados", relatorio.Resultado);
    }

    [Fact]
    public async Task Ficha_mostra_as_medicoes_em_tramite_da_escola_com_considerado_mais_reajuste()
    {
        var tela = await Abrir();
        tela.Selecionada = tela.Obras.First(o => o.Escola == "CEU Delta");

        // Nao pagas: analise GEC (80 + 5,50 de reajuste) e devolucao fiscal (20); a "sob gestao da SIURB" nao aparece.
        Assert.Equal(["00003", "00002"], tela.MedicoesEmTramiteDaObra.Select(m => m.Numero));
        Assert.Equal("R$ 85,50", tela.MedicoesEmTramiteDaObra[1].Valor.Replace('\u00A0', ' '));
        Assert.Equal("R$ 105,50", tela.TotalEmTramite.Replace('\u00A0', ' '));
        Assert.Contains("2 medições em trâmite desta escola", tela.ResumoDasMedicoes);
        Assert.Equal("VIGENTE · em andamento", tela.Selecionada.Situacao);
        Assert.Contains("ANÁLISE GEC", tela.Selecionada.Detalhe + tela.MedicoesEmTramiteDaObra[1].Status);
        Assert.StartsWith("004/EX/2025 · VIGENTE · LOTE 01 · OS 15/06/2025", tela.Selecionada.Detalhe);
    }

    [Fact]
    public async Task Filtro_que_deixou_de_existir_apos_atualizar_a_base_e_apontado()
    {
        var tela = await Abrir();
        tela.Contrato = "004/EX/2025";
        Assert.Single(tela.Obras);

        // A planilha e salva sem esse contrato.
        var menor = BaseSintetica.Mestra();
        var t = menor.TabelasPorNome["contratos_VW_ESCOLAS_CONTRATOS_MEDIDOS_PAGOS"];
        menor.Tabela(t.Nome, [.. t.Colunas], [.. t.Linhas.Take(4)]);
        string caminho = GravarMestra(menor);
        await Fontes.RelerAsync(Spobras.Core.Base.Fontes.Mestra);

        Assert.Equal("004/EX/2025", tela.Contrato); // nao some em silencio
        Assert.Empty(tela.Obras);
        Assert.Equal("Nenhuma obra com esses filtros.", tela.Resumo);
        Assert.Contains(tela.Avisos, a => a.Texto.Contains("O filtro Contrato (\"004/EX/2025\") não combina com nenhuma obra da base atual"));
        Assert.Contains("004/EX/2025", tela.Contratos);
        Assert.True(File.Exists(caminho));
    }
}
