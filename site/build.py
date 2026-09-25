"""
Build the deployable site.

index.html is the editable source. It has no <!doctype>/<html>/<head> because
that is the format the Claude artifact preview expects. This script wraps it in
a complete HTML document with the meta tags a real deployment needs, and writes
the result to dist/ alongside the images.

Edit index.html, then:

    python build.py

and deploy the dist/ folder.
"""

import os
import re
import shutil

# ----------------------------------------------------------------- settings
# Change SITE_URL once you have a real domain. Open Graph needs absolute URLs,
# so link previews stay blank until this is correct.
SITE_URL = "https://amir-pce.vercel.app"

AUTHOR = "Amirhossein"
DESCRIPTION = (
    "Backend developer working in ASP.NET Core, C# and SQL Server. "
    "APIs, data models and query performance for teams in Europe and the Gulf. "
    "Available for remote contract work."
)

HERE = os.path.dirname(os.path.abspath(__file__))
DIST = os.path.join(HERE, "dist")
ASSETS = ["og.png", "icon.png", "favicon.ico"]

# Already complete documents — copied through untouched rather than wrapped.
STANDALONE = ["admin.html"]

# ----------------------------------------------------------------- read source
with open(os.path.join(HERE, "index.html"), encoding="utf-8") as fh:
    source = fh.read()

title_match = re.search(r"<title>(.*?)</title>", source, re.S)
title = title_match.group(1).strip() if title_match else AUTHOR

# Everything except the <title>; it is re-emitted inside the real <head>.
body = source.replace(title_match.group(0), "", 1).lstrip() if title_match else source

site = SITE_URL.rstrip("/")

document = f"""<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">

<title>{title} — Backend Developer</title>
<meta name="description" content="{DESCRIPTION}">
<meta name="author" content="{AUTHOR}">
<link rel="canonical" href="{site}/">

<meta name="theme-color" content="#101010">
<meta name="color-scheme" content="dark">

<link rel="icon" href="/favicon.ico" sizes="any">
<link rel="icon" href="/icon.png" type="image/png">
<link rel="apple-touch-icon" href="/icon.png">

<meta property="og:type" content="website">
<meta property="og:site_name" content="{AUTHOR}">
<meta property="og:title" content="{AUTHOR} — Backend Developer">
<meta property="og:description" content="{DESCRIPTION}">
<meta property="og:url" content="{site}/">
<meta property="og:image" content="{site}/og.png">
<meta property="og:image:width" content="1200">
<meta property="og:image:height" content="630">
<meta property="og:image:alt" content="Backend that holds under load. ASP.NET Core, C#, SQL Server.">

<meta name="twitter:card" content="summary_large_image">
<meta name="twitter:title" content="{AUTHOR} — Backend Developer">
<meta name="twitter:description" content="{DESCRIPTION}">
<meta name="twitter:image" content="{site}/og.png">

<style>
  html {{ color-scheme: dark; }}
  :root {{
    padding-top: env(safe-area-inset-top, 0px);
    padding-bottom: env(safe-area-inset-bottom, 0px);
  }}
  body {{ margin: 0; }}
  img {{ max-width: 100%; }}
  [hidden] {{ display: none !important; }}
</style>

<script type="application/ld+json">
{{
  "@context": "https://schema.org",
  "@type": "Person",
  "name": "{AUTHOR}",
  "url": "{site}/",
  "jobTitle": "Backend Developer",
  "description": "{DESCRIPTION}",
  "knowsAbout": ["ASP.NET Core", "C#", "SQL Server", "Entity Framework Core", "T-SQL"]
}}
</script>
</head>
<body>
{body}
</body>
</html>
"""

# ----------------------------------------------------------------- write dist
os.makedirs(DIST, exist_ok=True)

with open(os.path.join(DIST, "index.html"), "w", encoding="utf-8") as fh:
    fh.write(document)

for name in ASSETS + STANDALONE:
    src = os.path.join(HERE, name)
    if os.path.exists(src):
        shutil.copy2(src, os.path.join(DIST, name))
    else:
        print(f"  missing file: {name}")

with open(os.path.join(DIST, "robots.txt"), "w", encoding="utf-8") as fh:
    fh.write(
        "User-agent: *\n"
        "Allow: /\n"
        "Disallow: /admin.html\n"
        f"\nSitemap: {site}/sitemap.xml\n"
    )

with open(os.path.join(DIST, "sitemap.xml"), "w", encoding="utf-8") as fh:
    fh.write(
        '<?xml version="1.0" encoding="UTF-8"?>\n'
        '<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">\n'
        f"  <url><loc>{site}/</loc><priority>1.0</priority></url>\n"
        "</urlset>\n"
    )

placeholders = source.count("REPLACE")
print(f"built dist/  ({len(document):,} bytes)")
if placeholders:
    print(f"  {placeholders} REPLACE placeholders still in the page — fill them before deploying")
print(f"  deploy with:  cd dist && npx vercel --prod")
