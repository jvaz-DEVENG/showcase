# Catálogo de tweaks

Cada tweak: chave, valor, efeito real, evidência, risco e reversão. Todos desligados por
padrão, aplicados individualmente, sempre com snapshot em `state-backup.json`.

**A coluna "efeito real" é honesta por obrigação (regra 4).** Se um tweak é marginal, está
escrito que é marginal.

---

## Implementados na Fase 5 (página Tweaks)

Catálogo completo da seção 5.8, aplicado item a item, cada um com ChangeRecord
antes da escrita. **Nada vem pré-marcado**, nem o que está marcado como
recomendado: recomendação é informação, não consentimento.

### Jogos

| Tweak | Chave | Valor | Efeito real | Risco |
|---|---|---|---|---|
| Agendamento de GPU por hardware (HAGS) | `HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers` → `HwSchMode` | `2` | Obrigatório para o Frame Generation do DLSS 3. Em RTX 30+ e RX 6000+ com driver atual costuma melhorar a regularidade dos quadros; em CPU antiga pode piorar. Usa até 1 GB de VRAM. **Exige reinício** | Médio |
| Modo Jogo do Windows | `HKCU\Software\Microsoft\GameBar` → `AutoGameModeEnabled` | `1` | No 22H2+ segura o Windows Update e a instalação de driver durante o jogo, que é o ganho concreto. Recomendado | Baixo |
| Gravação em segundo plano (Game DVR) | `HKCU\System\GameConfigStore` → `GameDVR_Enabled` e `HKCU\Software\Microsoft\GameBar` → `UseNexusForGameBarEnabled` | `0` | Desliga a gravação contínua. Ganho real em máquina modesta, pequeno em PC forte. Perde o atalho de gravar os últimos 30 s | Baixo |
| Prioridade da categoria Jogos | `HKLM\...\SystemProfile\Tasks\Games` → `GPU Priority`=8, `Priority`=6, `Scheduling Category`=High | — | **Marginal.** Nenhuma medição pública consistente mostra ganho em hardware atual. Está no catálogo por ser reversível e sem custo, não por render FPS | Baixo |
| Desativar Multi-Plane Overlay (MPO) | `HKLM\SOFTWARE\Microsoft\Windows\Dwm` → `OverlayTestMode` | `5` | Só vale com o sintoma: piscada preta, tremulação ou stutter na área de trabalho, comum em NVIDIA com dois monitores. **Não dá FPS.** Exige reinício | Médio |

### Sistema

| Tweak | Chave | Valor | Efeito real | Risco |
|---|---|---|---|---|
| Reserva de CPU para tarefas de fundo | `HKLM\...\SystemProfile` → `SystemResponsiveness` | `0` | **Marginal.** Devolve ao primeiro plano os 20% que o Windows reserva para multimídia de fundo. Pode causar engasgo em gravação e transmissão — o padrão 20 existe para proteger áudio | Baixo |
| Efeitos visuais no melhor desempenho | `HKCU\...\Explorer\VisualEffects` → `VisualFXSetting` | `2` | Ajuda só em máquina fraca ou sem GPU dedicada. Não afeta jogo em tela cheia, que nem passa pelo DWM | Baixo |

### Rede

| Tweak | Chave | Valor | Efeito real | Risco |
|---|---|---|---|---|
| Desativar limite de tráfego multimídia | `HKLM\...\SystemProfile` → `NetworkThrottlingIndex` | `0xFFFFFFFF` | **Marginal.** O limite de 10 pacotes/ms foi criado para placas de rede de 2007 e praticamente não morde em hardware atual | Baixo |

### Mouse e teclado

| Tweak | Chave | Valor | Efeito real | Risco |
|---|---|---|---|---|
| Desativar aceleração do mouse | `HKCU\Control Panel\Mouse` → `MouseSpeed`, `MouseThreshold1`, `MouseThreshold2` | `0` | Consistência de mira, não desempenho. Jogos que usam Raw Input já ignoram isto. Vai atrapalhar quem está acostumado com a aceleração ligada | Baixo |

### Energia

| Tweak | Onde | Efeito real | Risco |
|---|---|---|---|
| Desativar limitação de energia do processador | `HKLM\SYSTEM\CurrentControlSet\Control\Power\PowerThrottling` → `PowerThrottlingOff` = 1 | Faz diferença em notebook, onde a limitação é agressiva. Em desktop na tomada, quase nada. Custa consumo e temperatura | Baixo |
| Desativar hibernação | `powercfg /h off` | Libera em disco o equivalente à RAM instalada. Perde hibernação e Inicialização Rápida, e o boot fica alguns segundos mais lento. Vale por espaço, não por desempenho | Médio |

