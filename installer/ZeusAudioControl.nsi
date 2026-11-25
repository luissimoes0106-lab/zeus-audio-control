Unicode true
!include "MUI2.nsh"
!include "x64.nsh"
!include "LogicLib.nsh"

!ifndef APP_DIR
!define APP_DIR "../dist/portable"
!endif
!ifndef OUTPUT
!define OUTPUT "../dist/ZEUS-Audio-Control-Beta-0.5-Setup-x64.exe"
!endif

Name "ZEUS Audio Control Beta 0.5"
OutFile "${OUTPUT}"
InstallDir "$LOCALAPPDATA\Programs\ZEUS Audio Control Beta 0.5"
RequestExecutionLevel user
!define MUI_ICON "../src/ZeusAudioControl/Zeus.ico"
!define MUI_UNICON "../src/ZeusAudioControl/Zeus.ico"
!define UNINSTALL_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\ZeusAudioControlBeta05"
!define RUN_KEY "Software\Microsoft\Windows\CurrentVersion\Run"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_LICENSE "../LICENSE"
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "PortugueseBR"
!insertmacro MUI_LANGUAGE "English"

Function .onInit
  ${IfNot} ${RunningX64}
    MessageBox MB_ICONSTOP "Windows x64 is required."
    Abort
  ${EndIf}
FunctionEnd

Section "ZEUS Audio Control"
  SetShellVarContext current
  SetOutPath "$INSTDIR"
  File "${APP_DIR}/ZeusAudioControl.exe"
  File "../LICENSE"
  SetOutPath "$INSTDIR\licenses"
  File "../licenses/*.txt"
  SetOutPath "$INSTDIR"
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  CreateShortCut "$SMPROGRAMS\ZEUS Audio Control Beta 0.5.lnk" "$INSTDIR\ZeusAudioControl.exe"
  CreateShortCut "$DESKTOP\ZEUS Audio Control Beta 0.5.lnk" "$INSTDIR\ZeusAudioControl.exe"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayName" "ZEUS Audio Control Beta 0.5"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayVersion" "0.5.0-beta"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayIcon" "$INSTDIR\ZeusAudioControl.exe"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoRepair" 1
SectionEnd

Section "Uninstall"
  SetShellVarContext current
  ClearErrors
  Delete "$INSTDIR\ZeusAudioControl.exe"
  ${If} ${Errors}
    ${If} ${FileExists} "$INSTDIR\ZeusAudioControl.exe"
      MessageBox MB_ICONSTOP "Close ZEUS Audio Control from the system tray and retry." /SD IDOK
      SetErrorLevel 1
      Abort
    ${EndIf}
  ${EndIf}
  ReadRegStr $0 HKCU "${RUN_KEY}" "ZeusAudioControlBeta05"
  ${If} $0 == '"$INSTDIR\ZeusAudioControl.exe"'
    DeleteRegValue HKCU "${RUN_KEY}" "ZeusAudioControlBeta05"
  ${EndIf}
  Delete "$SMPROGRAMS\ZEUS Audio Control Beta 0.5.lnk"
  Delete "$DESKTOP\ZEUS Audio Control Beta 0.5.lnk"
  Delete "$INSTDIR\LICENSE"
  Delete "$INSTDIR\licenses\NAudio.txt"
  Delete "$INSTDIR\licenses\dotnet-runtime.txt"
  Delete "$INSTDIR\licenses\dotnet-desktop.txt"
  Delete "$INSTDIR\licenses\dotnet-third-party.txt"
  RMDir "$INSTDIR\licenses"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"
  DeleteRegKey HKCU "${UNINSTALL_KEY}"
SectionEnd
