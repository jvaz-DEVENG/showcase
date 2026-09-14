using GameBoost.Core.Abstractions;
using GameBoost.Core.Modules.GameMode;
using Xunit;

namespace GameBoost.Tests;

/// <summary>
/// Deteccao de jogo. O caso do Wallpaper Engine veio de um teste real: ele mora
/// em steamapps\common e estava sendo eleito "o jogo", o que faria o Modo Game
/// subir a prioridade do papel de parede em vez da do jogo.
/// </summary>
public sealed class GameDetectorTests
{
    private static ProcessInfo Proc(string nome, string? caminho, long ramMb = 800)
        => new(1000, nome, caminho, null, ramMb * 1024 * 1024, null, null, 1);

    private readonly GameDetector _detector = new();

    [Fact]
    public void Wallpaper_engine_nao_e_jogo_mesmo_morando_na_pasta_da_steam()
    {
        var processos = new[]
        {
            Proc("wallpaper64", @"D:\SteamLibrary\steamapps\common\wallpaper_engine\wallpaper64.exe", ramMb: 66)
        };

        Assert.Null(_detector.Detectar(processos));
        Assert.Null(_detector.DetectarPorNome(processos.Select(p => p.Name)));
    }

    [Theory]
    [InlineData("vrmonitor")]
    [InlineData("steamwebhelper")]
    [InlineData("aseprite")]
    [InlineData("gameoverlayui")]
    public void Utilitario_instalado_pela_steam_nao_e_jogo(string nome)
    {
        var processos = new[] { Proc(nome, $@"D:\SteamLibrary\steamapps\common\{nome}\{nome}.exe") };

        Assert.Null(_detector.Detectar(processos));
    }

    [Fact]
    public void Jogo_conhecido_e_detectado_pelo_nome()
    {
        var processos = new[] { Proc("cs2", @"D:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive\cs2.exe") };

        var jogo = _detector.Detectar(processos);

        Assert.NotNull(jogo);
        Assert.Equal("cs2", jogo!.Process.Name);
        Assert.Equal(100, jogo.Confianca);
    }

    [Fact]
    public void Jogo_desconhecido_em_pasta_de_launcher_precisa_ter_porte_de_jogo()
    {
        var leve = new[] { Proc("ferramentinha", @"D:\SteamLibrary\steamapps\common\x\ferramentinha.exe", ramMb: 40) };
        var pesado = new[] { Proc("jogoNovo", @"D:\SteamLibrary\steamapps\common\x\jogoNovo.exe", ramMb: 2500) };

        Assert.Null(_detector.Detectar(leve));
        Assert.NotNull(_detector.Detectar(pesado));
    }

    [Fact]
    public void Nome_conhecido_vence_processo_pesado_qualquer()
    {
        var processos = new[]
        {
            Proc("navegadorPesado", @"C:\Program Files\x\navegadorPesado.exe", ramMb: 4000),
            Proc("valorant", @"C:\Riot Games\VALORANT\live\valorant.exe", ramMb: 900)
        };

        var jogo = _detector.Detectar(processos);

        Assert.NotNull(jogo);
        Assert.Equal("valorant", jogo!.Process.Name);
    }

    [Fact]
    public void Maquina_sem_jogo_nao_inventa_um()
    {
        var processos = new[]
        {
            Proc("chrome", @"C:\Program Files\Google\chrome.exe", ramMb: 900),
            Proc("Code", @"C:\Users\User\AppData\Local\Programs\Code\Code.exe", ramMb: 700)
        };

        Assert.Null(_detector.DetectarPorNome(processos.Select(p => p.Name)));
    }
}
