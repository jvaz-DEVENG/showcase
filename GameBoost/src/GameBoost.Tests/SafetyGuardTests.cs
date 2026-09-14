using GameBoost.Core.Abstractions;
using GameBoost.Core.Safety;
using GameBoost.Core.Settings;
using Xunit;

namespace GameBoost.Tests;

/// <summary>
/// Teste obrigatorio da secao 9: um caso para cada lista de blindagem.
/// Se algum destes cair, o app nao pode ser publicado.
/// </summary>
public sealed class SafetyGuardTests
{
    private const int SessaoAtual = 1;
    private const int PidProprio = 4242;

    private static SafetyGuard Criar(AppSettings? settings = null, params string[] antivirus)
        => new(settings ?? new AppSettings(), SessaoAtual, PidProprio,
            new HashSet<string>(antivirus, StringComparer.OrdinalIgnoreCase),
            @"C:\Windows");

    private static ProcessInfo Processo(
        string nome,
        int pid = 1000,
        string? caminho = @"C:\Program Files\App\app.exe",
        long ram = 100L * 1024 * 1024,
        int sessao = SessaoAtual)
        => new(pid, nome, caminho, null, ram, null, null, sessao);

    // ---------------- Processos criticos ----------------

    [Theory]
    [InlineData("csrss")]
    [InlineData("wininit")]
    [InlineData("winlogon")]
    [InlineData("services")]
    [InlineData("lsass")]
    [InlineData("smss")]
    [InlineData("dwm")]
    [InlineData("svchost")]
    [InlineData("explorer")]
    [InlineData("audiodg")]
    public void Processo_critico_do_windows_e_protegido(string nome)
    {
        var veredito = Criar().CheckProcess(Processo(nome, caminho: @"C:\Windows\System32\" + nome + ".exe"));

        Assert.True(veredito.Protegido);
        Assert.Equal(ProtectionReason.ProcessoCritico, veredito.Motivo);
    }

    [Fact]
    public void Nome_com_extensao_exe_tambem_e_reconhecido()
    {
        var veredito = Criar().CheckProcess(Processo("LSASS.EXE", caminho: @"D:\falso\lsass.exe"));

        Assert.True(veredito.Protegido);
        Assert.Equal(ProtectionReason.ProcessoCritico, veredito.Motivo);
    }

    [Fact]
    public void Pids_do_kernel_sao_protegidos()
    {
        var veredito = Criar().CheckProcess(Processo("System", pid: 4, caminho: null));

        Assert.True(veredito.Protegido);
    }

    // ---------------- Anti-cheats ----------------

    [Theory]
    [InlineData("EasyAntiCheat")]
    [InlineData("BEService")]
    [InlineData("vgc")]
    [InlineData("vgtray")]
    [InlineData("FACEITService")]
    [InlineData("PnkBstrA")]
    [InlineData("XignCode")]
    [InlineData("GameGuard")]
    [InlineData("nProtect")]
    [InlineData("Ricochet")]
    [InlineData("EAAntiCheat")]
    public void Anti_cheat_nunca_pode_ser_encerrado(string nome)
    {
        var veredito = Criar().CheckProcess(Processo(nome, caminho: @"C:\Program Files\EAC\" + nome + ".exe"));

        Assert.True(veredito.Protegido);
        Assert.Equal(ProtectionReason.AntiCheat, veredito.Motivo);
    }

    [Fact]
    public void Anti_cheat_nunca_e_pre_marcado()
    {
        Assert.False(Criar().PodePreMarcar(Processo("vgc", caminho: @"C:\Program Files\Riot Vanguard\vgc.exe")));
    }

    // ---------------- Antivirus ----------------

    [Fact]
    public void Antivirus_da_lista_estatica_e_protegido()
    {
        var veredito = Criar().CheckProcess(Processo("MsMpEng", caminho: @"D:\qualquer\MsMpEng.exe"));

        Assert.True(veredito.Protegido);
        Assert.Equal(ProtectionReason.Seguranca, veredito.Motivo);
    }

    [Fact]
    public void Antivirus_detectado_por_wmi_e_protegido_mesmo_fora_da_lista()
    {
        var veredito = Criar(null, "AntivirusExotico")
            .CheckProcess(Processo("AntivirusExotico", caminho: @"C:\AV\av.exe"));

        Assert.True(veredito.Protegido);
        Assert.Equal(ProtectionReason.Seguranca, veredito.Motivo);
    }

    // ---------------- Caminho e sessao ----------------

    [Fact]
    public void Executavel_dentro_do_windows_e_protegido()
    {
        var veredito = Criar().CheckProcess(Processo("qualquercoisa", caminho: @"C:\Windows\System32\qualquercoisa.exe"));

        Assert.True(veredito.Protegido);
        Assert.Equal(ProtectionReason.DentroDoWindows, veredito.Motivo);
    }

    [Fact]
    public void WindowsApps_nao_conta_como_dentro_do_windows()
    {
        // Comparacao por segmento: C:\WindowsApps comeca com C:\Windows como texto.
        var veredito = Criar().CheckProcess(Processo("app", caminho: @"C:\WindowsApps\app\app.exe"));

        Assert.False(veredito.Protegido);
    }

    [Fact]
    public void Processo_de_outra_sessao_e_protegido()
    {
        var veredito = Criar().CheckProcess(Processo("bloco", sessao: 7));

        Assert.True(veredito.Protegido);
        Assert.Equal(ProtectionReason.OutraSessao, veredito.Motivo);
    }

    [Fact]
    public void O_proprio_gameboost_nunca_se_encerra()
    {
        var porPid = Criar().CheckProcess(Processo("qualquer", pid: PidProprio));
        var porNome = Criar().CheckProcess(Processo("GameBoost", pid: 999));

        Assert.Equal(ProtectionReason.ProprioApp, porPid.Motivo);
        Assert.Equal(ProtectionReason.ProprioApp, porNome.Motivo);
    }

    // ---------------- Lista do usuario ----------------

    [Fact]
    public void Lista_nunca_encerrar_do_usuario_e_respeitada()
    {
        var settings = new AppSettings { NuncaEncerrar = { "MeuApp.exe" } };
        var veredito = Criar(settings).CheckProcess(Processo("MeuApp"));

        Assert.True(veredito.Protegido);
        Assert.Equal(ProtectionReason.ListaDoUsuario, veredito.Motivo);
    }

    [Fact]
    public void Lista_sempre_encerrar_vence_a_lista_de_nunca_pre_marcados()
    {
        var settings = new AppSettings { SempreEncerrar = { "steam" } };

        Assert.True(Criar(settings).PodePreMarcar(Processo("steam", caminho: @"C:\Steam\steam.exe")));
    }

    [Fact]
    public void Lista_sempre_encerrar_nao_vence_a_blindagem()
    {
        // Um usuario nao pode se autorizar a matar o lsass editando o settings.json.
        var settings = new AppSettings { SempreEncerrar = { "lsass" } };

        Assert.False(Criar(settings).PodePreMarcar(Processo("lsass", caminho: @"D:\x\lsass.exe")));
    }

    // ---------------- Pre-marcacao (regra 3) ----------------

    [Theory]
    [InlineData("steam")]
    [InlineData("EpicGamesLauncher")]
    [InlineData("Battle.net")]
    [InlineData("RiotClientServices")]
    [InlineData("obs64")]
    [InlineData("devenv")]
    [InlineData("Code")]
    [InlineData("MSIAfterburner")]
    public void Launchers_gravadores_e_ides_aparecem_mas_nunca_pre_marcados(string nome)
    {
        var guard = Criar();
        var processo = Processo(nome, caminho: $@"C:\Program Files\{nome}\{nome}.exe");

        Assert.False(guard.CheckProcess(processo).Protegido);
        Assert.False(guard.PodePreMarcar(processo));
    }

    [Fact]
    public void Executavel_em_pasta_de_jogo_nunca_e_pre_marcado()
    {
        var processo = Processo("jogo", caminho: @"D:\SteamLibrary\steamapps\common\Cyberpunk 2077\bin\jogo.exe");

        Assert.False(Criar().PodePreMarcar(processo));
    }

    [Fact]
    public void App_comum_pode_ser_pre_marcado()
    {
        Assert.True(Criar().PodePreMarcar(Processo("chrome", caminho: @"C:\Program Files\Google\Chrome\chrome.exe")));
    }

    // ---------------- Servicos ----------------

    [Theory]
    [InlineData("DcomLaunch")]
    [InlineData("RpcSs")]
    [InlineData("Winmgmt")]
    [InlineData("EventLog")]
    [InlineData("Dhcp")]
    [InlineData("Dnscache")]
    [InlineData("Audiosrv")]
    [InlineData("WinDefend")]
    [InlineData("mpssvc")]
    [InlineData("TrustedInstaller")]
    public void Servico_essencial_nao_pode_ser_alterado(string nome)
    {
        var veredito = Criar().CheckService(nome, apenasPausar: false);

        Assert.True(veredito.Protegido);
        Assert.Equal(ProtectionReason.ServicoProtegido, veredito.Motivo);
    }

    [Fact]
    public void Windows_update_pode_ser_pausado_mas_nao_desabilitado()
    {
        var guard = Criar();

        Assert.False(guard.CheckService("wuauserv", apenasPausar: true).Protegido);
        Assert.True(guard.CheckService("wuauserv", apenasPausar: false).Protegido);
    }

    // ---------------- Caminhos ----------------

    [Fact]
    public void Caminho_fora_da_whitelist_e_bloqueado()
    {
        var veredito = Criar().CheckPath(@"C:\Windows\System32\drivers\etc\hosts", new[] { @"C:\Windows\Temp" });

        Assert.True(veredito.Protegido);
        Assert.Equal(ProtectionReason.CaminhoDeSistema, veredito.Motivo);
    }

    [Fact]
    public void Whitelist_vazia_nao_libera_nada()
    {
        Assert.True(Criar().CheckPath(@"C:\Windows\Temp\lixo.tmp", Array.Empty<string>()).Protegido);
    }

    [Fact]
    public void Caminho_dentro_da_whitelist_e_liberado()
    {
        Assert.False(Criar().CheckPath(@"C:\Windows\Temp\lixo.tmp", new[] { @"C:\Windows\Temp" }).Protegido);
    }

    [Theory]
    [InlineData(@"C:\Windows\Temp\projeto\.git\objects\abc")]
    [InlineData(@"C:\Windows\Temp\jogo\saves\slot1.sav")]
    [InlineData(@"C:\Windows\Temp\OneDrive\doc.txt")]
    [InlineData(@"C:\Windows\Temp\Saved Games\jogo\save.dat")]
    public void Segmento_proibido_bloqueia_mesmo_dentro_da_whitelist(string caminho)
    {
        var veredito = Criar().CheckPath(caminho, new[] { @"C:\Windows\Temp" });

        Assert.True(veredito.Protegido);
        Assert.Equal(ProtectionReason.SegmentoProibido, veredito.Motivo);
    }

    [Fact]
    public void Travessia_de_diretorio_nao_escapa_da_whitelist()
    {
        var veredito = Criar().CheckPath(@"C:\Windows\Temp\..\System32\config\SAM", new[] { @"C:\Windows\Temp" });

        Assert.True(veredito.Protegido);
    }

    [Fact]
    public void Pasta_pessoal_do_usuario_e_bloqueada()
    {
        var documentos = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var alvo = Path.Combine(documentos, "planilha.xlsx");

        // Whitelist larga de proposito: a protecao da pasta pessoal tem que valer mesmo assim.
        var veredito = Criar().CheckPath(alvo, new[] { Path.GetPathRoot(documentos)! });

        Assert.True(veredito.Protegido);
        Assert.Equal(ProtectionReason.CaminhoDoUsuario, veredito.Motivo);
    }
}
