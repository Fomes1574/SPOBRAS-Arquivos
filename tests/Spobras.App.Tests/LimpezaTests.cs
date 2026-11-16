namespace Spobras.App.Tests;

/// <summary>Miniaturas guardadas pelo computador: as sem uso ha 30 dias saem; as usadas ficam.</summary>
public class LimpezaTests : ComApp
{
    [Fact]
    public void Miniaturas_sem_uso_ha_30_dias_sao_apagadas_e_as_usadas_ficam()
    {
        var modelos = Modelos;
        string usada = modelos.Miniatura(CriarFoto(Path.Combine("Downloads", "a.jpg"), 400, 300));
        string esquecida = modelos.Miniatura(CriarFoto(Path.Combine("Downloads", "b.jpg"), 400, 300));
        var antes = DateTime.UtcNow.AddDays(-45);
        File.SetLastWriteTimeUtc(usada, antes);
        File.SetLastWriteTimeUtc(esquecida, antes);

        modelos.Miniatura(Em("Downloads", "a.jpg")); // usada de novo hoje
        Assert.Equal(1, modelos.LimparMiniaturasAntigas());
        Assert.True(File.Exists(usada));
        Assert.False(File.Exists(esquecida));
        Assert.True(File.Exists(Em("Downloads", "b.jpg"))); // a foto do usuario nunca e tocada
    }
}

/// <summary>Excluir pelo historico o que o proprio app gerou: vai para a Lixeira, com confirmacao.</summary>
public class ExcluirGeradoTests : ComApp
{
    private sealed class LixeiraFalsa(string pasta) : Spobras.App.Servicos.ILixeira
    {
        public List<string> Enviados { get; } = [];

        public void Enviar(string caminho)
        {
            Directory.CreateDirectory(pasta);
            File.Move(caminho, Path.Combine(pasta, Path.GetFileName(caminho)));
            Enviados.Add(caminho);
        }
    }

    [Fact]
    public void Arquivo_gerado_vai_para_a_lixeira_mesmo_alterado_e_o_do_usuario_nao_tem_o_botao()
    {
        Directory.CreateDirectory(Em("Downloads"));
        string relatorio = Em("Downloads", "Relatório.xlsx");
        File.WriteAllText(relatorio, "gerado");
        var historico = new Spobras.Dados.Historico.HistoricoSqlite(Banco);
        historico.CriarLote(new Spobras.Core.Operacoes.Lote
        {
            Tipo = Spobras.Core.Operacoes.TipoLote.Criar, Descricao = "Relatório fotográfico gerado", CriadoEm = Relogio.Agora,
            Itens = [new() { Origem = relatorio, Destino = relatorio, Estado = Spobras.Core.Operacoes.EstadoItem.Concluido }],
        });
        historico.CriarLote(new Spobras.Core.Operacoes.Lote
        {
            Tipo = Spobras.Core.Operacoes.TipoLote.Renomear, Descricao = "Renomeou 1 arquivo", CriadoEm = Relogio.Agora.AddMinutes(1),
            Itens = [new() { Origem = Em("Downloads", "a.pdf"), Destino = relatorio, Estado = Spobras.Core.Operacoes.EstadoItem.Concluido }],
        });
        File.AppendAllText(relatorio, " e alterado pelo usuário"); // alterado depois: pode excluir assim mesmo

        var lixeira = new LixeiraFalsa(Em("Lixeira"));
        var vm = new Spobras.App.ViewModels.HistoricoVm(Operacoes, Dialogos, new Spobras.App.Servicos.InterfaceDireta(), lixeira);
        vm.AoEntrar();
        var gerado = Assert.Single(vm.Lotes, l => l.PodeExcluirArquivo);
        Assert.Equal("Relatório fotográfico gerado", gerado.Descricao);

        Dialogos.Confirmacoes.Enqueue(false); // desistiu
        vm.ExcluirArquivoCommand.Execute(gerado);
        Assert.True(File.Exists(relatorio));

        Dialogos.Confirmacoes.Enqueue(true);
        vm.ExcluirArquivoCommand.Execute(gerado);
        Assert.False(File.Exists(relatorio));
        Assert.True(File.Exists(Em("Lixeira", "Relatório.xlsx"))); // recuperavel
        Assert.Contains("Lixeira", vm.Resultado);
        Assert.DoesNotContain(vm.Lotes, l => l.PodeExcluirArquivo);
    }
}
