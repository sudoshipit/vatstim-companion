$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

$packageRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$pluginPath = Join-Path $packageRoot 'release\FlightCompanion.Plugin.dll'
if (-not (Test-Path -LiteralPath $pluginPath)) { throw 'Run Build.cmd and Build-Setup.cmd first.' }
$packageVersion = [Reflection.AssemblyName]::GetAssemblyName($pluginPath).Version.ToString(3)
$distPath = Join-Path $packageRoot 'dist'
[IO.Directory]::CreateDirectory($distPath) | Out-Null
$zipName = 'vatstim-companion-vpilot-v' + $packageVersion + '.zip'
$zipPath = Join-Path $distPath $zipName
$temporaryZip = Join-Path $distPath ($zipName + '.tmp-' + [Guid]::NewGuid().ToString('N'))

# Explicit entries only: never recursively zip runtime configuration or scratch files.
$entries = @(
    'README.md',
    'VERIFICATION.md',
    'CHANGELOG.md',
    'release/FlightCompanion.Plugin.dll',
    'release/FlightCompanion.Plugin.example.json',
    'setup/FlightCompanion.Setup.exe',
    'setup/FlightCompanion.Setup.exe.config'
)
foreach ($entry in $entries) {
    if (-not (Test-Path -LiteralPath (Join-Path $packageRoot $entry))) { throw ('Missing release file: ' + $entry) }
}
$example = Get-Content -LiteralPath (Join-Path $packageRoot 'release\FlightCompanion.Plugin.example.json') -Raw | ConvertFrom-Json
if ($example.Enabled -or $example.BotToken -or $example.ProtectedBotToken -or $example.AllowedChatId -or $example.AllowedUserId) {
    throw 'Release example must be disabled and contain no token or personal IDs.'
}
try {
    $archive = [IO.Compression.ZipFile]::Open($temporaryZip, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($entry in $entries) {
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, (Join-Path $packageRoot $entry), $entry, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    } finally { $archive.Dispose() }
    if ([IO.File]::Exists($zipPath)) { [IO.File]::Replace($temporaryZip, $zipPath, [NullString]::Value) }
    else { [IO.File]::Move($temporaryZip, $zipPath) }
} finally {
    if ([IO.File]::Exists($temporaryZip)) { [IO.File]::Delete($temporaryZip) }
}
$checksum = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText($zipPath + '.sha256', $checksum + '  ' + $zipName + [Environment]::NewLine, [Text.Encoding]::ASCII)
Write-Output ('Packaged ' + $zipName + ' with ' + $entries.Count + ' allowlisted entries.')
$standalonePath = Join-Path $distPath 'FlightCompanion.Setup.exe'
Copy-Item -LiteralPath (Join-Path $packageRoot 'setup\FlightCompanion.Setup.exe') -Destination $standalonePath -Force
$setupChecksum = (Get-FileHash -LiteralPath $standalonePath -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText($standalonePath + '.sha256', $setupChecksum + '  FlightCompanion.Setup.exe' + [Environment]::NewLine, [Text.Encoding]::ASCII)
Write-Output 'Packaged standalone FlightCompanion.Setup.exe and checksum.'
