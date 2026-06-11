$wwwroot = "$PSScriptRoot\..\src\LegalManager.API\wwwroot"

$pixelBlock = @'
  <!-- Meta Pixel -- only loads in non-local environments -->
  <script>
    if (location.hostname !== 'localhost' && location.hostname !== '127.0.0.1') {
      !function(f,b,e,v,n,t,s)
      {if(f.fbq)return;n=f.fbq=function(){n.callMethod?
      n.callMethod.apply(n,arguments):n.queue.push(arguments)};
      if(!f._fbq)f._fbq=n;n.push=n;n.loaded=!0;n.version='2.0';
      n.queue=[];t=b.createElement(e);t.async=!0;
      t.src=v;s=b.getElementsByTagName(e)[0];
      s.parentNode.insertBefore(t,s)}(window, document,'script',
      'https://connect.facebook.net/en_US/fbevents.js');
      fbq('init', '1631412744787892');
      fbq('track', 'PageView');
    }
  </script>
  <noscript><img height="1" width="1" style="display:none"
    src="https://www.facebook.com/tr?id=1631412744787892&ev=PageView&noscript=1"
  /></noscript>
'@

# Normalize to CRLF so it matches Windows HTML files
$pixelBlock = $pixelBlock.Replace("`r`n", "`n").Replace("`n", "`r`n")

$anchor = '  <!-- Microsoft Clarity'

$utf8 = [System.Text.UTF8Encoding]::new($false)

$files = Get-ChildItem -Path $wwwroot -Filter '*.html' -Recurse |
    Where-Object { (Get-Content $_.FullName -Raw -Encoding UTF8) -match 'G-Z40PJ2FYE2' }

$count = 0
foreach ($file in $files) {
    $content = [System.IO.File]::ReadAllText($file.FullName, $utf8)
    if ($content -notmatch 'fbq\(') {
        $newContent = $content.Replace($anchor, "$pixelBlock$anchor")
        [System.IO.File]::WriteAllText($file.FullName, $newContent, $utf8)
        Write-Host "Updated: $($file.FullName)"
        $count++
    } else {
        Write-Host "Skipped (already has pixel): $($file.FullName)"
    }
}

Write-Host "`nDone. Updated $count files."
