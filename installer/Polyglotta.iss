; Instalador do Polyglotta — ADR-0011.
;
; Instalação por usuário, sem UAC: configuração, credenciais (DPAPI) e autostart já são
; todos do usuário (ADR-0007 e ADR-0009), então não há nada aqui que peça administrador.
;
; Para compilar à mão, depois de publicar em ..\publish:
;   & "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe" /DAppVersion=0.2.0 /DVersionInfo=0.2.0 installer\Polyglotta.iss

#ifndef AppVersion
  #define AppVersion "0.0.0-dev"
#endif
#ifndef VersionInfo
  #define VersionInfo "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish"
#endif

#define AppName "Polyglotta"
#define Publisher "Roni Carvalho"
#define RepoUrl "https://github.com/ronicarvalho/polyglotta"
#define CableUrl "https://vb-audio.com/Cable/"
#define TrayExe "Polyglotta.Tray.exe"
; Mesmo nome de tarefa que o WindowsAutostart cria (ADR-0007).
#define TaskName "Polyglotta"

[Setup]
; Fixo para sempre: é por ele que o Windows reconhece uma reinstalação como atualização.
AppId={{0D8E76D8-2340-4545-9F8F-933367052BF7}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
VersionInfoVersion={#VersionInfo}
AppPublisher={#Publisher}
AppPublisherURL={#RepoUrl}
AppSupportURL={#RepoUrl}/issues
AppUpdatesURL={#RepoUrl}/releases
; {autopf} com PrivilegesRequired=lowest resolve para %LocalAppData%\Programs.
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; WASAPI e as vozes do Azure: Windows 10 ou mais novo.
MinVersion=10.0
OutputDir=out
OutputBaseFilename=Polyglotta-{#AppVersion}-setup
UninstallDisplayIcon={app}\{#TrayExe}
UninstallDisplayName={#AppName} {#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; O mesmo ícone do app, para o instalador não parecer de outro programa.
SetupIconFile=..\src\Polyglotta.Tray\Assets\icon-active.ico

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; Flags: unchecked

[Files]
; Publish self-contained: a bandeja e o CLI de diagnóstico compartilham o runtime.
; Sem .pdb: os pacotes nativos (SkiaSharp, HarfBuzz) trazem 100 MB de símbolos de
; depuração que não servem a quem instala. O workflow já os apaga; isto cobre
; quem compilar o instalador à mão a partir de um publish cru.
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#TrayExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#TrayExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#TrayExe}"; Description: "Iniciar o Polyglotta agora"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Nesta ordem e antes de apagar arquivos: o agente segura o próprio executável, e a
; tarefa de logon aponta para um caminho que vai deixar de existir.
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM {#TrayExe}"; Flags: runhidden; RunOnceId: "StopAgent"
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /F /TN {#TaskName}"; Flags: runhidden; RunOnceId: "RemoveAutostart"

[Messages]
brazilianportuguese.FinishedLabel=A instalação do [name] terminou.%n%nA configuração fica no ícone da bandeja, em "Configurar...". Comece pela aba Credenciais.

[Code]

{ O VB-CABLE é driver de terceiro: exige administrador e não pode ser redistribuído
  (ADR-0011), então aqui só detectamos e avisamos. A chave de serviço é do driver WDM
  instalado pelo VB-CABLE; as outras cobrem variações de versão. }
function VirtualCableFound: Boolean;
begin
  Result := RegKeyExists(HKEY_LOCAL_MACHINE, 'SYSTEM\CurrentControlSet\Services\VBAudioVACMME')
         or RegKeyExists(HKEY_LOCAL_MACHINE, 'SYSTEM\CurrentControlSet\Services\VBAudioVACWDM')
         or RegKeyExists(HKEY_LOCAL_MACHINE, 'SYSTEM\CurrentControlSet\Services\VB-Cable');
end;

function InitializeSetup(): Boolean;
var
  ErrorCode: Integer;
begin
  Result := True;

  if not VirtualCableFound then
  begin
    if MsgBox('Não encontrei o VB-CABLE nesta máquina.'#13#10#13#10
            + 'O Polyglotta entrega a tradução num microfone virtual, e o VB-CABLE é esse '
            + 'microfone: sem ele, a chamada não tem o que escutar. É uma instalação separada, '
            + 'gratuita, que pede administrador e às vezes um reinício.'#13#10#13#10
            + 'Abrir a página de download agora? A instalação do Polyglotta continua de '
            + 'qualquer forma.', mbConfirmation, MB_YESNO) = IDYES then
      ShellExec('open', '{#CableUrl}', '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
  end;
end;

{ O agente fica na bandeja e mantém o próprio executável aberto; sem fechá-lo primeiro,
  reinstalar por cima falha com "arquivo em uso". }
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ErrorCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM {#TrayExe}', '',
       SW_HIDE, ewWaitUntilTerminated, ErrorCode);
  Sleep(500);
  Result := '';
end;
