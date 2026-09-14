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

## Pendências conhecidas desta fase

- `--report` (seção 5.12) e `--clean` (seção 5.2) respondem com "chega na Fase N" e código
  de saída 3. Estão no parser porque a superfície da CLI da seção 8 precisa existir inteira
  desde já.
- `Resources/Strings.pt-BR.resx` ainda não existe: as strings estão no código. A regra 7 diz
  que devem sair para recurso; fica para quando houver a segunda tela, para não criar um
  arquivo de recursos com meia dúzia de entradas.
- Detecção automática de jogo por `Win32_ProcessStartTrace` (seção 5.1) não foi feita: a
  detecção atual é sob demanda, durante a varredura. Entra na Fase 6, junto com os perfis.
- Timer resolution: o P/Invoke existe (`NativeMethodsBridge.SetTimerResolution`) mas não é
  chamado pelo Modo Game ainda. Entra na Fase 6.
