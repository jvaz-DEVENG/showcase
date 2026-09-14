# Decisões de projeto

Registro de toda decisão que fugiu do `GAMEBOOST_V2_SPEC.md`, com data e motivo.

---

## 2026-09-13 — A Fase 0 virou reconstrução, não refatoração

**Contexto.** A Fase 0 do spec diz "refatore o projeto para a estrutura da seção 4.1 **sem
alterar nenhum comportamento visível**". Isso pressupõe o código-fonte da v1.0.0.

**O que existia.** Apenas o binário: `GameBoost.rar` com `GameBoost.exe` (68 MB,
self-contained, compilado em 03/09/2026), `LEIA-ME.txt` e o desinstalador Inno. Nenhum
`.csproj`, `.sln`, `.iss` ou repositório git da v1 em nenhum disco da máquina.

**Decisão.** Reconstruir a v1 do zero já dentro da arquitetura alvo da seção 4.1, usando o
`LEIA-ME.txt` da v1 e a seção 1 do spec como especificação do comportamento a reproduzir.

**Consequência.** Não há garantia de paridade byte-a-byte com a v1 — só de paridade
funcional com o que estava documentado. O checklist de QA (`docs/QA.md`) lista o que precisa
ser validado à mão contra o binário antigo.

---

## 2026-09-13 — CLI é biblioteca, não executável separado

**Spec.** A seção 4.1 lista `GameBoost.Cli/` como projeto; a seção 8 diz
"`GameBoost.Cli` ou argumentos no exe principal".

**Decisão.** `GameBoost.Cli` é uma **biblioteca** (`CommandLineOptions` + `CommandRunner`)
consumida pelo `GameBoost.App`. O `App.OnStartup` faz o parse dos argumentos e, se for um
comando de linha, anexa ao console do processo pai (`AttachConsole(-1)`), executa e sai.

**Motivo.** A v1 expunha `GameBoost.exe --scan relatorio.txt`. Dois executáveis quebrariam
esse contrato, dobrariam o tamanho do publish self-contained e obrigariam o instalador a
distribuir os dois. Uma biblioteca preserva o comando da v1 e ainda deixa a lógica testável
sem levantar processo.

---

## 2026-09-13 — `AppSettings.SchemaVersion` começa em 0, não na versão atual

**Decisão.** O inicializador do campo foi removido; a propriedade começa em `0` e o
`SettingsStore.Save` carimba `VersaoAtual` na gravação.

**Motivo.** Com `= VersaoAtual` no campo, um `settings.json` da v1 — que não gravava
`schemaVersion` — desserializava como se já fosse da versão corrente, e o migrador **nunca
rodava**. O bug foi pego pelo teste
`Settings_da_v1_sem_schema_version_e_migrado_e_o_original_vira_bak`.

---

## 2026-09-13 — Prioridade do jogo não gera ChangeRecord

**Decisão.** Subir a prioridade do processo do jogo para Alta é registrado no log, mas não
gera entrada em `state-backup.json`.

**Motivo.** Prioridade de processo é estado volátil: morre junto com o processo e não
sobrevive a reboot. Um `ChangeRecord` pendente apontando para um PID que não existe mais só
poluiria a lista de reversão. A regra 1 continua valendo para tudo que **persiste**
(registro, serviço, plano de energia).

---

## 2026-09-13 — Processos críticos não aparecem na lista da UI

**Spec.** A seção 4.2 prevê itens protegidos aparecendo bloqueados, não selecionáveis.

**Decisão.** Itens protegidos por `ProcessoCritico`, `DentroDoWindows`, `OutraSessao` e
`ProprioApp` são **omitidos** da lista. Anti-cheats e antivírus **aparecem**, bloqueados,
com a explicação.

**Motivo.** Incluir todos encheria a tela com dezenas de `svchost` que o usuário nunca deve
tocar, escondendo o que importa. Anti-cheat e antivírus continuam visíveis de propósito: é
informação útil ("o Vanguard está rodando"), e ver que o app se recusa a tocar neles é o que
constrói confiança.

---

## 2026-09-13 — Escrita de arquivo com retry na troca atômica

**Decisão.** `WindowsFileSystem.WriteAllText` grava `.tmp` e faz `File.Move(overwrite)` com
até 5 tentativas e espera progressiva.

