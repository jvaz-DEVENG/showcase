# GameBoost

**O booster que mostra o que faz e desfaz tudo.**

Central de otimização e manutenção para PC gamer no Windows 10 e 11. Sem anúncio, sem conta,
sem telemetria, sem assinatura. PT-BR nativo.

> **Estado:** v2.0.0 em desenvolvimento — Fases 0 a 3 concluídas.
> Modo Game, diagnóstico, relatório de saúde, limpeza, ferramentas rápidas e analisador de
> espaço funcionais; os demais módulos chegam nas fases seguintes.

---

## O que faz hoje

**Modo Game.** Encerra os aplicativos que você confirmar, libera memória, aplica o plano de
energia de alto desempenho, sobe a prioridade do jogo e silencia notificações, gravação em
segundo plano e Windows Update. Ao desligar, devolve tudo ao estado anterior e reabre os
apps marcados com ★.

Três coisas que ele não faz, por princípio:

- **Não fecha o jogo nem o anti-cheat.** Vanguard, EasyAntiCheat, BattlEye e companhia nem
  aparecem selecionáveis.
- **Não mata nada sem pedir.** Todo app recebe primeiro o pedido de fechamento normal, com
  3 segundos para salvar. A tela de confirmação aparece antes de qualquer coisa ser fechada.
- **Não inventa número.** O ganho mostrado é o medido: RAM livre antes e depois, apps
  fechados, alterações aplicadas.

**Diagnóstico de gargalos.** Cinco medidores ao vivo (CPU, GPU, memória, disco, rede) com
gráfico dos últimos 60 segundos, os 10 processos que mais consomem, e uma lista de achados
em português claro: *"Chrome está usando 38% da CPU"*, *"seu monitor de 165 Hz está em
60 Hz"*, *"a atualização 26H2 desfez 4 ajustes seus"*. A coleta custa 0,1% de CPU.

**Relatório de saúde.** Nota de 0 a 100 por área (Desempenho, Espaço, Inicialização,
Configuração para jogos) com os cinco principais achados, exportável como página HTML única
para mandar a quem dá suporte.

**Limpeza.** Vinte alvos, do `%TEMP%` ao shader cache, cada um mostrando quanto ocupa e
qual o efeito colateral antes de você marcar. Arquivos seus vão para a Lixeira; navegador
aberto bloqueia o próprio cache; `Downloads` só é listado, nunca tocado.

**Ferramentas rápidas.** Reiniciar o Explorer ou o driver de vídeo, limpar o cache de DNS,
criar ponto de restauração, rodar `sfc` e `DISM` com a saída ao vivo, medir a velocidade do
disco e copiar as informações do sistema para mandar a quem dá suporte.

**Onde está o seu espaço.** Varre o disco inteiro em cerca de 20 segundos e mostra num
treemap onde foram parar os GB. Lista os 100 maiores arquivos, as 50 maiores pastas e a sua
biblioteca de jogos — com o tamanho de cada um e quando você jogou pela última vez. Explica o
que são `hiberfil.sys` e `pagefile.sys` em vez de só mostrar o tamanho. Não apaga nada: abre
no Explorer ou manda para a Lixeira, com confirmação.

**Aplicativos instalados.** Lista tudo que está instalado — registro, Microsoft Store e o
que o Windows registra de uso — com o tamanho medido de verdade, não o declarado no
registro, e quando você usou pela última vez. Desinstala em lote, sempre chamando o
desinstalador do próprio app, e depois procura o que ficou para trás. Uma aba à parte varre
`%APPDATA%`, `%LOCALAPPDATA%` e `%PROGRAMDATA%` atrás de pastas de programas que você já
desinstalou. Runtimes, drivers e antivírus aparecem bloqueados, com o motivo. **Nada vem
marcado** — nem o bloatware mais óbvio, nem o resto mais antigo.

**Atualizações.** Uma aba da mesma tela mostra o que tem versão nova, em cima do winget,
o gerenciador de pacotes da própria Microsoft — quem baixa e confere assinatura é ela, não
o GameBoost. Navegador, compactador e Java desatualizados aparecem marcados como
**atualização de segurança**, porque neles ficar para trás não é questão de conforto.
Driver aparece bloqueado: o GameBoost não instala driver, nem pelo winget. O que se
atualiza sozinho (Steam, Discord, navegadores) vem com a tag e a explicação de por que
forçar pode brigar com o atualizador do próprio app.

**O que abre com o Windows.** Chaves `Run`, pastas Inicializar e o estado real de cada item,
lido de onde o Gerenciador de Tarefas lê. Mostra o fabricante, se o executável é assinado e
uma estimativa grosseira do peso no boot — declarada como grosseira, porque vem do tamanho
do arquivo e não de medição. Desativar grava exatamente o mesmo formato do Gerenciador de
Tarefas, e dá para reverter aqui, lá ou os dois.

**Ajustes do Windows.** Doze tweaks de jogo e doze serviços do Windows na mesma lista, cada
um com o efeito real escrito sem promessa: quando um ajuste é marginal, está escrito que é
marginal. O isolamento de núcleo aparece explicado e **não** é alterado — essa escolha é
sua, na tela da Microsoft. SysMain, os serviços do Xbox e o Windows Update aparecem
bloqueados para explicar por que toda lista de otimização da internet erra ao mandar
desligar. Nada vem marcado, nem o que o próprio GameBoost recomenda.

