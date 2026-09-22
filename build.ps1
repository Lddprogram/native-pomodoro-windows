[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$ImagePath,

    [ValidateNotNullOrEmpty()]
    [string]$Name = "NativePomodoro",

    [string]$IconPath,

    [string]$OutputDirectory
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $PSScriptRoot "dist"
}

$sourcePath = Join-Path $PSScriptRoot "native_timer.cs"
$manifestPath = Join-Path $PSScriptRoot "app.manifest"
$resolvedImage = (Resolve-Path -LiteralPath $ImagePath).Path
$resolvedIcon = $null

if (-not [string]::IsNullOrWhiteSpace($IconPath)) {
    $resolvedIcon = (Resolve-Path -LiteralPath $IconPath).Path
    if ([IO.Path]::GetExtension($resolvedIcon).ToLowerInvariant() -ne ".ico") {
        throw "Icon must be an ICO file."
    }
}

if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
    throw "Source file not found: $sourcePath"
}
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "Manifest not found: $manifestPath"
}

$extension = [IO.Path]::GetExtension($resolvedImage).ToLowerInvariant()
if ($extension -notin @(".png", ".jpg", ".jpeg", ".bmp")) {
    throw "Image must be PNG, JPG, JPEG, or BMP."
}

$invalidNameChars = [IO.Path]::GetInvalidFileNameChars()
if ($Name.IndexOfAny($invalidNameChars) -ge 0) {
    throw "Name contains characters that are not valid in a Windows file name."
}

$compilerCandidates = @(
    (Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"),
    (Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe")
)
$compiler = $compilerCandidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
if (-not $compiler) {
    throw "The .NET Framework C# compiler was not found. Enable .NET Framework 4.x or build from a Visual Studio Developer PowerShell."
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$outputPath = Join-Path $OutputDirectory ($Name + ".exe")

$arguments = @(
    "/nologo",
    "/target:winexe",
    "/optimize+",
    "/platform:anycpu",
    "/out:$outputPath",
    "/win32manifest:$manifestPath",
    "/resource:$resolvedImage,TomatoImage",
    "/reference:System.dll",
    "/reference:System.Drawing.dll",
    "/reference:System.Windows.Forms.dll",
    $sourcePath
)

if ($resolvedIcon) {
    $arguments = $arguments[0..4] + ("/win32icon:" + $resolvedIcon) + $arguments[5..($arguments.Length - 1)]
}

& $compiler $arguments
if ($LASTEXITCODE -ne 0) {
    throw "Compilation failed with exit code $LASTEXITCODE."
}

Write-Host "Built successfully:" -ForegroundColor Green
Write-Host $outputPath
