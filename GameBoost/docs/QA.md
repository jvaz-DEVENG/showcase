# Checklist de QA manual

O que os testes automatizados não cobrem. Nenhuma release sai sem isto.

Legenda: `[ ]` não testado · `[x]` passou · `[!]` falhou, ver observação.

---

## Fase 0 — paridade com a v1.0.0

A v1 foi reconstruída a partir do `LEIA-ME.txt` (ver `DECISOES.md`), então a paridade
funcional precisa ser conferida à mão contra o binário antigo.

- [ ] `GameBoost.exe --scan relatorio.txt` gera relatório e **não encerra nada**
- [ ] `GameBoost.exe --scan` sem argumento imprime no terminal que chamou
- [ ] Ativar o Modo Game abre a tela de confirmação com tudo pré-marcado **antes** de fechar
      qualquer coisa
- [ ] Um app com trabalho não salvo (Bloco de Notas com texto) mostra o próprio diálogo de
      salvar; só é finalizado à força depois dos 3 s
- [ ] Apps marcados com ★ reabrem sozinhos ao desligar
- [ ] Apps não marcados aparecem na lista de restauração com botão individual
- [ ] Plano de energia volta ao anterior ao desligar
- [ ] Windows Update volta a rodar ao desligar (`Get-Service wuauserv`)
- [ ] Game DVR e Game Bar voltam aos valores originais (conferir no `regedit`)

## Reversão e recuperação

- [ ] Ativar o Modo Game, **matar o GameBoost pelo Gerenciador de Tarefas**, reabrir:
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

## Ambiente

- [ ] **Usuário sem admin**: varreduras funcionam, botões de ação desabilitados com aviso
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
- [ ] Modo portátil: `portable.txt` ao lado do exe faz os dados irem para `.\data\`
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
| | | |