**Motivo.** O antivírus desta máquina segura o handle do `.tmp` por alguns milissegundos
depois da escrita, fazendo a troca atômica falhar com `IOException` de forma intermitente.
Sem o retry, `state-backup.json` poderia não ser gravado exatamente no momento mais crítico:
logo antes de alterar o sistema.

---

## 2026-09-13 — ReadyToRun dobra o tamanho do executável

**Medido.** O publish self-contained single-file da v2 saiu com **138 MB**, contra 68 MB do
`GameBoost.exe` da v1.0.0.

**Causa.** `PublishReadyToRun=true`, pedido pela seção 10 do spec para reduzir o tempo de
abertura. Ele embute código nativo pré-compilado junto do IL.

**Decisão.** Manter ligado, como o spec pede, e medir o tempo de abertura com e sem antes de
decidir de vez. Trimming continua proibido (quebra WPF), então não há como recuperar o
tamanho por esse caminho.

**A conferir na Fase 7:** se o ganho de abertura não justificar +70 MB de download, desligar
ReadyToRun é a alternativa óbvia — o instalador comprimido sofre menos com isso que o
download do portátil.

---

## 2026-09-13 — Fase 1: nada de PerformanceCounter por nome traduzido

**Problema.** A seção 5.5 sugere contadores como `\Processor Information(_Total)\% Processor Performance`.
Num Windows **pt-BR esse contador não existe** — chama-se "Informações do Processador".
`PerformanceCounter` exige o nome no idioma do sistema, então o código do spec falharia
nesta máquina e em qualquer Windows não-inglês.

**Decisão.** Onde há API nativa, usar API nativa: `NtQuerySystemInformation` para CPU,
Standby List e processos; `GlobalMemoryStatusEx` para memória; `DriveInfo` para disco;
`System.Net.NetworkInformation` para rede. Todas devolvem números, não texto.

Para GPU não existe equivalente público, então ali se usa PDH — mas via
`PdhAddEnglishCounter`, que aceita o nome em inglês em qualquer idioma.

---

## 2026-09-13 — A coleta custava 5% de CPU; três correções a levaram a 0,1%

O critério de aceite da seção 5.5 é **menos de 1,5% de CPU**. A primeira versão media
**5,02%**, com ciclo de **1039 ms** — mais lenta que o próprio intervalo de coleta.

| O que estava errado | Correção | Resultado |
|---|---|---|
| Um `PerformanceCounter` por instância de GPU; cada `NextValue()` é uma consulta separada | Uma consulta PDH com curinga cobre todas as instâncias de uma vez | ciclo 1039 ms → 36 ms |
| `Process.GetProcesses()` + `TotalProcessorTime` abria um handle por processo, ~400 vezes por segundo | `NtQuerySystemInformation(SystemProcessInformation)`: tudo numa chamada, sem handle | — |
| Detecção de jogo chamava WMI `Win32_Process` a cada 10 s | Detecta pelo nome nos processos que a coleta já leu | — |
| Interfaces de rede reenumeradas a cada segundo | Lista revalidada a cada 30 s | — |

**Medição final:** 0,115% de CPU, ciclo de 36 ms, app inteiro em 0,61%.

### A medição do custo também estava errada

O primeiro cálculo usava `Stopwatch`, que mede **tempo de parede**: um ciclo parado
esperando WMI parecia caro sem ter gasto CPU. Foi trocado por `GetThreadTimes`, que mede o
tempo de CPU realmente consumido.

Isso exigiu tirar o laço do `Task.Delay`: com `await`, a continuação volta em **outra**
thread do pool e a medição compara threads diferentes. O monitor passou a usar uma thread
dedicada, com prioridade abaixo do normal — o que também garante que ele ceda lugar ao jogo.

Medir errado o próprio custo é exatamente o tipo de número inventado que a regra 4 proíbe.

---

## 2026-09-13 — Temperaturas ficam indisponíveis nesta fase

**Spec.** A seção 5.5 pede LibreHardwareMonitorLib para temperaturas de CPU e GPU.

**Decisão.** A Fase 1 usa apenas WMI `MSAcpi_ThermalZoneTemperature`, que nesta máquina
responde "Sem suporte". A UI mostra **"indisponível"**, nunca zero.

