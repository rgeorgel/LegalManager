# Hook PostToolUse: verifica sintaxe do arquivo .js editado.
# Lê JSON do stdin para obter o caminho do arquivo.
param()

try {
    $raw = [Console]::In.ReadToEnd()
    $data = $raw | ConvertFrom-Json -ErrorAction Stop
    $filePath = $data.tool_input.file_path ?? ''
} catch {
    exit 0
}

if (-not $filePath -or $filePath -notmatch '\.js$') { exit 0 }

$result = node --check "$filePath" 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "Erro de sintaxe JS em: $filePath" -ForegroundColor Red
    Write-Host ($result -join "`n")
    exit 2
}
