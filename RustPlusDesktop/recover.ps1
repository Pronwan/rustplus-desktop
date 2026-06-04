$transcriptPath = 'C:\Users\carst\.gemini\antigravity-ide\brain\8ad6e5b3-63a2-455c-82d7-4c431e23fb21\.system_generated\logs\transcript.jsonl'
$linesDict = @{}

Get-Content $transcriptPath -Encoding UTF8 | ForEach-Object {
    try {
        $data = ConvertFrom-Json $_
        if ($data.type -eq 'TOOL_RESPONSE' -and $null -ne $data.tool_responses) {
            foreach ($resp in $data.tool_responses) {
                if ($resp.output -match 'SupabaseAuthManager\.cs' -and $resp.output -match 'The following code has been modified to include a line number') {
                    $regex = '(?m)^(\d+):\s(.*)$'
                    $matches = [regex]::Matches($resp.output, $regex)
                    foreach ($m in $matches) {
                        $lineNum = [int]$m.Groups[1].Value
                        $content = $m.Groups[2].Value
                        $linesDict[$lineNum] = $content
                    }
                }
            }
        }
    } catch {}
}

if ($linesDict.Count -gt 0) {
    $maxLine = ($linesDict.Keys | Measure-Object -Maximum).Maximum
    $outLines = @()
    for ($i = 1; $i -le $maxLine; $i++) {
        if ($linesDict.Contains($i)) {
            $outLines += $linesDict[$i]
        } else {
            $outLines += ''
        }
    }
    $outPath = 'C:\Users\carst\source\repos\RustPlusDesktop\RustPlusDesktop\RustPlusDesktop\Services\Auth\SupabaseAuthManager.cs'
    [IO.File]::WriteAllLines($outPath, $outLines, [System.Text.Encoding]::UTF8)
    Write-Output "Recovered $maxLine lines."
} else {
    Write-Output 'No lines found.'
}
