# Executa a suite de testes frontend.
# Uso: .\scripts\test-frontend.ps1 [-Suite all|smoke|flows|lint|syntax]
param(
    [ValidateSet('all', 'smoke', 'flows', 'lint', 'syntax')]
    [string]$Suite = 'all'
)

$ErrorActionPreference = 'Stop'
$frontendDir = Resolve-Path (Join-Path $PSScriptRoot "..\tests\frontend")

# Instala dependências na primeira execução
if (-not (Test-Path (Join-Path $frontendDir "node_modules"))) {
    Write-Host "Instalando dependências de teste..." -ForegroundColor Yellow
    Push-Location $frontendDir
    npm install
    npx playwright install chromium --with-deps
    Pop-Location
}

function Invoke-Syntax {
    Write-Host "`n=== Verificando sintaxe JS ===" -ForegroundColor Cyan
    node (Join-Path $frontendDir "scripts" "check-syntax.mjs")
}

function Invoke-Lint {
    Write-Host "`n=== ESLint ===" -ForegroundColor Cyan
    Push-Location $frontendDir
    try { npx eslint --config .eslintrc.json ..\..\src\LegalManager.API\wwwroot\js --ext .js }
    finally { Pop-Location }
}

function Invoke-Playwright([string]$testDir = '') {
    Write-Host "`n=== Playwright: $testDir ===" -ForegroundColor Cyan
    Push-Location $frontendDir
    try {
        if ($testDir) {
            npx playwright test $testDir
        } else {
            npx playwright test
        }
    }
    finally { Pop-Location }
}

switch ($Suite) {
    'syntax'  { Invoke-Syntax }
    'lint'    { Invoke-Lint }
    'smoke'   { Invoke-Playwright 'tests/smoke' }
    'flows'   { Invoke-Playwright 'tests/flows' }
    default {
        Invoke-Syntax
        Invoke-Lint
        Invoke-Playwright 'tests/smoke'
        Invoke-Playwright 'tests/flows'
    }
}
