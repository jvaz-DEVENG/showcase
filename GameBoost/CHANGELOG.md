# Changelog

Formato baseado em Keep a Changelog. Versionamento semantico.

## [2.0.0] - 2026-09-14

### QA manual, quarta rodada - o Reverter da inicializacao nao revertia (2026-09-14)

#### Corrigido
- **Reverter da Inicializacao devolvia sucesso sem fazer nada.** A tela passa GUIDs de
  ChangeRecord, mas o modulo esperava NOMES de programa e tentava casar com
  `id.Contains(e.Nome)`. Um GUID nunca contem "WallpaperEngine": a lista de alvos saia
  vazia, nada era desfeito e o metodo retornava sucesso. A tela dizia "Pronto" e o item
  continuava desativado.

  Alem de errado, aquilo dependia de alguem ter varrido na mesma sessao: reabrir o app e
  clicar em Reverter nao funcionaria nem com o casamento certo. Agora usa o
  RollbackEngine, que le o proprio ChangeRecord — mesmo caminho dos Tweaks.

#### Adicionado
- Teste do ciclo completo da inicializacao, com uma entrada descartavel criada so para
  ele: desativa, confere os 12 bytes com o primeiro em 0x03, reativa e confere que voltou.

#### Notas
- Reativar pode apagar o valor em vez de gravar 0x02, e as duas formas valem: no
  StartupApproved a ausencia significa habilitado. Apagar e mais limpo, porque nao deixa
  residuo de uma chave que nao existia antes.

### QA manual, terceira rodada - a Lixeira derrubava o aplicativo (2026-09-14)

#### Corrigido
- **`Pack = 1` na struct do SHFileOperation derrubava o processo.** Em x64 o `Pack = 1`
  tira o preenchimento entre os campos, e o ponteiro `pFrom` sai no deslocamento 12 em vez
  de 16. O shell32 continua lendo do 16, pega metade de um ponteiro colada na metade de
  outro e usa aquilo como endereco: violacao de acesso (0xc0000005), processo morto sem
  excecao gerenciada, sem log e sem caixa de erro.

  **A Lixeira nunca funcionou em lugar nenhum.** Limpeza, Espaco e Restos usam a mesma
  funcao. Era o item que estava marcado como nao testado no docs/QA.md desde a Fase 2, e o
  que ele escondia era um crash garantido em toda tentativa.

#### Adicionado
- Teste que manda um arquivo de verdade para a Lixeira e confere que ele saiu da origem.
  Marshalling errado nao quebra a compilacao: so uma chamada real pega.

### QA manual, segunda rodada - medicao de velocidade e quatro correcoes (2026-09-14)

Achados nos blocos de tweaks e de rede do docs/QA.md.

#### Corrigido
- **O teste de velocidade media a rajada, nao a linha.** Ele transferia 25 MB e
  cronometrava. Numa fibra domestica a operadora deixa passar bem acima do contratado por
  um ou dois segundos, e uma transferencia que termina em 0,6 s mede so isso. Com 8, 25 e
  50 MB o upload saiu 131, 354 e 275 Mbps — tres numeros que nao se parecem, na mesma
  linha e no mesmo minuto. Agora mede por **tempo** (8 s por amostra), descartando os 2
  primeiros segundos, onde moram a rajada e o slow start do TCP. O erro caiu de +44% para
  -15% contra um teste dedicado, e 15% abaixo e o resultado certo para um teste de um
  servidor so e uma conexao so.
- **A lista nao atualizava depois de agir.** O badge de um servico continuava dizendo
  "automatico" depois de ele ja ter virado "manual": a tela parecia nao ter feito nada.
  Onde a varredura e barata (Tweaks), ela refaz sozinha; onde e cara (Apps), a tela avisa
  que a lista mostra o estado de antes.
- **Markdown cru aparecia na tela.** Textos escritos com `**negrito**` mostravam os
  asteriscos, porque o WPF nao renderiza markdown. A enfase passou a vir da estrutura da
  frase.
- **Glifos de icone se perdiam.** Os icones do menu sao caracteres da area de uso privado
  do Unicode, e todo reescrita do arquivo por outra ferramenta os apagava. Agora estao em
  escape `\uXXXX`, que e ASCII puro.

#### Notas
- O teste de velocidade ganhou um teto de 120 MB por amostra, para nao comer a franquia de
  quem tem limite.
