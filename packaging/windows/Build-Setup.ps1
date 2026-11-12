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

Add-Type -AssemblyName System.Drawing
$brand = Join-Path $source 'CPUSTATS/Assets/Brand'
$logo = [Drawing.Image]::FromFile((Join-Path $brand 'cpustats-dark.png'))
try {
    foreach ($design in @(
        @{ Name = 'banner'; Width = 493; Height = 58; Size = 44; X = 435; Y = 6; Fill = '#E9EFEE'; Accent = '#0B7056' },
        @{ Name = 'dialog'; Width = 493; Height = 312; Size = 100; X = 32; Y = 40; Fill = '#FAFCFB'; Accent = '#85E6C3' }
    )) {
        $bitmap = [Drawing.Bitmap]::new($design.Width, $design.Height, [Drawing.Imaging.PixelFormat]::Format24bppRgb)
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        $accent = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml($design.Accent))
        try {
            $graphics.Clear([Drawing.ColorTranslator]::FromHtml($design.Fill))
            if ($design.Name -eq 'dialog') {
                $panel = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#171D20'))
                try { $graphics.FillRectangle($panel, 0, 0, 163, $design.Height) }
                finally { $panel.Dispose() }
            }
            $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.DrawImage($logo, $design.X, $design.Y, $design.Size, $design.Size)
            $accentWidth = if ($design.Name -eq 'dialog') { 163 } else { $design.Width }
            $graphics.FillRectangle($accent, 0, ($design.Height - 3), $accentWidth, 3)
            $bitmap.Save((Join-Path $assets "$($design.Name).bmp"), [Drawing.Imaging.ImageFormat]::Bmp)
        } finally {
            $accent.Dispose()
            $graphics.Dispose()
            $bitmap.Dispose()
        }
    }
} finally {
    $logo.Dispose()
}

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

dotnet publish "$source/CPUSTATS/CPUSTATS.csproj" -c Release -r win-x64 --self-contained true "-p:Version=$Version" -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -o $publish
if ($LASTEXITCODE -ne 0) { throw 'Could not publish CPUSTATS.' }
Copy-Item -LiteralPath (Join-Path $publish 'CPUSTATS.exe') -Destination (Join-Path $assets 'CPUSTATS.SensorService.exe')

$msi = Join-Path $assets 'CPUSTATS.msi'
& $wixExe build -arch x64 -ext WixToolset.UI.wixext -ext WixToolset.Util.wixext -d "Version=$Version" -d "SourceRoot=$source" -d "PublishDir=$publish" -d "ArtDir=$assets" -o $msi "$PSScriptRoot/CPUSTATS.wxs"
if ($LASTEXITCODE -ne 0) { throw 'Could not build the Windows Installer package.' }

$versionSource = Join-Path $assets 'Version.cs'
Set-Content -LiteralPath $versionSource -Encoding ascii -Value "[assembly: System.Reflection.AssemblyFileVersion(`"$Version.0`")]"
$setup = Join-Path $outputDir "CPUSTATS-v$Version-windows-x64-setup.exe"
$manifest = Join-Path $source 'CPUSTATS\Windows\elevated.manifest'
$icon = Join-Path $brand 'cpustats.ico'
& $csc /nologo /target:winexe /platform:x64 /r:System.Windows.Forms.dll "/win32manifest:$manifest" "/win32icon:$icon" "/resource:$msi,CPUSTATS.msi" "/out:$setup" (Join-Path $PSScriptRoot 'Setup.cs') $versionSource
if ($LASTEXITCODE -ne 0) { throw 'Could not compile CPUSTATS Setup.' }
Get-Item -LiteralPath $setup