**Motivo.** LibreHardwareMonitorLib carrega um driver em modo kernel para ler os sensores.
Isso é uma decisão de segurança e de distribuição (assinatura, antivírus, SmartScreen) grande
demais para entrar junto com o resto da Fase 1. A regra de throttling térmico já está escrita
e testada: ela simplesmente não dispara enquanto não houver leitura.

---

## 2026-09-13 — `AoEntrar` no binding, não no comando de navegação

O menu lateral troca de página pelo binding de `SelectedItem`, que **nunca passa pelo
comando** `Navegar`. Com o gancho no comando, o Diagnóstico não começava a coletar e o
Histórico não recarregava. O gancho foi para `OnPaginaAtualChanged`, que roda em qualquer
caminho de navegação. Descoberto porque o log não registrava o início da coleta.

---

## 2026-09-13 — Fase 3: sem leitura crua da MFT, e não foi preciso

**Spec.** A seção 5.4 pede leitura da MFT via `FSCTL_ENUM_USN_DATA`, com o critério de
**500 GB e 1 milhão de arquivos em menos de 30 s**.

**Problema com o caminho sugerido.** `FSCTL_ENUM_USN_DATA` devolve `USN_RECORD`, que traz
nome, referência do pai e atributos — mas **não traz o tamanho do arquivo**. Para os
tamanhos seria preciso parsear os registros da MFT crus, lendo os atributos `$DATA` a mão. É
o que o WizTree faz, e é bem mais trabalho e risco do que a seção sugere.

**Decisão.** A alternativa que o próprio spec autoriza: `FindFirstFileEx` com
`FIND_FIRST_EX_LARGE_FETCH` e `FindExInfoBasic`, paralelizado por subárvore com uma fila de
trabalho. O tamanho já vem no resultado da enumeração, sem uma chamada extra por arquivo.

**Medido nesta máquina** (disco do sistema, NVMe de 953 GB):

```
1.548.745 arquivos, 809,5 GB somados, em 20,3 s
```

**Um milhão e meio de arquivos e 809 GB em 20 s** — acima do volume do critério e abaixo do
tempo. A MFT crua fica para quando houver uma máquina onde isto não seja suficiente.

### O contador de pastas inacessíveis estava mentindo

A primeira versão contava **40.871 pastas "sem acesso"**. O número real é **543**: eu estava
contando toda pasta vazia como inacessível, porque `FindFirstFileEx` devolve lista vazia nos
dois casos. Agora o código separa pelos códigos de erro do Windows
(`ERROR_FILE_NOT_FOUND`, `ERROR_PATH_NOT_FOUND`, `ERROR_NO_MORE_FILES` não são falta de
permissão). Reportar 40 mil problemas onde há 543 é exatamente o número inventado que a
regra 4 proíbe.

### Outras decisões da varredura

- **Reparse points são ignorados.** Junção e link simbólico apontam para outro lugar: segui-los
  contaria o mesmo espaço duas vezes e pode virar laço infinito.
