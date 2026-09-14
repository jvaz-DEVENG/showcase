# Checklist de QA manual

O que os testes automatizados não cobrem. Nenhuma release sai sem isto.

Legenda: `[ ]` não testado · `[x]` passou · `[!]` falhou, ver observação.

---

## Fase 0 — paridade com a v1.0.0

A v1 foi reconstruída a partir do `LEIA-ME.txt` (ver `DECISOES.md`), então a paridade
funcional precisa ser conferida à mão contra o binário antigo.

- [x] `GameBoost.exe --scan relatorio.txt` gera relatório e **não encerra nada**
- [x] `GameBoost.exe --scan` sem argumento imprime no terminal que chamou
- [x] Ativar o Modo Game abre a tela de confirmação **antes** de fechar qualquer coisa,
      e lista só o que está marcado
- [x] Um app com trabalho não salvo (Bloco de Notas com texto) não perde nada. O Bloco de
      Notas do Windows 11 persiste sozinho e por isso não pergunta; o texto volta intacto
- [x] Apps marcados com ★ reabrem sozinhos ao desligar
- [x] Apps não marcados aparecem na lista de restauração com botão individual
- [x] Plano de energia volta ao anterior ao desligar
- [x] Windows Update volta a rodar ao desligar (`Get-Service wuauserv`)
- [x] Game DVR e Game Bar voltam aos valores originais (conferir no `regedit`)

## Reversão e recuperação

- [x] Ativar o Modo Game, **matar o GameBoost pelo Gerenciador de Tarefas**, reabrir:
      aparece o aviso "foi fechado com o Modo Game ainda ativo" com o botão Restaurar agora
- [ ] Ativar o Modo Game, **reiniciar o PC**, abrir: mesmo aviso
- [ ] "Reverter tudo" funciona a qualquer momento, mesmo sem Modo Game ativo
- [ ] Rodar o desinstalador com o Modo Game ativo reverte antes de remover
- [ ] Reverter duas vezes seguidas não reaplica nada
- [ ] Ativar e desativar **10 vezes seguidas** sem vazar estado
      (`state-backup.json` termina sem pendências)

## Blindagem — o teste que não pode falhar

- [ ] **Com Valorant aberto (Vanguard rodando)**: nem `vgc`, nem `vgtray`, nem `vgk`
      aparecem selecionáveis. Ativar o Modo Game **não derruba o jogo**
- [ ] Com jogo da Steam aberto: o jogo aparece na categoria "Jogo", bloqueado
- [ ] Steam, Epic e Battle.net aparecem **desmarcados**
- [ ] OBS gravando aparece desmarcado
- [ ] Nenhum processo do antivírus aparece selecionável

## Fase 1 — diagnóstico e relatório

- [x] Página Diagnóstico: os cinco medidores se movem e o gráfico de 60 s preenche
- [x] GPU aparece com valor numa máquina com placa dedicada (medido: 14%, 1,9 GB de VRAM)
- [x] Temperatura aparece como "indisponível", nunca como 0 °C
- [ ] Sair da página Diagnóstico para a coleta (conferir no log a linha de custo)
- [ ] Achados não piscam: um que apareceu continua na tela por pelo menos 30 s
- [ ] Botão de ação de cada achado leva ao lugar certo (Segurança do Windows, vídeo, energia)
- [x] `--report saida.html` gera arquivo que abre em qualquer navegador, sem link externo
- [x] `--report saida.json` gera JSON válido
- [x] Custo da coleta abaixo de 1,5% de CPU em máquina fraca (medido: 0,115% com 20 núcleos)
- [ ] Numa máquina bem cuidada, a nota fica acima de 90 e quase não há achados

## Fase 2 — limpeza e ferramentas

- [x] Varredura de limpeza mede o disco real (medido: 22,3 GB encontrados, 4,8 GB no preset seguro)
- [x] Shader cache e prefetch aparecem desmarcados, com a advertência visível
- [x] `Downloads` aparece bloqueado, com "o GameBoost nunca remove arquivo daqui"
- [x] Cache de navegador aberto aparece bloqueado, pedindo para fechar
- [x] `--clean --preset seguro --dry-run` não remove nada
- [x] Teste de velocidade de disco apaga o arquivo de 1 GB, inclusive se falhar
- [x] Informações do sistema leem dados reais da máquina
- [ ] Limpeza real: rodar duas vezes seguidas, segunda libera ~0
- [ ] Reiniciar o Explorer devolve a barra de tarefas
- [ ] Reiniciar o driver de vídeo não derruba jogo aberto
- [ ] Ponto de restauração aparece na Restauração do Sistema do Windows
- [ ] `sfc /scannow` mostra a saída ao vivo e não parece travado

## Fase 3 — analisador de espaço

