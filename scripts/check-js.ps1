# Verifica sintaxe de todos os .js em wwwroot via `node --check`.
# Uso: .\scripts\check-js.ps1
param()

$wwwroot = Resolve-Path (Join-Path $PSScriptRoot "..\src\LegalManager.API\wwwroot")
$jsFiles = Get-ChildItem -Path $wwwroot -Filter "*.js" -Recurse
$errorCount = 0

foreach ($file in $jsFiles) {
    $result = node --check $file.FullName 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Sintaxe inválida: $($file.Name)" -ForegroundColor Red
        Write-Host ($result -join "`n") -ForegroundColor Red
        $errorCount++
    }
}

if ($errorCount -gt 0) {
    Write-Host "`n$errorCount arquivo(s) com erro de sintaxe JS." -ForegroundColor Red
    exit 1
}

Write-Host "Todos os $($jsFiles.Count) arquivo(s) JS sao validos." -ForegroundColor Green
