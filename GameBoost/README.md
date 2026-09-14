# GameBoost

**O booster que mostra o que faz e desfaz tudo.**

Central de otimização e manutenção para PC gamer no Windows 10 e 11. Sem anúncio, sem conta,
sem telemetria, sem assinatura. PT-BR nativo.

> **Estado:** v2.0.0 em desenvolvimento — Fase 0 (fundação) concluída.
> O Modo Game está funcional; os demais módulos chegam nas fases seguintes.

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

## O que vem depois

| Fase | Módulo |
|---|---|
| 1 | Diagnóstico de gargalos ("por que está lento?") e relatório de saúde |
| 2 | Limpeza de temporários e caches, ferramentas rápidas |
| 3 | Analisador de espaço em disco, biblioteca de jogos |
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
GameBoost.exe --report saida.html      relatório de saúde completo        (Fase 1)
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
