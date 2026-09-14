# GameBoost v2 · Especificação completa para o Claude Code

> Documento de trabalho para o Claude Code evoluir o GameBoost de "booster de jogos" para "central de otimização e manutenção do PC gamer". Leia inteiro antes de escrever código. As regras da seção 2 valem para todo o projeto.

---

## 0. Como usar este documento

1. Abra o repositório do GameBoost (`src/GameBoost/GameBoost.csproj`, `installer/GameBoost.iss`).
2. Rode a Fase 0 (seção 12) antes de qualquer feature nova: refatorar para módulos, criar a infraestrutura compartilhada.
3. Implemente as fases na ordem. Cada fase termina com build limpo, testes passando e um commit.
4. Toda decisão que fugir deste documento deve ser registrada em `docs/DECISOES.md` com data e motivo.

---

## 1. Estado atual (v1.0.0)

**Stack**: C# / .NET 9 / WPF, publish self-contained single-file, instalador Inno Setup 6, requer admin (manifest `requireAdministrator`).

**O que já existe e deve ser preservado:**

| Recurso | Implementação atual |
|---|---|
| Modo Game (ativar/desativar) | Varredura de processos, tela de confirmação pré-marcada, encerramento gracioso (3 s) depois forçado |
| Reabertura de apps essenciais (★) | `session.json` guarda caminho + argumentos |
| Limpeza de RAM | `EmptyWorkingSet` em todos os processos + purga da Standby List (`NtSetSystemInformation` / `SystemMemoryListInformation`) |
| Plano de energia | Alto desempenho / Desempenho Máximo, restaura o anterior |
| Prioridade do jogo | Detecção do jogo e prioridade Alta (nunca Realtime) |
| Silenciar sistema | Notificações, Game DVR, Game Bar overlay, pausa Windows Update |
| Blindagem | Lista de processos críticos, tudo em `C:\Windows`, antivírus/EDR via WMI, drivers, outras sessões |
| Snapshot e reversão | `state-backup.json`, botão "Reverter tudo", aviso na abertura se ficou ativo, reversão no desinstalador |
| Diagnóstico | `GameBoost.exe --scan relatorio.txt` |
| Persistência | `%LOCALAPPDATA%\GameBoost\` (`settings.json`, `session.json`, `state-backup.json`, `gameboost.log`) |

**Limitações conhecidas da v1:**
- Uma única função (Modo Game). Usuário instala, usa uma vez, esquece.
- Sem visão de "o que está pesando" na máquina. O usuário não sabe por que o PC está lento.
- Sem manutenção contínua (limpeza, inicialização, espaço em disco).
- Sem assinatura digital (SmartScreen e falsos positivos de antivírus).
- Sem auto-update.

---

## 2. Regras inegociáveis (valem para todo código novo)

1. **Reversível ou não existe.** Toda alteração de sistema (registro, serviço, plano de energia, tweak) grava o valor anterior em `state-backup.json` antes de aplicar e tem um caminho de reversão testado.
2. **Nunca deletar sem lixeira ou confirmação.** Arquivos de usuário vão para a Lixeira (`SHFileOperation` com `FOF_ALLOWUNDO`). Arquivos de sistema temporários podem ser deletados direto, mas só das pastas da whitelist (seção 5.2).
3. **Nada é pré-marcado se puder causar perda.** Jogos, launchers, IDEs com trabalho aberto, apps de gravação: aparecem, nunca vêm marcados.
4. **Honestidade sobre ganho.** Cada ação mostra o ganho real medido (MB liberados, apps fechados, segundos de boot economizados). Proibido inventar número. Proibido "limpeza de registro" (não traz ganho mensurável e é risco puro).
5. **Sem telemetria, sem anúncios, sem upsell.** Nada sai da máquina sem o usuário clicar em "Enviar diagnóstico".
6. **Blindagem primeiro.** As listas de proteção (seção 9) são aplicadas em todos os módulos, não só no Modo Game.
7. **Tudo em PT-BR**, com strings em arquivo de recursos (`Resources/Strings.pt-BR.resx`) para permitir EN depois.
8. **Log de tudo.** Toda ação grava linha estruturada em `gameboost.log` (timestamp, módulo, ação, alvo, resultado).
9. **Sem PowerShell quando houver API.** Prefira Win32/WMI/CIM via P/Invoke ou `System.Management`. PowerShell só para o que não tem API razoável (ex.: `Get-AppxPackage`), e sempre com `-NoProfile -NonInteractive -ExecutionPolicy Bypass`.
10. **Nunca travar a UI.** Toda varredura roda em `Task` com `IProgress<T>` e `CancellationToken`.

---

## 3. Pesquisa de mercado

> Referência de concorrentes e do que os usuários pedem. Validado com fontes de 2026 (guias de otimização, reviews e fóruns) em 13/09/2026. Fontes principais listadas em 3.5.

### 3.1 Concorrentes diretos (boosters de jogos)

| Produto | Preço | Pontos fortes | Reclamações recorrentes dos usuários |
|---|---|---|---|
| **Razer Cortex** | Grátis | Ainda o "free all-rounder" mais citado em 2026: suspende serviços, limpa RAM, restaura ao sair, contador de FPS, launcher unificado | Anúncios e promoções embutidos, ganho mínimo em PC bom, relatos de conflito com apps e jogos, consumo próprio de recursos, exige conta |
| **Wise Game Booster** | Grátis | Leve, simples, gerencia serviços e processos | Interface datada, sem monitoramento, pouco mantido |
| **IObit Advanced SystemCare / Game Booster** | Freemium (~US$20/ano) | Suite completa (limpeza, startup, drivers, privacidade) | Bloatware, pop-ups agressivos, instala outros produtos IObit, "limpeza de registro" placebo |
| **Smart Game Booster** | Freemium | Overlay FPS, monitor de temperatura, auto-boost | Falsos positivos de antivírus, upsell constante |
| **Game Fire / GearUP Booster / Chris-PC Game Booster** | Freemium | Preset por jogo antes de abrir, restauração ao sair, teste antes/depois dentro do app (Game Fire e GearUP) | Pouco conhecidos, parte das funções paga |
| **Process Lasso** | Freemium | Regras persistentes por processo, ProBalance, afinidade de CPU. Sempre citado como "o que funciona de verdade" | Curva de aprendizado, não é pensado pra leigo |
| **Windows Game Mode + Xbox Game Bar** | Grátis (nativo) | Integração de sistema, sem instalação | Game Bar consome recursos, Game Mode faz pouco, sem controle fino |
| **AMD Adrenalin / NVIDIA App** | Grátis | Overlay, gravação, tweaks de driver | Só GPU, não cuida de processos/disco |

### 3.2 Concorrentes indiretos (limpeza e manutenção)

| Categoria | Referências | O que fazem bem |
|---|---|---|
| Limpeza | CCleaner (Piriform/Gen), BleachBit (open source), Wise Disk Cleaner, Windows Storage Sense | Presets por app (navegadores, Discord, Steam shader cache), agendamento |
| Análise de disco | **WizTree 4.30 (mar/2026)**: lê a MFT, varre SSD grande em segundos, trata hard links, treemap, top arquivos, busca rápida, export CSV e localizador de duplicados desde a v4. WinDirStat, TreeSize Free, SpaceSniffer | É a referência unânime em 2026 ("free, blindingly fast"). Não apaga nada sozinho, só mostra e abre no Explorer. Guias sempre listam os mesmos suspeitos: hiberfil.sys, Windows.old (20 GB+ após update), temp, jogos de 80 a 150 GB, backups velhos |
| Desinstalação | **Bulk Crap Uninstaller** (Apache 2.0, acha desinstaladores quebrados/ocultos, portáteis, Store, Windows Features, Chocolatey, apps de lojas de jogos), **Revo Uninstaller Free 2.7.0 (mai/2026)** com Hunter Mode pra apps da Store e restos melhorados, Geek Uninstaller | Varredura de restos após desinstalar (com aviso de que não é garantida), desinstalação silenciosa em lote, remoção de bloatware da Store |
| Inicialização | Autoruns (Sysinternals), Gerenciador de Tarefas, Startup Delayer | Mostrar impacto real no boot, tarefas agendadas, serviços |
| Gerência de processos | Process Lasso, Process Explorer | Regras por processo persistentes, afinidade de CPU, ProBalance |
| Tweaks | Chris Titus WinUtil, Winaero Tweaker, O&O ShutUp10++, Atlas OS / ReviOS | Tweaks documentados, reversíveis, com explicação de cada um |
| Monitoramento | HWiNFO64, MSI Afterburner + RTSS, LibreHardwareMonitor, LatencyMon | Temperaturas, clocks, throttling, DPC latency, overlay em jogo |
| Drivers | Driver Booster (IObit), DDU, Snappy Driver Installer | Detectar driver de GPU desatualizado (mas instalar driver automaticamente é risco) |

### 3.3 O que os usuários realmente pedem (Reddit r/pcgaming, r/Windows11, r/pcmasterrace, Steam forums, reviews da Microsoft Store)

**Querem:**
1. Saber **o que está pesando** o PC agora (o "por que está lento?").
2. **Espaço em disco**: onde estão os GB perdidos (shader cache, Windows.old, downloads antigos, jogos que não jogam mais).
3. **Desinstalar de verdade**, inclusive bloatware da Store e apps que reaparecem.
4. **Inicialização limpa**: o PC demora pra ligar e não sabem o que desativar com segurança.
5. **Ganho de FPS honesto**: dizer claramente o que faz diferença (fechar Chrome/Discord overlay, plano de energia, driver de GPU, HAGS, Fullscreen Optimizations) e o que é placebo.
6. **Leve, sem anúncio, sem conta, sem assinatura**, de preferência portátil.
7. **Explicação em cada opção**: "o que isso faz, qual o risco, como desfazer".
8. **Não quebrar nada**: medo de tweak que trava Windows Update, some com o Wi-Fi, quebra a impressora.
9. **Perfis por jogo**: ao abrir Valorant faz X, ao abrir Cyberpunk faz Y.
10. **Temperatura e throttling**: saber se o FPS caiu porque a CPU/GPU está esquentando.

**Odeiam:**
- Limpeza de registro, "otimização de DNS mágica", "acelerador de RAM" sem número.
- Pop-up de upgrade, instalação de toolbar, envio de dados.
- App que fecha o anti-cheat ou o próprio jogo.
- Alterações sem explicação e sem "desfazer".

### 3.4 Consenso técnico de 2026 sobre o que realmente dá ganho

Os guias mais sérios convergem: cerca de seis ajustes explicam quase todo o ganho real, o resto é placebo, já é padrão ou troca segurança por poucos %.

1. **Plano de energia em Alto desempenho / Melhor desempenho** (evita clock baixo).
2. **Podar inicialização e apps em background** (junto com o item 1, são os dois de maior valor e os mais fáceis de reverter).
3. **Game Mode do Windows ligado** (versão 22H2+ ajuda e evita Windows Update durante o jogo; só desligar se houver conflito).
4. **Game Bar / gravação em segundo plano desligados.**
5. **Jogo em SSD** (lançamentos de 2025/2026 já exigem SSD e engasgam em HDD).
6. **Driver de GPU atualizado da fonte oficial** e monitor configurado na taxa de atualização certa (Windows volta pra 60 Hz depois de driver ou troca de cabo com frequência).

Pontos que exigem teste, não regra:
- **HAGS**: obrigatório para DLSS Frame Generation; melhora frame pacing em GPUs recentes (RTX 30+, RX 6000+) com driver atual, mas causa stutter em CPUs antigas ou certas versões de driver. É "o único ajuste que você precisa testar em vez de só ligar". Custa até 1 GB de VRAM (atenção em placas de 8 GB).
- **VBS / Memory Integrity (Core Isolation)**: vem ligado por padrão no Windows 11 e os guias medem 5 a 15% de FPS em jogos CPU-bound (até 25% em casos extremos). É recurso de segurança contra malware de kernel. Consenso: desligar só em PC exclusivo de jogo, manter em máquina de trabalho/banco. Por isso o GameBoost **informa e mede, mas não desliga**: mostra o estado, o impacto estimado e o caminho nas Configurações do Windows.
- **Updates grandes do Windows (24H2, 26H2) resetam** Game Mode, HAGS, plano de energia, efeitos visuais e Game Bar. O GameBoost deve detectar isso e avisar "seu perfil foi desfeito por uma atualização".

Placebo ou risco confirmados: limpeza de registro, "defrag" em SSD, updaters de driver automáticos, tweaks agressivos de serviço.

### 3.5 Fontes consultadas (set/2026)
- Frozen Tweaks, Gear-Rank, SmoothFPS, Switchblade Gaming, TechFixGrid: guias de otimização Windows 11 2026 (VBS, HAGS, Game Mode, 26H2).
- iTechGuides, TechBloat, TechBre, Techlasi, Gitnux, Worldmetrics: rankings de boosters 2026.
- Neat Net Tricks e Swastiktechnology: reviews WizTree 2026; RottenWifi: BCU, Revo 2.7.0, Storage Sense.
- Steam Community e Tom's Guide: percepção de usuário sobre boosters ("snake oil", "bloatware em PC bem cuidado").
- Microsoft Learn: SmartScreen reputation, opções de assinatura de código, Azure Artifact Signing.

### 3.6 Posicionamento do GameBoost v2

**"O booster que mostra o que faz e desfaz tudo."** Diferenciais:
- Transparência total (cada botão explica antes, mede depois, reverte sempre).
- Zero anúncios, zero telemetria, PT-BR nativo.
- Diagnóstico de gargalo em linguagem humana ("seu Chrome está usando 40% da CPU com 62 abas").
- Portátil opcional (mesmo exe roda sem instalar).
- **Prova de ganho**: medição antes/depois em cada ação (Game Fire e GearUP já fazem isso e os reviews valorizam muito).
- **Detector de reset pós-update**: nenhum concorrente avisa que o Windows desfez os ajustes.

---

## 4. Arquitetura alvo

### 4.1 Estrutura de pastas

```
GameBoost/
├── src/
│   ├── GameBoost.Core/              # Lógica pura, sem WPF. Testável.
│   │   ├── Abstractions/            # Interfaces (IProcessService, IRegistryService, IFileSystem, IClock...)
│   │   ├── Native/                  # P/Invoke centralizado (Kernel32, Psapi, NtDll, Advapi32, Shell32, PowrProf)
│   │   ├── Safety/                  # Blindagem: ProtectedProcesses, ProtectedPaths, ProtectedServices, AntiCheatList
│   │   ├── State/                   # Snapshot/rollback (StateBackup, ChangeRecord, RollbackEngine)
│   │   ├── Logging/                 # Logger estruturado (JSON lines + texto legível)
│   │   ├── Settings/                # Settings, perfis, migração de versão
│   │   └── Modules/
│   │       ├── GameMode/            # (existente, refatorado)
│   │       ├── Cleaner/             # Limpeza de temporários e caches
│   │       ├── Uninstaller/         # Desinstalador Win32 + Store + restos
│   │       ├── DiskAnalyzer/        # Análise de espaço (MFT + fallback)
│   │       ├── Bottleneck/          # Monitor e diagnóstico de gargalos
│   │       ├── Startup/             # Inicialização, tarefas agendadas
│   │       ├── Services/            # Serviços do Windows
│   │       ├── Tweaks/              # Tweaks de jogos reversíveis
│   │       ├── Network/             # Diagnóstico e ajustes de rede
│   │       ├── Drivers/             # Verificação de drivers de GPU
│   │       ├── Profiles/            # Perfis por jogo
│   │       └── HealthReport/        # Relatório e pontuação
│   ├── GameBoost.App/               # WPF: Views, ViewModels, Converters, Themes
│   │   ├── Views/
│   │   ├── ViewModels/
│   │   ├── Controls/                # Treemap, Gauge, ProcessList, ConfirmDialog
│   │   ├── Themes/                  # Dark.xaml (padrão), Light.xaml
│   │   └── Resources/Strings.pt-BR.resx
│   ├── GameBoost.Cli/               # Modo linha de comando (--scan, --clean, --report)
│   └── GameBoost.Tests/             # xUnit + NSubstitute
├── installer/GameBoost.iss
├── docs/
│   ├── DECISOES.md
│   ├── TWEAKS.md                    # Catálogo de cada tweak: chave, valor, efeito, risco, fonte
│   └── PROTECAO.md                  # Listas de blindagem documentadas
├── CLAUDE.md                        # Regras resumidas para o Claude Code (gerar a partir da seção 2)
└── README.md
```

### 4.2 Padrões

- **MVVM** com CommunityToolkit.Mvvm (`[ObservableProperty]`, `[RelayCommand]`).
- **DI** com `Microsoft.Extensions.DependencyInjection`. Toda classe de módulo recebe interfaces, nunca `static`.
- **Cada módulo expõe** `IModule` com: `ScanAsync(progress, ct)` → `ScanResult`, `ApplyAsync(selection, ct)` → `ApplyResult`, `RevertAsync(changeIds)`.
- **Cada item acionável** é um `ActionItem { Id, Categoria, Titulo, Descricao, Risco (Baixo/Medio/Alto), GanhoEstimado, PreMarcado, ComoDesfazer }`. A UI é genérica: uma lista de `ActionItem` com checkbox, badge de risco e botão "?" com a explicação.
- **Elevação**: manter `requireAdministrator`. Adicionar modo "somente leitura" quando rodar sem admin (varreduras funcionam, ações ficam desabilitadas com aviso).

---

## 5. Módulos: especificação funcional

Cada módulo abaixo tem: objetivo, fonte de dados/API, itens gerados, regras, UI, critérios de aceite.

### 5.1 Modo Game (existente, melhorias)

**Melhorias:**
- Detecção automática de jogo: monitorar criação de processos (WMI `Win32_ProcessStartTrace` ou polling a cada 2 s) e cruzar com (a) lista de jogos conhecidos, (b) executáveis dentro de `steamapps/common`, `Epic Games`, `Riot Games`, `Battle.net`, `GOG Galaxy/Games`, `XboxGames`, (c) janela em fullscreen exclusivo/borderless ocupando 100% da tela. Ao detectar, oferecer "Ativar Modo Game para <jogo>?" via notificação (ou ativar direto se o perfil do jogo mandar).
- Timer resolution: `NtSetTimerResolution(0.5 ms)` enquanto Modo Game ativo (Windows 11 já lida bem, mas em 10 ajuda). Reverter ao sair.
- Afinidade de CPU opcional para o jogo (perfil avançado): permitir reservar núcleos. Padrão desligado.
- Modo "Só o essencial": encerrar apenas processos com >2% CPU ou >300 MB RAM nos últimos 30 s (evita lista gigante).
- Exibir ganho: RAM livre antes/depois, nº de processos fechados, % CPU em background antes/depois.

**Critérios de aceite:** ativar e desativar 10 vezes seguidas sem vazar estado; matar o app durante o modo ativo e reabrir mostra o aviso de restauração; jogo nunca aparece pré-marcado.

### 5.2 Limpeza de arquivos temporários e caches

**Objetivo:** liberar espaço com segurança, mostrando o que será apagado e quanto ocupa.

**Categorias e alvos (whitelist, só estes caminhos):**

| Categoria | Caminhos | Risco | Pré-marcado |
|---|---|---|---|
| Temp do usuário | `%TEMP%` (arquivos não bloqueados, >24 h) | Baixo | Sim |
| Temp do sistema | `C:\Windows\Temp` | Baixo | Sim |
| Prefetch | `C:\Windows\Prefetch\*.pf` mais antigos que 30 dias | Baixo | Não (afeta boot por alguns dias) |
| Windows Update cache | `C:\Windows\SoftwareDistribution\Download` (parar serviço `wuauserv` antes) | Baixo | Sim |
| Delivery Optimization | via `IDOManager` ou `Get-DeliveryOptimizationStatus`; simples: `Clear-DeliveryOptimizationCache` não existe, usar Limpeza de Disco API `cleanmgr /sageset` presets ou pasta `%WINDIR%\ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache` | Baixo | Sim |
| Windows.old | `C:\Windows.old` (só se existir; usar `cleanmgr` handler "Previous Windows installation(s)" ou takeown + delete) | Médio | Não |
| Lixeira | `SHEmptyRecycleBin` | Médio | Não |
| Miniaturas / cache de ícones | `%LOCALAPPDATA%\Microsoft\Windows\Explorer\thumbcache_*.db`, `iconcache_*.db` | Baixo | Não |
| Logs do Windows | `C:\Windows\Logs\CBS\*.log` antigos, `%WINDIR%\Panther` (não), `C:\ProgramData\Microsoft\Windows\WER\ReportQueue` | Baixo | Sim (só WER) |
| Dumps de memória | `C:\Windows\Minidump`, `C:\Windows\MEMORY.DMP` | Baixo | Sim |
| Cache de navegadores | Chrome/Edge/Brave/Opera (`User Data\<Profile>\Cache`, `Code Cache`, `GPUCache`), Firefox (`cache2`). NUNCA cookies, senhas, histórico | Baixo | Sim, só se navegador fechado |
| Discord | `%APPDATA%\discord\Cache`, `Code Cache`, `GPUCache` | Baixo | Sim, se fechado |
| Spotify | `%LOCALAPPDATA%\Spotify\Storage`, `Data` | Baixo | Não |
| Shader cache | NVIDIA: `%LOCALAPPDATA%\NVIDIA\DXCache`, `%LOCALAPPDATA%\NVIDIA\GLCache`, `%PROGRAMDATA%\NVIDIA Corporation\NV_Cache`; AMD: `%LOCALAPPDATA%\AMD\DxCache`, `GLCache`; DirectX: `%LOCALAPPDATA%\D3DSCache`; Steam: `Steam\steamapps\shadercache` | Médio (jogos vão engasgar ao recompilar) | Não, com explicação |
| Steam | `Steam\appcache\httpcache`, `Steam\logs`, `Steam\dumps` | Baixo | Sim |
| Epic / Riot / Battle.net | Logs e caches de cada launcher (documentar caminhos em `TWEAKS.md`) | Baixo | Sim |
| Instaladores esquecidos | `%USERPROFILE%\Downloads\*.exe|*.msi|*.zip` > 30 dias | Alto | Nunca. Apenas listar em "Sugestões" |

**Regras:**
- Varredura calcula tamanho por categoria antes de mostrar. Exibir total "até X GB".
- Arquivos em uso (`IOException` sharing violation) são pulados e contados em "não foi possível remover N arquivos (em uso)".
- Cache de navegador/Discord só limpa se o processo não estiver rodando; senão, oferece "Fechar e limpar".
- Nunca tocar em: `AppData\Roaming` de jogos (saves), `Documents`, `Pictures`, `OneDrive`, qualquer pasta com `.git`.
- Guardar histórico: `cleanup-history.json` com data, categorias, MB liberados.
- Agendamento opcional: tarefa no Agendador (`schtasks`) semanal com `GameBoost.exe --clean --preset seguro`.

**Critérios de aceite:** rodar limpeza duas vezes seguidas, segunda vez libera ~0; nenhum arquivo fora da whitelist é tocado (teste com `IFileSystem` fake registrando todos os caminhos); tamanho reportado bate com o `du` real com margem de 5%.

### 5.3 Desinstalador de aplicativos

**Objetivo:** listar tudo instalado (Win32, Store/UWP, portáteis detectados), permitir desinstalação em lote e limpar restos.

**Fontes:**
- Registro: `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall`, `HKLM\SOFTWARE\WOW6432Node\...\Uninstall`, `HKCU\...\Uninstall`. Ler `DisplayName`, `DisplayVersion`, `Publisher`, `InstallDate`, `InstallLocation`, `EstimatedSize`, `UninstallString`, `QuietUninstallString`, `WindowsInstaller` (MSI → `msiexec /x {GUID} /qn`).
- Store/UWP: `Get-AppxPackage -AllUsers` via PowerShell (ou `PackageManager` da WinRT via `Windows.Management.Deployment`). Marcar `NonRemovable`, `IsFramework` e pacotes de sistema como protegidos.
- Uso real: cruzar com `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\UserAssist` (decodificar ROT13) e `LastWriteTime` dos executáveis para mostrar "último uso" e "nunca usado".
- Tamanho real: `EstimatedSize` do registro é pouco confiável; se `InstallLocation` existir, calcular tamanho da pasta em background.

**Lista de bloatware conhecido (sugestão, nunca pré-marcado):** Candy Crush, Spotify (Store), TikTok, Xbox TCUI (não remover se for jogar), Microsoft Solitaire, 3D Viewer, Clipchamp, LinkedIn, McAfee/Norton trial, Booking.com, Disney+, Netflix (Store shortcut), OEM utilities (HP Wolf, Lenovo Vantage é útil, cuidado), Cortana, Feedback Hub, Get Help, Mixed Reality Portal.

**Nunca desinstalar (protegido):** Visual C++ Redistributables, .NET Runtimes, DirectX, drivers de GPU/áudio/chipset, Microsoft Edge (WebView2 é dependência), Xbox Identity Provider e Xbox Game Services (anti-cheat e jogos do Game Pass dependem), antivírus ativo, qualquer app do publisher "Microsoft Corporation" da lista de frameworks.

**Fluxo:**
1. Lista com busca, filtros (Store / Win32 / Grandes / Nunca usados / Bloatware sugerido), ordenação por tamanho e último uso.
2. Seleção múltipla → "Desinstalar N apps" → tela de confirmação com badge de risco.
3. Execução sequencial: preferir `QuietUninstallString`; se não houver, rodar `UninstallString` e mostrar aviso "este app abre o desinstalador próprio". Timeout de 10 min por app.
4. Após cada desinstalação, **varredura de restos**: pasta `InstallLocation` residual, `%APPDATA%\<Publisher|Nome>`, `%LOCALAPPDATA%\<Nome>`, `%PROGRAMDATA%\<Nome>`, chaves `HKCU\Software\<Nome>` e `HKLM\Software\<Nome>`. Só sugerir se o nome tiver match exato (nunca substring curta). Restos vão para a Lixeira / chave exportada em `.reg` antes de apagar.
5. Ponto de restauração do sistema opcional antes de lote grande (`Checkpoint-Computer` via PowerShell ou `SystemRestore` WMI), com aviso de que o Windows limita a 1 por 24 h por padrão.

**Aba "Restos de apps antigos"** (pedido de 13/09/2026, além da varredura pós-desinstalação):

- Varrer `%APPDATA%`, `%LOCALAPPDATA%` e `%PROGRAMDATA%` e listar pastas cujo nome **não corresponde a nenhum app instalado**.
- Match **exato**, ignorando maiúsculas. Nunca substring curta.
- Ignorar pastas de Microsoft, Windows, NVIDIA, AMD e Intel, e tudo que estiver na lista de proteção (seção 9).
- Mostrar tamanho da pasta e data da última modificação.
- **Nunca pré-marcado.** Remoção vai para a Lixeira (`FOF_ALLOWUNDO`).
- A lista de pastas ignoradas é documentada em `docs/PROTECAO.md`.

**Critérios de aceite:** lista bate com "Aplicativos instalados" do Windows (±2 itens de diferença aceitável por apps ocultos); desinstalação silenciosa de um app MSI de teste funciona; restos de app removido são detectados; nenhum item protegido aparece selecionável.

### 5.4 Analisador de espaço em disco

**Objetivo:** responder "onde foram parar meus GB" em segundos, estilo WizTree.

**Implementação:**
- **Caminho rápido (NTFS):** ler a MFT diretamente com `FSCTL_ENUM_USN_DATA` (`DeviceIoControl` em `\\.\C:`), reconstruir a árvore por `ParentFileReferenceNumber`, obter tamanho via `FSCTL_GET_RETRIEVAL_POINTERS` ou, mais simples, via `NtQueryDirectoryFile`/`FindFirstFileEx` com `FIND_FIRST_EX_LARGE_FETCH` só nos diretórios já enumerados. Alternativa aceitável na primeira versão: `FindFirstFileEx` recursivo paralelizado com `FIND_FIRST_EX_LARGE_FETCH`, que já é bem mais rápido que `DirectoryInfo`.
- **Fallback (FAT/exFAT/sem admin):** enumeração recursiva normal.
- Resultado em árvore com tamanho agregado, contagem de arquivos, extensão dominante.

**UI:**
- **Treemap** (controle próprio em WPF, retângulos por tamanho, cor por categoria: jogos, vídeo, instaladores, cache, sistema, outros). Clique para descer nível, breadcrumb para voltar.
- **Top 100 maiores arquivos** e **Top 50 maiores pastas**.
- **Categorias inteligentes:** Jogos (pastas de launchers), Vídeos (>500 MB `.mp4/.mkv`), Instaladores (`.exe/.msi/.iso` em Downloads), Caches, Windows.old, Arquivos de hibernação (`hiberfil.sys`) e paginação (`pagefile.sys`) com explicação e opção "Desativar hibernação (`powercfg /h off`)" com badge de risco Médio.
- Ações por item: abrir no Explorer, mover para Lixeira, "Ignorar sempre".
- Duplicados (fase posterior): hash parcial (primeiros 64 KB + tamanho) e só depois hash completo dos candidatos.

**Critérios de aceite:** varredura de 500 GB com 1 milhão de arquivos em < 30 s via MFT em SSD; treemap responde a clique em < 100 ms; nunca oferece apagar nada dentro de `C:\Windows`, `Program Files` (só via Desinstalador) ou pastas de saves.

### 5.5 Diagnóstico de gargalos (o "por que está lento?")

**Objetivo:** mostrar em tempo real e em linguagem simples o que está consumindo CPU, GPU, RAM, disco e rede, e quem é o culpado.

**Coleta (intervalo 1 s, buffer de 5 min em memória):**
- **CPU:** total e por processo (`PerformanceCounter` ou delta de `Process.TotalProcessorTime`; preferir `NtQuerySystemInformation(SystemProcessInformation)` que traz tudo numa chamada). Frequência atual vs. base (`Win32_Processor.CurrentClockSpeed` é lento; usar contador `\Processor Information(_Total)\% Processor Performance`).
- **RAM:** `GlobalMemoryStatusEx`, Standby List, Commit, por processo (Working Set privado).
- **GPU:** contadores `\GPU Engine(*)\Utilization Percentage` e `\GPU Adapter Memory(*)\Dedicated Usage` (disponíveis no Windows 10 1709+). Por processo via instância do contador (`pid_XXXX_luid...`). Temperatura e clocks via **LibreHardwareMonitorLib** (NuGet, MPL 2.0) para NVIDIA/AMD/Intel.
- **Disco:** `\PhysicalDisk(*)\% Idle Time`, fila, MB/s; por processo via ETW (`Microsoft.Diagnostics.Tracing.TraceEvent`, kernel provider `DiskIO`) ou, mais simples, `Process` IO counters delta (`GetProcessIoCounters`).
- **Rede:** `\Network Interface(*)\Bytes Total/sec`; por processo via `GetExtendedTcpTable` + ETW `TcpIp` provider (fase posterior).
- **Temperaturas:** CPU package, GPU core, NVMe (LibreHardwareMonitor). Detectar **throttling**: CPU com `% Processor Performance` < 80% enquanto uso > 70% e temperatura > 90 °C.
- **Latência DPC/ISR** (opcional, avançado): ETW kernel `DPC`/`Interrupt` events para apontar driver problemático (estilo LatencyMon). Marcar como experimental.

**Diagnóstico automático (regras, cada uma gera um `Finding` com texto humano e ação sugerida):**

| Regra | Texto exemplo | Ação sugerida |
|---|---|---|
| Processo > 25% CPU por 30 s fora do jogo | "Chrome está usando 38% da CPU (62 abas abertas)" | Fechar / adicionar ao Modo Game |
| RAM disponível < 10% | "Só 1,2 GB de RAM livre. Discord + Chrome usam 4,1 GB" | Limpar RAM / fechar apps |
| Standby List > 40% da RAM | "6 GB de RAM presos em cache do sistema" | Purgar Standby |
| Disco do sistema > 90% ocupado | "C: com 94% de uso. SSD cheio fica lento" | Abrir Analisador de Disco |
| Disco com % Idle < 20% por 30 s | "Seu disco está no limite. Windows Search está indexando" | Pausar indexação / ver processo |
| GPU > 95% e FPS baixo | "GPU no máximo. Reduza resolução/qualidade ou ative DLSS/FSR" | Informativo |
| CPU > 95% e GPU < 60% em jogo | "Gargalo de CPU: o processador não alimenta a placa de vídeo" | Fechar apps / prioridade |
| Throttling térmico | "CPU a 97 °C reduzindo clock. Limpe o cooler / ajuste curva" | Informativo |
| Plano de energia balanceado em jogo | "Plano Balanceado ativo durante o jogo" | Ativar Alto desempenho |
| Driver de GPU > 6 meses | "Driver NVIDIA de fev/2026. Nova versão disponível" | Abrir página oficial |
| Muitos apps na inicialização | "23 programas iniciam com o Windows (boot estimado +48 s)" | Abrir Inicialização |
| Windows Search / Defender / Update usando disco | "Windows Defender fazendo varredura completa" | Informativo (nunca desativar Defender) |
| Xbox Game Bar / Game DVR ligado | "Gravação em segundo plano ativa" | Desativar (reversível) |
| Build do Windows mudou desde a última execução e tweaks/perfil não batem mais | "A atualização 26H2 desfez 4 ajustes seus" | Reaplicar perfil |
| Monitor a 60 Hz sendo capaz de mais (`EnumDisplaySettings`) | "Seu monitor de 165 Hz está em 60 Hz" | Abrir configurações de vídeo |
| VBS / Memory Integrity ativo | "Isolamento do núcleo ativo (custo típico 5 a 15% em jogos CPU-bound)" | Informativo com link |
| HDD como disco do jogo | "Jogo instalado em HDD. Mova para SSD" | Informativo |
| Memória em single channel | Via `Win32_PhysicalMemory` contagem de pentes | Informativo |

**UI:** painel com 5 medidores (CPU, GPU, RAM, Disco, Rede) + temperatura, gráfico de 60 s, tabela "Top 10 processos" por recurso selecionado, e lista de `Findings` ordenada por impacto. Modo compacto "sempre no topo" (janela pequena) para deixar ao lado do jogo em segundo monitor.

**Critérios de aceite:** coleta consome < 1,5% de CPU e < 60 MB de RAM; funciona sem LibreHardwareMonitor (temperaturas ficam "indisponível"); Findings não repetem a cada segundo (debounce de 30 s).

### 5.6 Gerenciador de inicialização

**Fontes:** `HKCU/HKLM\...\Run`, `RunOnce`, pasta Startup do usuário e comum, `HKLM\...\Explorer\StartupApproved` (estado ativado/desativado igual ao Gerenciador de Tarefas), Tarefas Agendadas com gatilho "no logon" (`Microsoft.Win32.TaskScheduler` NuGet ou `schtasks /query /xml`), serviços com início Automático de terceiros.

**Dados por item:** nome, publisher, caminho, assinado digitalmente (sim/não, via `WinVerifyTrust` ou `X509Certificate.CreateFromSignedFile`), impacto no boot (ler `HKCU\...\Explorer\StartupApproved` não traz impacto; estimar via tamanho do exe + nº de DLLs importadas, ou usar dados de `Windows Performance` se disponíveis; mostrar como Baixo/Médio/Alto estimado).

**Ações:** Desativar (gravar em `StartupApproved` com o mesmo formato do Gerenciador de Tarefas, para o Windows mostrar coerente), Atrasar (criar tarefa agendada com delay de N s e desativar a entrada original), Abrir local, Ver no Desinstalador.

**Protegidos:** drivers de áudio/vídeo, antivírus, OneDrive (avisar), Bluetooth/touchpad OEM, SecurityHealth. Sugerir desativar: Spotify, Discord (se o usuário quiser abrir manualmente), Steam/Epic/EA app, Teams, Adobe Updater, iTunes Helper, Cortana.

### 5.7 Serviços do Windows

Lista de serviços com filtro "terceiros" e "otimizáveis para jogos". Tweaks reversíveis (guardar `StartType` anterior):

| Serviço | Ação sugerida | Risco |
|---|---|---|
| `SysMain` (Superfetch) | Desativar só em SSD com <16 GB RAM? Não. Manter. Apenas explicar | Médio |
| `WSearch` | Manual (busca do Explorer fica lenta) | Baixo |
| `DiagTrack` (telemetria) | Desativar | Baixo |
| `XblAuthManager`, `XblGameSave`, `XboxNetApiSvc` | Manter se usa Game Pass / anti-cheat da MS. Explicar | Médio |
| `Fax`, `RemoteRegistry`, `WMPNetworkSvc`, `MapsBroker` | Desativar | Baixo |
| `Spooler` | Manual se não tem impressora | Baixo |
| `wuauserv` | Nunca desativar. Só pausar dentro do Modo Game (já existe) | Alto |
| Serviços de antivírus, `WinDefend`, `SecurityHealthService` | Protegido | Alto |

### 5.8 Tweaks de jogos (catálogo documentado em `docs/TWEAKS.md`)

Cada tweak: chave/valor, efeito, evidência, risco, reversão. Todos desligados por padrão, aplicados individualmente, com snapshot.

| Tweak | Onde | Efeito real | Risco |
|---|---|---|---|
| Hardware-Accelerated GPU Scheduling (HAGS) | `HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers\HwSchMode = 2` (reboot) | Obrigatório para DLSS Frame Gen; melhora frame pacing em RTX 30+/RX 6000+ com driver atual, piora em CPU antiga. Usa até 1 GB de VRAM. Oferecer "Testar com/sem" com benchmark rápido | Médio |
| Game Mode do Windows | `HKCU\Software\Microsoft\GameBar\AutoGameModeEnabled` | Positivo no 22H2+ (bloqueia Update durante o jogo). Recomendar ligado; desligar só se der conflito com streaming | Baixo |
| Game DVR / Game Bar | `HKCU\System\GameConfigStore\GameDVR_Enabled = 0`, `HKCU\Software\Microsoft\GameBar\UseNexusForGameBarEnabled = 0` | Reduz overhead de gravação | Baixo |
| Fullscreen Optimizations por exe | `HKCU\System\GameConfigStore` `GameDVR_FSEBehaviorMode`, e `HKCU\...\AppCompatFlags\Layers` `DISABLEDXMAXIMIZEDWINDOWEDMODE` por jogo | Depende do jogo; oferecer por perfil | Baixo |
| Multi-Plane Overlay (MPO) | `HKLM\SOFTWARE\Microsoft\Windows\Dwm\OverlayTestMode = 5` | Resolve stutter/flicker em alguns setups NVIDIA | Médio |
| Nagle (TcpAckFrequency, TCPNoDelay) | `HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\<GUID>` | Ganho marginal em jogos com muitos pacotes pequenos; a maioria já usa UDP. Explicar honestamente | Baixo |
| Network Throttling Index | `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\NetworkThrottlingIndex = 0xFFFFFFFF` | Marginal | Baixo |
| SystemResponsiveness | mesma chave, `SystemResponsiveness = 0` | Prioriza foreground | Baixo |
| Games task priority | `...\SystemProfile\Tasks\Games` `GPU Priority = 8`, `Priority = 6`, `Scheduling Category = High` | Marginal | Baixo |
| Mouse: desativar aceleração | `HKCU\Control Panel\Mouse` `MouseSpeed=0`, `MouseThreshold1/2=0` | Consistência de mira | Baixo |
| Power throttling off para o jogo | `powercfg /setacvalueindex ... PERFBOOSTMODE` e `HKLM\...\Power\PowerThrottling\PowerThrottlingOff` | Evita clock baixo em notebook | Baixo |
| Desativar hibernação | `powercfg /h off` | Libera espaço = tamanho da RAM, perde Inicialização Rápida | Médio |
| Ultimate Performance plan | `powercfg /duplicatescheme e9a42b02-...` | Já existe no Modo Game | Baixo |
| Visual effects "melhor desempenho" | `HKCU\...\Explorer\VisualEffects` | Ajuda só em máquina muito fraca; afeta UX | Baixo |
| Timer resolution | Já no Modo Game | Baixo |
| Core Isolation / VBS (Memory Integrity) | **Não alterar.** Detectar via `msinfo32`/WMI `Win32_DeviceGuard` e mostrar Finding: "VBS ativo, custo medido pela comunidade de 5 a 15% em jogos CPU-bound". Botão abre Configurações → Segurança do Windows → Isolamento do núcleo. Explicar o trade-off (PC só de jogo vs. máquina de trabalho/banco) | Alto |
| Spectre/Meltdown mitigations | **Nunca.** | Alto |

### 5.9 Rede

- Diagnóstico: ping para 3 hosts (gateway, 1.1.1.1, servidor de jogo configurado), jitter, perda, DNS resolução tempo (comparar DNS atual vs. Cloudflare/Google/Quad9, mostrar tabela, deixar o usuário decidir).
- Ações: `ipconfig /flushdns`, resetar Winsock (`netsh winsock reset`, exige reboot, risco Médio), trocar DNS do adaptador (`SetDNSServerSearchOrder` via WMI, reversível).
- Mostrar quem está usando banda agora (`GetExtendedTcpTable` + contadores de processo).
- Wi-Fi: mostrar banda (2,4/5/6 GHz), sinal, canal, e recomendar cabo para jogo competitivo.

### 5.10 Drivers de GPU

- Detectar fabricante e versão (`Win32_VideoController.DriverVersion`, `DriverDate`) e traduzir para versão "de marketing" (NVIDIA: últimos 5 dígitos do DriverVersion → `5xx.xx`).
- Comparar com versão mais recente conhecida via arquivo JSON estático atualizado a cada release do GameBoost e, se web permitida pelo usuário, endpoint oficial (NVIDIA tem API não documentada; AMD/Intel via página). Sem web: só "seu driver tem N meses".
- **Nunca instalar driver.** Abrir a página oficial. Opcional: link para DDU com explicação de quando usar.

### 5.11 Perfis por jogo

- Perfil = `{ executavel, nome, acoes: [modo game on, plano energia X, prioridade Y, afinidade Z, tweaks por exe, apps a fechar, apps a reabrir], auto: bool }`.
- Detecção via 5.1. Ao fechar o jogo, reverter automaticamente.
- Biblioteca: varrer launchers para listar jogos instalados (Steam `libraryfolders.vdf` + `appmanifest_*.acf`; Epic `C:\ProgramData\Epic\EpicGamesLauncher\Data\Manifests\*.item`; GOG registry; Xbox `.GamingRoot`), mostrar tamanho e último jogado (para sugerir desinstalar jogos parados, que é onde estão os GB de verdade).

### 5.12 Relatório de saúde e pontuação

- Executa todas as varreduras em modo leitura e gera nota 0 a 100 por área (Desempenho, Espaço, Inicialização, Configuração para jogos) com os 5 principais `Findings`.
- Exportar `relatorio.html` (para o usuário mandar para quem dá suporte) e `relatorio.json`.
- `GameBoost.exe --report saida.html` na CLI.

### 5.13 Ferramentas rápidas (atalhos de coisas que o usuário procura)

- Reiniciar Explorer, reiniciar driver de vídeo (`Win+Ctrl+Shift+B` programático via `SendInput`), limpar cache DNS, esvaziar Lixeira, abrir Limpeza de Disco do Windows, criar ponto de restauração, verificar integridade (`sfc /scannow` e `DISM /RestoreHealth` com console embutido), teste de velocidade de disco (leitura/escrita sequencial 1 GB em arquivo temporário), informações do sistema (CPU, GPU, RAM canais/velocidade, discos e tipo, versão do Windows, BIOS) com botão copiar.

---

## 6. UI / UX

- **Tema escuro padrão**, acento único (manter identidade atual do GameBoost). Fluent-like, sem gradientes exagerados.
- **Navegação lateral** com ícones: Início (Modo Game + pontuação + findings), Diagnóstico, Limpeza, Espaço, Apps, Inicialização, Jogos (perfis), Tweaks, Rede, Ferramentas, Configurações.
- **Padrão de tela de módulo:** cabeçalho com botão "Varrer" e resumo (ex.: "12,4 GB podem ser liberados"), lista de `ActionItem` agrupada por categoria com checkbox, badge de risco (verde/amarelo/vermelho), botão "?" que abre painel lateral com "O que faz / Risco / Como desfazer", rodapé fixo com "Aplicar N selecionados" e "Reverter".
- **Tela de confirmação única e genérica** (reusar a do Modo Game) para tudo que altera o sistema.
- **Histórico** (Configurações → Histórico): toda ação aplicada, com botão "Desfazer" individual enquanto a reversão for possível.
- **Bandeja do sistema**: ícone com menu (Ativar Modo Game, Limpar RAM, Abrir, Sair). Opção "iniciar minimizado com o Windows" (desligada por padrão, e o GameBoost aparece na própria lista de inicialização, com honestidade).
- **Acessibilidade**: fontes escaláveis, navegação por teclado, contraste AA.
- **Onboarding** na primeira abertura: 3 telas explicando "reversível / sem telemetria / o que faz diferença de verdade" e oferecendo o Relatório de Saúde inicial.

---

## 7. Dados e persistência

Tudo em `%LOCALAPPDATA%\GameBoost\`:

| Arquivo | Conteúdo | Formato |
|---|---|---|
| `settings.json` | Preferências, tema, whitelist, essenciais, agendamentos, `schemaVersion` | JSON |
| `profiles/*.json` | Um perfil por jogo | JSON |
| `state-backup.json` | Pilha de `ChangeRecord { Id, Modulo, Tipo (Registry/Service/Power/File/Startup), Alvo, ValorAnterior, ValorNovo, Data, Revertido }` | JSON |
| `session.json` | Sessão do Modo Game | JSON |
| `history/*.json` | Histórico de ações por dia | JSON |
| `cache/disk-scan-<volume>.bin` | Último resultado da varredura de disco (para abrir instantâneo) | Binário |
| `gameboost.log` | Log texto rotacionado (5 MB × 5 arquivos) | Texto |
| `backups/*.reg` | Export de chaves antes de remover restos | REG |

Migração: ao abrir, se `schemaVersion` < atual, rodar migradores em sequência. Nunca apagar arquivo antigo, renomear para `.bak`.

Modo portátil: se existir `portable.txt` ao lado do exe, usar `.\data\` em vez de `%LOCALAPPDATA%`.

---

## 8. CLI (`GameBoost.Cli` ou argumentos no exe principal)

```
GameBoost.exe --scan [arquivo]            # relatório de varredura do Modo Game (já existe)
GameBoost.exe --report saida.html         # relatório de saúde completo
GameBoost.exe --clean --preset seguro     # limpeza com preset (seguro | completo)
GameBoost.exe --clean --categorias temp,wu,wer
GameBoost.exe --revert-all                # reverte tudo do state-backup
GameBoost.exe --gamemode on|off
GameBoost.exe --dry-run                   # combina com qualquer comando, não altera nada
```

Saída em texto e `--json`.

---

## 9. Blindagem (documentar em `docs/PROTECAO.md`)

Aplicada por `SafetyGuard` central, consultado por todos os módulos.

- **Processos:** lista da v1 + anti-cheats (`EasyAntiCheat`, `BEService`, `vgc`, `vgtray` (Vanguard), `FACEIT`, `PnkBstr`, `XignCode`, `GameGuard`, `nProtect`, `Ricochet`, `EAAntiCheat`), launchers nunca pré-marcados, `RTSS`, `MSIAfterburner` (usuário pode querer), overlays de captura (aviso).
- **Caminhos:** `C:\Windows` (exceto whitelist de limpeza), `Program Files*` (exceto via Desinstalador), pastas de usuário (`Documents`, `Pictures`, `Videos`, `Desktop`, `OneDrive`), qualquer pasta contendo `.git`, `saves`, `Saved Games`, `%APPDATA%` de jogos.
- **Serviços:** núcleo do Windows, antivírus, `wuauserv` (só pausa), rede (`Dhcp`, `Dnscache`, `nsi`, `NlaSvc`), áudio (`Audiosrv`, `AudioEndpointBuilder`), `Themes`, `DcomLaunch`, `RpcSs`, `Winmgmt`, `EventLog`, `Schedule`, `TrustedInstaller`, `CryptSvc`, `BFE`, `mpssvc`.
- **Registro:** nunca tocar em `HKLM\SYSTEM\CurrentControlSet\Services\*` além de `Start`, nunca em `Winlogon`, `Image File Execution Options`, `Policies`.
- **Apps:** ver 5.3.
- **Tweaks proibidos:** VBS/Core Isolation, mitigações de CPU, Defender, UAC, SmartScreen, assinatura de drivers.

Teste obrigatório: `SafetyGuardTests` com casos para cada lista.

---

## 10. Build, distribuição e atualização

- `dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishReadyToRun=true` (ReadyToRun reduz tempo de abertura).
- Trimming: **não** habilitar (WPF e reflexão quebram).
- Versão semântica em `Directory.Build.props`, propagada para Inno Setup.
- **Assinatura de código (validado set/2026):**
  - **Azure Artifact Signing** (ex-Trusted Signing, a partir de US$ 9,99/mês) é a opção barata da Microsoft, mas para **desenvolvedor individual só aceita EUA e Canadá**; organizações precisam estar em EUA/Canadá/UE/UK e outros poucos países. **Brasil está fora**, tanto PF quanto PJ. Não contar com isso.
  - **Certificado OV** de uma CA (Certum, Sectigo, DigiCert, GlobalSign): caminho viável para o Brasil. Desde jun/2023 a chave privada precisa ficar em token USB ou HSM em nuvem (a CA fornece). Faixa de US$ 70 a 300/ano. Certum costuma ser a mais barata para dev individual.
  - **EV não pula mais o SmartScreen** desde 2024: OV e EV passam pelo mesmo processo de reputação. Não vale pagar EV.
  - **Microsoft Store (MSIX)**: a Microsoft reassina o pacote e o SmartScreen nunca aparece. Exige `runFullTrust` e o app precisa passar na certificação (o Modo Game e as ações de sistema podem gerar perguntas). Vale como meta de médio prazo.
  - Independente da opção: reputação só acumula com downloads, então assinar sempre com a mesma identidade e submeter cada release ao Microsoft Security Intelligence para reduzir falsos positivos.
- **Auto-update:** Velopack (sucessor do Squirrel, .NET moderno) apontando para GitHub Releases. Checar na abertura, baixar em background, aplicar no próximo início. Configurável. Sem web, apenas mostrar "versão X disponível" se o usuário clicar "Verificar".
- **Portátil:** o mesmo exe publicado, zipado, com `portable.txt`.
- **CI:** GitHub Actions `windows-latest`: build, testes, publish, Inno Setup, anexar ao Release. Gerar `SHA256SUMS.txt`.
- **Instalador:** manter Inno Setup. Adicionar página de opções: atalho na bandeja ao iniciar, associar tarefa de limpeza semanal (desligada por padrão), instalar para todos os usuários vs. só atual.

---

## 11. Testes

- **Unit (xUnit):** `SafetyGuard`, `RollbackEngine` (aplicar → reverter → estado igual), parsers (registro Uninstall, `libraryfolders.vdf`, UserAssist ROT13, versão de driver NVIDIA), regras de `Bottleneck` (dado um snapshot de métricas, gera os Findings esperados), whitelist do Cleaner (todo caminho gerado pertence à whitelist).
- **Integração (rodam só em Windows, tag `[Trait("Category","Windows")]`):** limpeza real em pasta temp sintética; leitura de MFT em volume real (só leitura); desinstalação de um MSI de teste (criar com WiX ou usar `msiexec` em pacote conhecido); startup toggle em chave `HKCU` fake.
- **Manual (checklist em `docs/QA.md`):** SmartScreen, antivírus, máquina com 8 GB, notebook com iGPU + dGPU, usuário sem admin, Windows 10 22H2 e Windows 11 24H2+, disco FAT32 externo, jogo com Vanguard rodando (nada pode ser fechado).
- **Dry-run** em todos os módulos: `--dry-run` produz o mesmo relatório sem tocar em nada; teste garante que `IFileSystem`/`IRegistry` fakes não recebem chamadas de escrita.

---

## 12. Fases de implementação (ordem obrigatória)

Cada fase: branch própria, PR, changelog em `CHANGELOG.md`.

### Fase 0 · Fundação (refatorar sem mudar comportamento)
- Separar `GameBoost.Core` (sem WPF) de `GameBoost.App`.
- Extrair `SafetyGuard`, `RollbackEngine`, `Logger`, `Settings` com `schemaVersion`.
- Criar `IModule` / `ActionItem` e portar o Modo Game para esse contrato.
- Criar a tela genérica de lista de `ActionItem` + confirmação.
- Configurar DI, CommunityToolkit.Mvvm, xUnit, GitHub Actions.
- Gerar `CLAUDE.md` com a seção 2 e o mapa de pastas.
- **Pronto quando:** app faz exatamente o que a v1 fazia, com testes de `SafetyGuard` e `RollbackEngine` passando.

### Fase 1 · Diagnóstico de gargalos (5.5) + Relatório (5.12)
Motivo: é o que faz o usuário abrir o app todo dia e entender o valor do resto.

### Fase 2 · Limpeza (5.2) + Ferramentas rápidas (5.13)

### Fase 3 · Analisador de disco (5.4) + Biblioteca de jogos (parte de 5.11)

### Fase 4 · Desinstalador (5.3) + Inicialização (5.6)

### Fase 5 · Tweaks (5.8) + Serviços (5.7) + Rede (5.9) + Drivers (5.10)

### Fase 6 · Perfis por jogo com detecção automática (5.1 + 5.11), bandeja, onboarding

### Fase 7 · Distribuição: Velopack, CI completo, portátil, assinatura, submissão de falsos positivos

### Backlog (depois da v2.0)
- Overlay de FPS in-game (integração com RTSS via shared memory, ou hook próprio em D3D11/12, que é trabalho grande e risco com anti-cheat; preferir RTSS).
- Duplicados e arquivos grandes esquecidos com sugestão de compressão NTFS (`compact /c /exe:xpress8k` em pastas de jogos, que economiza 20 a 40% sem perda de FPS em SSD).
- Curva de ventoinha (fora de escopo, depende de hardware).
- Versão EN e loja Microsoft Store (MSIX com `runFullTrust`).
- Benchmark rápido antes/depois (rodar 30 s de carga sintética e comparar) para provar ganho.

---

## 13. Prompts sugeridos para o Claude Code (um por fase)

**Fase 0**
> Leia `GAMEBOOST_V2_SPEC.md` inteiro. Execute a Fase 0: refatore o projeto para a estrutura da seção 4.1 sem alterar nenhum comportamento visível. Crie `SafetyGuard`, `RollbackEngine`, `Logger` e `Settings` conforme seções 2, 7 e 9. Porte o Modo Game para `IModule`/`ActionItem`. Adicione xUnit com testes para `SafetyGuard` e `RollbackEngine`. Gere `CLAUDE.md` com as regras da seção 2. Ao final, `dotnet build` e `dotnet test` limpos, e liste em `docs/DECISOES.md` qualquer desvio.

**Fase 1**
> Implemente o módulo `Bottleneck` (seção 5.5) e `HealthReport` (5.12). Coleta a cada 1 s com buffer de 5 min, contadores de CPU/GPU/RAM/disco/rede, LibreHardwareMonitorLib opcional para temperaturas. Implemente todas as regras de diagnóstico da tabela como classes `IFindingRule` testáveis. Tela com 5 medidores, gráfico de 60 s, top 10 processos e lista de Findings. Exporte `relatorio.html`. Meça e garanta < 1,5% CPU.

**Fases seguintes:** mesmo padrão: "Implemente o módulo X conforme seção 5.Y, respeitando seções 2 e 9, com testes das regras, dry-run funcional e entrada no `docs/TWEAKS.md` ou `docs/PROTECAO.md` quando aplicável."

---

## 14. Glossário rápido (para textos da UI)

- **Standby List:** memória que o Windows guarda "por precaução" com dados já usados. Liberar ajuda quando falta RAM, não aumenta FPS por si só.
- **Shader cache:** arquivos que o jogo gera para não recompilar efeitos. Apagar libera espaço, mas o jogo engasga nas primeiras horas até recriar.
- **HAGS:** deixa a placa de vídeo gerenciar a própria fila de trabalho. Ajuda em alguns casos, atrapalha em outros. Teste com e sem.
- **Throttling:** o processador reduz a velocidade para não superaquecer. Sinal de cooler sujo ou pasta térmica velha.
- **Gargalo de CPU:** o processador não consegue preparar quadros rápido o suficiente; a placa de vídeo fica esperando.

---

*Documento gerado em 13/09/2026 para o projeto GameBoost, com pesquisa de mercado validada por fontes web na mesma data. Autor do produto: Jonathan Vaz.*
