param(
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [Parameter(Mandatory)][string]$Wix,
    [Parameter(Mandatory)][string]$Output
)

$ErrorActionPreference = 'Stop'
$source = (Resolve-Path "$PSScriptRoot/../..").Path
$wixExe = (Resolve-Path -LiteralPath $Wix).Path
$outputDir = [IO.Path]::GetFullPath($Output)
$assets = Join-Path $outputDir 'setup-assets'
$publish = Join-Path $outputDir 'publish'
New-Item -ItemType Directory -Path $assets -Force | Out-Null

$brand = Join-Path $source 'CPUSTATS/Assets/Brand'
$driver = Join-Path $assets 'PawnIO_setup.exe'
$driverHash = '1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032'
if (-not (Test-Path -LiteralPath $driver)) {
    Invoke-WebRequest 'https://github.com/namazso/PawnIO.Setup/releases/download/2.2.0/PawnIO_setup.exe' -OutFile $driver
}
if ((Get-FileHash -LiteralPath $driver).Hash -ne $driverHash) {
    throw 'The PawnIO installer checksum does not match the pinned release.'
}
$module = Join-Path $source 'CPUSTATS/Windows/IntelMCHBAR.bin'
if (-not (Test-Path -LiteralPath $module)) {
    & (Join-Path $source 'CPUSTATS/Windows/Get-IntelMchbar.ps1')
}
if ((Get-FileHash -LiteralPath $module).Hash -ne '3F82B832D99B4AAC37D2A20FDB7C9BAA2A3BC0488612C9019C9484EB0E8A6EAE') {
    throw 'The Intel temperature module checksum does not match the pinned release.'
}

$csc = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$driverHelper = Join-Path $assets 'InstallDriver.exe'
& $csc /nologo /target:winexe /platform:x64 "/out:$driverHelper" (Join-Path $PSScriptRoot 'InstallDriver.cs')
if ($LASTEXITCODE -ne 0) { throw 'Could not compile the sensor driver installer.' }

dotnet publish "$source/CPUSTATS/CPUSTATS.csproj" -c Release -r win-x64 --self-contained true "-p:Version=$Version" -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o $publish
if ($LASTEXITCODE -ne 0) { throw 'Could not publish CPUSTATS.' }
Copy-Item -LiteralPath (Join-Path $publish 'CPUSTATS.exe') -Destination (Join-Path $assets 'CPUSTATS.SensorService.exe')

$msi = Join-Path $assets 'CPUSTATS.msi'
& $wixExe build -arch x64 -ext WixToolset.Util.wixext -d "Version=$Version" -d "SourceRoot=$source" -d "PublishDir=$publish" -d "ArtDir=$assets" -o $msi "$PSScriptRoot/CPUSTATS.wxs"
if ($LASTEXITCODE -ne 0) { throw 'Could not build the Windows Installer package.' }

$versionSource = Join-Path $assets 'Version.cs'
$installer = New-Object -ComObject WindowsInstaller.Installer
$database = $installer.OpenDatabase($msi, 0)
$view = $database.OpenView('SELECT Value FROM Property WHERE Property = ''ProductCode''')
$view.Execute()
$productCode = $view.Fetch().StringData(1)
if ($productCode -notmatch '^\{[0-9A-Fa-f-]{36}\}$') { throw 'Could not read the MSI product code.' }
Set-Content -LiteralPath $versionSource -Encoding ascii -Value @(
    "[assembly: System.Reflection.AssemblyFileVersion(`"$Version.0`")]"
    "internal static class BuildVersion { public const string ProductCode = `"$productCode`"; }"
)
$setup = Join-Path $outputDir "CPUSTATS-v$Version-windows-x64-setup.exe"
$manifest = Join-Path $source 'CPUSTATS\Windows\elevated.manifest'
$icon = Join-Path $brand 'cpustats.ico'
$setupSources = 'Setup.cs', 'SetupWindow.cs', 'SetupLog.cs', 'SetupTheme.cs', 'WindowsInstaller.cs' | ForEach-Object { Join-Path $PSScriptRoot $_ }
& $csc /nologo /target:winexe /platform:x64 /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.ServiceProcess.dll "/win32manifest:$manifest" "/win32icon:$icon" "/resource:$msi,CPUSTATS.msi" "/out:$setup" $setupSources $versionSource
if ($LASTEXITCODE -ne 0) { throw 'Could not compile CPUSTATS Setup.' }
Get-Item -LiteralPath $setup