- [x] Varredura do disco do sistema (medido: 1.548.745 arquivos, 809,5 GB, em 18 a 20 s)
- [x] Soma bate com o espaço ocupado, e o que falta é declarado no rodapé
- [x] Treemap desenha proporcional e colorido por categoria
- [x] Biblioteca lê Steam e Epic com tamanho e último jogo (14 jogos, 974 GB)
- [x] Contador de pastas inacessíveis reporta o número real (543, não 40.871)
- [x] Memória com a árvore aberta (medido: 594 MB para 1,5 milhão de nós)
- [ ] Clicar num bloco do treemap desce um nível, e Subir volta
- [x] Enviar um arquivo para a Lixeira pela página e conferir que dá para restaurar
- [ ] Varredura de um segundo volume (D:)
- [ ] Cancelar a varredura no meio deixa a interface utilizável

## Fase 4 — desinstalador e inicialização

- [x] Inventário lê a máquina real (medido: 217 apps, 113 da Store, 78 protegidos)
- [x] Nada vem pré-marcado, nem o bloatware mais óbvio (rodapé abre em "Aplicar 0 selecionados")
- [x] IDEs e apps de desenvolvimento aparecem na lista e **nunca** pré-marcados (regra 3)
- [x] Runtimes, drivers e antivírus aparecem bloqueados com o motivo visível
- [x] Total declarado é plausível (1044 GB para 2145 GB de disco; antes dizia 1812 GB)
- [x] Lista ordenada pelo tamanho **medido**, não pelo declarado no registro
- [x] Aba "Restos de apps antigos" sem falso positivo óbvio (30 pastas, 3,9 GB)
- [x] Inicialização lê registro e pastas (medido: 26 entradas, 4 bloqueadas, 8 sugeridas)
- [x] O número bate com o que o módulo de Diagnóstico informa
- [x] Dry-run da inicialização não escreve `ChangeRecord` nenhum
- [x] App da Store em WindowsApps não é acusado de "sem assinatura digital"
- [x] Desinstalar um app de verdade (medido: Call of Duty, 180,3 GB; 217→216 apps, 1044→863,7 GB)
- [ ] Desinstalar um app da Store (`Remove-AppxPackage`)
- [x] Desinstalar um app cujo desinstalador abre janela própria (o GameBoost espera, não trava)
- [x] Desativar um item e conferir no Gerenciador de Tarefas (medido: WallpaperEngine,
      byte 0x03 + FILETIME, "Desabilitado" nos dois lados)
- [x] Reverter a desativação e conferir que o programa volta a abrir no boot
- [x] Enviar uma pasta de resto para a Lixeira (medido: qwen-updater, 119 MB, restaurável)

## Complemento da Fase 4 — aba Atualizações (winget)

- [x] winget detectado e versão lida (medido: 1.29.290)
- [x] Tabela lida por posição, não por idioma (teste com cabeçalho em português)
- [x] Lista bate com `winget upgrade` rodado à mão (medido: 35 atualizações)
- [x] Nenhum id sai com espaço no meio; toda fonte é winget ou msstore
- [x] Driver aparece bloqueado com o motivo
- [x] Runtime e "atualiza sozinho" aparecem com risco médio e explicação
- [x] Atualização de segurança marcada só em quem abre arquivo da internet
- [x] "Chrome Remote Desktop Host" **não** é marcado como navegador
- [x] App aberto é detectado e a linha avisa "feche antes"
- [x] Nada pré-marcado, nem a atualização de segurança
- [x] Abas legíveis no tema escuro
- [x] Controles dentro das abas visíveis para a automação (leitor de tela)
- [x] Aba "Restos de apps antigos" na interface (medido: 30 pastas, 3,8 GB)
- [x] Atualizar um app de verdade (medido: 7-Zip 24.09 → 26.03)
- [ ] Atualizar um app que está aberto e conferir a mensagem de erro
- [ ] Atualizar um app que falha e conferir o código traduzido e o link do log
- [x] Conferir o `updates-history.json` depois de uma atualização real
- [ ] Máquina sem winget: conferir o cartão e o botão da Store
- [ ] Windows em inglês: conferir que a tabela continua sendo lida

## Fase 5 — tweaks, serviços, rede e drivers

