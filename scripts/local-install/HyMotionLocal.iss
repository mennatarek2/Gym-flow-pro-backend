; HyMotion Local Lifetime Edition - Windows installer (Inno Setup 6 script).
;
; NOT BUILT/VERIFIED in this environment - no Inno Setup compiler (ISCC.exe) or WiX toolset was
; available to actually produce or test an .exe/.msi (see Phase 3 report, "Installer" section).
; This is the complete, reviewable source; building it just requires installing Inno Setup 6
; (https://jrsoftware.org/isinfo.php - free) and running: ISCC.exe HyMotionLocal.iss
;
; Why Inno Setup over WiX/MSIX for this project specifically:
;   - Single self-contained script (no XML authoring/WiX toolset extension packages to manage)
;   - First-class support for what this installer actually needs: copying a folder tree
;     (self-contained publish output), running an external prerequisite-check script, creating a
;     Windows Service via [Run] + sc.exe (no custom actions/DLLs required), Desktop/Start Menu
;     shortcuts, and a normal upgrade/uninstall flow with an explicit "keep or remove data" choice.
;   - WiX gives finer MSI/enterprise-deployment control (Group Policy install, SCCM) that this
;     single-gym, one-machine product does not need yet; MSIX's sandboxing model conflicts with a
;     Windows Service + arbitrary ProgramData paths. Revisit WiX if enterprise/silent mass
;     deployment becomes a real requirement later.

#define AppName "HyMotion"
#define AppPublisher "HyMotion"
#define AppExeName "GMS.Api.exe"
#define ServiceName "HyMotion"
; Bump this for every release; Inno Setup uses it to detect upgrades vs. fresh installs.
#define AppVersion "1.1.0"

[Setup]
AppId={{B6C1E1B0-9C6A-4A6E-9B7D-HYMOTIONLOCAL1}}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\HyMotion\app
DefaultGroupName=HyMotion
DisableProgramGroupPage=yes
; Service registration and ProgramData ACLs require admin rights.
PrivilegesRequired=admin
OutputBaseFilename=HyMotionLocalSetup-{#AppVersion}
SetupIconFile=..\..\GMS.Api\hymotion.ico
Compression=lzma2
SolidCompression=yes
; Self-contained publish output is large (~150-200MB) - no separate .NET runtime installer needed.
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
; Publish first: scripts\local-install\publish-local.ps1 -OutputDir <this source dir>\publish-output
; Excludes uploads/logs/secrets - those are runtime data (ProgramData), never shipped by the installer.
Source: "publish-output\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; The launcher + team Setup wizard (tools\HyMotion.Desktop) - published into the same folder by publish-local.ps1
Source: "publish-output\HyMotionLauncher.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "publish-output\HyMotionSetup.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "publish-output\HyMotionBackup.exe"; DestDir: "{app}"; Flags: ignoreversion
; Install-time scripts, kept alongside the app so [Run]/[UninstallRun] above can find them.
Source: "install-service.ps1"; DestDir: "{app}\install-scripts"; Flags: ignoreversion
Source: "uninstall-service.ps1"; DestDir: "{app}\install-scripts"; Flags: ignoreversion
Source: "Test-SqlServerAvailability.ps1"; DestDir: "{app}\install-scripts"; Flags: ignoreversion
; Backup & Recovery subsystem (see backup\BackupCommon.ps1 header). Ships alongside the app so
; a gym owner/support tech can run Restore-HyMotion.ps1 by hand even without re-running Setup.
Source: "backup\BackupCommon.ps1"; DestDir: "{app}\install-scripts\backup"; Flags: ignoreversion
Source: "backup\Backup-HyMotion.ps1"; DestDir: "{app}\install-scripts\backup"; Flags: ignoreversion
Source: "backup\Restore-HyMotion.ps1"; DestDir: "{app}\install-scripts\backup"; Flags: ignoreversion
Source: "backup\Register-BackupTask.ps1"; DestDir: "{app}\install-scripts\backup"; Flags: ignoreversion
; Second copy, extracted to {tmp} for the pre-install SQL check in [Code] below (CheckSqlServerAvailable)
; - runs before {app} exists, so it can't read the copy above yet. "dontcopy" means it ships inside
; the compiled installer's data but is not placed under {app} by the normal file-copy step.
Source: "Test-SqlServerAvailability.ps1"; DestDir: "{tmp}"; Flags: dontcopy

[Icons]
Name: "{group}\HyMotion"; Filename: "{app}\HyMotionLauncher.exe"
Name: "{group}\HyMotion Setup"; Filename: "{app}\HyMotionSetup.exe"
Name: "{group}\HyMotion Backup"; Filename: "{app}\HyMotionBackup.exe"
Name: "{commondesktop}\HyMotion"; Filename: "{app}\HyMotionLauncher.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Run]
; SQL Server prerequisite check now runs from [Code]'s NextButtonClick (see CheckSqlServerAvailable
; below) BEFORE any files are copied, so an unmet prerequisite aborts cleanly with no partial
; install left behind - this replaces the Phase 3 version of this step, which ran as a [Run] entry
; after file copy and did not branch installer UI on the script's exit code (the known gap Phase
; 3.1 was asked to close).

