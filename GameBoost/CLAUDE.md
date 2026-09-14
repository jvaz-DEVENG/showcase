# CLAUDE.md — GameBoost

Regras resumidas para quem escreve código neste repositório. Geradas a partir da seção 2
do `GAMEBOOST_V2_SPEC.md`. **Valem para todo código novo, sem exceção.**

---

## As 10 regras inegociáveis

1. **Reversível ou não existe.** Toda alteração de sistema (registro, serviço, plano de
   energia, tweak) grava o valor anterior num `ChangeRecord` em `state-backup.json` **antes**
   de aplicar, e tem caminho de reversão testado em `RollbackEngineTests`.
2. **Nunca deletar sem lixeira ou confirmação.** Arquivos de usuário vão para a Lixeira
   (`SHFileOperation` com `FOF_ALLOWUNDO`). Temporários de sistema podem ser apagados direto,
   mas só das pastas da whitelist do módulo.
3. **Nada é pré-marcado se puder causar perda.** Jogos, launchers, IDEs com trabalho aberto e
   apps de gravação aparecem na lista, nunca marcados.
4. **Honestidade sobre ganho.** Cada ação mostra o ganho **medido** (MB liberados, apps
   fechados, segundos de boot). Proibido inventar número. Proibido "limpeza de registro".
5. **Sem telemetria, sem anúncios, sem upsell.** Nada sai da máquina sem o usuário clicar em
   "Enviar diagnóstico".
6. **Blindagem primeiro.** `ISafetyGuard` é consultado por **todos** os módulos, não só pelo
   Modo Game.
7. **Tudo em PT-BR.** Strings de UI em recurso para permitir EN depois.
8. **Log de tudo.** Toda ação grava linha estruturada em `gameboost.log`
   (timestamp, módulo, ação, alvo, resultado) via `IGameBoostLogger`.
9. **Sem PowerShell quando houver API.** Win32/WMI/CIM via P/Invoke ou `System.Management`.
   PowerShell só para o que não tem API razoável (`Get-AppxPackage`), sempre com
   `-NoProfile -NonInteractive -ExecutionPolicy Bypass`.
10. **Nunca travar a UI.** Toda varredura roda em `Task` com `IProgress<T>` e
    `CancellationToken`.

---

## Mapa de pastas

```
GameBoost/
├── src/
│   ├── GameBoost.Core/           Lógica pura, sem WPF. Testável.
│   │   ├── Abstractions/         Interfaces: IProcessService, IRegistryService, IFileSystem, IClock…
│   │   ├── Native/               P/Invoke centralizado (NativeMethods interno + Bridge público)
│   │   ├── Safety/               SafetyGuard, ProtectedProcesses/Paths/Services, AntivirusDetector
│   │   ├── State/                ChangeRecord, StateBackup, RollbackEngine
│   │   ├── Logging/              Logger estruturado, rotação 5 MB × 5
│   │   ├── Settings/             AppPaths, AppSettings, SettingsStore, SettingsMigrator
│   │   ├── Services/             Implementações Windows das Abstractions
│   │   └── Modules/
│   │       ├── IModule.cs, ActionItem.cs, RiskLevel.cs
│   │       └── GameMode/         GameModeModule, GameDetector, SystemSilencer, GameSession
│   ├── GameBoost.App/            WPF: Views, ViewModels, Converters, Themes
│   ├── GameBoost.Cli/            Parser e runner dos comandos de linha
│   └── GameBoost.Tests/          xUnit + NSubstitute
├── installer/GameBoost.iss
└── docs/                         DECISOES.md, PROTECAO.md, TWEAKS.md, QA.md
```

---

## Contratos que todo módulo novo segue

```csharp
public interface IModule
{
    string Id { get; }
    Task<ScanResult>  ScanAsync(IProgress<ModuleProgress>? progress, CancellationToken ct);
    Task<ApplyResult> ApplyAsync(IReadOnlyList<string> itemIds, bool dryRun, CancellationToken ct);
    Task<ApplyResult> RevertAsync(IReadOnlyList<string> changeIds, bool dryRun, CancellationToken ct);
}
```

Cada item acionável é um `ActionItem { Id, Categoria, Titulo, Descricao, Risco,
GanhoEstimado, PreMarcado, ComoDesfazer, Bloqueado, MotivoBloqueio }`. A UI é genérica: uma
lista de `ActionItem` com checkbox, badge de risco e botão "?".

**Todo módulo precisa de `dryRun` funcional**: mesmo relatório, zero escrita. Existe teste
provando que os fakes não recebem chamadas de escrita.

---

## Padrões

- **MVVM** com CommunityToolkit.Mvvm (`[ObservableProperty]`, `[RelayCommand]`).
- **DI** com `Microsoft.Extensions.DependencyInjection`, montado em `CoreServices`.
  Classe de módulo recebe interfaces, nunca `static`.
- **Elevação**: `requireAdministrator` no manifest. Sem admin o app entra em modo somente
  leitura: varreduras funcionam, ações ficam desabilitadas com aviso.
- **Trimming**: proibido. Quebra WPF e reflexão.

---

## Antes de abrir PR

```powershell
dotnet build GameBoost.sln          # 0 erros, 0 avisos
dotnet test  GameBoost.sln          # tudo verde
```

Qualquer decisão que fuja do `GAMEBOOST_V2_SPEC.md` vai para `docs/DECISOES.md` com data e
motivo. Tweak novo entra em `docs/TWEAKS.md`; lista de proteção nova, em `docs/PROTECAO.md`.