- Um teste de velocidade dedicado tambem erra quando o servidor escolhido esta
  congestionado: na mesma maquina e no mesmo minuto, um servidor deu 73 Mbps de upload e
  outro deu 264. A tela agora diz que o numero sai 10% a 15% abaixo e por que.

### QA manual, primeira rodada - seis correcoes (2026-09-14)

Achados rodando o app elevado na maquina real, no bloco de reversibilidade do
docs/QA.md. Dois deles apareciam em toda sessao.

#### Corrigido
- **Sessao nunca era encerrada.** O botao "Restaurar agora" revertia tudo e limpava a tela,
  mas nao marcava o session.json como encerrado. O aviso "foi fechado com o Modo Game ainda
  ativo" voltava em TODA abertura, mesmo com zero pendencias. Um aviso que aparece sempre
  deixa de ser aviso.
- **Excecao em toda saida.** `GameWatcher.Dispose()` era chamado pelo App e de novo pelo
  container de DI, que descarta todo singleton IDisposable. Na segunda chamada o `Cancel()`
  caia num CancellationTokenSource ja descartado, e a excecao escapava para a caixa de erro
  porque o `ServiceProvider.Dispose()` estava fora do try. Dispose agora e idempotente.
- **Lista de restauracao nao existia.** O rodape anunciava "N aguardando na lista de
  restauracao" e nao havia lista nenhuma. Agora ha o bloco "Aguardando para reabrir", com
  botao por app e a explicacao de por que ele nao voltou sozinho.
- **O contador contradizia a acao.** O resumo dizia "38 apps marcados" com um unico item
  marcado, porque era o texto da varredura e nao acompanhava a selecao. Agora ha contagem
  viva ao lado, e o resumo da varredura fica em segundo plano.

#### Adicionado
- **Busca em toda pagina de modulo.** Entrou na classe base, entao vale para Inicio, Apps,
  Limpeza, Tweaks e todas as outras. Ignora acento e caixa: quem digita "gravacao" acha
  "Gravação". Filtrar **nao** desmarca nada — a contagem continua sobre a lista inteira.
- **Marcar todos e Limpar na tela Inicio**, que era a unica pagina sem eles.

### Fase 7 - Distribuicao: auto-update, CI de release, portatil e assinatura (2026-09-14)

#### Adicionado
- Auto-update com Velopack apontando para as releases do GitHub. Checagem silenciosa na
  abertura, download em segundo plano, e a troca de versao so no proximo inicio ou quando
  o usuario mandar.
- `VelopackApp.Build().Run()` como primeira linha do Main, antes de ler argumentos: o
  Velopack roda o proprio exe para instalar e atualizar, e essa chamada precisa
  interceptar.
- Secao "Versao do GameBoost" em Configuracoes: versao atual, botao Verificar e, quando
  ha download pronto, "Instalar e reiniciar".
- Workflow `gameboost-release.yml`: dispara em tag `gameboost-v*`, confere que a tag bate
  com o Directory.Build.props, testa, publica, assina (quando ha certificado), empacota
  com o Velopack, monta o instalador e o portatil, gera SHA256SUMS e anexa tudo a release.
- Passo de assinatura condicional. Sem o segredo, a release sai sem assinatura e o CI
  emite um aviso visivel, em vez de falhar a build inteira.
- Portatil montado no CI: o mesmo exe, mais um `portable.txt` explicando o que ele faz.
- Instalador com escolha entre instalar para todos os usuarios ou so para o atual. A
  chave de autostart passou de HKCU para HKA, que resolve para a raiz certa nos dois casos.
- `docs/ASSINATURA.md`: o que foi apurado sobre certificado (o Brasil esta fora do Azure
  Artifact Signing, EV nao pula mais o SmartScreen desde 2024, OV e o caminho viavel) e
  como submeter falso positivo.
- `installer/NOTAS-DA-RELEASE.md`: o que baixar, e como conferir o SHA256 quando o
  SmartScreen avisar.

#### Corrigido
- A here-string do PowerShell (`@'...'@`) exige o terminador na coluna 0, e isso quebrava
  o bloco YAML do workflow, que precisa de todo conteudo indentado. Trocada por array de
  strings.
- O `.iss` tinha `#define AppVersion` fixo, entao o `/DAppVersion` do CI seria ignorado.
  Agora e `#ifndef`.
