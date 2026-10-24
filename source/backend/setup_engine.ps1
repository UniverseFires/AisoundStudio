param(
    [Parameter(Mandatory=$true)][string]$EngineRoot,
    [Parameter(Mandatory=$true)][string]$AppDirectory,
    [switch]$AudioOnly
)
$ErrorActionPreference='Stop'
[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
[Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12
$EngineRoot=[IO.Path]::GetFullPath($EngineRoot)
$AppDirectory=[IO.Path]::GetFullPath($AppDirectory)
$downloads=Join-Path $EngineRoot 'downloads'
$temp=Join-Path $downloads 'temp'
$pythonHome=Join-Path $EngineRoot 'python'
$taskPython=Join-Path $EngineRoot 'runtime\Scripts\python.exe'
$rvcRoot=Join-Path $EngineRoot 'RVC'
New-Item -ItemType Directory -Force -Path $downloads,$temp | Out-Null
$env:AISOUND_ROOT=$EngineRoot
$env:PIP_CONFIG_FILE='NUL'
$env:PIP_INDEX_URL='https://pypi.org/simple'
$env:PIP_EXTRA_INDEX_URL=''
$env:PIP_CACHE_DIR=Join-Path $downloads 'pip-cache'
$env:HF_HOME=Join-Path $downloads 'hf-cache'
$env:PYTHONUTF8='1'
$env:PYTHONIOENCODING='utf-8'
$env:TEMP=$temp
$env:TMP=$temp

if (!$AudioOnly) {
    $gpuCommand=Get-Command 'nvidia-smi.exe' -ErrorAction SilentlyContinue
    if (!$gpuCommand) {$gpuCommand=Get-Command 'nvidia-smi' -ErrorAction SilentlyContinue}
    if (!$gpuCommand) {throw 'AI voice processing currently requires a supported NVIDIA GPU. No NVIDIA driver was detected, so no large files were downloaded.'}
    $gpuName=(& $gpuCommand.Source '--query-gpu=name' '--format=csv,noheader' 2>$null | Select-Object -First 1)
    if (!$gpuName) {throw 'NVIDIA GPU detection failed. Update the NVIDIA driver and try again.'}
    $cuda=if ($gpuName -match 'RTX\s*50') {'128'} else {'118'}
    Write-Output "NVIDIA GPU: $gpuName · CUDA package: $cuda"
}

function Invoke-Download([string]$Url,[string]$Path) {
    Write-Output "Download source: $Url"
    Invoke-WebRequest -Uri $Url -OutFile $Path -UseBasicParsing
}
function Invoke-StudioPython([string[]]$Arguments) {
    & $taskPython @Arguments
    if ($LASTEXITCODE -ne 0) {throw 'Installation step failed. Completed downloads are retained; fix the issue and choose install again to continue.'}
}

if (!(Test-Path -LiteralPath $taskPython)) {
    $installer=Join-Path $downloads 'python-3.12.10-amd64.exe'
    if (!(Test-Path -LiteralPath $installer)) {
        Invoke-Download 'https://www.python.org/ftp/python/3.12.10/python-3.12.10-amd64.exe' $installer
    }
    $signature=Get-AuthenticodeSignature -LiteralPath $installer
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'Python Software Foundation') {
        throw 'Python installer signature is not valid or is not from the Python Software Foundation. The file was not run.'
    }
    Write-Output 'Installing Python 3.12 locally in the selected AI environment folder.'
    $args=@('/quiet','InstallAllUsers=0',('TargetDir="'+$pythonHome+'"'),'Include_pip=1','Include_launcher=0','Include_test=0','Include_doc=0','Shortcuts=0','PrependPath=0','AssociateFiles=0')
    $setup=Start-Process -FilePath $installer -ArgumentList $args -Wait -PassThru
    if ($setup.ExitCode -ne 0 -or !(Test-Path -LiteralPath (Join-Path $pythonHome 'python.exe'))) {throw 'Python setup did not finish successfully.'}
    Remove-Item -LiteralPath $installer -Force
    $basePython=Join-Path $pythonHome 'python.exe'
    & $basePython -m venv (Join-Path $EngineRoot 'runtime')
    if ($LASTEXITCODE -ne 0) {throw 'Could not create the private Python environment.'}
}
if (!(Test-Path -LiteralPath $taskPython)) {throw 'Private Python environment is incomplete. Retry installation.'}

Write-Output 'Preparing Python and FFmpeg audio tools from PyPI.'
Invoke-StudioPython @('-m','pip','install','--upgrade','pip','setuptools<81','wheel','imageio-ffmpeg','--timeout','30','--retries','2')
if ($AudioOnly) {
    Write-Output 'Checking FFmpeg audio support.'
    $audioLines=& $taskPython -u (Join-Path $AppDirectory 'backend\bridge.py') diagnostics
    if ($LASTEXITCODE -ne 0) {throw 'Audio environment diagnostics could not run.'}
    $audioResult=$null
    foreach ($line in $audioLines) {try {$event=$line | ConvertFrom-Json; if ($event.type -eq 'result') {$audioResult=$event.data}} catch {}}
    if (!$audioResult -or !$audioResult.audio_ready) {throw 'FFmpeg audio support is still not ready. Review the environment check and retry.'}
    Write-Output 'Audio tools are ready.'
    exit 0
}

if (!(Test-Path -LiteralPath (Join-Path $rvcRoot 'requirments_cu118_py312.txt'))) {
    if (Test-Path -LiteralPath $rvcRoot) {throw 'The selected location already contains an RVC folder that this installer does not recognize. No existing files were changed; choose an empty folder or repair that environment manually.'}
    $archive=Join-Path $downloads ('rvc-source-'+[guid]::NewGuid().ToString('N')+'.zip')
    Invoke-Download 'https://github.com/RVC-Project/Retrieval-based-Voice-Conversion-WebUI/archive/refs/heads/main.zip' $archive
    $unpack=Join-Path $downloads ('rvc-source-'+[guid]::NewGuid().ToString('N'))
    Expand-Archive -LiteralPath $archive -DestinationPath $unpack -Force
    $repo=Get-ChildItem -LiteralPath $unpack -Directory | Select-Object -First 1
    if (!$repo -or !(Test-Path (Join-Path $repo.FullName 'requirments_cu118_py312.txt'))) {throw 'Downloaded RVC source did not contain the expected Python 3.12 requirements.'}
    Move-Item -LiteralPath $repo.FullName -Destination $rvcRoot
    Remove-Item -LiteralPath $unpack -Recurse -Force
    Remove-Item -LiteralPath $archive -Force
}

Write-Output '[1/4] Installing Python packages from official PyPI and PyTorch sources.'
Invoke-StudioPython @('-m','pip','install','huggingface_hub','--timeout','30','--retries','2')
$torchIndex="https://download.pytorch.org/whl/cu$cuda"
Invoke-StudioPython @('-m','pip','install',"torch==2.7.1+cu$cuda", "torchaudio==2.7.1+cu$cuda",'--index-url',$torchIndex,'--extra-index-url','https://pypi.org/simple','--timeout','30','--retries','2')
$requirements=Join-Path $rvcRoot "requirments_cu${cuda}_py312.txt"
if (!(Test-Path -LiteralPath $requirements)) {throw "RVC did not provide the expected CUDA $cuda dependency file."}
$cleanRequirements=Join-Path $downloads "requirements-cu$cuda.txt"
$requirementText=[IO.File]::ReadAllText($requirements).Replace('https://mirrors.pku.edu.cn/pypi/simple','https://pypi.org/simple')
[IO.File]::WriteAllText($cleanRequirements,$requirementText,[Text.UTF8Encoding]::new($false))
Invoke-StudioPython @('-m','pip','install','-r',$cleanRequirements,'--timeout','30','--retries','2')

Write-Output '[2/4] Downloading the RVC, separation, and dereverb models from Hugging Face.'
Invoke-StudioPython @('-u',(Join-Path $AppDirectory 'backend\prepare_models.py'))

Write-Output '[3/4] Checking the installed environment.'
$diagnosticLines=& $taskPython -u (Join-Path $AppDirectory 'backend\bridge.py') diagnostics
if ($LASTEXITCODE -ne 0) {throw 'AI environment diagnostics could not run.'}
$result=$null
foreach ($line in $diagnosticLines) {
    try {$event=$line | ConvertFrom-Json; if ($event.type -eq 'result') {$result=$event.data}} catch {}
}
if (!$result -or !$result.train_ready -or !$result.cover_ready) {throw 'The downloads finished, but the AI environment is not ready. Review the environment check in Aisound Studio and retry.'}
Write-Output '[4/4] AI environment is ready.'