- [x] Catálogo lê o estado real de cada ajuste (medido: 24 itens, 1 já ativo)
- [x] Nada pré-marcado, nem o que está marcado como recomendado
- [x] VBS aparece bloqueado e só informativo
- [x] Windows Update e Defender aparecem bloqueados com o motivo
- [x] Ajuste que só precisa de elevação diz "Precisa de admin", não "Protegido"
- [x] Estado da hibernação lido do registro, correto sem elevação
- [x] Dry-run dos tweaks não escreve ChangeRecord nenhum
- [x] NAT classificado como aberto/moderado/estrito (medido: aberto, semáforo verde)
- [x] STUN devolve endereço público de verdade, nunca um privado
- [x] CGNAT, NAT duplo, UPnP, Teredo e firewall detectados e explicados
- [x] Teste de velocidade recusa sair para a rede sem a permissão, e diz por quê
- [x] Driver de vídeo lido com versão de marketing (medido: GTX 1660 Ti, 616.64, 0 meses)
- [x] Adaptador virtual do Hyper-V não entra na lista de placas
- [x] Aplicar um tweak de HKCU elevado e conferir a chave (mouse: 1/6/10 viraram 0/0/0)
- [x] Reverter tweak cujo valor **não existia**: `VisualFXSetting` foi apagado, não zerado
- [x] Mudar um serviço para Manual e conferir (WSearch: Automatic → Manual)
- [x] Reverter o serviço e conferir que o estado de execução também volta
- [ ] Trocar o DNS do adaptador e reverter para automático (DHCP)
- [ ] Reativar o Teredo numa máquina onde ele está desligado e conferir `netsh interface teredo show state`
- [x] Teste de velocidade comparado com teste dedicado (medido: -15%, dentro do esperado)
- [ ] Máquina em CGNAT: conferir que o diagnóstico acusa e não sugere abrir porta
- [ ] Máquina com NAT duplo: conferir a contagem de saltos privados
- [ ] Comparar a classificação de NAT com o que o app do Xbox mostra na mesma rede
- [ ] Máquina no Wi-Fi: conferir SSID, padrão e banda

## Fase 6 — perfis, bandeja e onboarding

- [x] Perfil grava e lê de volta, com afinidade e timer
- [x] Perfil novo nunca vem com "ativa sozinho" marcado
- [x] Afinidade recusa índice de núcleo que não existe na máquina (medido: 20 núcleos)
- [x] Afinidade com lista vazia devolve todos os núcleos
- [x] Varredura de janelas para tela cheia responde rápido (medido: 3 ms)
- [x] Biblioteca cruza com perfis (medido: 14 jogos, 974,2 GB, 0 com perfil)
- [x] Ícone na bandeja aparece e o vigia inicia (confirmado no log)
- [x] Onboarding aparece na primeira abertura e é alcançável pela automação
- [x] Abrir um jogo de verdade e conferir o convite (medido: Warframe, confiança 80, tela cheia)
- [x] Aceitar o convite e conferir que o perfil é aplicado (prioridade Normal → AboveNormal)
- [x] Fechar o jogo e conferir o comportamento (prioridade morre com o processo; o texto diz isso)
- [ ] Marcar "ativa sozinho" e conferir que o próximo boot do jogo não pergunta
- [x] Recusar o convite e conferir que ele não volta na mesma sessão
- [ ] Matar o GameBoost com perfil aplicado e conferir o aviso na reabertura
- [x] Fechar a janela e conferir que o app fica na bandeja, com o aviso uma vez só
- [x] "Sair" da bandeja encerra o processo de verdade
- [ ] "Limpar RAM" pela bandeja com o app minimizado
- [ ] Onboarding marcado como concluído não volta na segunda abertura
- [x] Anti-cheat: o jogo aparece na lista do Modo Game como **Protegido**, com "Este parece
      ser o jogo", risco Alto e caixa desabilitada
- [x] Atualizador de launcher (Agent do Battle.net) **não** é detectado como jogo
- [ ] Perfil com afinidade num jogo real: conferir no Gerenciador de Tarefas

## Fase 7 — distribuição

- [x] Modo portátil grava tudo em `.\data\` ao lado do exe (medido com o binário real)
- [x] Modo portátil não deixa nada em `%LOCALAPPDATA%\GameBoost`
- [x] Serviço de atualização sempre explica por que não pode verificar
- [x] YAML do workflow de release é válido (14 passos)
- [x] `#ifndef AppVersion` deixa o CI passar a versão
- [ ] Rodar o workflow de release por `workflow_dispatch` e conferir os artefatos
- [ ] Instalar pelo `.exe` gerado e conferir o atalho e a entrada em Programas
- [ ] Instalar "só para o usuário atual" sem senha de administrador
- [ ] Instalar "para todos" e conferir que o autostart foi para HKLM
- [ ] Desinstalar e conferir que não sobra chave de autostart
- [ ] Marcar a limpeza semanal e conferir a tarefa em `taskschd.msc`
- [ ] Publicar uma release e conferir que o auto-update encontra a versão nova
- [ ] Aplicar a atualização e conferir que a versão mudou
- [ ] Anotar exatamente o que o SmartScreen diz no primeiro download
- [ ] Conferir se o Defender bloqueia o instalador ou a execução
- [ ] Submeter ao Microsoft Security Intelligence se houver falso positivo

