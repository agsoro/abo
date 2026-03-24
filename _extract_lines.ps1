$lines = Get-Content "Abo.Pm/wwwroot/llm-traffic/index.html"
$start = 690
$end = 715
Write-Host "=== Lines $start-$end ==="
for ($i = $start-1; $i -lt $end; $i++) {
    Write-Host "$($i+1): $($lines[$i])"
}

Write-Host "`n=== Lines 1253-1365 ==="
$start2 = 1253
$end2 = 1365
for ($i = $start2-1; $i -lt $end2; $i++) {
    Write-Host "$($i+1): $($lines[$i])"
}

Write-Host "`n=== Lines 1200-1252 ==="
$start3 = 1200
$end3 = 1252
for ($i = $start3-1; $i -lt $end3; $i++) {
    Write-Host "$($i+1): $($lines[$i])"
}
