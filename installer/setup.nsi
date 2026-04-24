Unicode True
RequestExecutionLevel admin

!define APP_NAME     "Porganizer"
!define SERVICE_NAME "Porganizer"
!define APP_EXE      "porganizer.Api.exe"
!define UNINST_REG   "Software\Microsoft\Windows\CurrentVersion\Uninstall\${SERVICE_NAME}"

!ifndef APP_VERSION
  !define APP_VERSION "0.0.0"
!endif

!include "nsDialogs.nsh"
!include "LogicLib.nsh"

Name    "${APP_NAME} ${APP_VERSION}"
OutFile "Porganizer-${APP_VERSION}-setup.exe"
InstallDir "$PROGRAMFILES64\${APP_NAME}"
ShowInstDetails show
ShowUninstDetails show

Var PortNum
Var Dialog
Var PortLabel
Var PortInput

; ── Pages ─────────────────────────────────────────────────────────────────────
Page custom PortPageCreate PortPageLeave
Page instfiles
UninstPage instfiles

; ── Port page ─────────────────────────────────────────────────────────────────
Function PortPageCreate
  nsDialogs::Create 1018
  Pop $Dialog
  ${If} $Dialog == error
    Abort
  ${EndIf}

  ${NSD_CreateLabel} 0 0 100% 20u "HTTP port:"
  Pop $PortLabel

  ${NSD_CreateNumber} 0 25u 80u 14u "8080"
  Pop $PortInput

  nsDialogs::Show
FunctionEnd

Function PortPageLeave
  ${NSD_GetText} $PortInput $PortNum
  ${If} $PortNum == ""
    StrCpy $PortNum "8080"
  ${EndIf}
FunctionEnd

; ── Install ───────────────────────────────────────────────────────────────────
Section "Install"
  ; Stop and remove any previous installation
  nsExec::Exec 'sc stop "${SERVICE_NAME}"'
  Sleep 2000
  nsExec::Exec 'sc delete "${SERVICE_NAME}"'
  Sleep 1000

  SetOutPath "$INSTDIR"
  File /r "publish\*"

  ; Use $COMMONAPPDATA directly — shell folder constants expand correctly as
  ; direct instruction arguments but not when stored via StrCpy first
  CreateDirectory "$COMMONAPPDATA\${APP_NAME}"
  CreateDirectory "$COMMONAPPDATA\${APP_NAME}\logs"

  ; Register Windows Service
  nsExec::ExecToLog 'sc create "${SERVICE_NAME}" binPath= "$INSTDIR\${APP_EXE}" start= auto DisplayName= "${APP_NAME}"'
  nsExec::ExecToLog 'sc description "${SERVICE_NAME}" "${APP_NAME} service"'

  ; Set service environment variables (REG_MULTI_SZ) via PowerShell.
  ; Paths are resolved inside PowerShell using [Environment]::GetFolderPath
  ; to avoid NSIS shell-constant expansion issues.
  nsExec::ExecToLog "powershell -NonInteractive -Command $\"$$d=[Environment]::GetFolderPath('CommonApplicationData')+'\${APP_NAME}'; Set-ItemProperty -Path 'HKLM:\SYSTEM\CurrentControlSet\Services\${SERVICE_NAME}' -Name Environment -Type MultiString -Value @('ASPNETCORE_URLS=http://+:$PortNum',('DB_PATH='+$$d+'\app.db'),('LOGS_PATH='+$$d+'\logs\app-.log'),'ASPNETCORE_ENVIRONMENT=Production')$\""

  nsExec::ExecToLog 'sc start "${SERVICE_NAME}"'

  ; Start Menu shortcut — opens the web UI in the default browser
  CreateDirectory "$SMPROGRAMS\${APP_NAME}"
  WriteINIStr "$SMPROGRAMS\${APP_NAME}\Open ${APP_NAME}.url" "InternetShortcut" "URL" "http://localhost:$PortNum"

  ; Uninstaller
  WriteUninstaller "$INSTDIR\Uninstall.exe"

  ; Add/Remove Programs entry
  WriteRegStr   HKLM "${UNINST_REG}" "DisplayName"    "${APP_NAME}"
  WriteRegStr   HKLM "${UNINST_REG}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegStr   HKLM "${UNINST_REG}" "InstallLocation" "$INSTDIR"
  WriteRegStr   HKLM "${UNINST_REG}" "DisplayVersion"  "${APP_VERSION}"
  WriteRegStr   HKLM "${UNINST_REG}" "Publisher"       "pnclaw"
  WriteRegDWORD HKLM "${UNINST_REG}" "NoModify"        1
  WriteRegDWORD HKLM "${UNINST_REG}" "NoRepair"        1
SectionEnd

; ── Uninstall ─────────────────────────────────────────────────────────────────
Section "Uninstall"
  nsExec::Exec 'sc stop "${SERVICE_NAME}"'
  Sleep 2000
  nsExec::Exec 'sc delete "${SERVICE_NAME}"'
  Sleep 1000

  RMDir /r "$INSTDIR"
  RMDir /r "$SMPROGRAMS\${APP_NAME}"

  ; Data in ProgramData is intentionally preserved on uninstall
  DeleteRegKey HKLM "${UNINST_REG}"
SectionEnd
