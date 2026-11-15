using Spobras.Core.Base;

namespace Spobras.Core.Tests;

/// <summary>Regras de status escolhidas pelo usuario (estado global: esta classe roda sozinha).</summary>
[Collection(nameof(RegrasStatusTests))]
[CollectionDefinition(nameof(RegrasStatusTests), DisableParallelization = true)]
public class RegrasStatusTests
{
    [Fact]
    public void Regra_escolhida_vale_por_cima_da_padrao_e_voltar_ao_padrao_desfaz()
    {
        try
        {
            Assert.Equal(SituacaoContrato.Desconhecido, MedicoesEmTramite.Classificar("PARALISADO"));
            MedicoesEmTramite.DefinirRegras(new Dictionary<string, SituacaoContrato> { ["Paralisado"] = SituacaoContrato.Concluido, ["vigente"] = SituacaoContrato.Ignorado });
            Assert.Equal(SituacaoContrato.Concluido, MedicoesEmTramite.Classificar("PARALISADO"));
            Assert.Equal(SituacaoContrato.Ignorado, MedicoesEmTramite.Classificar("VIGENTE"));
            Assert.Equal(SituacaoContrato.Andamento, MedicoesEmTramite.ClassificarPadrao("VIGENTE"));
        }
        finally
        {
            MedicoesEmTramite.DefinirRegras(null);
        }

        Assert.Equal(SituacaoContrato.Andamento, MedicoesEmTramite.Classificar("VIGENTE"));
    }
}
