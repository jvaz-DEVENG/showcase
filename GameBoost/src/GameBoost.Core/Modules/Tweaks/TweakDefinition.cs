using GameBoost.Core.Abstractions;
using GameBoost.Core.Modules;

namespace GameBoost.Core.Modules.Tweaks;

/// <summary>
/// Como o tweak é aplicado. Nem tudo é chave de registro.
/// </summary>
public enum TweakKind
{
    /// <summary>Um valor no registro. O caso comum.</summary>
    Registro,

    /// <summary>Vários valores de uma vez, aplicados e revertidos em bloco.</summary>
    RegistroMultiplo,

    /// <summary>Chama `powercfg`. Reversão pelo comando inverso.</summary>
    Powercfg,

    /// <summary>
    /// Só diagnóstico: o GameBoost mostra o estado e explica, mas não altera.
    /// É o caso do VBS / Isolamento de núcleo (seção 5.8) — mexer ali é decisão
    /// de segurança do usuário, tomada na tela da Microsoft, não aqui.
    /// </summary>
    SomenteLeitura
}

/// <summary>Um par chave/valor dentro de um tweak.</summary>
public sealed record TweakValue(
    RegistryRoot Root,
    string SubKey,
    string ValueName,
    object ValorLigado,
    RegistryValueKindLite Kind);

/// <summary>
/// Um ajuste do catálogo da seção 5.8, documentado em docs/TWEAKS.md.
///
/// A propriedade <see cref="EfeitoReal"/> é a que a UI mostra, e ela é honesta
/// por obrigação (regra 4): quando o ganho é marginal, o texto diz que é
/// marginal. Um booster que promete FPS em cima de `SystemResponsiveness` está
/// mentindo, e este não vai fazer isso.
/// </summary>
public sealed record TweakDefinition
{
    public required string Id { get; init; }
    public required string Nome { get; init; }
    public required string Categoria { get; init; }

    /// <summary>O que o ajuste faz de verdade, sem promessa inflada.</summary>
    public required string EfeitoReal { get; init; }

    /// <summary>De onde vem a afirmação acima. Vai para docs/TWEAKS.md.</summary>
    public required string Evidencia { get; init; }

    public required RiskLevel Risco { get; init; }
    public required string ComoDesfazer { get; init; }

    public TweakKind Tipo { get; init; } = TweakKind.Registro;

    /// <summary>Os valores gravados quando o tweak é ligado.</summary>
    public IReadOnlyList<TweakValue> Valores { get; init; } = Array.Empty<TweakValue>();

    /// <summary>Só vale depois de reiniciar, e a UI precisa avisar antes.</summary>
    public bool ExigeReboot { get; init; }

    /// <summary>Precisa de elevação para gravar (qualquer coisa em HKLM).</summary>
    public bool ExigeAdmin => Valores.Any(v => v.Root == RegistryRoot.LocalMachine)
                              || Tipo == TweakKind.Powercfg;

    /// <summary>
    /// Recomendado pelo GameBoost. **Recomendado não é pré-marcado** (regra 3):
    /// o selo aparece na lista, a caixa continua vazia.
    /// </summary>
    public bool Recomendado { get; init; }

    /// <summary>Comando de powercfg para ligar e para desligar, quando Tipo = Powercfg.</summary>
    public string? ComandoLigar { get; init; }
    public string? ComandoDesligar { get; init; }
}
