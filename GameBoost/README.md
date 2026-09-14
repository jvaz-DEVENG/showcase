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

## O que vem depois

| Fase | Módulo |
|---|---|
| 4 | Desinstalador (Win32 + Store + restos), gerenciador de inicialização |
| 5 | Tweaks de jogos, serviços, rede, drivers |
| 6 | Perfis por jogo com detecção automática, bandeja, onboarding |
| 7 | Distribuição: auto-update, CI completo, portátil, assinatura |

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