## Ambiente

- [x] **Usuário sem admin**: varreduras funcionam, botões de ação desabilitados com aviso
      visível de modo somente leitura
- [ ] Windows 10 22H2
- [ ] Windows 11 24H2 ou mais novo
- [ ] Máquina com 8 GB de RAM (é onde a limpeza de RAM rende de verdade)
- [ ] Notebook com iGPU + dGPU
- [ ] Dois usuários logados: processos da outra sessão não aparecem
- [ ] Monitor 4K e escala de 150% (DPI per-monitor v2)

## Distribuição

- [ ] SmartScreen: anotar exatamente o que aparece no primeiro download
- [ ] Windows Defender não bloqueia o instalador nem a execução
- [ ] Submeter ao Microsoft Security Intelligence se houver falso positivo
- [ ] Instalação limpa em máquina sem .NET instalado (publish é self-contained)
- [ ] Desinstalação não deixa `%LOCALAPPDATA%\GameBoost` com dados do usuário sem avisar

## Desempenho

- [ ] Varredura completa em menos de 5 s numa máquina com ~250 processos
- [ ] A UI não congela durante a varredura (o botão Cancelar responde)
- [ ] O app ocioso consome menos de 60 MB de RAM

---

## Observações de execução

| Data | Item | Resultado |
|---|---|---|
| 14/09/2026 | QA manual bloco 6 (perfis, deteccao, bandeja) | **6 bugs achados**: a deteccao funcionava, o resto nao |
| 14/09/2026 | QA manual bloco 5 (inicializacao) | **1 bug achado**: o Reverter nao revertia |
| 14/09/2026 | QA manual bloco 4 (apps, restos, winget) | **1 crash achado e corrigido**: a Lixeira derrubava o app |
| 14/09/2026 | QA manual blocos 2 e 3 (tweaks, servicos, rede) | **4 bugs achados e corrigidos**, depois passou |
| 14/09/2026 | QA manual bloco 1 (reversibilidade) elevado, na maquina real | **6 bugs achados e corrigidos**, depois passou |
| 14/09/2026 | Modo portatil com o binario real: data\ ao lado do exe | Passou |
| 14/09/2026 | Perfis, afinidade, tela cheia, bandeja e onboarding | Passou apos 2 correcoes |
| 14/09/2026 | Telas de Jogos e onboarding renderizadas | Passou |
| 14/09/2026 | winget: 35 atualizacoes lidas e classificadas | Passou apos 3 correcoes |
| 14/09/2026 | Abas de Apps (Instalados, Atualizacoes, Restos) renderizadas | Passou |
| 14/09/2026 | Tweaks, servicos, NAT, velocidade e driver de video (nao elevado) | Passou apos 4 correcoes |
| 14/09/2026 | Telas de Tweaks e Rede renderizadas e conferidas | Passou |
| 14/09/2026 | Inventario de apps, restos, inicializacao (nao elevado) | Passou. 217 apps, 30 restos, 26 entradas |
| 14/09/2026 | Telas de Apps e Inicializacao renderizadas e conferidas | Passou apos 4 correcoes |
| 13/09/2026 | Ciclo real ativar → desligar, com privilégios | **Passou.** Ver detalhe abaixo |
| 13/09/2026 | `--scan`, `--report` (HTML, JSON e texto) no binário de produção | Passou |
| 13/09/2026 | Custo da coleta | 0,115% de CPU, ciclo de 36 ms, em 20 núcleos |
| 13/09/2026 | Telas Início e Diagnóstico | Passou, depois de corrigir um crash de XAML |

### Ciclo real de reversibilidade — 13/09/2026

Executado com o binário de produção (`requireAdministrator`), com todos os 129 processos da
máquina em `NuncaEncerrar` e um único alvo descartável compilado para o teste.

| Item | Antes | Com o Modo Game ativo | Depois de desligar |
|---|---|---|---|
| `GameDVR_Enabled` | `1` | `0` | `1` |
| `UseNexusForGameBarEnabled` | não existia | `0` | **apagado** |
| `ShowStartupPanel` | não existia | `0` | **apagado** |
| `ToastEnabled` | não existia | `0` | **apagado** |
| Plano de energia | Equilibrado | Alto desempenho | Equilibrado |
| `wuauserv` | parado | parado (não foi tocado) | parado |
| App marcado com ★ | rodando | encerrado | **reaberto** |

Os três valores que **não existiam** foram apagados na reversão, não zerados — que é a
diferença entre reverter e deixar um rastro. `state-backup.json` terminou com **0 pendências**.

Ganho medido na ativação: RAM livre de 1,3 GB para 3,9 GB, com 13,2 GB retirados do working
set de 462 processos.
