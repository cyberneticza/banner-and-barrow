# Renders voice lines with the Windows speech synthesizer (placeholder voices for development).
# Input: a JSON file of [{ "file": "...wav", "voice": "Microsoft George", "text": "...", "rate": 2, "pitch": "low" }].
# Called by build_audio.py; run with Windows PowerShell or pwsh on Windows.
param([Parameter(Mandatory)][string]$Manifest)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Speech
$lines = Get-Content -Raw $Manifest | ConvertFrom-Json
$format = New-Object System.Speech.AudioFormat.SpeechAudioFormatInfo(22050, [System.Speech.AudioFormat.AudioBitsPerSample]::Sixteen, [System.Speech.AudioFormat.AudioChannel]::Mono)

foreach ($line in $lines) {
    $synth = New-Object System.Speech.Synthesis.SpeechSynthesizer
    try {
        $synth.SelectVoice($line.voice)
        $synth.Rate = [int]$line.rate
        $synth.SetOutputToWaveFile($line.file, $format)
        $escaped = [System.Security.SecurityElement]::Escape($line.text)
        $ssml = "<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='en-GB'>" +
                "<prosody pitch='$($line.pitch)' volume='x-loud'><emphasis level='strong'>$escaped</emphasis></prosody></speak>"
        $synth.SpeakSsml($ssml)
    }
    finally {
        $synth.Dispose()
    }
}
Write-Host "Spoke $($lines.Count) lines"
