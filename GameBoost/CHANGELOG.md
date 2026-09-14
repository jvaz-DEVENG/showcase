# Changelog

Formato baseado em Keep a Changelog. Versionamento semantico.

## [2.0.0] - em desenvolvimento

### Fase 2 - Limpeza e ferramentas rapidas (2026-09-13)

#### Adicionado
- Modulo Cleaner (secao 5.2): 20 alvos de limpeza, do %TEMP% ao shader cache, com
  pagina no shell e o comando `--clean --preset seguro|completo`.
- Whitelist de leitura separada da de remocao: a varredura pode olhar Downloads para
  dizer quanto ha de instalador velho, sem nunca ganhar permissao de apagar de la.
- Remocao de conteudo do usuario sempre pela Lixeira (SHFileOperation com FOF_ALLOWUNDO).
- Historico de limpeza em cleanup-history.json.
- Ferramentas rapidas (secao 5.13): reiniciar Explorer e driver de video, limpar cache
  de DNS, esvaziar Lixeira, abrir Limpeza de Disco, criar ponto de restauracao, sfc e
  DISM com saida ao vivo, teste de velocidade de disco e informacoes do sistema.

#### Notas
- Cada alvo de limpeza diz o efeito colateral antes: shader cache avisa do engasgo,
  prefetch avisa que raramente compensa. Ha teste exigindo advertencia em todo alvo de
  risco medio ou alto.
- RevertAsync do Cleaner diz que nao ha reversao automatica, em vez de fingir que desfaz.
- `ipconfig /flushdns` virou DnsFlushResolverCache, e Checkpoint-Computer virou WMI
  SystemRestore: regra 9, sem chamar executavel quando ha API.
- A pagina de Limpeza nao tem XAML proprio: herda a tela inteira do ModulePageViewModel.

### Shell de navegacao (2026-09-13)

- Menu lateral com as 11 entradas da secao 6, tema escuro mantido.
- ModulePageViewModel: padrao de pagina de modulo reutilizavel, com cabecalho e Varrer,
  lista generica de ActionItem, rodape com "Aplicar N selecionados" e "Reverter".
- Paginas dos modulos ainda nao implementados explicam o que farao e em qual fase chegam.
- Configuracoes com abas Preferencias e Historico, lendo o state-backup.

### Fase 1 - Diagnostico de gargalos e relatorio de saude (2026-09-13)

#### Adicionado
- Modulo Bottleneck (secao 5.5): coleta a cada 1 s com buffer de 5 minutos, medindo CPU,
  memoria, Standby List, GPU, disco, rede e temperatura.
- 17 regras de diagnostico como classes IFindingRule testaveis, cada uma escrevendo em
  linguagem humana: "Chrome esta usando 38% da CPU", nao "cpu_threshold_exceeded".
- Deteccao de reset por atualizacao do Windows: guarda a build e avisa quando um update
  desfez os ajustes. Nenhum concorrente faz isso.
- FindingEngine com debounce de 30 s, entao a lista nao pisca a cada segundo.
- Pagina Diagnostico: cinco medidores ao vivo com grafico de 60 s, top 10 processos e a
  lista de achados com botao de acao.
- Modulo HealthReport (secao 5.12): nota 0 a 100 por area com os 5 principais achados,
  exportavel em HTML (arquivo unico, sem link externo) e JSON.
- Pagina Inicio mostra a nota de saude e exporta o relatorio.
- `GameBoost.exe --report saida.html` na CLI, com versao texto e `--json`.

#### Desempenho
- Custo da coleta: **0,115% de CPU, ciclo de 36 ms**, contra o limite de 1,5% da secao 5.5.
  A primeira versao media 5,02% com ciclo de 1039 ms. Ver docs/DECISOES.md.

#### Notas
- Nada de PerformanceCounter por nome: num Windows pt-BR os contadores do spec nao existem
  com aquele nome. Onde ha API nativa, usa-se API nativa.
- Temperaturas ficam "indisponivel" nesta fase: a leitura boa exige driver de sensores.
  Nunca aparece zero no lugar.

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
