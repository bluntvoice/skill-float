[CmdletBinding()]
param(
    [switch]$SkipInstaller,
    [string]$MSBuildPath,
    [string]$MakensisPath
)

$ErrorActionPreference = 'Stop'
[Console]::InputEncoding = [Text.UTF8Encoding]::new($false)
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$OutputEncoding = [Text.UTF8Encoding]::new($false)

if ($PSVersionTable.PSVersion -lt [Version]'7.0') {
    throw '请使用 PowerShell 7.0 或更新稳定版执行构建。'
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'native\SkillFloat\SkillFloat.csproj'
$versionFile = Join-Path $repoRoot 'VERSION'
$version = (Get-Content -LiteralPath $versionFile -Raw -Encoding utf8).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw "VERSION 格式无效：$version" }

function Find-MSBuild {
    param([string]$ExplicitPath)
    if ($ExplicitPath) {
        if (Test-Path -LiteralPath $ExplicitPath -PathType Leaf) { return (Resolve-Path -LiteralPath $ExplicitPath).Path }
        throw "指定的 MSBuild 不存在：$ExplicitPath"
    }
    $vswhereCandidates = @(@(
        (Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'),
        (Join-Path $env:ProgramFiles 'Microsoft Visual Studio\Installer\vswhere.exe')
    ) | Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Leaf) })
    $vswhereOnPath = Get-Command vswhere.exe -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($vswhereOnPath) { $vswhereCandidates += @($vswhereOnPath.Source) }
    foreach ($vswhere in $vswhereCandidates | Select-Object -Unique) {
        $found = & $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
        if ($found -and (Test-Path -LiteralPath $found -PathType Leaf)) { return $found }
    }
    $fromPath = Get-Command msbuild.exe -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($fromPath) { return $fromPath.Source }
    $common = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'),
        (Join-Path $env:ProgramFiles 'Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe'),
        (Join-Path $env:ProgramFiles 'Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe')
    )
    return $common | Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Leaf) } | Select-Object -First 1
}

function Find-Makensis {
    param([string]$ExplicitPath)
    if ($ExplicitPath) {
        if (Test-Path -LiteralPath $ExplicitPath -PathType Leaf) { return (Resolve-Path -LiteralPath $ExplicitPath).Path }
        throw "指定的 makensis 不存在：$ExplicitPath"
    }
    $fromPath = Get-Command makensis.exe -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($fromPath) { return $fromPath.Source }
    $common = @(
        (Join-Path ${env:ProgramFiles(x86)} 'NSIS\makensis.exe'),
        (Join-Path $env:ProgramFiles 'NSIS\makensis.exe')
    )
    return $common | Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Leaf) } | Select-Object -First 1
}

$msbuild = Find-MSBuild $MSBuildPath
if (-not $msbuild) {
    throw '找不到 MSBuild。请安装 Visual Studio 2022 Build Tools（.NET 桌面生成工具），或通过 -MSBuildPath 指定路径。'
}

$referenceRoot = Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8'
$runtimeRoot = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$buildArgs = @($project, '/p:Configuration=Release', '/m', '/v:minimal')
if (-not (Test-Path -LiteralPath (Join-Path $referenceRoot 'System.Windows.Forms.dll'))) {
    if (-not (Test-Path -LiteralPath (Join-Path $runtimeRoot 'System.Windows.Forms.dll'))) {
        throw '找不到 .NET Framework 4.8 目标包或兼容运行程序集。请安装 .NET Framework 4.8 Developer Pack。'
    }
    Write-Warning '未找到 .NET Framework 4.8 Developer Pack，当前使用系统运行程序集兼容构建；公开构建环境建议安装 Developer Pack。'
    $buildArgs += "/p:FrameworkPathOverride=$runtimeRoot"
}

& $msbuild @buildArgs
if ($LASTEXITCODE -ne 0) { throw "原生程序构建失败，退出码：$LASTEXITCODE" }

$exe = Join-Path $repoRoot 'native\SkillFloat\bin\Release\SkillFloat.exe'
$test = Start-Process -FilePath $exe -ArgumentList '--self-test' -Wait -PassThru -WindowStyle Hidden
if ($test.ExitCode -ne 0) { throw "自检失败，退出码：$($test.ExitCode)" }

if (-not $SkipInstaller) {
    $makensis = Find-Makensis $MakensisPath
    if (-not $makensis) {
        throw '找不到 NSIS makensis。请安装 NSIS 并加入 PATH，或通过 -MakensisPath 指定路径；脚本不会自动下载依赖。'
    }
    $release = Join-Path $repoRoot 'release'
    New-Item -ItemType Directory -Path $release -Force | Out-Null
    Push-Location $repoRoot
    try {
        & $makensis /INPUTCHARSET UTF8 /V2 "/DPRODUCT_VERSION=$version" "/DPRODUCT_VERSION_QUAD=$version.0" 'installer\SkillFloat.nsi'
        if ($LASTEXITCODE -ne 0) { throw "安装包构建失败，退出码：$LASTEXITCODE" }
    }
    finally { Pop-Location }
}

Write-Output "构建完成：$exe"
if (-not $SkipInstaller) {
    Write-Output (Join-Path $repoRoot "release\Skill Float_${version}_x64-setup.exe")
}