- **Prefixo `\?\`** em todo caminho, senão a varredura para nos 260 caracteres — o que
  acontece em qualquer `node_modules` ou pasta de build.
- **`Consolidar` é iterativo**, não recursivo: há um teste com 20 mil níveis de profundidade,
  que estouraria a pilha numa versão recursiva.

---

## 2026-09-14 — A árvore do disco custava 1,1 GB de RAM

Depois da primeira varredura pela interface, o app estava com **1118 MB**. A causa era o
`DiskNode` guardar o **caminho completo** em cada nó: numa árvore de 1,5 milhão de arquivos,
com caminhos de 60 a 100 caracteres, isso sozinho passa de 240 MB, mais o overhead de cada
string.

O caminho já está na estrutura — é a sequência de nomes até a raiz. Agora só a raiz guarda um
caminho completo, e os demais montam o seu subindo pelos pais.

**Medido: 594 MB**, contra 1118 MB. Redução de 47%.

Ao fazer isso apareceu uma armadilha: o scanner montava o caminho de cada filho a partir de
`pasta.Caminho`, o que passou a subir a árvore inteira **por arquivo** e tornaria a varredura
quadrática na profundidade. O caminho da pasta passou a ser calculado uma vez por pasta, não
por arquivo. O construtor foi separado em dois — um para a raiz, um para filho — para que
esse erro não seja possível de escrever de novo.

**Ainda são 594 MB** para 1,5 milhão de nós, e não vale esconder isso: uma versão futura pode
guardar a árvore achatada em arrays em vez de objetos ligados. Por ora o custo aparece só
enquanto a página de Espaço está aberta.

---

## 2026-09-14 — Fase 4: `Get-AppxPackage` devolvia zero numa máquina com 142 pacotes

Duas causas somadas, e cada uma sozinha já bastava para zerar a lista.

A primeira: `powershell -Command "..."` passa pela análise de linha de comando do próprio
PowerShell, que reinterpreta pipe e cifrão antes de o script existir. A correção é
`-EncodedCommand` com o script em Base64 UTF-16LE — o texto chega intacto.

A segunda é mais traiçoeira. O `stderr` estava redirecionado e ninguém lia. O PowerShell
escreve o progresso ali em CLIXML; o buffer do pipe encheu, o processo parou de escrever à
espera de alguém ler, e morreu no timeout de 30 s sem uma linha de erro. Agora as **duas**
saídas são lidas em paralelo, e o script começa com `$ProgressPreference = 'SilentlyContinue'`.

Vale para qualquer processo filho: redirecionar um fluxo obriga a lê-lo.

## 2026-09-14 — O casamento exato de nomes acusava app instalado como resto

A primeira varredura de restos devolveu 53 pastas e 26 GB, e entre elas estavam
`BraveSoftware`, `EpicGamesLauncher` e uma pasta `Programs` de 9 GB. Os dois primeiros estão
instalados — só escrevem a pasta sem os espaços do nome. `Programs` é guarda-chuva: tem
vários apps ativos dentro, não é resto de nada.

A comparação passou a normalizar (fora espaço e pontuação, tudo minúsculo) e a aceitar
prefixo a partir de 5 caracteres, e as pastas guarda-chuva entraram na lista de ignoradas.
Caiu para 30 pastas e 3,9 GB.

O corte de 5 caracteres é arbitrário e foi escolhido por um motivo assimétrico: **deixar de
listar um resto custa espaço em disco; apontar um app ativo como resto custa os dados do
usuário.** Quando errar é inevitável, errar para o lado barato.

## 2026-09-14 — O resumo dos apps anunciava 1812 GB num disco de 953 GB

A tela ficou pronta, rodou na máquina real e disse "217 aplicativos, 1812,4 GB no total".
Nenhum teste pegaria: cada tamanho individual estava certo. O que estava errado era somar.

Dois erros diferentes, os dois só visíveis no total:

1. Instalador que grava `InstallLocation` apontando para `C:\Program Files` ou para a raiz
   do disco. Medir aquela pasta atribui o disco inteiro a um programa só. Agora pasta
   genérica é recusada e o app fica sem tamanho medido, que é a resposta honesta.
2. Vários apps declarando a **mesma** pasta — componentes de uma suíte, por exemplo. A soma
   contava a pasta uma vez por app. Agora agrupa por pasta antes de somar.

Deu 1044 GB para 2145 GB de disco. O teste que trava isso não confere um número exato: ele
compara o total com a capacidade real dos discos e recusa qualquer app que sozinho responda
por mais de um terço. É o tipo de invariante que sobrevive à máquina mudar.

No mesmo teste apareceu um terceiro erro. A lista vinha ordenada pelo tamanho **declarado no
registro**, porque o inventário ordena antes de a medição acontecer. O ARK, com 309 GB
reais, aparecia atrás do AutoCAD, que declara 4 GB — exatamente o contrário do que a tela
serve para mostrar.

## 2026-09-14 — "Sem assinatura digital" era acusação falsa

O Teams aparecia na inicialização com a observação "sem assinatura digital". O executável
dele mora em `WindowsApps`, cuja ACL barra leitura até para o administrador: o
`WinVerifyTrust` não reprovou a assinatura, ele nunca conseguiu abrir o arquivo.

`Assinado` virou tri-estado (`bool?`): verdadeiro, falso e **não deu para verificar**. São
coisas diferentes e a tela agora diz qual é qual. Atalho da pasta Inicializar não fala de
assinatura nenhuma — o `.lnk` aponta para outro lugar e nunca é assinado, então dizer que
ele não tem assinatura não diria nada sobre o programa.

---

## 2026-09-14 — O spec foi atualizado no meio da Fase 5: o que conflita

O `GAMEBOOST_V2_SPEC.md` foi substituído por uma versão nova enquanto a Fase 5
estava sendo escrita. Duas seções mudaram de tamanho: a **5.9 (Rede)** virou um
módulo inteiro com NAT, e a **5.3 (Desinstalador)** ganhou uma aba de
atualizações via `winget` — esta última atribuída à Fase 4, que já foi entregue.

Antes de tocar em código, o que já existe e diverge do texto novo:

### Onde o arquivo estava

O spec novo não chegou ao repositório: o `GAMEBOOST_V2_SPEC.md` de lá continuava
sendo o de 13/09, e as duas versões novas estavam em `C:\Users\User\Downloads`
como `GAMEBOOST_V2_SPEC (1).md` e `(2).md`. A `(2)`, de 14/09 12:20, é a única
com as duas mudanças (8 menções a `winget`, 4 a `STUN`); foi ela que entrou no
repositório.

### 5.9 — o que já estava escrito e diverge

| O que existe | O que o spec novo pede | Situação |
|---|---|---|
| NAT em 5 categorias próprias (sem NAT, aberto, simétrico, CGNAT, duplo) | 3 categorias no vocabulário do Xbox: **Aberto / Moderado / Estrito** | Corrigido nesta fase |
| STUN de **uma** porta local para 2 servidores | 2 servidores **por 2 portas locais** | Corrigido nesta fase |
| Semáforo do ping em 20/50/100 ms | 30 / 80 ms | Corrigido nesta fase |
| Teste de velocidade: um download de 25 MB | 3 amostras de 10 s, reportar a **mediana** | Corrigido nesta fase |
| Aviso de jitter a partir de 5 ms no gateway | Finding com jitter > 15 ms e perda > 1% | Os dois convivem: ver nota abaixo |
| DNS trocado por `netsh` | `SetDNSServerSearchOrder` via WMI | Fica como está: ver nota abaixo |
| Banda por processo: recusada, mostra contagem de conexões | bytes por processo via `GetExtendedTcpTable`/`GetExtendedUdpTable` | Não é possível como descrito: ver nota abaixo |

**Jitter.** O spec define o Finding em 15 ms, e está certo para a conexão como um
todo. O aviso de 5 ms que já existia é outra coisa e continua: ele mede o jitter
até o **próprio roteador**, onde 5 ms já é anormal e aponta problema dentro de
casa. São duas medidas diferentes com dois limiares diferentes, não uma
contradição.

**DNS por `netsh`, não por WMI.** `SetDNSServerSearchOrder` pertence à
`Win32_NetworkAdapterConfiguration`, congelada desde o Windows 8, e falha em
adaptador configurado por DHCP em parte das máquinas. O `netsh` é o caminho que o
próprio Windows usa, e é ele que permite voltar ao estado "automático (DHCP)" —
que o método WMI não expressa. A reversão para automático é justamente o caso
mais fácil de perder.

**Bytes por processo.** `GetExtendedTcpTable` devolve **conexões com o PID dono**,
não contadores de tráfego. Não existe contador por processo de rede no Windows
sem ETW, e um consumidor de ETW rodando continuamente estoura o teto de 1,5% de
CPU da regra 9. O que a tela faz é o que dá para afirmar: quantas conexões cada
processo mantém e para onde. O texto na tela diz isso com todas as letras, em vez
de apresentar contagem de conexão como se fosse velocidade.

### 5.9 — o que o spec novo pede e ainda não existe

Fica como pendência declarada desta fase, não como coisa feita:

- Histórico em `network-history.json` ("sua conexão piorou à noite").
- Ping para servidores de jogo por região (Riot, Valve, Blizzard, Epic, EA em
  São Paulo).
- UPnP hoje é só descoberta SSDP. O `AddPortMapping` de teste, e a abertura de
  porta com lease de 24 h removida ao sair do jogo, dependem do detector de jogo
  em execução, que é da Fase 6.
- Firewall: hoje lê o estado dos perfis pelo registro. `INetFwPolicy2` via COM
  para procurar regra do executável do jogo, e `Set-NetConnectionProfile` para
  marcar a rede como Privada, também dependem do jogo detectado.
- Checagem de IPv6 e de velocidade de link do adaptador.
- Tabela de ASN para nomear a operadora, texto pronto para o suporte e passo a
  passo por marca de roteador.

### 5.3 — a aba "Atualizações" (winget) é da Fase 4, que já foi entregue

O spec novo põe a aba de atualizações via `winget` na seção 5.3, e a seção 12
mantém a 5.3 na **Fase 4**, que foi fechada e commitada antes desta versão do
documento existir.

Ela **não** foi feita, e não vai ser embutida na Fase 5 fingindo que sempre
esteve lá. Entra como a primeira coisa depois que a Fase 5 fechar, com o commit
dizendo que é complemento retroativo da Fase 4. Misturar as duas deixaria o
histórico mentindo sobre o que cada fase entregou.

Na mesma seção 5.3 há outros dois pontos ainda não atendidos:

- A varredura de restos cobre pastas, mas não as chaves `HKCU\Software\<Nome>` e
  `HKLM\Software\<Nome>`, nem a exportação para `.reg` antes de apagar.
- O ponto de restauração antes de um lote grande existe em Ferramentas, mas não é
  oferecido dentro do fluxo de desinstalação.

---

## 2026-09-14 — A saída do winget não tem formato para máquina ler

`winget upgrade` não tem `--output json`, nem XML, nem nada estruturado. O que
existe é uma tabela de largura fixa desenhada para humano.

O jeito óbvio de ler seria procurar a coluna "Available". Ele quebra na primeira
máquina em outro idioma: em português o cabeçalho é "Disponível", e nesta máquina
o winget imprimiu os cabeçalhos em inglês enquanto traduzia os **nomes** dos
pacotes ("Subsistema do Windows para Linux"). Depender de qualquer palavra seria
depender de uma combinação que nem é consistente dentro da mesma saída.

A leitura é por **posição de coluna**: acha-se a régua de tracinhos, o cabeçalho é
a linha acima dela, e as colunas começam onde há caractere visível logo depois de
um espaço. Nenhuma palavra é lida. Sobre isso vêm duas defesas: id com espaço no
meio é descartado (sinal de que a fatia caiu errado), e a fonte precisa ser uma
das que o winget conhece.

O teste que trava isso passa a mesma tabela com cabeçalho em português. Se o
parser voltar a depender de idioma, ele devolve zero e o teste falha.

## 2026-09-14 — Um template de TabControl apagou a árvore de acessibilidade

As abas da página de Apps saíram ilegíveis no tema escuro: o `TabItem` padrão do
WPF ainda usa o visual do Windows Classic, com fundo claro e texto escuro. Reescrevi
o `ControlTemplate` do `TabItem` e do `TabControl`.

O script de captura de tela parou de achar os botões. A investigação mostrou algo
pior que um problema de teste: **a automação não enxergava nenhum controle dentro
das abas**. As três abas apareciam; da aba para dentro, árvore vazia.

A causa é que o `TabControlAutomationPeer` do WPF procura um `ContentPresenter`
chamado exatamente `PART_SelectedContentHost` para expor o conteúdo da aba
selecionada. O meu tinha `ContentSource="SelectedContent"` e nenhum nome, então
renderizava certo e sumia da automação.

Isso vale registrar por dois motivos. O primeiro é que um leitor de tela não
alcançaria botão nenhum dessa página — regra de acessibilidade da seção 6.
O segundo é o método: o defeito só apareceu porque **a mesma automação que um
leitor de tela usa** é a que dirige os testes de tela. Um teste que clicasse por
coordenada teria passado.

## 2026-09-14 — Prefixo de id não é fronteira

A classificação das atualizações casava o id do winget por prefixo cru. Na tela
real, "Chrome Remote Desktop Host" apareceu marcado como **atualização de
segurança** e como navegador que se atualiza sozinho: o id dele é
`Google.ChromeRemoteDesktopHost`, e `StartsWith("Google.Chrome")` é verdadeiro.

O id do winget é hierárquico e separado por ponto. O casamento passou a exigir
fronteira: ou igualdade, ou o caractere seguinte é um ponto. Padrão terminado em
ponto (`AMD.`, `Intel.`, `RiotGames.`) continua valendo para a família inteira, e
isso é proposital.

O mesmo erro pegaria `Git.GitLFS` como se fosse o `Git.Git`, e
`Microsoft.EdgeWebView2Runtime` como se fosse o navegador Edge. Os três casos
estão no teste.

---

## 2026-09-14 — Detecção de jogo por polling, não por WMI

A seção 5.1 sugere `Win32_ProcessStartTrace`, que entrega o evento no instante em
que o processo nasce. A alternativa é listar processos de tempos em tempos.

Ficou o polling de 2 segundos, e o motivo é medido, não teórico: na Fase 1 a
coleta de métricas chegou a **5,02% de CPU**, e o WMI era a maior parcela
daquilo. O `ProcessStartTrace` exige uma consulta WMI viva o tempo todo — é
exatamente a forma que saiu de lá. Listar processos por
`NtQuerySystemInformation` custa microssegundos.

Dois segundos de atraso não importam para isto. O que acontece ao detectar é uma
pergunta num diálogo; ninguém percebe a diferença entre responder no segundo 0 e
no segundo 2. Se um dia houver um caso que precise do instante exato, o
argumento muda.

## 2026-09-14 — `UseWindowsForms` contaminou a aplicação inteira

O WPF não tem ícone de bandeja. O caminho normal é o `NotifyIcon` do WinForms,
que exige `<UseWindowsForms>true</UseWindowsForms>` no csproj.

O que esse switch faz, além de referenciar a biblioteca, é injetar `global using
System.Windows.Forms` e `global using System.Drawing` em **todo arquivo do
projeto**. `Control`, `Application`, `Brush` e `MouseEventArgs` existem nos dois
mundos, então metade da aplicação parou de compilar por ambiguidade — inclusive
o `Treemap` e o `MiniGrafico`, que não têm nada a ver com bandeja.

A correção é remover só os usings implícitos, mantendo a referência:

```xml
<ItemGroup>
  <Using Remove="System.Windows.Forms" />
  <Using Remove="System.Drawing" />