**Rede.** Responde duas perguntas: "minha internet está boa pra jogar?" e "por que eu não
entro na partida do meu amigo?". Mede latência, jitter e perda, compara o seu DNS com
Cloudflare, Google e Quad9, e descobre o tipo do seu NAT com um cliente STUN próprio —
aberto, moderado ou estrito, no mesmo vocabulário que o jogo usa. Quando é estrito, diz
**por quê**: CGNAT da operadora, dois roteadores em série, UPnP desligado, Teredo
desativado ou firewall. Corrige o que é do Windows, com desfazer, e para o resto abre
[docs/PORTAS.md](docs/PORTAS.md) com as portas de cada jogo e onde fica cada coisa em sete
marcas de roteador. O teste de velocidade é um botão à parte e desligado por padrão, porque
gasta 100 MB da sua franquia.

**Perfis por jogo.** O GameBoost reconhece quando um jogo abre e pergunta se quer ativar o
Modo Game para ele. Cada jogo pode ter o seu perfil — prioridade, núcleos, tweaks, apps a
fechar — aplicado ao entrar e **desfeito ao sair**, sem exceção. Perfil novo nunca ativa
sozinho: só passa a agir sem perguntar quem você marcar como automático.

**Bandeja e primeira abertura.** Ícone ao lado do relógio com Ativar Modo Game, Limpar RAM,
Abrir e Sair. Fechar a janela esconde na bandeja, e ele avisa isso na primeira vez —
programa que some sem dizer para onde foi é o motivo de as pessoas irem no Gerenciador de
Tarefas. Na primeira abertura, três telas explicando o contrato: tudo é reversível, nada
sai daqui, e o que faz diferença de verdade.

## Como instalar

| Arquivo | Para quem |
|---|---|
| `GameBoost-Setup-*.exe` | A maioria. Instala, cria atalho e habilita a atualização automática |
| `GameBoost-*-portatil.zip` | Pen drive ou máquina de terceiro. Guarda os dados ao lado do executável e não se atualiza sozinho |
| `SHA256SUMS.txt` | Conferir que o download veio inteiro |

O instalador pergunta se quer instalar para todos os usuários ou só para você, e
oferece — desmarcados — o atalho na área de trabalho, iniciar com o Windows
minimizado na bandeja, e uma limpeza semanal agendada.

Se o SmartScreen avisar, é porque esta versão pode não estar assinada: **Mais
informações → Executar assim mesmo**. O porquê está em
[docs/ASSINATURA.md](docs/ASSINATURA.md), junto com o que foi apurado sobre
certificado de código para quem está no Brasil.

---

## Reversibilidade

Antes de alterar qualquer coisa, o GameBoost grava o valor anterior em
`state-backup.json`. Isso vale para registro, serviços e plano de energia.

- O botão **Reverter tudo** está sempre visível.
- Se o app travar ou o PC reiniciar com o Modo Game ativo, a próxima abertura avisa e
  oferece a restauração.
- O desinstalador roda a reversão antes de remover o app.

Sem isso, um travamento com o Windows Update pausado deixaria a máquina sem atualizações
indefinidamente.

---

## Linha de comando

```
GameBoost.exe                          abre a interface
GameBoost.exe --scan [arquivo]         relatório de varredura, não encerra nada
GameBoost.exe --report saida.html      relatório de saúde completo (HTML ou JSON)
GameBoost.exe --clean --preset seguro  limpeza                            (Fase 2)
GameBoost.exe --revert-all             reverte tudo do state-backup
GameBoost.exe --gamemode on|off        liga ou desliga o Modo Game
```

Modificadores: `--dry-run` (combina com qualquer comando, não altera nada), `--json`,
`--help`.

## Onde ficam os dados

`%LOCALAPPDATA%\GameBoost\` — `settings.json`, `session.json`, `state-backup.json`,
`gameboost.log` (rotacionado, 5 MB × 5).

**Modo portátil:** criando um `portable.txt` ao lado do executável, tudo passa a ficar em
`.\data\`.

---

## Compilar

Requisitos: .NET 9 SDK e, para o instalador, Inno Setup 6.

```powershell
dotnet build GameBoost.sln
dotnet test  GameBoost.sln

# Executável final (self-contained, arquivo único)
dotnet publish src\GameBoost.App\GameBoost.App.csproj -c Release -o publish

# Instalador
& "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" installer\GameBoost.iss
```

O app exige privilégios de administrador (encerrar processos, controlar serviços e purgar a
Standby List). Sem elevação ele abre em modo somente leitura: as varreduras funcionam e as
ações ficam desabilitadas.

## Avisos conhecidos

- **SmartScreen.** O instalador ainda não é assinado. Ver seção 10 do spec para o caminho de
  assinatura (certificado OV; Azure Artifact Signing não atende o Brasil).
- **Antivírus.** Encerrar processos e elevar privilégios é o mesmo comportamento que um
  malware teria — alguns antivírus reclamam de qualquer booster.

## Documentação

| Arquivo | Conteúdo |
|---|---|
| [CLAUDE.md](CLAUDE.md) | Regras obrigatórias para código novo |
| [docs/PROTECAO.md](docs/PROTECAO.md) | Listas de blindagem, documentadas |
| [docs/TWEAKS.md](docs/TWEAKS.md) | Catálogo de tweaks com efeito real e risco |
| [docs/DECISOES.md](docs/DECISOES.md) | Decisões que fugiram do spec, com motivo |
| [docs/QA.md](docs/QA.md) | Checklist de teste manual |

---

Autor: Jonathan Vaz.
