param()

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$msbuild = if (Test-Path -LiteralPath $vswhere) {
    & $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\Current\Bin\amd64\MSBuild.exe' | Select-Object -First 1
} else { $null }
if (-not $msbuild) {
    $command = Get-Command msbuild.exe -ErrorAction SilentlyContinue
    if ($command) { $msbuild = $command.Source }
}
if (-not $msbuild) { throw 'MSBuild do Visual Studio não encontrado.' }

$nativeOutput = Join-Path $projectRoot 'previews\PortableBuild\native'
$publishOutput = Join-Path $projectRoot 'previews\PortableBuild\publish'
$dist = Join-Path $projectRoot 'dist'
[System.IO.Directory]::CreateDirectory($nativeOutput) | Out-Null
[System.IO.Directory]::CreateDirectory($publishOutput) | Out-Null
[System.IO.Directory]::CreateDirectory($dist) | Out-Null

& $msbuild (Join-Path $projectRoot 'KBot.Native\KBot.Native.vcxproj') '/t:Build' '/p:Configuration=Debug' '/p:Platform=x64' "/p:OutDir=$nativeOutput/" '/v:minimal'
if ($LASTEXITCODE -ne 0) { throw 'A compilação do núcleo nativo falhou.' }
$nativeExe = Join-Path $nativeOutput 'KBot.Native.exe'
if (-not (Test-Path -LiteralPath $nativeExe)) { throw 'KBot.Native.exe não foi gerado.' }

& dotnet publish (Join-Path $projectRoot 'KBot.App\KBot.App.csproj') '-c' 'Release' '-r' 'win-x64' '--self-contained' 'true' '-p:Platform=x64' '-p:PublishSingleFile=true' '-p:IncludeNativeLibrariesForSelfExtract=true' '-p:DebugType=None' '-p:DebugSymbols=false' "-p:NativeExecutablePath=$nativeExe" '-o' $publishOutput
if ($LASTEXITCODE -ne 0) { throw 'A publicação do aplicativo falhou.' }
$publishedExe = Join-Path $publishOutput 'KBot.App.exe'
if (-not (Test-Path -LiteralPath $publishedExe)) { throw 'O executável publicado não foi encontrado.' }

$finalExe = Join-Path $dist 'NevesBot.exe'
Copy-Item -LiteralPath $publishedExe -Destination $finalExe -Force
Write-Host "Pronto para abrir: $finalExe"