### Segurança: aparece, explica e não mexe

| Item | Por quê |
|---|---|
| Isolamento de núcleo (VBS) | Aparece **só de leitura**. As medições da comunidade põem o custo entre 5% e 15% em jogos limitados por CPU, e em troca é o que impede um driver malicioso de ler a memória do sistema. Num PC que também é banco e trabalho, desligar é troca ruim; num PC só de jogo é escolha defensável — do dono, na tela da Microsoft. O botão abre Segurança do Windows |
| Mitigações de Spectre/Meltdown | **Não estão no catálogo, nem bloqueadas.** Item na tela vira ideia na cabeça de alguém. Desligar rende alguns por cento em CPU antiga e abre um buraco de segurança real |

### Como o estado é lido

Um tweak "desligado" quase nunca significa valor zero: na maioria das chaves
significa **valor ausente**, com o Windows assumindo o padrão. Por isso a
reversão apaga o valor quando ele não existia antes, em vez de gravar zero —
confundir os dois é o que faz ferramenta de tweak deixar lixo que nunca sai.

A hibernação é lida do registro (`HibernateEnabled`, com `HibernateEnabledDefault`
como fallback), não pela existência de `C:\hiberfil.sys`: aquele arquivo é de
sistema e `File.Exists` devolve falso sem elevação, o que faria o app dizer "já
desligada" para quem abriu sem ser administrador.

---

## Serviços do Windows (seção 5.7, Fase 5)

Régua diferente da dos tweaks: um serviço desligado por engano não tira alguns
FPS, tira a impressora, a busca ou o Game Pass.

### Dá para desativar

| Serviço | Sugestão | O que você perde |
|---|---|---|
| `DiagTrack` | Desativado | Nada de sistema. O motivo de desligar é privacidade, não FPS |
| `MapsBroker` | Desativado | Mapas offline do aplicativo Mapas |
| `RemoteRegistry` | Desativado | Nada. Já vem desativado no Windows doméstico |
| `WMPNetworkSvc` | Desativado | Compartilhar a biblioteca do Media Player na rede |
| `Fax` | Desativado | Fax por modem |
| `WSearch` | Manual | A busca do Iniciar e do Explorer fica lenta. Vale em HDD; em SSD o índice compensa |
| `Spooler` | Manual | Impressão, **inclusive salvar em PDF** |

### Aparece para explicar por que **não** mexer

| Serviço | Por quê |
|---|---|
| `SysMain` | Toda lista de otimização manda desligar, e está errada. Em SSD ele quase não faz leitura antecipada; o que faz hoje é gerenciar memória. Desligar não devolve RAM, devolve cache — e o Windows usa RAM livre para cache de qualquer jeito |
| `XblAuthManager`, `XboxNetApiSvc` | Sem eles o Game Pass não abre e alguns anti-cheat falham. Parados não custam nada; desligados quebram o Game Pass |
| `wuauserv` | Nunca desativar. Máquina sem atualização de segurança é problema maior que qualquer FPS. O Modo Game já pausa o Update durante o jogo e religa depois |
| `WinDefend`, `SecurityHealthService` | O GameBoost não desliga antivírus, nem o do Windows nem o de terceiros |

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

## Do catálogo da seção 5.8, o que ficou de fora

Três itens do spec não entraram na Fase 5, e cada um tem um motivo diferente.

| Tweak | Por que não entrou |
|---|---|
| Fullscreen Optimizations por executável | `GameDVR_FSEBehaviorMode` e `DISABLEDXMAXIMIZEDWINDOWEDMODE` são ajustes **por jogo**, não globais. O spec já diz "oferecer por perfil", e perfil por jogo é a Fase 6 |
| Nagle (`TcpAckFrequency`, `TCPNoDelay`) | A chave fica em `...\Tcpip\Parameters\Interfaces\<GUID>`, uma por adaptador, e o ganho é marginal mesmo no melhor caso: a maioria dos jogos usa UDP, onde Nagle não existe. Entra junto com o perfil por jogo, que sabe qual jogo justifica mexer |
| Timer resolution | O P/Invoke existe em `NativeMethodsBridge`, mas a resolução de timer só vale enquanto o processo que pediu está vivo. Faz sentido dentro da sessão do Modo Game, não como um botão que liga e desliga sozinho. Fase 6 |

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
