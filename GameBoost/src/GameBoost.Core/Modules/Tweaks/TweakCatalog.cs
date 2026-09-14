using GameBoost.Core.Abstractions;
using GameBoost.Core.Modules;

namespace GameBoost.Core.Modules.Tweaks;

/// <summary>
/// O catálogo da seção 5.8, inteiro e por extenso.
///
/// Duas ausências são propositais e valem tanto quanto o que está aqui:
///
/// - **Mitigações de Spectre/Meltdown** não entram. Desligar rende alguns por
///   cento em CPU antiga e abre um buraco de segurança real. O spec diz "Nunca",
///   e nem como item bloqueado isso aparece: item na tela vira ideia na cabeça
///   de alguém.
/// - **VBS / Isolamento de núcleo** aparece, mas só de leitura. O GameBoost
///   mostra o estado, explica o custo e abre a tela da Microsoft. Quem decide
///   trocar segurança por FPS é o dono da máquina, na tela dele.
/// </summary>
public static class TweakCatalog
{
    public const string CategoriaJogos = "Jogos";
    public const string CategoriaSistema = "Sistema";
    public const string CategoriaRede = "Rede";
    public const string CategoriaEntrada = "Mouse e teclado";
    public const string CategoriaEnergia = "Energia";
    public const string CategoriaSeguranca = "Segurança";

    private const string GameConfigStore = @"System\GameConfigStore";
    private const string GameBar = @"Software\Microsoft\GameBar";
    private const string SystemProfile = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
    private const string TarefasJogos = SystemProfile + @"\Tasks\Games";
    private const string GraphicsDrivers = @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers";
    private const string Dwm = @"SOFTWARE\Microsoft\Windows\Dwm";
    private const string Mouse = @"Control Panel\Mouse";
    private const string DeviceGuard = @"SYSTEM\CurrentControlSet\Control\DeviceGuard";

