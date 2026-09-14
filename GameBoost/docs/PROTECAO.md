# Blindagem

Documentação das listas de proteção da seção 9 do spec. Toda lista aqui tem teste
correspondente em `SafetyGuardTests`. **Se algum desses testes cair, o app não pode ser
publicado.**

Ponto único de consulta: `ISafetyGuard`, implementado por
[`SafetyGuard`](../src/GameBoost.Core/Safety/SafetyGuard.cs). Nenhum módulo decide sozinho o
que pode tocar.

---

## 1. Processos

### 1.1 Núcleo do Windows — omitidos da lista

`ProtectedProcesses.Criticos`. Encerrar qualquer um derruba a sessão ou o sistema.

`system`, `idle`, `registry`, `memory compression`, `secure system`, `smss`, `csrss`,
`wininit`, `winlogon`, `services`, `lsass`, `lsaiso`, `svchost`, `spoolsv`, `dwm`,
`explorer`, `fontdrvhost`, `sihost`, `taskhostw`, `ctfmon`, `runtimebroker`,
`shellexperiencehost`, `startmenuexperiencehost`, `searchhost`, `searchindexer`, `audiodg`,
`conhost`, `dllhost`, `wudfhost`, `wmiprvse`, `trustedinstaller`, `tiworker`, `logonui`,
`userinit`, `dashost`, `applicationframehost`, `textinputhost`, `systemsettings`,
`backgroundtaskhost`

Além da lista: **qualquer PID ≤ 4** e **qualquer executável dentro de `C:\Windows`**.

> A comparação de caminho é por segmento. `C:\WindowsApps` **não** conta como dentro de
> `C:\Windows` — há teste para isso.

### 1.2 Anti-cheats — aparecem bloqueados

`ProtectedProcesses.AntiCheats`. Fechar derruba o jogo e pode gerar banimento.

`easyanticheat` (+ `_eos`, `_setup`), `beservice`, `bedaisy`, `battleye`, `vgc`, `vgtray`,
`vgk` (Vanguard), `faceitservice`, `faceit`, `pnkbstra/b/k` (PunkBuster), `xigncode`,
`xhunter1`, `gameguard`, `gamemon`, `npggnt`, `nprotect`, `ricochet`, `eaanticheat`,
`anticheatexpert`, `ace-base`, `ace-guard`, `mhyprot`, `sgguard`, `steamservice`

### 1.3 Antivírus e EDR — aparecem bloqueados

Detecção em duas camadas:

1. **WMI** `root\SecurityCenter2` (`AntiVirusProduct`, `AntiSpywareProduct`,
   `FirewallProduct`) → nome do executável assinado. É a fonte de verdade.
2. **Lista estática** `ProtectedProcesses.Seguranca`, rede de segurança para quando o WMI
   falhar ou o produto não se registrar: Defender, Kaspersky, Avast/AVG, Bitdefender,
   McAfee, Norton, ESET, Sophos, Carbon Black, CrowdStrike, SentinelOne, Cylance, Trend
   Micro, Webroot, Malwarebytes.

### 1.4 Nunca pré-marcados — aparecem desmarcados (regra 3)

`ProtectedProcesses.NuncaPreMarcados`. O usuário pode marcar à mão; o app nunca marca por
ele.

| Grupo | Exemplos |
|---|---|
| Launchers | Steam, Epic, Battle.net, Riot Client, GOG Galaxy, EA Desktop, Origin, Ubisoft Connect, Xbox |
| Overlay e monitoramento | RTSS, RivaTuner, MSI Afterburner |
| Gravação e streaming | OBS, Streamlabs, XSplit, NVIDIA Share, Radeon Software |
| IDEs e editores | Visual Studio, VS Code, Rider, IntelliJ, PyCharm, WebStorm |
| Criação | Photoshop, Illustrator, Premiere, After Effects |
| Office | Excel, Word, PowerPoint, Outlook |
| Virtualização | VMware, VirtualBox, Docker Desktop |
| Sincronização de nuvem | OneDrive, OneDrive.Sync.Service, FileCoAuth, Dropbox, Google Drive, MEGAsync, Syncthing, Nextcloud |

**Instaladores e atualizadores em execução** também nunca vêm marcados. A detecção é por
padrão de nome (`setup`, `install`, `updater`, `update`, `upgrade`, `patch`, `msiexec`,
`redist`, `unins`, ou terminando em `.tmp`), e não por lista: instalador novo aparece toda
semana. Encerrar um no meio do trabalho corrompe a instalação.

