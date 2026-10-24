$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$publishDir = [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts\publish'))
$artifactsDir = [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts'))
if (!$publishDir.StartsWith($repoRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Publish output resolved outside the project folder.'
}
if (Test-Path -LiteralPath $publishDir) { Remove-Item -LiteralPath $publishDir -Recurse -Force }
$oldInstaller = Join-Path $artifactsDir 'AisoundStudio-Setup.exe'
if (Test-Path -LiteralPath $oldInstaller) { Remove-Item -LiteralPath $oldInstaller -Force }
New-Item -ItemType Directory -Path $publishDir -Force | Out-Null

$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_HOME = Join-Path $repoRoot '.dotnet-home'
$env:NUGET_PACKAGES = Join-Path $repoRoot '.nuget\packages'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $repoRoot '.nuget\http-cache'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

$project = Join-Path $repoRoot 'source\AisoundStudio.csproj'
dotnet publish $project --configuration Release --runtime win-x64 --self-contained true `
    -p:DebugType=None -p:DebugSymbols=false -p:PublishSingleFile=false `
    --output $publishDir
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

$compiler = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
if (!$compiler) {
    $candidates = @(
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 7\ISCC.exe')
    )
    $compilerPath = $candidates | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1
} else { $compilerPath = $compiler.Source }
if (!$compilerPath) {
    throw 'The application published successfully, but Inno Setup Compiler (ISCC.exe) is missing. Install Inno Setup and rerun this script to create the installer.'
}

$iss = Join-Path $repoRoot 'installer\AisoundStudio.iss'
& $compilerPath "/DPublishDir=$publishDir" $iss
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup could not compile the installer.' }
Write-Output (Join-Path $artifactsDir 'AisoundStudio-Setup.exe')