</ItemGroup>
```

O arquivo da bandeja declara os dele explicitamente. O resto da aplicação
continua vendo só o WPF.

## 2026-09-14 — O timer de 0,5 ms não faz o que dizem que faz

`NtSetTimerResolution(0.5 ms)` é receita fixa de todo guia de otimização. Desde o
Windows 10 2004 o pedido é **por processo**: o GameBoost pedir 0,5 ms afeta o
GameBoost, não o jogo. O que sobra é o sistema honrar a menor resolução pedida
por alguém para temporizadores globais, e é daí que vem o ganho residual que
ainda aparece no Windows 10.

Ele entrou no perfil porque é reversível e não custa nada, fica **desligado por
padrão**, e o texto na tela não promete FPS. É o mesmo tratamento dado ao
`SystemResponsiveness` e ao `NetworkThrottlingIndex` na Fase 5: marginal escrito
como marginal.

---

## Pendências conhecidas desta fase

- `--clean` (seção 5.2) responde com "chega na Fase 2" e código de saída 3. Está no parser
  porque a superfície da CLI da seção 8 precisa existir inteira desde já.
  `--report` foi implementado na Fase 1.
- `Resources/Strings.pt-BR.resx` ainda não existe: as strings estão no código. A regra 7 diz
  que devem sair para recurso; fica para quando houver a segunda tela, para não criar um
  arquivo de recursos com meia dúzia de entradas.
- Detecção automática de jogo por `Win32_ProcessStartTrace` (seção 5.1) não foi feita: a
  detecção atual é sob demanda, durante a varredura. Entra na Fase 6, junto com os perfis.
- Findings de uso de disco e rede **por processo** (seção 5.5) dependem de ETW e ficaram de
  fora; as regras que existem usam CPU, memória e os fatos do sistema.
- Latência DPC/ISR, marcada como experimental no spec, não foi implementada.
- Timer resolution: o P/Invoke existe (`NativeMethodsBridge.SetTimerResolution`) mas não é
  chamado pelo Modo Game ainda. Entra na Fase 6.
