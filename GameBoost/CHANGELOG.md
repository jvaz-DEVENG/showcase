# Changelog

Formato baseado em Keep a Changelog. Versionamento semantico.

## [2.0.0] - em desenvolvimento

### Fase 4 - Desinstalador e gerenciador de inicializacao (2026-09-14)

#### Adicionado
- Inventario de aplicativos (secao 5.3) lendo registro (32 e 64 bits, maquina e usuario),
  Microsoft Store e UserAssist. Medido: 217 apps, 113 da Store, 78 protegidos.
- Desinstalacao em lote, sempre pelo desinstalador do proprio app: QuietUninstallString,
  depois `msiexec /x {GUID} /qn`, depois `Remove-AppxPackage`, e por ultimo o desinstalador
  com janela. Limite de 10 minutos por app. Saidas 0, 3010 e 1605 contam como sucesso.
- Varredura de restos depois de cada desinstalacao, com remocao para a Lixeira.
- Aba "Restos de apps antigos": varre %APPDATA%, %LOCALAPPDATA% e %PROGRAMDATA% e lista
  pastas sem app correspondente, com tamanho e data. Medido: 30 pastas, 3,9 GB.
- Gerenciador de inicializacao (secao 5.6): chaves Run, pastas Inicializar e o estado do
  StartupApproved. Desativar grava o mesmo formato de 12 bytes do Gerenciador de Tarefas,
  com ChangeRecord antes de cada escrita. Medido: 26 entradas, 4 bloqueadas, 8 sugeridas.
- Verificacao de assinatura Authenticode com WinVerifyTrust, para mostrar o fabricante.

#### Corrigido
- O resumo dos apps anunciava 1812,4 GB numa maquina com 953 GB. Eram duas contas erradas:
  app que declara InstallLocation generica (media a pasta inteira) e varios apps declarando
  a MESMA pasta, contada uma vez por app. Agora sao 1044 GB para 2145 GB de disco.
- A lista vinha ordenada pelo tamanho DECLARADO no registro, medido so depois: o ARK, com
  309 GB reais, aparecia atras do AutoCAD, que declara 4 GB.
- App da Store cujo executavel mora em WindowsApps era acusado de "sem assinatura digital".
  A ACL de la barra ate o administrador; nao dava para verificar, e agora o texto diz isso.
- O impacto no boot aparecia duas vezes na mesma linha.
- `WinVerifyTrust` rodava duas vezes por entrada de inicializacao.

#### Notas
- Desinstalacao nao tem desfazer automatico, e a tela diz isso antes de confirmar.
- `Get-AppxPackage` so funciona com `-EncodedCommand` e com as duas saidas lidas: com
  `-Command` o PowerShell reinterpreta pipes, e sem ler o stderr o buffer de progresso
  CLIXML enche e o processo trava. Ver docs/DECISOES.md.
- Nada e pre-marcado nesta fase, em nenhuma das tres listas (regra 3).

### Fase 3 - Analisador de espaco e biblioteca de jogos (2026-09-14)

#### Adicionado
- Varredura de volume com FindFirstFileEx e FIND_FIRST_EX_LARGE_FETCH, paralelizada por
  subarvore. Medido: 1.548.745 arquivos e 809,5 GB em 20,3 s, acima do volume do criterio
  da secao 5.4 e abaixo do tempo.
- Treemap navegavel com algoritmo squarified, colorido por categoria, com clique para
  descer e trilha para voltar.
- Top 100 arquivos e top 50 pastas, com abrir no Explorer e enviar para a Lixeira.
- Categorias inteligentes: jogos, videos, imagens, instaladores, caches, sistema,
  documentos, codigo e musica. A pasta manda mais que a extensao.
- Arquivos especiais (hiberfil.sys, pagefile.sys, Windows.old) explicados em vez de so
  listados, com o risco de cada um.
- Biblioteca de jogos (secao 5.11) lendo Steam, Epic, GOG e Xbox, com tamanho e quando foi
  jogado pela ultima vez. Jogo grande e parado ha mais de 6 meses ganha selo.

#### Notas
- A MFT crua nao foi usada: FSCTL_ENUM_USN_DATA nao devolve o tamanho do arquivo. Ver
  docs/DECISOES.md.
- O contador de pastas inacessiveis reportava 40.871; o numero real e 543.
- A arvore consumia 1,1 GB de RAM por guardar o caminho completo em cada no; agora sao
  594 MB.

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
