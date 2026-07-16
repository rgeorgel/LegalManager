$wwwroot = "C:\code\personal\LegalManager\src\LegalManager.API\wwwroot"
$files = @('pages\honorarios-contratos.html','pages\honorarios-contrato-detalhe.html','pages\honorarios-contrato-novo.html','pages\honorarios-config.html')
$tmp = "$env:TEMP\inline_check"
New-Item -ItemType Directory -Force -Path $tmp | Out-Null
$err = 0
foreach ($rel in $files) {
  $path = Join-Path $wwwroot $rel
  $content = Get-Content $path -Raw
  $pattern = '(?s)<script type="module">(.*?)</script>'
  $matches_ = [regex]::Matches($content, $pattern)
  if ($matches_.Count -gt 0) {
    $script = $matches_[0].Groups[1].Value
    $tmpFile = Join-Path $tmp ([System.IO.Path]::GetFileNameWithoutExtension($rel) + ".mjs")
    Set-Content -Path $tmpFile -Value $script -Encoding UTF8
    Write-Host "Validando $rel..." -NoNewline
    $out = node --check $tmpFile 2>&1
    if ($LASTEXITCODE -eq 0) { Write-Host " OK" -ForegroundColor Green }
    else { Write-Host " ERRO" -ForegroundColor Red; $err++; Write-Host $out }
  }
}
if ($err -gt 0) { exit 1 }
