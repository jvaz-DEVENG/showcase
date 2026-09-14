namespace GameBoost.Core.Abstractions;

/// <summary>Relogio injetavel. Existe para tornar tempo testavel (regra 10 nao se aplica, mas testes exigem).</summary>
public interface IClock
{
    DateTimeOffset Now { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset Now => DateTimeOffset.Now;
}
