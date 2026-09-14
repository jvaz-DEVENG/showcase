# Catálogo de tweaks

Cada tweak: chave, valor, efeito real, evidência, risco e reversão. Todos desligados por
padrão, aplicados individualmente, sempre com snapshot em `state-backup.json`.

**A coluna "efeito real" é honesta por obrigação (regra 4).** Se um tweak é marginal, está
escrito que é marginal.

---

## Implementados na Fase 0 (Modo Game)

Aplicados em bloco pelo Modo Game, revertidos no desligamento. Ver
[`SystemSilencer`](../src/GameBoost.Core/Modules/GameMode/SystemSilencer.cs).

| Tweak | Chave | Valor | Efeito real | Risco |
|---|---|---|---|---|
| Notificações do Windows | `HKCU\Software\Microsoft\Windows\CurrentVersion\PushNotifications` → `ToastEnabled` | `0` | Impede notificação aparecendo sobre o jogo em tela cheia. Efeito de conforto, não de FPS | Baixo |
| Game DVR | `HKCU\System\GameConfigStore` → `GameDVR_Enabled` | `0` | Desliga gravação contínua em segundo plano. Ganho real em máquina fraca; em PC forte é pequeno | Baixo |
| Overlay da Game Bar | `HKCU\Software\Microsoft\GameBar` → `UseNexusForGameBarEnabled` | `0` | Evita a injeção do overlay em todo jogo aberto | Baixo |
| Painel da Game Bar | `HKCU\Software\Microsoft\GameBar` → `ShowStartupPanel` | `0` | Só evita o painel aparecer ao abrir o jogo | Baixo |

**Reversão:** cada um grava o valor anterior e o flag `ValorAnteriorExistia`. Quando a chave
não existia, a reversão **apaga** o valor em vez de gravar zero.

### Plano de energia

Aplica **Desempenho Máximo** se existir, senão **Alto desempenho**; guarda o GUID do plano
anterior e restaura no desligamento.

| Plano | GUID |
|---|---|
| Balanceado | `381b4222-f694-41f0-9685-ff5bb260df2e` |
| Alto desempenho | `8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c` |
| Desempenho Máximo | `e9a42b02-d5df-448d-aa00-03f14749eb61` |
| Economia de energia | `a1841308-3541-4fab-bc81-f71556f20b4a` |

**Evidência:** é o item nº 1 do consenso técnico de 2026 (seção 3.4 do spec) — evita que a
CPU baixe o clock no meio do jogo. Junto com a poda da inicialização, é o ajuste de maior
valor e o mais fácil de reverter.

### Pausa do Windows Update

Para o serviço `wuauserv` (só para, nunca altera `StartType`). Grava `ChangeRecord` com
`estavaRodando=true`.

**Por que importa reverter:** um travamento com o Update pausado deixaria a máquina sem
atualizações indefinidamente. Por isso a reversão acontece no desligamento, no "Reverter
tudo", na próxima abertura após travamento **e** no desinstalador.

---

## Catálogo da Fase 5 (ainda não implementados)

Listados aqui para que a pesquisa não se perca entre as fases. Nada disso está no código.

| Tweak | Onde | Efeito real | Risco |
|---|---|---|---|
| HAGS | `HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers` → `HwSchMode = 2` (exige reboot) | **Obrigatório para DLSS Frame Generation.** Melhora frame pacing em RTX 30+/RX 6000+ com driver atual, **piora** em CPU antiga ou certas versões de driver. Custa até 1 GB de VRAM — atenção em placas de 8 GB. É o único ajuste que precisa ser **testado** em vez de só ligado: oferecer "Testar com/sem" | Médio |
| Game Mode do Windows | `HKCU\Software\Microsoft\GameBar` → `AutoGameModeEnabled` | Positivo no 22H2+; bloqueia Windows Update durante o jogo. Recomendar **ligado**; desligar só em conflito com streaming | Baixo |
| Fullscreen Optimizations por exe | `HKCU\System\GameConfigStore` → `GameDVR_FSEBehaviorMode` e `HKCU\...\AppCompatFlags\Layers` → `DISABLEDXMAXIMIZEDWINDOWEDMODE` | Depende do jogo. Oferecer por perfil, nunca global | Baixo |
| Multi-Plane Overlay (MPO) | `HKLM\SOFTWARE\Microsoft\Windows\Dwm` → `OverlayTestMode = 5` | Resolve stutter e flicker em alguns setups NVIDIA. Não faz nada na maioria | Médio |
| Nagle | `HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\<GUID>` → `TcpAckFrequency`, `TCPNoDelay` | **Marginal.** Só ajuda em jogos com muitos pacotes TCP pequenos; a maioria usa UDP | Baixo |
| Network Throttling Index | `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile` → `NetworkThrottlingIndex = 0xFFFFFFFF` | **Marginal** | Baixo |
| SystemResponsiveness | mesma chave → `SystemResponsiveness = 0` | Prioriza o processo em foreground. Efeito pequeno | Baixo |
| Games task priority | `...\SystemProfile\Tasks\Games` → `GPU Priority = 8`, `Priority = 6`, `Scheduling Category = High` | **Marginal** | Baixo |
| Aceleração do mouse | `HKCU\Control Panel\Mouse` → `MouseSpeed = 0`, `MouseThreshold1/2 = 0` | Consistência de mira. Não muda FPS | Baixo |
| Power throttling off | `powercfg /setacvalueindex … PERFBOOSTMODE` e `HKLM\…\Power\PowerThrottling` → `PowerThrottlingOff` | Evita clock baixo em notebook | Baixo |
| Desativar hibernação | `powercfg /h off` | Libera espaço igual ao tamanho da RAM. **Perde a Inicialização Rápida** | Médio |
| Efeitos visuais "melhor desempenho" | `HKCU\…\Explorer\VisualEffects` | Ajuda só em máquina muito fraca; piora a experiência de uso | Baixo |
| Timer resolution | `NtSetTimerResolution(0.5 ms)` enquanto o Modo Game estiver ativo | O Windows 11 já lida bem; ajuda mais no 10. P/Invoke já existe em `NativeMethodsBridge` | Baixo |

### Nunca alterados

| Item | Por quê |
|---|---|
| **VBS / Core Isolation (Memory Integrity)** | Custo medido pela comunidade de 5 a 15% em jogos CPU-bound (até 25% em casos extremos), mas é proteção contra malware de kernel. O GameBoost **informa e mede, não desliga**: mostra o estado via WMI `Win32_DeviceGuard`, o impacto estimado e o caminho nas Configurações do Windows. Trade-off de PC exclusivo de jogo vs. máquina de trabalho/banco é decisão do usuário |
| **Mitigações de Spectre/Meltdown** | Troca segurança real por poucos % |
| **Defender, UAC, SmartScreen, assinatura de drivers** | Fora de escopo, sempre |
| **Limpeza de registro** | Sem ganho mensurável. Risco puro (regra 4) |
| **Desfragmentação de SSD** | Placebo, desgasta a unidade |
| **Atualização automática de driver** | Instalar driver sozinho é risco. O GameBoost só detecta a idade e abre a página oficial |

---

## Detector de reset pós-update

Atualizações grandes do Windows (24H2, 26H2) **resetam** Game Mode, HAGS, plano de energia,
efeitos visuais e Game Bar. Nenhum concorrente avisa disso.

Planejado para a Fase 1 como `Finding`: guardar a build do Windows junto do perfil aplicado
e, quando a build mudar e os valores não baterem mais, mostrar *"a atualização 26H2 desfez 4
ajustes seus"* com o botão de reaplicar.
