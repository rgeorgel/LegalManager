import os

wwwroot = os.path.join(os.path.dirname(__file__), '..', 'src', 'LegalManager.API', 'wwwroot')
wwwroot = os.path.normpath(wwwroot)

pixel_block = (
    "  <!-- Meta Pixel -- only loads in non-local environments -->\n"
    "  <script>\n"
    "    if (location.hostname !== 'localhost' && location.hostname !== '127.0.0.1') {\n"
    "      !function(f,b,e,v,n,t,s)\n"
    "      {if(f.fbq)return;n=f.fbq=function(){n.callMethod?\n"
    "      n.callMethod.apply(n,arguments):n.queue.push(arguments)};\n"
    "      if(!f._fbq)f._fbq=n;n.push=n;n.loaded=!0;n.version='2.0';\n"
    "      n.queue=[];t=b.createElement(e);t.async=!0;\n"
    "      t.src=v;s=b.getElementsByTagName(e)[0];\n"
    "      s.parentNode.insertBefore(t,s)}(window, document,'script',\n"
    "      'https://connect.facebook.net/en_US/fbevents.js');\n"
    "      fbq('init', '1631412744787892');\n"
    "      fbq('track', 'PageView');\n"
    "    }\n"
    "  </script>\n"
    "  <noscript><img height=\"1\" width=\"1\" style=\"display:none\"\n"
    "    src=\"https://www.facebook.com/tr?id=1631412744787892&ev=PageView&noscript=1\"\n"
    "  /></noscript>\n"
)

anchor = "  <!-- Microsoft Clarity"

count = 0
for root, dirs, files in os.walk(wwwroot):
    for fname in files:
        if not fname.endswith('.html'):
            continue
        path = os.path.join(root, fname)
        with open(path, 'r', encoding='utf-8') as f:
            content = f.read()
        if 'G-Z40PJ2FYE2' not in content:
            continue
        if 'fbq(' in content:
            print(f"Skipped (already has pixel): {path}")
            continue
        eol = '\r\n' if '\r\n' in content else '\n'
        block = pixel_block.replace('\n', eol)
        new_content = content.replace(anchor, block + anchor)
        with open(path, 'w', encoding='utf-8', newline='') as f:
            f.write(new_content)
        print(f"Updated: {path}")
        count += 1

print(f"\nDone. Updated {count} files.")