- O `UseWindowsForms` da Fase 6 ligou os analisadores do WinForms, e o WFO0003 manda tirar
  a configuracao de DPI do manifesto — regra que nao se aplica a um app WPF. Com o
  `-warnaserror` do CI, isso quebraria a build de Release inteira. Suprimido com o motivo
  escrito no csproj.

#### Notas
- Modo portatil nao tem auto-update, e a tela diz isso: o Velopack atualiza uma instalacao
  que ele mesmo montou, e um exe solto numa pasta nao tem o que atualizar.
- O teste do modo portatil nao pode exigir que o caminho fique fora de `%LOCALAPPDATA%`:
  a pasta temporaria do Windows mora dentro dele. A invariante testada e nao cair na pasta
  padrao do app.

### Fase 7 - Distribuicao: auto-update, CI de release, portatil e assinatura (2026-09-14)

#### Adicionado
- Auto-update com Velopack apontando para as releases do GitHub. Checagem silenciosa na
  abertura, download em segundo plano, e a troca de versao so no proximo inicio ou quando
  o usuario mandar.
- `VelopackApp.Build().Run()` como primeira linha do Main, antes de ler argumentos: o
  Velopack roda o proprio exe para instalar e atualizar, e essa chamada precisa
  interceptar.
- Secao "Versao do GameBoost" em Configuracoes: versao atual, botao Verificar e, quando
  ha download pronto, "Instalar e reiniciar".
- Workflow `gameboost-release.yml`: dispara em tag `gameboost-v*`, confere que a tag bate
  com o Directory.Build.props, testa, publica, assina (quando ha certificado), empacota
  com o Velopack, monta o instalador e o portatil, gera SHA256SUMS e anexa tudo a release.
- Passo de assinatura condicional. Sem o segredo, a release sai sem assinatura e o CI
  emite um aviso visivel, em vez de falhar a build inteira.
- Portatil montado no CI: o mesmo exe, mais um `portable.txt` explicando o que ele faz.
- Instalador com escolha entre instalar para todos os usuarios ou so para o atual. A
  chave de autostart passou de HKCU para HKA, que resolve para a raiz certa nos dois casos.
- `docs/ASSINATURA.md`: o que foi apurado sobre certificado (o Brasil esta fora do Azure
  Artifact Signing, EV nao pula mais o SmartScreen desde 2024, OV e o caminho viavel) e
  como submeter falso positivo.
- `installer/NOTAS-DA-RELEASE.md`: o que baixar, e como conferir o SHA256 quando o
  SmartScreen avisar.

#### Corrigido
- A here-string do PowerShell (`@'...'@`) exige o terminador na coluna 0, e isso quebrava
  o bloco YAML do workflow, que precisa de todo conteudo indentado. Trocada por array de
  strings.
- O `.iss` tinha `#define AppVersion` fixo, entao o `/DAppVersion` do CI seria ignorado.
  Agora e `#ifndef`.

#### Notas
- Modo portatil nao tem auto-update, e a tela diz isso: o Velopack atualiza uma instalacao
  que ele mesmo montou, e um exe solto numa pasta nao tem o que atualizar.
- O teste do modo portatil nao pode exigir que o caminho fique fora de `%LOCALAPPDATA%`:
  a pasta temporaria do Windows mora dentro dele. A invariante testada e nao cair na pasta
  padrao do app.

### Fase 6 - Perfis por jogo, bandeja e onboarding (2026-09-14)

#### Adicionado
- Perfis por jogo (secao 5.11): um arquivo por jogo em `profiles/*.json`. Um perfil diz o
  que fazer quando o jogo abre e o que desfazer quando ele fecha; a reversao nao e
  opcional nem configuravel.
- Deteccao automatica de jogo (secao 5.1): vigia os processos a cada 2 s numa thread de
  prioridade baixa, cruzando nome conhecido, pasta de launcher e janela em tela cheia.
- Perfil novo **nunca** ativa sozinho. O convite aparece, e so quem marcar "ativa sozinho"
  passa a ter o perfil aplicado sem perguntar (regra 3).
- Afinidade de CPU por perfil, desligada por padrao, recusando indice de nucleo que nao
  existe na maquina atual.
- Resolucao de timer em 0,5 ms por perfil, desligada por padrao e com o texto dizendo que
  vale menos do que a internet promete.
- Pagina Jogos: biblioteca dos launchers cruzada com os perfis. Medido: 14 jogos,
  974,2 GB.
