$ErrorActionPreference = 'Stop'

$release = 'https://github.com/namazso/PawnIO.Modules/releases/download/0.2.11/release_0_2_11.zip'
$archiveHash = '43608CB89BC84247FEF1368A139013F7D043E17DB6D6C8DFC9B46BF0905A81F4'
$moduleHash = '3F82B832D99B4AAC37D2A20FDB7C9BAA2A3BC0488612C9019C9484EB0E8A6EAE'
$archivePath = [IO.Path]::GetTempFileName()
$modulePath = [IO.Path]::GetTempFileName()

try {
    Invoke-WebRequest -UseBasicParsing -Uri $release -OutFile $archivePath
    if ((Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash -ne $archiveHash) {
        throw 'PawnIO Modules archive checksum mismatch.'
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($archivePath)
    try {
        [IO.Compression.ZipFileExtensions]::ExtractToFile(
            $archive.GetEntry('IntelMCHBAR.bin'), $modulePath, $true)
    } finally {
        $archive.Dispose()
    }

    if ((Get-FileHash -LiteralPath $modulePath -Algorithm SHA256).Hash -ne $moduleHash) {
        throw 'IntelMCHBAR module checksum mismatch.'
    }
    Copy-Item -LiteralPath $modulePath -Destination (Join-Path $PSScriptRoot 'IntelMCHBAR.bin') -Force
} finally {
    Remove-Item -LiteralPath $archivePath, $modulePath -Force
}
