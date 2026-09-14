namespace GameBoost.Cli;

public enum CliCommand
{
    None,
    Scan,
    Report,
    Clean,
    RevertAll,
    GameModeOn,
    GameModeOff,
    Help
}

/// <summary>
/// Parser dos argumentos da secao 8 do spec. Sem dependencia externa: um
/// parser de 80 linhas custa menos que arrastar System.CommandLine para um
/// publish single-file.
/// </summary>
public sealed class CommandLineOptions
{
    public CliCommand Comando { get; private set; } = CliCommand.None;
    public string? ArquivoDeSaida { get; private set; }
    public string? Preset { get; private set; }
    public IReadOnlyList<string> Categorias { get; private set; } = Array.Empty<string>();

    /// <summary>Combina com qualquer comando: produz o mesmo relatorio sem tocar em nada.</summary>
    public bool DryRun { get; private set; }

    public bool Json { get; private set; }
    public string? Erro { get; private set; }

    public bool EhCli => Comando != CliCommand.None;

    public static CommandLineOptions Parse(string[] args)
    {
        var o = new CommandLineOptions();
        if (args.Length == 0)
            return o;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            var proximo = i + 1 < args.Length ? args[i + 1] : null;
            var proximoEhValor = proximo is not null && !proximo.StartsWith("--", StringComparison.Ordinal);

            switch (arg.ToLowerInvariant())
            {
                case "--scan":
                    o.Comando = CliCommand.Scan;
                    if (proximoEhValor) { o.ArquivoDeSaida = proximo; i++; }
                    break;

                case "--report":
                    o.Comando = CliCommand.Report;
                    if (proximoEhValor) { o.ArquivoDeSaida = proximo; i++; }
                    break;

                case "--clean":
                    o.Comando = CliCommand.Clean;
                    break;

                case "--revert-all":
                    o.Comando = CliCommand.RevertAll;
                    break;

                case "--gamemode":
                    if (!proximoEhValor)
                    {
                        o.Erro = "--gamemode exige on ou off.";
                        return o;
                    }

                    o.Comando = proximo!.ToLowerInvariant() switch
                    {
                        "on" => CliCommand.GameModeOn,
                        "off" => CliCommand.GameModeOff,
                        _ => CliCommand.None
                    };

                    if (o.Comando == CliCommand.None)
                        o.Erro = $"Valor invalido para --gamemode: {proximo}. Use on ou off.";

                    i++;
                    break;

                case "--preset":
                    if (proximoEhValor) { o.Preset = proximo!.ToLowerInvariant(); i++; }
                    else o.Erro = "--preset exige um valor (seguro ou completo).";
                    break;

                case "--categorias":
                    if (proximoEhValor)
                    {
                        o.Categorias = proximo!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        i++;
                    }
                    else
                    {
                        o.Erro = "--categorias exige a lista separada por virgula.";
                    }
                    break;

                case "--dry-run":
                    o.DryRun = true;
                    break;

                case "--json":
                    o.Json = true;
                    break;

                case "--help":
                case "-h":
                case "/?":
                    o.Comando = CliCommand.Help;
                    break;

                default:
                    if (arg.StartsWith("--", StringComparison.Ordinal))
                        o.Erro = $"Argumento desconhecido: {arg}";
                    break;
            }
        }

        if (o.Preset is not null && o.Preset is not ("seguro" or "completo"))
            o.Erro = $"Preset invalido: {o.Preset}. Use seguro ou completo.";

        return o;
    }

    public static string TextoDeAjuda => """
        GameBoost - central de otimizacao e manutencao do PC gamer

        Uso:
          GameBoost.exe                          abre a interface grafica
          GameBoost.exe --scan [arquivo]         relatorio de varredura do Modo Game
          GameBoost.exe --report saida.html      relatorio de saude completo
          GameBoost.exe --clean --preset seguro  limpeza com preset (seguro | completo)
          GameBoost.exe --clean --categorias temp,wu,wer
          GameBoost.exe --revert-all             reverte tudo do state-backup
          GameBoost.exe --gamemode on|off        liga ou desliga o Modo Game

        Modificadores:
          --dry-run    combina com qualquer comando, nao altera nada
          --json       saida em JSON em vez de texto
          --help       esta ajuda
        """;
}
