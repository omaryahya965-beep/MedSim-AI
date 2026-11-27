# Renders the approved offline lines with Windows built-in SAPI voices (no network, no third-party service).
# patient -> Microsoft David (slower, strained); instructor -> Microsoft Zira.
param([string]$Manifest = "AgentScripts/voice_manifest.tsv", [string]$OutRoot = "Assets/MedSimAI/Resources/Audio/Voice")
Add-Type -AssemblyName System.Speech
$fmt = New-Object System.Speech.AudioFormat.SpeechAudioFormatInfo(22050, [System.Speech.AudioFormat.AudioBitsPerSample]::Sixteen, [System.Speech.AudioFormat.AudioChannel]::Mono)
$count = 0
foreach ($line in [System.IO.File]::ReadAllLines((Resolve-Path $Manifest), [System.Text.Encoding]::UTF8)) {
    $parts = $line.Split("`t", 3); if ($parts.Length -lt 3) { continue }
    $persona = $parts[0]; $key = $parts[1]; $text = $parts[2]
    $dir = Join-Path $OutRoot $persona; New-Item -ItemType Directory -Force $dir | Out-Null
    $out = Join-Path $dir "$key.wav"
    $s = New-Object System.Speech.Synthesis.SpeechSynthesizer
    if ($persona -eq "patient") { $s.SelectVoice("Microsoft David Desktop"); $s.Rate = -2 } else { $s.SelectVoice("Microsoft Zira Desktop"); $s.Rate = 0 }
    $s.SetOutputToWaveFile((Join-Path (Get-Location) $out), $fmt)
    $s.Speak($text)
    $s.Dispose(); $count++
}
"rendered $count lines"
