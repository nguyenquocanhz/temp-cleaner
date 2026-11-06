# Publish Temp Cleaner (self-contained, 1 file) rồi đóng gói bằng Inno Setup.
# Chạy: powershell -ExecutionPolicy Bypass -File tools\build-installer.ps1
# Kết quả: dist\TempCleaner-Setup-<version>.exe và dist\TempCleaner-<version>-win-x64-portable.zip
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
Set-Location $root

$version = ([xml](Get-Content TempCleaner.csproj -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw 'Không đọc được <Version> trong TempCleaner.csproj' }

$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
          "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
          "$env:ProgramFiles\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Chưa cài Inno Setup 6: winget install JRSoftware.InnoSetup' }

Remove-Item publish\win-x64 -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o publish\win-x64
if ($LASTEXITCODE) { throw 'dotnet publish lỗi' }

New-Item -ItemType Directory -Force dist | Out-Null
& $iscc /Qp "/DAppVersion=$version" installer\TempCleaner.iss
if ($LASTEXITCODE) { throw 'ISCC lỗi' }

$zip = "dist\TempCleaner-$version-win-x64-portable.zip"
Remove-Item $zip -ErrorAction SilentlyContinue
Compress-Archive -Path publish\win-x64\TempCleaner.exe, LICENSE -DestinationPath $zip

Get-ChildItem dist | Where-Object Name -like "*$version*" | ForEach-Object {
    '{0}  {1:N1} MB  SHA256 {2}' -f $_.Name, ($_.Length / 1MB), (Get-FileHash $_.FullName).Hash
}
