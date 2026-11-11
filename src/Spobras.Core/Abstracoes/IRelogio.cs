namespace Spobras.Core.Abstracoes;

public interface IRelogio
{
    DateTimeOffset Agora { get; }
}

public sealed class RelogioSistema : IRelogio
{
    public DateTimeOffset Agora => DateTimeOffset.Now;
}
