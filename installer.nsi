; ZeeVault NSIS Installer Script
; Requires: NSIS 3.x

!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "FileFunc.nsh"

; ── General ──────────────────────────────────────────────────────
Name "ZeeVault"
OutFile "ZeeVault-Setup.exe"
InstallDir "$PROGRAMFILES\ZeeVault"
InstallDirRegKey HKCU "Software\ZeeVault" "InstallDir"
RequestExecutionLevel admin
BrandingText "ZeeVault Installer"

; ── Version Info ─────────────────────────────────────────────────
VIProductVersion "1.1.0.0"
VIAddVersionKey "ProductName" "ZeeVault"
VIAddVersionKey "CompanyName" "Charlotte Zee"
VIAddVersionKey "FileDescription" "ZeeVault Installer"
VIAddVersionKey "FileVersion" "1.1.0.0"
VIAddVersionKey "ProductVersion" "1.1.0"
VIAddVersionKey "LegalCopyright" "Copyright (c) 2026 Charlotte Zee / ZenytheLabs"

; ── Interface ────────────────────────────────────────────────────
!define MUI_ICON "D:\Publish\ZeeVault\app.ico"
!define MUI_UNICON "D:\Publish\ZeeVault\app.ico"
!define MUI_ABORTWARNING
!define MUI_WELCOMEFINISHPAGE_BITMAP "${NSISDIR}\Contrib\Graphics\Wizard\win.bmp"
!define MUI_UNWELCOMEFINISHPAGE_BITMAP "${NSISDIR}\Contrib\Graphics\Wizard\win.bmp"
!define MUI_FINISHPAGE_RUN "$INSTDIR\ZeeVault.exe"
!define MUI_FINISHPAGE_RUN_TEXT "Launch ZeeVault"

; ── Pages ────────────────────────────────────────────────────────
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_LICENSE "D:\Publish\ZeeVault\LICENSE"
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

; ── Languages ────────────────────────────────────────────────────
!insertmacro MUI_LANGUAGE "English"

; ── Installer Section ───────────────────────────────────────────
Section "ZeeVault (required)" SecMain
    SectionIn RO

    SetOutPath "$INSTDIR"
    SetOverwrite on

    ; Store install path
    WriteRegStr HKCU "Software\ZeeVault" "InstallDir" "$INSTDIR"

    ; ── Install ALL files from publish folder recursively ────────
    File /r "D:\Publish\ZeeVault\publish\*.exe"
    File /r "D:\Publish\ZeeVault\publish\*.dll"
    File /r "D:\Publish\ZeeVault\publish\*.json"
    File /r "D:\Publish\ZeeVault\publish\*.txt"

    ; ── Localization folders ─────────────────────────────────────
    SetOutPath "$INSTDIR\cs"
    File /r "D:\Publish\ZeeVault\publish\cs\*.*"
    SetOutPath "$INSTDIR\de"
    File /r "D:\Publish\ZeeVault\publish\de\*.*"
    SetOutPath "$INSTDIR\es"
    File /r "D:\Publish\ZeeVault\publish\es\*.*"
    SetOutPath "$INSTDIR\fr"
    File /r "D:\Publish\ZeeVault\publish\fr\*.*"
    SetOutPath "$INSTDIR\it"
    File /r "D:\Publish\ZeeVault\publish\it\*.*"
    SetOutPath "$INSTDIR\ja"
    File /r "D:\Publish\ZeeVault\publish\ja\*.*"
    SetOutPath "$INSTDIR\ko"
    File /r "D:\Publish\ZeeVault\publish\ko\*.*"
    SetOutPath "$INSTDIR\pl"
    File /r "D:\Publish\ZeeVault\publish\pl\*.*"
    SetOutPath "$INSTDIR\pt-BR"
    File /r "D:\Publish\ZeeVault\publish\pt-BR\*.*"
    SetOutPath "$INSTDIR\ru"
    File /r "D:\Publish\ZeeVault\publish\ru\*.*"
    SetOutPath "$INSTDIR\tr"
    File /r "D:\Publish\ZeeVault\publish\tr\*.*"
    SetOutPath "$INSTDIR\zh-Hans"
    File /r "D:\Publish\ZeeVault\publish\zh-Hans\*.*"
    SetOutPath "$INSTDIR\zh-Hant"
    File /r "D:\Publish\ZeeVault\publish\zh-Hant\*.*"

    ; Reset output path
    SetOutPath "$INSTDIR"

    ; ── Start Menu shortcuts ─────────────────────────────────────
    CreateDirectory "$SMPROGRAMS\ZeeVault"
    CreateShortCut "$SMPROGRAMS\ZeeVault\ZeeVault.lnk" "$INSTDIR\ZeeVault.exe" "" "$INSTDIR\ZeeVault.exe"
    CreateShortCut "$SMPROGRAMS\ZeeVault\Uninstall ZeeVault.lnk" "$INSTDIR\uninstall.exe"

    ; ── Desktop shortcut ─────────────────────────────────────────
    CreateShortCut "$DESKTOP\ZeeVault.lnk" "$INSTDIR\ZeeVault.exe" "" "$INSTDIR\ZeeVault.exe"

    ; ── Uninstaller ──────────────────────────────────────────────
    WriteUninstaller "$INSTDIR\uninstall.exe"

    ; ── Add / Remove Programs entry ──────────────────────────────
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\ZeeVault" \
        "DisplayName" "ZeeVault"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\ZeeVault" \
        "UninstallString" '"$INSTDIR\uninstall.exe"'
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\ZeeVault" \
        "InstallLocation" "$INSTDIR"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\ZeeVault" \
        "DisplayIcon" "$INSTDIR\ZeeVault.exe"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\ZeeVault" \
        "Publisher" "Charlotte Zee / ZenytheLabs"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\ZeeVault" \
        "DisplayVersion" "1.1.0"
    WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\ZeeVault" \
        "NoModify" 1
    WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\ZeeVault" \
        "NoRepair" 1

    ; Calculate installed size
    ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
    IntFmt $0 "0x%08X" $0
    WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\ZeeVault" \
        "EstimatedSize" "$0"

SectionEnd

; ── Uninstaller ─────────────────────────────────────────────────
Section "Uninstall"

    ; Remove everything in install directory
    RMDir /r "$INSTDIR"

    ; Remove Start Menu shortcuts
    Delete "$SMPROGRAMS\ZeeVault\ZeeVault.lnk"
    Delete "$SMPROGRAMS\ZeeVault\Uninstall ZeeVault.lnk"
    RMDir "$SMPROGRAMS\ZeeVault"

    ; Remove desktop shortcut
    Delete "$DESKTOP\ZeeVault.lnk"

    ; Remove registry keys
    DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\ZeeVault"
    DeleteRegKey HKCU "Software\ZeeVault"

SectionEnd
