$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$packageRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$pluginPath = Join-Path $packageRoot 'release\FlightCompanion.XPilot.dll'
if (-not (Test-Path -LiteralPath $pluginPath)) { throw 'Run Build.cmd and Build-Setup.cmd first.' }
$packageVersion = [Reflection.AssemblyName]::GetAssemblyName($pluginPath).Version.ToString(3)
$distPath = Join-Path $packageRoot 'dist'
[IO.Directory]::CreateDirectory($distPath) | Out-Null
$zipName = 'vatstim-companion-xpilot-v' + $packageVersion + '.zip'
$zipPath = Join-Path $distPath $zipName
$temporaryZip = Join-Path $distPath ($zipName + '.tmp-' + [Guid]::NewGuid().ToString('N'))

# Explicit mappings only: no runtime credentials, vendor SDK DLLs or recursive build folders.
$entries = [ordered]@{
    'README.md' = 'README.md'
    'VERIFICATION.md' = 'VERIFICATION.md'
    'THIRD-PARTY-NOTICES.md' = 'THIRD-PARTY-NOTICES.md'
    'sdk/LICENSE' = 'xPilot-SDK-LICENSE.txt'
    'release/FlightCompanion.XPilot.dll' = 'FlightCompanion.XPilot/FlightCompanion.XPilot.dll'
    'release/FlightCompanion.XPilot.deps.json' = 'FlightCompanion.XPilot/FlightCompanion.XPilot.deps.json'
    'release/FlightCompanion.XPilot.runtimeconfig.json' = 'FlightCompanion.XPilot/FlightCompanion.XPilot.runtimeconfig.json'
    'release/FlightCompanion.XPilot.example.json' = 'FlightCompanion.XPilot/FlightCompanion.XPilot.example.json'
    'release/setup/FlightCompanion.XPilot.Setup.dll' = 'setup/FlightCompanion.XPilot.Setup.dll'
    'release/setup/FlightCompanion.XPilot.Setup.deps.json' = 'setup/FlightCompanion.XPilot.Setup.deps.json'
    'release/setup/FlightCompanion.XPilot.Setup.runtimeconfig.json' = 'setup/FlightCompanion.XPilot.Setup.runtimeconfig.json'
}
foreach ($entry in $entries.Keys) {
    if (-not (Test-Path -LiteralPath (Join-Path $packageRoot $entry))) { throw ('Missing release file: ' + $entry) }
}
$example = Get-Content -LiteralPath (Join-Path $packageRoot 'release\FlightCompanion.XPilot.example.json') -Raw | ConvertFrom-Json
if ($example.Enabled -or $example.BotToken -or $example.AllowedChatId -or $example.AllowedUserId) {
    throw 'Release example must be disabled and contain no token or personal IDs.'
}
try {
    $archive = [IO.Compression.ZipFile]::Open($temporaryZip, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($entry in $entries.Keys) {
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, (Join-Path $packageRoot $entry), $entries[$entry], [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    } finally { $archive.Dispose() }
    if ([IO.File]::Exists($zipPath)) { [IO.File]::Replace($temporaryZip, $zipPath, [NullString]::Value) }
    else { [IO.File]::Move($temporaryZip, $zipPath) }
} finally { if ([IO.File]::Exists($temporaryZip)) { [IO.File]::Delete($temporaryZip) } }
$checksum = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText($zipPath + '.sha256', $checksum + '  ' + $zipName + [Environment]::NewLine, [Text.Encoding]::ASCII)
Write-Output ('Packaged ' + $zipName + ' with ' + $entries.Count + ' allowlisted entries.')