- Icone na bandeja com o menu da secao 6 (Ativar Modo Game, Limpar RAM, Abrir, Sair).
  Fechar a janela esconde na bandeja, com aviso na primeira vez; "Sair" sai de verdade.
- "Limpar RAM" como acao propria, com ganho medido antes e depois. Quando nao ha o que
  liberar, o texto diz isso em vez de inventar um numero.
- Onboarding de tres telas na primeira abertura: reversivel, sem telemetria, e o que faz
  diferenca de verdade. Nao vende FPS.
- Dois interruptores novos em Configuracoes: icone na bandeja e deteccao automatica.

#### Corrigido
- `UseWindowsForms` (necessario para o NotifyIcon) injeta using global de
  `System.Windows.Forms` e `System.Drawing` em todo arquivo, tornando `Control`,
  `Application`, `Brush` e `MouseEventArgs` ambiguos com os tipos do WPF na aplicacao
  inteira. Os usings implicitos foram removidos no csproj.
- O texto de estado vazio da pagina Jogos aparecia por cima da lista cheia: a
  visibilidade dependia do Resumo, que nunca fica vazio.

#### Notas
- A deteccao e por polling, nao por `Win32_ProcessStartTrace`. WMI em laco foi o que
  estourou o orcamento de CPU na Fase 1; 2 segundos de atraso nao importam para algo que
  o usuario vai confirmar num dialogo.
- Sair do app desfaz o perfil aplicado antes de encerrar. Sem isso, prioridade e tweaks
  ficariam de pe depois que o processo sumiu.

### Complemento da Fase 4 - aba Atualizacoes (winget) e aba Restos na tela (2026-09-14)

Entrega retroativa: a secao 5.3 ganhou a aba de atualizacoes depois que a Fase 4 ja
estava commitada. Vem em commit proprio para o historico nao mentir sobre o que cada
fase entregou.

#### Adicionado
- Aba "Atualizacoes" em cima do winget: lista o que tem versao nova, classificado e
  agrupado. Medido: 35 atualizacoes, 4 de seguranca, 7 que se atualizam sozinhos.
- Classificacao por id: driver (bloqueado, o GameBoost nao instala driver), runtime,
  "atualiza sozinho" e "o winget nao sabe a versao instalada". Cada grupo tras o texto
  que explica por que tratar diferente.
- Apps que abrem arquivo vindo da internet (navegador, compactador, leitor de PDF, Java)
  sao marcados como atualizacao de seguranca: neles ficar desatualizado nao e conforto.
- App aberto e detectado e a linha avisa "feche antes".
- Atualizacao uma a uma, com progresso por item. `winget upgrade --all` seria mais curto
  e perderia o que importa: com `--all` um app que falha some no meio da saida.
- Codigos de saida do winget viram frase em portugues; o caminho do log detalhado aparece
  quando algo falha. Historico em updates-history.json.
- Quando o winget nao existe, a aba explica o que ele e e abre a pagina do App Installer
  na Microsoft Store.
- Aba "Restos de apps antigos" **na interface**. A varredura existia no Core desde a Fase
  4, mas nunca tinha sido ligada a uma tela.
- Pagina de Apps reorganizada em tres abas, cada uma com o seu proprio botao de acao.

#### Corrigido
- O template de TabControl escrito para o tema escuro quebrava a arvore de automacao:
  nenhum controle dentro das abas era exposto. Leitor de tela nao alcancaria nada, e a
  navegacao por teclado perdia o destino. Faltava o `ContentPresenter` chamado
  `PART_SelectedContentHost`, que e o nome que o `TabControlAutomationPeer` procura.
- O casamento de id do winget era por prefixo cru, entao "Google.Chrome" pegava
  "Google.ChromeRemoteDesktopHost": o host de acesso remoto aparecia como navegador e
  como atualizacao de seguranca. Agora exige fronteira de ponto.
- As abas do TabControl saiam ilegiveis: o estilo padrao do WPF usa fundo claro e texto
  escuro, que sobre a superficie escura do GameBoost fica branco no branco.

#### Notas
- A saida do `winget upgrade` nao tem formato estruturado: nem JSON, nem XML. E uma
  tabela de largura fixa com cabecalho traduzido. A leitura e por **posicao de coluna**,
  calculada a partir do cabecalho, o que funciona em qualquer idioma.
- Nada e pre-marcado, nem a atualizacao de seguranca: instalar versao nova pode quebrar
  o que funcionava, e a escolha e de quem usa a maquina.

