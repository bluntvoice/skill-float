[CmdletBinding()]
param(
    [switch]$SkipInstaller
)

$ErrorActionPreference = 'Stop'
[Console]::InputEncoding = [Text.UTF8Encoding]::new($false)
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$OutputEncoding = [Text.UTF8Encoding]::new($false)

if ($PSVersionTable.PSVersion -lt [Version]'7.6.4') {
    throw '请使用 PowerShell 7.6.4 或更新稳定版执行构建。'
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'native\SkillFloat\SkillFloat.csproj'
$msbuild = 'F:\Visual Studio\MSBuild\Current\Bin\MSBuild.exe'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$makensis = Join-Path $env:LOCALAPPDATA 'tauri\NSIS\makensis.exe'

if (-not (Test-Path -LiteralPath $msbuild)) { throw "找不到 MSBuild：$msbuild" }
if (-not (Test-Path -LiteralPath (Join-Path $framework 'System.Windows.Forms.dll'))) { throw '找不到 .NET Framework 4.x 运行程序集。' }

& $msbuild $project /p:Configuration=Release /p:FrameworkPathOverride=$framework /m /v:minimal
if ($LASTEXITCODE -ne 0) { throw "原生程序构建失败，退出码：$LASTEXITCODE" }

$exe = Join-Path $repoRoot 'native\SkillFloat\bin\Release\SkillFloat.exe'
$test = Start-Process -FilePath $exe -ArgumentList '--self-test' -Wait -PassThru -WindowStyle Hidden
if ($test.ExitCode -ne 0) { throw "自检失败，退出码：$($test.ExitCode)" }

if (-not $SkipInstaller) {
    if (-not (Test-Path -LiteralPath $makensis)) { throw "找不到 NSIS：$makensis" }
    Push-Location $repoRoot
    try {
        & $makensis /INPUTCHARSET UTF8 /V2 'installer\SkillFloat.nsi'
        if ($LASTEXITCODE -ne 0) { throw "安装包构建失败，退出码：$LASTEXITCODE" }
    }
    finally { Pop-Location }
}

Write-Output "构建完成：$exe"
if (-not $SkipInstaller) {
    Write-Output (Join-Path $repoRoot 'release\Skill Float_0.3.0_x64-setup.exe')
}
