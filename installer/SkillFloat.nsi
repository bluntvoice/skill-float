Unicode true
RequestExecutionLevel user
SetCompressor /SOLID lzma
SetCompressorDictSize 32

!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "x64.nsh"

!define PRODUCT_NAME "Skill Float"
!define PRODUCT_VERSION "0.3.0"
!define PRODUCT_PUBLISHER "bluntvoice"
!define UNINSTALL_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\Skill Float"
!define APP_EXE "SkillFloat.exe"

Name "${PRODUCT_NAME}"
OutFile "..\release\Skill Float_0.3.0_x64-setup.exe"
InstallDir "$LOCALAPPDATA\Programs\Skill Float"
InstallDirRegKey HKCU "${UNINSTALL_KEY}" "InstallLocation"
Icon "..\src-tauri\icons\icon.ico"
UninstallIcon "..\src-tauri\icons\icon.ico"
BrandingText "Skill Float · 轻量原生版"
ManifestDPIAware true
VIProductVersion "0.3.0.0"
VIAddVersionKey /LANG=2052 "ProductName" "Skill Float"
VIAddVersionKey /LANG=2052 "CompanyName" "bluntvoice"
VIAddVersionKey /LANG=2052 "FileDescription" "Skill Float 中文安装程序"
VIAddVersionKey /LANG=2052 "FileVersion" "0.3.0"
VIAddVersionKey /LANG=2052 "ProductVersion" "0.3.0"
VIAddVersionKey /LANG=2052 "LegalCopyright" "Copyright 2026 bluntvoice"

!define MUI_ABORTWARNING
!define MUI_ICON "..\src-tauri\icons\icon.ico"
!define MUI_UNICON "..\src-tauri\icons\icon.ico"
!define MUI_WELCOMEPAGE_TITLE "安装 Skill Float"
!define MUI_WELCOMEPAGE_TEXT "轻量的 Skill 悬浮选择器。$\r$\n$\r$\n更新时会自动保留 API 配置、汉化结果、分类、标签、收藏与调用统计。"
!define MUI_DIRECTORYPAGE_TEXT_TOP "选择安装位置。已有版本会在原位置直接升级，不显示旧版卸载界面。"
!define MUI_FINISHPAGE_TITLE "Skill Float 已安装"
!define MUI_FINISHPAGE_TEXT "升级与数据迁移已经完成。"
!define MUI_FINISHPAGE_RUN "$INSTDIR\${APP_EXE}"
!define MUI_FINISHPAGE_RUN_TEXT "打开 Skill Float"
!define MUI_FINISHPAGE_NOAUTOCLOSE
!define MUI_UNFINISHPAGE_NOAUTOCLOSE

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_WELCOME
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_UNPAGE_FINISH

!insertmacro MUI_LANGUAGE "SimpChinese"

Var OldUninstaller
Var OldInstallLocation
Var MigrationExit
Var UninstallExit

Function .onInit
    SetShellVarContext current
    ${IfNot} ${RunningX64}
        MessageBox MB_ICONSTOP|MB_OK "Skill Float 仅支持 64 位 Windows。"
        Abort
    ${EndIf}
    ReadRegStr $OldInstallLocation HKCU "${UNINSTALL_KEY}" "InstallLocation"
    ${If} $OldInstallLocation != ""
        ; Tauri 旧版把安装目录连同双引号写入注册表；先去掉首尾引号。
        StrCpy $0 $OldInstallLocation 1
        StrCmp $0 $\" 0 +2
        StrCpy $OldInstallLocation $OldInstallLocation -1 1
        StrCpy $0 $OldInstallLocation 1 -1
        StrCmp $0 $\" 0 +2
        StrCpy $OldInstallLocation $OldInstallLocation -1
        StrCpy $INSTDIR $OldInstallLocation
    ${EndIf}
FunctionEnd