### Fase 5 - Tweaks, servicos, rede com NAT e drivers (2026-09-14)

#### Adicionado
- Pagina Tweaks (secoes 5.8 e 5.7): 12 ajustes do catalogo e 12 servicos do Windows na
  mesma lista, agrupados por categoria. Cada ajuste tras o efeito real, a evidencia, o
  risco e como desfazer. Medido: 24 itens, 1 ja ativo na maquina de teste.
- Nada e pre-marcado, nem o que o proprio GameBoost recomenda: o selo "recomendado"
  aparece no texto e a caixa continua vazia (regra 3).
- Isolamento de nucleo (VBS) aparece so de leitura, com o custo declarado (5% a 15%
  segundo testes publicos) e o botao que abre a tela da Microsoft. Mitigacoes de
  Spectre/Meltdown nao entram nem como item bloqueado.
- SysMain, servicos do Xbox e Windows Update aparecem bloqueados **para explicar por que
  nao mexer**: a informacao de que nao se deve desligar vale tanto quanto a sugestao de
  desligar.
- Pagina Rede (secao 5.9) em tres blocos: "Posso jogar online?" com semaforo, "Velocidade"
  e as tabelas de latencia, DNS e conexoes.
- Cliente STUN proprio (RFC 5389) com dois servidores por duas portas locais. Classifica o
  NAT no vocabulario dos jogos: aberto, moderado, estrito. Medido na maquina real: aberto.
- Deteccao das causas: CGNAT pela faixa 100.64.0.0/10, NAT duplo por saltos privados no
  traceroute, UPnP por SSDP, Teredo pelo registro e perfis do firewall.
- Teste de velocidade com 3 amostras e mediana, opt-in: gasta cerca de 100 MB, entao
  depende de permissao explicita em Configuracoes, desligada por padrao.
- Reativacao do Teredo, reversivel, com ChangeRecord por chave. E a correcao do caso mais
  comum de "Game Pass nao conecta": alguem desativou seguindo tutorial de otimizacao.
- Troca de DNS por adaptador, reversivel inclusive para "automatico (DHCP)", e reset de
  Winsock com risco Alto e o aviso de que nao tem desfazer.
- Comparacao de DNS por consulta UDP montada a mao, porque `Dns.GetHostEntry` perguntaria
  sempre ao resolvedor do sistema.
- Leitura do driver de video (secao 5.10) pelo registro, com traducao da versao da NVIDIA
  para a de marketing (32.0.16.1664 vira 616.64) e a idade em meses.
- `docs/PORTAS.md`: portas por launcher e por jogo, o que fazer em CGNAT e em NAT duplo, e
  onde fica UPnP em sete marcas de roteador.

#### Corrigido
- O badge "Protegido" aparecia em ajuste que so precisava de elevacao. Sao coisas
  diferentes: protegido e o GameBoost se recusando a mexer; precisar de admin some ao
  reabrir elevado. `ActionItem` ganhou `RotuloBloqueio`.
- A leitura da hibernacao procurava `C:\hiberfil.sys`, e `File.Exists` devolve **false**
  para aquele arquivo mesmo com a hibernacao ligada: a ACL barra a consulta sem elevacao.
  O app diria "ja desligada" para todo mundo que abrisse sem ser administrador. Agora le o
  registro.
- O driver de video listava o adaptador virtual do Hyper-V, carimbado com 21/06/2006, e a
  tela anunciaria "seu driver tem 242 meses" numa maquina com driver do mes passado.
- A lista de quem usa a rede repetia o mesmo nome varias vezes, uma por PID. Agora agrupa
  por nome e diz quantos processos sao.
- A barra de status da pagina Rede repetia a explicacao do NAT que ja tem bloco proprio, e
  saia cortada no meio da frase.

#### Notas
- O spec foi substituido no meio desta fase. Os conflitos entre o que ja estava escrito e o
  texto novo estao listados em docs/DECISOES.md, com o que foi corrigido e o que ficou
  pendente.
- A aba "Atualizacoes" via winget (secao 5.3) e da Fase 4, que ja foi entregue. Entra como
  complemento retroativo depois desta fase, com commit proprio.
- Banda por processo nao e possivel como o spec descreve: `GetExtendedTcpTable` devolve
  conexoes com o PID dono, nao contadores de trafego. A tela mostra conexoes e diz isso.

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