Além da lista: **qualquer executável dentro de pasta de jogo** —
`\steamapps\common\`, `\Epic Games\`, `\Riot Games\`, `\Battle.net\`,
`\GOG Galaxy\Games\`, `\XboxGames\`, `\EA Games\`, `\Origin Games\`, `\Ubisoft\`.

### 1.5 Listas do usuário

`settings.json` aceita `NuncaEncerrar` e `SempreEncerrar`.

**`SempreEncerrar` não vence a blindagem.** Colocar `lsass` nessa lista não libera nada — há
teste garantindo. Ela só supera a lista de "nunca pré-marcados": quem quer que a Steam venha
marcada pode pedir isso.

---

## 2. Caminhos

`CheckPath(caminho, whitelist)` só libera se o caminho estiver **dentro** de algum item da
whitelist do módulo. **Whitelist vazia não libera nada.** O caminho é normalizado com
`Path.GetFullPath` antes de qualquer comparação, então `..\` não escapa.

Bloqueados mesmo dentro da whitelist — se o segmento aparecer em **qualquer nível**:

`.git`, `onedrive`, `dropbox`, `google drive`, `icloud`, `saves`, `savegames`,
`saved games`, `savedgames`, `my games`

Pastas pessoais (`Documentos`, `Imagens`, `Vídeos`, `Música`, `Área de Trabalho`,
`Favoritos`) são bloqueadas a menos que a própria whitelist do módulo aponte para dentro
delas — o que exige decisão explícita de quem escreveu o módulo.

Pastas de sistema (`C:\Windows`, `Program Files`, `Program Files (x86)`, `System32`,
`SysWOW64`) só são alcançáveis pela whitelist explícita de limpeza da seção 5.2, e
`Program Files` só via Desinstalador.

---

## 3. Serviços

`ProtectedServices.Intocaveis` — não podem ser parados nem ter o `StartType` alterado:

| Grupo | Serviços |
|---|---|
| Núcleo | `DcomLaunch`, `RpcSs`, `RpcEptMapper`, `Winmgmt`, `EventLog`, `Schedule`, `TrustedInstaller`, `CryptSvc`, `PlugPlay`, `Power`, `ProfSvc`, `SamSs`, `LSM`, `Themes`, `UserManager`, `gpsvc`, `BrokerInfrastructure`, `SystemEventsBroker` |
| Rede | `Dhcp`, `Dnscache`, `nsi`, `NlaSvc`, `netprofm`, `WlanSvc`, `LanmanWorkstation` |
| Áudio | `Audiosrv`, `AudioEndpointBuilder` |
| Segurança | `WinDefend`, `SecurityHealthService`, `wscsvc`, `Sense`, `mpssvc`, `BFE` |
| Update | `wuauserv` |

`ProtectedServices.PausaveisTemporariamente` — podem ser **parados** durante o Modo Game,
nunca desabilitados: `wuauserv`, `UsoSvc`, `DoSvc`, `BITS`.

A parada grava `ChangeRecord` com `estavaRodando=true`, então o serviço volta no
desligamento do Modo Game, no "Reverter tudo", na próxima abertura após um travamento e no
desinstalador.

---

## 4. Registro

- Nunca tocar em `HKLM\SYSTEM\CurrentControlSet\Services\*` além de `Start`.
- Nunca tocar em `Winlogon`, `Image File Execution Options`, `Policies`.
- Toda escrita passa por `ChangeRecord` com `root`, `kind` e `ValorAnteriorExistia`. Quando
  o valor **não existia**, reverter significa **apagar**, não gravar zero — há teste.

---

## 5. Tweaks proibidos

Nunca alterados pelo GameBoost, em nenhuma fase:

- **VBS / Core Isolation (Memory Integrity)** — só informar e medir, com link para as
  Configurações do Windows. É segurança contra malware de kernel; a decisão é do usuário.
- **Mitigações de Spectre/Meltdown**
- **Windows Defender**, **UAC**, **SmartScreen**, **assinatura de drivers**
- **Limpeza de registro** — sem ganho mensurável, risco puro (regra 4)

---

## 6. O que **não** é jogo, mesmo morando na pasta da Steam

`GameDetector.NaoSaoJogos`. Sem esta lista, o Wallpaper Engine era eleito "o jogo" só por
estar em `steamapps\common` — e o Modo Game subia a prioridade de CPU do papel de parede em
vez da do jogo de verdade. Foi encontrado no primeiro teste com privilégios numa máquina real.

`wallpaper64`, `wallpaper32`, `wallpaperservice64`, `webwallpaper32`, `vrmonitor`,
`vrserver`, `vrcompositor`, `vrdashboard`, `steamvr`, `aseprite`, `blender`, `krita`,
`obs64`, `3dsmax`, `unity`, `unityhub`, `rpcs3`, `pcsx2`, `dolphin`, `retroarch`,
`steamwebhelper`, `gameoverlayui`, `steamerrorreporter`

Além da lista, a heurística de pasta ficou mais exigente: um executável desconhecido dentro
de pasta de launcher só é considerado jogo se ocupar **mais de 300 MB** de memória.
Utilitário não tem porte de jogo.
