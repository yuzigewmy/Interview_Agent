[CmdletBinding()]
param(
    [string]$TargetDirectory = (
        Join-Path $env:LOCALAPPDATA 'InterviewAgent\models\bge-small-zh-v1.5'
    )
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$revision = 'fcecc3c5fef6becfa2b2bdda15c1c938857be534'
$mirrorRoot = 'https://hf-mirror.com/Xenova/bge-small-zh-v1.5'
$modelUri = "$mirrorRoot/resolve/$revision/onnx/model_quantized.onnx"
$vocabularyUri = "$mirrorRoot/resolve/$revision/vocab.txt"
$modelHash = '15B717C382BCB518BA457B93EA6850EDE7F4F1CD8937454AA06972366CD19BCC'
$vocabularyHash = '45BBAC6B341C319ADC98A532532882E91A9CEFC0329AA57BAC9AE761C27B291C'

function Install-VerifiedFile {
    param(
        [Parameter(Mandatory)]
        [string]$Source,
        [Parameter(Mandatory)]
        [string]$Destination,
        [Parameter(Mandatory)]
        [string]$ExpectedSha256
    )

    if (Test-Path -LiteralPath $Destination) {
        $existingHash = (Get-FileHash -LiteralPath $Destination -Algorithm SHA256).Hash
        if ($existingHash -eq $ExpectedSha256) {
            Write-Host "已存在且校验通过：$Destination"
            return
        }
    }

    $temporaryPath = "$Destination.$([Guid]::NewGuid().ToString('N')).download"
    try {
        Write-Host "正在下载：$Source"
        Import-Module BitsTransfer
        Start-BitsTransfer -Source $Source -Destination $temporaryPath
        $actualHash = (Get-FileHash -LiteralPath $temporaryPath -Algorithm SHA256).Hash
        if ($actualHash -ne $ExpectedSha256) {
            throw "下载文件 SHA-256 校验失败：$Destination"
        }

        Move-Item -LiteralPath $temporaryPath -Destination $Destination -Force
    }
    finally {
        if (Test-Path -LiteralPath $temporaryPath) {
            Remove-Item -LiteralPath $temporaryPath -Force
        }
    }
}

$resolvedTarget = [System.IO.Path]::GetFullPath($TargetDirectory)
New-Item -ItemType Directory -Path $resolvedTarget -Force | Out-Null

Install-VerifiedFile `
    -Source $modelUri `
    -Destination (Join-Path $resolvedTarget 'model.onnx') `
    -ExpectedSha256 $modelHash
Install-VerifiedFile `
    -Source $vocabularyUri `
    -Destination (Join-Path $resolvedTarget 'vocab.txt') `
    -ExpectedSha256 $vocabularyHash

Write-Host ''
Write-Host 'BGE-small-zh-v1.5 已安装。'
Write-Host '重新启动 Interview Agent 并重新导入面经后，本地语义检索将自动启用。'
