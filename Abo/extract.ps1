$jsonPath = "c:\src\agsoro\abo\Abo\Docs\xpectolive-swagger.json"
$targetPath = "c:\src\agsoro\abo\Abo\Docs\wiki_schemas.json"

$content = Get-Content -Path $jsonPath -Raw
$json = ConvertFrom-Json $content

$schemas = $json.components.schemas

$wikiSchemas = @{}

foreach ($prop in $schemas.psobject.properties) {
    if ($prop.Name -match "(Space|Wiki|Page|ContentUpdate)") {
        $wikiSchemas[$prop.Name] = $prop.Value
    }
}

$wikiSchemas | ConvertTo-Json -Depth 10 | Set-Content -Path $targetPath
Write-Host "Extracted to $targetPath"
