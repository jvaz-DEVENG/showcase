# Changelog

Formato baseado em Keep a Changelog. Versionamento semantico.

## [2.0.0] - em desenvolvimento

### Fase 0 - Fundacao (2026-09-13)

#### Adicionado
- Solucao dividida em `GameBoost.Core` (sem WPF, testavel), `GameBoost.App` (WPF),
  `GameBoost.Cli` (parser e runner) e `GameBoost.Tests` (xUnit + NSubstitute).
- `SafetyGuard`: blindagem central consultada por todos os modulos. Listas de processos
  criticos, anti-cheats, antivirus (WMI + estatica), caminhos e servicos protegidos.
- `RollbackEngine` e `StateBackup`: toda alteracao de sistema grava o valor anterior antes
  de ser aplicada e tem reversao testada. Reversao em ordem inversa; falha em um item nao
  interrompe os demais.
- `IGameBoostLogger` com log estruturado e rotacao (5 MB x 5 arquivos).
- `AppSettings` com `schemaVersion` e migradores em sequencia; arquivo antigo vira `.bak`.
- Modo portatil: `portable.txt` ao lado do exe move os dados para `.\data\`.
- Contrato `IModule` / `ActionItem` e tela generica de lista com checkbox, badge de risco e
  botao "?" com o que faz, qual o risco e como desfazer.
- Modo Game portado para o contrato: varredura com blindagem, encerramento gracioso com
  fallback forcado, limpeza de RAM (`EmptyWorkingSet` + purga da Standby List), plano de
  energia, prioridade do jogo, silenciamento do sistema e pausa do Windows Update.
- Deteccao de jogo por nome conhecido, pasta de launcher e heuristica de consumo.
- CLI com a superficie da secao 8 do spec; `--scan` preserva o comportamento da v1.
- `--dry-run` funcional em todos os caminhos: mesmo relatorio, zero escrita.
- Modo somente leitura quando o app roda sem privilegios de administrador.
- Aviso na abertura quando o app foi fechado com o Modo Game ativo.
- CI no GitHub Actions: build, testes, publish e instalador.
- Documentacao: `CLAUDE.md`, `docs/PROTECAO.md`, `docs/TWEAKS.md`, `docs/DECISOES.md`,
  `docs/QA.md`.

#### Notas
- A v1.0.0 foi reconstruida a partir do `LEIA-ME.txt` porque o codigo-fonte original nao
  existia mais. Ver `docs/DECISOES.md`.
- `--report` e `--clean` estao no parser mas respondem com a fase em que chegam.

## [1.0.0] - 2026-09-03

Versao original. Modo Game com encerramento de processos, limpeza de RAM, plano de energia,
prioridade do jogo, silenciamento do sistema, snapshot e reversao, e `--scan`.
Codigo-fonte perdido.
