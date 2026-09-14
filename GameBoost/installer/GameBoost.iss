; Instalador do GameBoost (Inno Setup 6).
; Compilar com:  ISCC.exe installer\GameBoost.iss
; Antes, publicar:  dotnet publish src\GameBoost.App\GameBoost.App.csproj -c Release -o publish

#define AppName    "GameBoost"
#define AppVersion "2.0.0"
#define AppAutor   "Jonathan Vaz"
#define AppExe     "GameBoost.exe"

[Setup]
AppId={{7C3F9A21-4E8B-4A6D-9F2C-8B1D5E7A3C90}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppAutor}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
OutputDir=Output
OutputBaseFilename={#AppName}-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; O app encerra processos, controla servicos e purga a Standby List.
PrivilegesRequired=admin
UninstallDisplayIcon={app}\{#AppExe}
MinVersion=10.0.17763

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "Criar atalho na area de trabalho"; GroupDescription: "Atalhos:"
Name: "startminimized"; Description: "Iniciar o GameBoost junto com o Windows, minimizado na bandeja"; GroupDescription: "Inicializacao:"; Flags: unchecked
; Desligada por padrao de proposito: agendamento so com escolha explicita do usuario.
Name: "limpezasemanal"; Description: "Agendar limpeza semanal com o preset seguro"; GroupDescription: "Manutencao:"; Flags: unchecked

[Files]
Source: "..\publish\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md";         DestDir: "{app}"; DestName: "LEIA-ME.txt"; Flags: ignoreversion isreadme

[Icons]
Name: "{group}\{#AppName}";                 Filename: "{app}\{#AppExe}"
Name: "{group}\Desinstalar {#AppName}";     Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}";           Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; \
    ValueName: "GameBoost"; ValueData: """{app}\{#AppExe}"" --minimizado"; \
    Flags: uninsdeletevalue; Tasks: startminimized

[Run]
Filename: "{app}\{#AppExe}"; Description: "Abrir o {#AppName}"; Flags: nowait postinstall skipifsilent

; Tarefa semanal opcional, aos domingos as 10h.
Filename: "{sys}\schtasks.exe"; \
    Parameters: "/Create /F /TN ""GameBoost - Limpeza semanal"" /SC WEEKLY /D SUN /ST 10:00 /RL HIGHEST /TR ""\""{app}\{#AppExe}\"" --clean --preset seguro"""; \
    Flags: runhidden; Tasks: limpezasemanal

[UninstallRun]
; Regra 1: o desinstalador desfaz as alteracoes de sistema ANTES de remover o app.
; Sem isto, desinstalar com o Modo Game ativo deixaria o Windows Update pausado
; e o plano de energia trocado para sempre.
Filename: "{app}\{#AppExe}"; Parameters: "--revert-all"; Flags: runhidden waituntilterminated; RunOnceId: "ReverterTudo"
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /F /TN ""GameBoost - Limpeza semanal"""; Flags: runhidden; RunOnceId: "RemoverTarefa"

[UninstallDelete]
Type: filesandordirs; Name: "{app}\data"

[Code]
// Os dados do usuario (preferencias, historico, log) ficam fora de {app} e nao sao
// apagados sem pergunta.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Dados: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    Dados := ExpandConstant('{localappdata}\GameBoost');
    if DirExists(Dados) then
      if MsgBox('Remover tambem suas preferencias, historico e log?' + #13#10 + #13#10 + Dados,
                mbConfirmation, MB_YESNO) = IDYES then
        DelTree(Dados, True, True, True);
  end;
end;
