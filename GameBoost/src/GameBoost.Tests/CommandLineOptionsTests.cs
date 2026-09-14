using GameBoost.Cli;
using Xunit;

namespace GameBoost.Tests;

public sealed class CommandLineOptionsTests
{
    [Fact]
    public void Sem_argumentos_abre_a_interface()
    {
        var o = CommandLineOptions.Parse(Array.Empty<string>());

        Assert.Equal(CliCommand.None, o.Comando);
        Assert.False(o.EhCli);
    }

    [Fact]
    public void Scan_com_arquivo_preserva_o_comportamento_da_v1()
    {
        var o = CommandLineOptions.Parse(new[] { "--scan", "relatorio.txt" });

        Assert.Equal(CliCommand.Scan, o.Comando);
        Assert.Equal("relatorio.txt", o.ArquivoDeSaida);
    }

    [Fact]
    public void Scan_sem_arquivo_escreve_no_console()
    {
        var o = CommandLineOptions.Parse(new[] { "--scan" });

        Assert.Equal(CliCommand.Scan, o.Comando);
        Assert.Null(o.ArquivoDeSaida);
    }

    [Theory]
    [InlineData("on", CliCommand.GameModeOn)]
    [InlineData("off", CliCommand.GameModeOff)]
    [InlineData("ON", CliCommand.GameModeOn)]
    public void Gamemode_aceita_on_e_off(string valor, CliCommand esperado)
    {
        Assert.Equal(esperado, CommandLineOptions.Parse(new[] { "--gamemode", valor }).Comando);
    }

    [Fact]
    public void Gamemode_sem_valor_reclama()
    {
        Assert.NotNull(CommandLineOptions.Parse(new[] { "--gamemode" }).Erro);
    }

    [Fact]
    public void Gamemode_com_valor_invalido_reclama()
    {
        Assert.NotNull(CommandLineOptions.Parse(new[] { "--gamemode", "talvez" }).Erro);
    }

    [Fact]
    public void Dry_run_combina_com_qualquer_comando()
    {
        var o = CommandLineOptions.Parse(new[] { "--gamemode", "on", "--dry-run" });

        Assert.Equal(CliCommand.GameModeOn, o.Comando);
        Assert.True(o.DryRun);
    }

    [Fact]
    public void Clean_aceita_preset_e_categorias()
    {
        var o = CommandLineOptions.Parse(new[] { "--clean", "--preset", "seguro", "--categorias", "temp, wu ,wer" });

        Assert.Equal(CliCommand.Clean, o.Comando);
        Assert.Equal("seguro", o.Preset);
        Assert.Equal(new[] { "temp", "wu", "wer" }, o.Categorias);
    }

    [Fact]
    public void Preset_invalido_reclama()
    {
        Assert.NotNull(CommandLineOptions.Parse(new[] { "--clean", "--preset", "turbo" }).Erro);
    }

    [Fact]
    public void Argumento_desconhecido_reclama()
    {
        Assert.NotNull(CommandLineOptions.Parse(new[] { "--otimizar-tudo" }).Erro);
    }

    [Fact]
    public void Flag_seguinte_nao_e_confundida_com_valor()
    {
        var o = CommandLineOptions.Parse(new[] { "--scan", "--json" });

        Assert.Null(o.ArquivoDeSaida);
        Assert.True(o.Json);
    }
}
