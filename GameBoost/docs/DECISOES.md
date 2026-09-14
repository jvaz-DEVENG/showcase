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