Section "安装 Skill Float" SEC_MAIN
    SetShellVarContext current
    SetOutPath "$PLUGINSDIR"
    File /oname=SkillFloat-migrate.exe "..\native\SkillFloat\bin\Release\SkillFloat.exe"
    DetailPrint "正在迁移旧版设置与 API 密钥…"
    ExecWait '"$PLUGINSDIR\SkillFloat-migrate.exe" --migrate-before-upgrade' $MigrationExit
    ${If} $MigrationExit != 0
        MessageBox MB_ICONSTOP|MB_OK "无法迁移旧版数据，安装已停止。原程序和数据均未修改。"
        Abort
    ${EndIf}

    DetailPrint "正在平滑关闭旧版本…"
    ExecWait '"$PLUGINSDIR\SkillFloat-migrate.exe" --shutdown'
    nsExec::ExecToLog '"$SYSDIR\taskkill.exe" /F /T /IM "skill-float.exe"'
    nsExec::ExecToLog '"$SYSDIR\taskkill.exe" /F /T /IM "SkillFloat.exe"'
    Sleep 600

    ReadRegStr $OldUninstaller HKCU "${UNINSTALL_KEY}" "UninstallString"
    ${If} $OldUninstaller != ""
        IfFileExists "$OldInstallLocation\uninstall.exe" 0 old_uninstall_done
        DetailPrint "正在后台替换旧版本…"
        ExecWait '$OldUninstaller /S _?=$OldInstallLocation' $UninstallExit
        ${If} $UninstallExit != 0
            MessageBox MB_ICONSTOP|MB_OK "旧版本替换失败（代码 $UninstallExit）。已迁移的数据仍安全保留。"
            Abort
        ${EndIf}
    ${EndIf}
old_uninstall_done:

    SetOutPath "$INSTDIR"
    File /oname=${APP_EXE} "..\native\SkillFloat\bin\Release\SkillFloat.exe"
    WriteUninstaller "$INSTDIR\uninstall.exe"

    CreateDirectory "$SMPROGRAMS\Skill Float"
    CreateShortcut "$SMPROGRAMS\Skill Float\Skill Float.lnk" "$INSTDIR\${APP_EXE}" "" "$INSTDIR\${APP_EXE}" 0
    CreateShortcut "$SMPROGRAMS\Skill Float\卸载 Skill Float.lnk" "$INSTDIR\uninstall.exe"

    WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayName" "Skill Float"
    WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayVersion" "${PRODUCT_VERSION}"
    WriteRegStr HKCU "${UNINSTALL_KEY}" "Publisher" "${PRODUCT_PUBLISHER}"
    WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayIcon" "$INSTDIR\${APP_EXE}"
    WriteRegStr HKCU "${UNINSTALL_KEY}" "InstallLocation" "$INSTDIR"
    WriteRegStr HKCU "${UNINSTALL_KEY}" "UninstallString" '"$INSTDIR\uninstall.exe"'
    WriteRegStr HKCU "${UNINSTALL_KEY}" "QuietUninstallString" '"$INSTDIR\uninstall.exe" /S'
    WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoModify" 1
    WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoRepair" 1
    WriteRegDWORD HKCU "${UNINSTALL_KEY}" "EstimatedSize" 1800
SectionEnd

Section "Uninstall"
    SetShellVarContext current
    nsExec::ExecToLog '"$SYSDIR\taskkill.exe" /F /T /IM "SkillFloat.exe"'
    Sleep 300
    Delete "$INSTDIR\${APP_EXE}"
    Delete "$INSTDIR\uninstall.exe"
    Delete "$SMPROGRAMS\Skill Float\Skill Float.lnk"
    Delete "$SMPROGRAMS\Skill Float\卸载 Skill Float.lnk"
    RMDir "$SMPROGRAMS\Skill Float"
    RMDir "$INSTDIR"
    DeleteRegKey HKCU "${UNINSTALL_KEY}"
    DetailPrint "用户数据与 API 配置已保留，可供以后重新安装使用。"
SectionEnd