    public static IReadOnlyList<TweakDefinition> Todos { get; } = new[]
    {
        // ------------------------------------------------------------------
        // Jogos
        // ------------------------------------------------------------------

        new TweakDefinition
        {
            Id = "hags",
            Nome = "Agendamento de GPU por hardware (HAGS)",
            Categoria = CategoriaJogos,
            EfeitoReal = "Obrigatório para o Frame Generation do DLSS 3. Em placas RTX 30 ou "
                       + "superiores e RX 6000 ou superiores, com driver atual, costuma melhorar a "
                       + "regularidade dos quadros. Em CPU antiga pode piorar. Consome até 1 GB de VRAM. "
                       + "Se você não usa Frame Generation, ligue, jogue uma sessão e compare: é o único "
                       + "jeito honesto de saber se ajuda na sua máquina.",
            Evidencia = "Documentação da Microsoft sobre GPU scheduling e requisito declarado do "
                      + "DLSS 3 pela NVIDIA. O efeito em frame pacing varia por máquina, e por isso "
                      + "o texto acima não promete número.",
            Risco = RiskLevel.Medio,
            ExigeReboot = true,
            ComoDesfazer = "Voltar o valor anterior por aqui e reiniciar. Se o vídeo ficar instável, "
                         + "o Modo de Segurança do Windows também permite desfazer.",
            Valores = new[]
            {
                new TweakValue(RegistryRoot.LocalMachine, GraphicsDrivers, "HwSchMode", 2, RegistryValueKindLite.DWord)
            }
        },

        new TweakDefinition
        {
            Id = "game-mode",
            Nome = "Modo Jogo do Windows",
            Categoria = CategoriaJogos,
            EfeitoReal = "No Windows 11 e no 10 22H2 em diante ele segura o Windows Update e a "
                       + "instalação de driver enquanto você joga, que é o ganho concreto. Ligado é a "
                       + "recomendação padrão; desligue só se atrapalhar software de captura ou live.",
            Evidencia = "Comportamento documentado pela Microsoft a partir do 22H2. Versões "
                      + "anteriores tinham relatos de stutter, hoje resolvidos.",
            Risco = RiskLevel.Baixo,
            Recomendado = true,
            ComoDesfazer = "Desligar por aqui ou em Configurações, Jogos, Modo Jogo.",
            Valores = new[]
            {
                new TweakValue(RegistryRoot.CurrentUser, GameBar, "AutoGameModeEnabled", 1, RegistryValueKindLite.DWord)
            }
        },

        new TweakDefinition
        {
            Id = "game-dvr",
            Nome = "Gravação em segundo plano (Game DVR)",
            Categoria = CategoriaJogos,
            EfeitoReal = "Desliga a gravação contínua que a Game Bar mantém aberta. Ganho real em "
                       + "máquina modesta; em PC forte é pequeno. Você perde o atalho de gravar os "
                       + "últimos 30 segundos.",
            Evidencia = "Overhead medido pela própria Microsoft ao documentar a captura em "
                      + "segundo plano. A ordem de grandeza depende da GPU.",
            Risco = RiskLevel.Baixo,
            Recomendado = true,
            ComoDesfazer = "Religar por aqui, ou em Configurações, Jogos, Capturas.",
            Valores = new[]
            {
                new TweakValue(RegistryRoot.CurrentUser, GameConfigStore, "GameDVR_Enabled", 0, RegistryValueKindLite.DWord),
                new TweakValue(RegistryRoot.CurrentUser, GameBar, "UseNexusForGameBarEnabled", 0, RegistryValueKindLite.DWord)
            },
            Tipo = TweakKind.RegistroMultiplo
        },

        new TweakDefinition
        {
            Id = "tarefas-jogos",
            Nome = "Prioridade da categoria Jogos no agendador",
            Categoria = CategoriaJogos,
            EfeitoReal = "Marginal. Diz ao agendador multimídia que tarefas da categoria Jogos têm "
                       + "prioridade alta. Na maioria das máquinas modernas a diferença não aparece nem "
                       + "no gráfico de frametime. Está aqui porque é reversível e não custa nada, "
                       + "não porque vá render FPS.",
            Evidencia = "Chave documentada do Multimedia Class Scheduler Service. Nenhuma medição "
                      + "pública consistente mostra ganho em hardware atual.",
            Risco = RiskLevel.Baixo,
            ComoDesfazer = "Voltar os três valores anteriores por aqui.",
            Tipo = TweakKind.RegistroMultiplo,
            Valores = new[]
            {
                new TweakValue(RegistryRoot.LocalMachine, TarefasJogos, "GPU Priority", 8, RegistryValueKindLite.DWord),
                new TweakValue(RegistryRoot.LocalMachine, TarefasJogos, "Priority", 6, RegistryValueKindLite.DWord),
                new TweakValue(RegistryRoot.LocalMachine, TarefasJogos, "Scheduling Category", "High", RegistryValueKindLite.String)
            }
        },

        new TweakDefinition
        {
            Id = "mpo",
            Nome = "Desativar Multi-Plane Overlay (MPO)",
            Categoria = CategoriaJogos,
            EfeitoReal = "Só vale se você tem um sintoma específico: piscada preta, tremulação ou "
                       + "stutter na área de trabalho e em janelas, comum em setups NVIDIA com dois "
                       + "monitores. Se você não tem esse sintoma, não ligue: isto não dá FPS.",
            Evidencia = "Contorno reconhecido pela própria NVIDIA e pela Microsoft para o problema "
                      + "de flicker com MPO.",
            Risco = RiskLevel.Medio,
            ExigeReboot = true,
            ComoDesfazer = "Apagar o valor por aqui e reiniciar.",
            Valores = new[]
            {
                new TweakValue(RegistryRoot.LocalMachine, Dwm, "OverlayTestMode", 5, RegistryValueKindLite.DWord)
            }
        },

        // ------------------------------------------------------------------
        // Sistema
        // ------------------------------------------------------------------

        new TweakDefinition
        {
            Id = "system-responsiveness",
            Nome = "Reserva de CPU para tarefas de fundo",
            Categoria = CategoriaSistema,
            EfeitoReal = "Marginal. O Windows reserva 20% da CPU para tarefas multimídia de fundo; "
                       + "isto zera a reserva e devolve a fatia ao aplicativo em primeiro plano. "
                       + "Pode causar engasgo em áudio de gravação ou transmissão.",
            Evidencia = "Chave documentada do MMCSS. O valor padrão 20 existe justamente para "
                      + "proteger áudio, e é por isso que este está marcado como marginal e não como "
                      + "recomendado.",
            Risco = RiskLevel.Baixo,
            ComoDesfazer = "Voltar o valor anterior por aqui, normalmente 20.",
            Valores = new[]
            {
                new TweakValue(RegistryRoot.LocalMachine, SystemProfile, "SystemResponsiveness", 0, RegistryValueKindLite.DWord)
            }
        },

        new TweakDefinition
        {
            Id = "efeitos-visuais",
            Nome = "Efeitos visuais no melhor desempenho",
            Categoria = CategoriaSistema,
            EfeitoReal = "Tira animação de janela, sombra e transparência. Ajuda de verdade só em "
                       + "máquina bem fraca ou sem GPU dedicada. Em PC de jogo o ganho é nulo e a "
                       + "interface fica visivelmente mais crua.",
            Evidencia = "Afeta composição da área de trabalho, não o jogo em tela cheia, que nem "
                      + "passa pelo DWM.",
            Risco = RiskLevel.Baixo,
            ComoDesfazer = "Voltar o valor anterior por aqui, ou marcar Ajustar para melhor "
                         + "aparência nas opções de desempenho do Windows.",
            Valores = new[]
            {
                new TweakValue(RegistryRoot.CurrentUser,
                    @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects",
                    "VisualFXSetting", 2, RegistryValueKindLite.DWord)
            }
        },

        // ------------------------------------------------------------------
        // Rede
        // ------------------------------------------------------------------

        new TweakDefinition
        {
            Id = "network-throttling",
            Nome = "Desativar limite de tráfego multimídia",
            Categoria = CategoriaRede,
            EfeitoReal = "Marginal. O Windows limita o processamento de rede a 10 pacotes por "
                       + "milissegundo enquanto há áudio ou vídeo tocando. Isto remove o limite. Em "
                       + "conexão doméstica normal você não vai notar.",
            Evidencia = "Chave documentada do MMCSS. O limite foi criado para placas de rede de "
                      + "2007 e praticamente não morde em hardware atual.",
            Risco = RiskLevel.Baixo,
            ComoDesfazer = "Voltar o valor anterior por aqui, normalmente 10.",
            Valores = new[]
            {
                new TweakValue(RegistryRoot.LocalMachine, SystemProfile,
                    "NetworkThrottlingIndex", unchecked((int)0xFFFFFFFF), RegistryValueKindLite.DWord)
            }
        },

        // ------------------------------------------------------------------
        // Mouse e teclado
        // ------------------------------------------------------------------

        new TweakDefinition
        {
            Id = "aceleracao-mouse",
            Nome = "Desativar aceleração do mouse",
            Categoria = CategoriaEntrada,
            EfeitoReal = "Faz o cursor andar sempre a mesma distância para o mesmo movimento de "
                       + "mão, independente da velocidade. Não é ganho de desempenho, é consistência "
                       + "de mira, e a maioria dos jogos de tiro já ignora isso pelo input bruto. "
                       + "Vai atrapalhar se você está acostumado com a aceleração ligada.",
            Evidencia = "A aceleração é aplicada pelo Windows antes de o jogo receber o evento, "
                       + "exceto quando o jogo usa Raw Input, e aí esta chave não muda nada.",
            Risco = RiskLevel.Baixo,
            Tipo = TweakKind.RegistroMultiplo,
            ComoDesfazer = "Voltar os três valores por aqui, ou remarcar Aumentar a precisão do "
                         + "ponteiro nas propriedades do mouse.",
            Valores = new[]
            {
                new TweakValue(RegistryRoot.CurrentUser, Mouse, "MouseSpeed", "0", RegistryValueKindLite.String),
                new TweakValue(RegistryRoot.CurrentUser, Mouse, "MouseThreshold1", "0", RegistryValueKindLite.String),
                new TweakValue(RegistryRoot.CurrentUser, Mouse, "MouseThreshold2", "0", RegistryValueKindLite.String)
            }
        },

        // ------------------------------------------------------------------
        // Energia
        // ------------------------------------------------------------------

        new TweakDefinition
        {
            Id = "power-throttling",
            Nome = "Desativar limitação de energia do processador",
            Categoria = CategoriaEnergia,
            EfeitoReal = "Impede o Windows de reduzir o clock de processos que ele julga ociosos. "
                       + "Faz diferença em notebook, onde a limitação é agressiva. Em desktop na "
                       + "tomada, quase nada. Custa consumo e temperatura.",
            Evidencia = "Power Throttling documentado pela Microsoft, ativo por padrão em "
                      + "plataformas com Speed Shift.",
            Risco = RiskLevel.Baixo,
            ComoDesfazer = "Voltar o valor anterior por aqui.",
            Valores = new[]
            {
                new TweakValue(RegistryRoot.LocalMachine,
                    @"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling",
                    "PowerThrottlingOff", 1, RegistryValueKindLite.DWord)
            }
        },

        new TweakDefinition
        {
            Id = "hibernacao",
            Nome = "Desativar hibernação",
            Categoria = CategoriaEnergia,
            EfeitoReal = "Libera em disco o equivalente à sua memória RAM, apagando o "
                       + "hiberfil.sys. Em troca você perde a hibernação e a Inicialização Rápida, "
                       + "então o boot fica alguns segundos mais lento. Vale quando o SSD está "
                       + "apertado; não vale por desempenho.",
            Evidencia = "O hiberfil.sys ocupa por padrão cerca de 40% da RAM instalada, e 100% "
                      + "quando a hibernação completa está ativa. A página de Espaço mostra o tamanho "
                      + "real do seu.",
            Risco = RiskLevel.Medio,
            Tipo = TweakKind.Powercfg,
            ComandoLigar = "/h off",
            ComandoDesligar = "/h on",
            ComoDesfazer = "Religar por aqui, que roda powercfg /h on e recria o arquivo."
        },

        // ------------------------------------------------------------------
        // Segurança: aparece, explica, e não mexe
        // ------------------------------------------------------------------

        new TweakDefinition
        {
            Id = "vbs",
            Nome = "Isolamento de núcleo (VBS)",
            Categoria = CategoriaSeguranca,
            EfeitoReal = "A virtualização de segurança do Windows custa desempenho em jogos "
                       + "limitados por processador. As medições da comunidade ficam entre 5% e 15%, "
                       + "variando muito com o jogo e a CPU. Em troca ela é o que impede um driver "
                       + "malicioso de ler a memória do sistema. Num PC que também é o seu banco e o "
                       + "seu trabalho, desligar é uma troca ruim. Num PC só de jogo, é uma escolha "
                       + "defensável — sua, não do GameBoost.",
            Evidencia = "Faixa de 5% a 15% vem de testes independentes publicados por veículos de "
                      + "hardware. Não é número medido nesta máquina, e o texto diz isso.",
            Risco = RiskLevel.Alto,
            Tipo = TweakKind.SomenteLeitura,
            ComoDesfazer = "O GameBoost não altera isto. O botão abre a Segurança do Windows, "
                         + "em Segurança do dispositivo, Isolamento de núcleo.",
            Valores = new[]
            {
                new TweakValue(RegistryRoot.LocalMachine, DeviceGuard + @"\Scenarios\HypervisorEnforcedCodeIntegrity",
                    "Enabled", 1, RegistryValueKindLite.DWord)
            }
        }
    };

    public static TweakDefinition? PorId(string id)
        => Todos.FirstOrDefault(t => t.Id == id);
}