; 1. Register + start the Windows Service (see install-service.ps1: least-privileged NETWORK
;    SERVICE account, ASPNETCORE_ENVIRONMENT=Local via the service's own Environment registry
;    value, automatic restart-on-failure recovery - not a custom restart loop).
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; \
    Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\install-scripts\install-service.ps1"" -InstallDir ""{app}"""; \
    StatusMsg: "Registering the HyMotion service..."; Flags: runhidden waituntilterminated

; 2. Register the nightly backup Scheduled Task + grant SQL Server's own service account write
;    access to the backup folder (see Register-BackupTask.ps1 header - this is a separate
;    permission from the calling account's). Idempotent, so upgrades update the existing task
;    instead of duplicating it. Not install-blocking on failure (the gym-management app itself
;    still works without backups; Backup Health surfaces the problem instead - see section 25).
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; \
    Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\install-scripts\backup\Register-BackupTask.ps1"""; \
    StatusMsg: "Setting up nightly backups..."; Flags: runhidden waituntilterminated

; 3. Launch (health-polls, then opens the dashboard in the default browser - see HyMotion.Launcher).
Filename: "{app}\HyMotionLauncher.exe"; Description: "Launch HyMotion"; Flags: postinstall nowait skipifsilent

[Registry]
Root: HKCU; Subkey: "Software\Classes\hymotion-backup"; ValueType: string; ValueName: ""; ValueData: "URL:HyMotion Backup"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\hymotion-backup"; ValueType: string; ValueName: "URL Protocol"; ValueData: ""
Root: HKCU; Subkey: "Software\Classes\hymotion-backup\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\HyMotionBackup.exe"" ""%1"""

[UninstallRun]
; Service + shortcuts only. This intentionally does NOT touch %ProgramData%\HyMotion - see
; [UninstallDelete] below and uninstall-service.ps1's own -RemoveAllData confirmation gate.
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; \
    Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\install-scripts\uninstall-service.ps1"""; \
    Flags: runhidden waituntilterminated; RunOnceId: "RemoveHyMotionService"

[UninstallDelete]
; Deliberately nothing here for %ProgramData%\HyMotion - config/uploads/secrets/logs/database
; connection details survive uninstall by default (Phase 3 "Uninstall" requirement). A gym owner
; who explicitly wants everything gone runs uninstall-service.ps1 -RemoveAllData by hand (typed
; "DELETE" confirmation required - never automatic).

[Code]
// Phase 3.1: closes the Phase 3 gap where the SQL prerequisite check ran as a [Run] entry and
// could not branch installer UI on its exit code. Runs Test-SqlServerAvailability.ps1 via Exec,
// which DOES give us the process exit code, and aborts the wizard with a clear message on failure
// instead of installing into a broken state. Real SQL detection logic is not duplicated here in
// Pascal - this only shells out to the one existing PowerShell script.
//
// Post-3.1 fix: this step runs while Setup itself is elevated (admin), which is also the ONLY
// point in the whole install where anyone has the rights to grant the Windows Service's own
// identity (NT AUTHORITY\NETWORK SERVICE - see install-service.ps1) access to SQL Server. The
// script now does that here (pre-creates the database, grants db_owner on it only - no
// sysadmin) - see Test-SqlServerAvailability.ps1's header comment for the full root-cause writeup
// of why the service could pass this installer's own check yet still fail to connect at runtime.
function CheckSqlServerAvailable(): Boolean;
var
  ResultCode: Integer;
  ScriptPath: String;
begin
  ExtractTemporaryFile('Test-SqlServerAvailability.ps1');
  ScriptPath := ExpandConstant('{tmp}\Test-SqlServerAvailability.ps1');
  Result := Exec('powershell.exe',
    '-NoProfile -ExecutionPolicy Bypass -File "' + ScriptPath + '" -WriteConfig',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  // wpReady = the "ready to install" confirmation page, the last chance to bail out before any
  // files are copied - so an unmet prerequisite leaves nothing behind to clean up.
  if CurPageID = wpReady then
  begin
    if not CheckSqlServerAvailable() then
    begin
      MsgBox('SQL Server is required for HyMotion Local.' + #13#10 +
        'Please install SQL Server Express and run this installer again.' + #13#10#13#10 +
        'Download (free): https://www.microsoft.com/en-us/sql-server/sql-server-downloads' + #13#10#13#10 +
        'During SQL Server Express setup, either the default instance or a named "SQLEXPRESS" ' +
        'instance both work with HyMotion.',
        mbCriticalError, MB_OK);
      Result := False;
    end;
  end;
end;
