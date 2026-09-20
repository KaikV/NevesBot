# Finds candidate offsets (HP / battle state) by dumping raw memory around the
# position anchor that the KBot.Native core already reads successfully.
#
# Usage (Windows, game open + native core attached to the client):
#   # 1) note your HP exactly as shown in-game (e.g. 342, or a % like 70)
#   # 2) dump +/- around the position field and search for that number:
#   pwsh .\tools\Find-Offset.ps1 -TargetValue 342 -Before 512 -After 512
#   # 3) repeat with a second distinct HP value; the offset present in BOTH
#   #    dumps is the real one. Report it as: base + <offset>
#
# The anchor is the pointer/field the confirmed X/Y/Z read uses, so nearby
# struct members (HP, MP, battle flags) are the usual candidates.

[CmdletBinding()]
param(
    [string]$PipeName = "KBot.NativePipe.CharacterV3",
    # value you can read in the game UI right now (HP, or a % if that's all you have)
    [int]$TargetValue = 0,
    # how many bytes to walk before/after the position anchor
    [int]$Before = 512,
    [int]$After = 1024,
    [int]$OffsetFromAnchor = 0,
    # scan every N bytes for a little-endian int32 equal to TargetValue
    [int]$Step = 1,
    [string]$OutputPath = ""
)

$ErrorActionPreference = "Stop"

function Read-PipeLine([string]$Command) {
    $c = New-Object System.IO.Pipes.NamedPipeClientStream(".", $PipeName, [System.IO.Pipes.PipeDirection]::InOut)
    try {
        $c.Connect(1500)
        $sw = New-Object System.IO.StreamWriter($c, [System.Text.Encoding]::UTF8); $sw.NewLine = "`n"
        $sr = New-Object System.IO.StreamReader($c, [System.Text.Encoding]::UTF8)
        $sw.Write($Command); $sw.Flush()
        $line = $sr.ReadLine()
        $sw.Dispose(); $sr.Dispose()
        return $line
    } finally { $c.Dispose() }
}

$jsonStart = Read-PipeLine "DUMP_START $OffsetFromAnchor $($Before + $After)" | ConvertFrom-Json
if (-not $jsonStart.ok) { Write-Error "Dump start failed: $($jsonStart.message)"; return }
Write-Host "Anchor: $($jsonStart.anchor)  (range $($Before) before / $($After) after, offset $($OffsetFromAnchor))" -ForegroundColor Cyan

$totalBytes = $Before + $After
$remaining = $totalBytes
$hexParts = [System.Collections.Generic.List[string]]::new()
while ($remaining -gt 0) {
    $chunk = Read-PipeLine "DUMP_CHUNK 4096" | ConvertFrom-Json
    if (-not $chunk.got) { break }
    if ($chunk.hex) { $hexParts.Add($chunk.hex) }
    $remaining = [int]$chunk.remaining
}

Read-PipeLine "DUMP_RESET" | Out-Null

$hex = ($hexParts -join " ") -replace '\s', ''
$bytes = [System.Collections.Generic.List[byte]]::new()
for ($i = 0; $i -lt $hex.Length - $i -lt 1; $i += 2) {
    $bytes.Add([byte][Convert]::ToInt32($hex.Substring($i, 2), 16))
}
Write-Host "Read $($bytes.Count) bytes." -ForegroundColor Cyan

# The dump STARTs at (anchor + OffsetFromAnchor - Before). A byte at index i is
# at address anchor + OffsetFromAnchor - Before + i. We only know the *anchor*
# for sure, so report offsets relative to the anchor (what you'd plug into code).
$baseDelta = $OffsetFromAnchor - $Before

$candidates = [System.Collections.Generic.List[object]]::new()
for ($i = 0; $i -le $bytes.Count - 4; $i += $Step) {
    $value = [BitConverter]::ToInt32($bytes.ToArray(), $i)
    if ($TargetValue -eq 0) {
        # no target given: just show a hex/decimal table so you can eyeball it
    } elseif ($value -eq $TargetValue) {
        $candidates.Add([pscustomobject]@{ Offset = ("{0:#,0}" -f ($baseDelta + $i)); Value = $value; HexOffset = ("0x{0:X}" -f ($baseDelta + $i)) })
    }
}

if ($TargetValue -ne 0) {
    Write-Host "`nCandidates where int32 == $TargetValue (offset from anchor):" -ForegroundColor Yellow
    $candidates | Format-Table -AutoSize
    if ($candidates.Count -eq 0) {
        Write-Host "No exact match. Try a different value (try HP%, or both your two HP samples), or widen -Before/-After." -ForegroundColor DarkYellow
    } else {
        Write-Host "Confirm by re-running with a SECOND different HP value and keeping the offset that survives both." -ForegroundColor Green
    }
}

if ($OutputPath) {
    $dump = [ordered]@{ anchor = $jsonStart.anchor; baseDelta = $baseDelta; step = $Step; target = $TargetValue; bytes = $bytes.ToArray() }
    $dump | ConvertTo-Json -Depth 4 | Set-Content $OutputPath
    Write-Host "Raw dump written to $OutputPath" -ForegroundColor DarkGreen
}
