param([switch]$SelfContained)
$ErrorActionPreference = 'Stop'
$projectPath = Join-Path $PSScriptRoot 'BiliRemote.csproj'
$outputPath = Join-Path $PSScriptRoot 'artifacts\BiliRemote'
if ($SelfContained) {
    dotnet publish $projectPath -c Release -r win-x64 --self-contained true -o $outputPath
} else {
    dotnet publish $projectPath -c Release -r win-x64 --self-contained false -o $outputPath
}
if ($LASTEXITCODE -ne 0) { throw '构建失败，请检查上方错误。' }
Write-Host "程序已生成：$outputPath\BiliRemote.exe"
