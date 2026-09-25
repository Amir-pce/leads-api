<#
    End-to-end smoke test for the Leads API.

    Runs against a live instance and asserts the status code of every case, including
    the ones that are supposed to fail. Wipes the Leads table first, so point it at a
    development database only.

    Usage:
        pwsh tests/api-smoke.ps1
        pwsh tests/api-smoke.ps1 -BaseUrl http://localhost:5223

    The admin key is read from user-secrets, so nothing secret lives in this file.
#>

[CmdletBinding()]
param(
    [string]$BaseUrl  = 'http://localhost:5223',
    [string]$Database = 'AmirhosseinSite',
    [string]$Server   = 'localhost',
    [string]$ApiProject
)

$ErrorActionPreference = 'Stop'

# ----------------------------------------------------------------- setup

# Resolved here rather than as a parameter default: Windows PowerShell 5.1 does not
# populate $PSScriptRoot while it is binding parameters.
if (-not $ApiProject) {
    $root = Split-Path -Parent $MyInvocation.MyCommand.Path
    $ApiProject = Join-Path $root '..\src\Api\Api.csproj'
}

$secrets = dotnet user-secrets list --project $ApiProject 2>$null
$adminKey = ($secrets | Select-String -Pattern '^Admin:Key\s*=\s*(.+)$').Matches.Groups[1].Value

if (-not $adminKey) {
    Write-Output "Admin:Key is not set. Run:"
    Write-Output "  dotnet user-secrets set `"Admin:Key`" `"<a long random string>`" --project $ApiProject"
    exit 1
}

Write-Output "Resetting $Database.dbo.Leads ..."
sqlcmd -S $Server -E -d $Database -h -1 -Q `
    "DELETE FROM dbo.Leads; DBCC CHECKIDENT('dbo.Leads', RESEED, 0) WITH NO_INFOMSGS;" | Out-Null

$results = @()

function Invoke-Case {
    param([string]$Name, [int]$Expect, [scriptblock]$Call)

    $body = $null
    try {
        $body = & $Call
        $code = 200
    }
    catch {
        $response = $_.Exception.Response
        if (-not $response) { throw }
        $code = [int]$response.StatusCode
        $body = (New-Object IO.StreamReader($response.GetResponseStream())).ReadToEnd()
    }

    if ($code -eq $Expect) { $verdict = 'PASS' } else { $verdict = 'FAIL' }

    $script:results += [pscustomobject]@{
        Result   = $verdict
        Case     = $Name
        Expected = $Expect
        Actual   = $code
    }

    return $body
}

$auth = @{ 'X-Admin-Key' = $adminKey }
function Json($o) { $o | ConvertTo-Json -Depth 5 }

# ----------------------------------------------------------------- public API

Invoke-Case 'submit a valid enquiry' 200 {
    Invoke-RestMethod "$BaseUrl/api/leads" -Method Post -ContentType 'application/json' -Body (Json @{
        name = 'Lukas Meyer'; email = '  LUKAS@northbyte.de '; company = 'Northbyte GmbH'
        message = 'We have a .NET Framework 4.8 app that needs moving to .NET 8.'
        source = 'hero-cta'
    })
} | Out-Null

Invoke-Case 'reject bad email and short fields' 400 {
    Invoke-RestMethod "$BaseUrl/api/leads" -Method Post -ContentType 'application/json' -Body (Json @{
        name = 'X'; email = 'nope'; message = 'hi'
    })
} | Out-Null

Invoke-Case 'accept-and-drop a filled honeypot' 200 {
    Invoke-RestMethod "$BaseUrl/api/leads" -Method Post -ContentType 'application/json' -Body (Json @{
        name = 'Bot'; email = 'b@spam.example'; message = 'Buy cheap backlinks now'
        website = 'http://spam.example'
    })
} | Out-Null

Invoke-Case 'block a repeat inside the duplicate window' 429 {
    Invoke-RestMethod "$BaseUrl/api/leads" -Method Post -ContentType 'application/json' -Body (Json @{
        name = 'Lukas Meyer'; email = 'lukas@northbyte.de'; message = 'Sending this again immediately.'
    })
} | Out-Null

# ----------------------------------------------------------------- admin auth

Invoke-Case 'refuse admin list with no key'    401 { Invoke-RestMethod "$BaseUrl/api/admin/leads" } | Out-Null
Invoke-Case 'refuse admin list with wrong key' 401 { Invoke-RestMethod "$BaseUrl/api/admin/leads" -Headers @{ 'X-Admin-Key' = 'wrong' } } | Out-Null

# ----------------------------------------------------------------- admin reads

$list = Invoke-Case 'list with no query string' 200 { Invoke-RestMethod "$BaseUrl/api/admin/leads" -Headers $auth }
Invoke-Case 'reject a page size above the cap'  400 { Invoke-RestMethod "$BaseUrl/api/admin/leads?limit=999" -Headers $auth } | Out-Null
Invoke-Case 'reject an unparseable status'      400 { Invoke-RestMethod "$BaseUrl/api/admin/leads?status=Nonsense" -Headers $auth } | Out-Null
Invoke-Case '404 on a lead that does not exist' 404 { Invoke-RestMethod "$BaseUrl/api/admin/leads/999" -Headers $auth } | Out-Null

# ----------------------------------------------------------------- admin writes

$updated = Invoke-Case 'patch status and note' 200 {
    Invoke-RestMethod "$BaseUrl/api/admin/leads/1" -Method Patch -ContentType 'application/json' -Headers $auth -Body (Json @{
        status = 'Replied'; note = 'Quoted 3 days for the assessment.'
    })
}

$replied = Invoke-Case 'filter by status=Replied' 200 {
    Invoke-RestMethod "$BaseUrl/api/admin/leads?status=Replied" -Headers $auth
}

# ----------------------------------------------------------------- report

$results | Format-Table Result, Case, Expected, Actual -AutoSize | Out-String -Width 100 | Write-Output

Write-Output 'Data assertions'
Write-Output "  email trimmed and lowercased : $($list.items[0].email)"
Write-Output "  status serialised as a name  : $($list.items[0].status)"
Write-Output "  after patch                  : $($updated.status) / $($updated.note)"
Write-Output "  rows stored (honeypot + duplicate must be absent, so 1) : $($list.total)"
Write-Output "  Replied after patch          : $($replied.total)"

$failed = @($results | Where-Object Result -eq 'FAIL').Count
$passed = $results.Count - $failed

Write-Output ''
Write-Output "$passed passed, $failed failed"

# if/else rather than a ternary: this machine has Windows PowerShell 5.1, where `? :` is a parse error.
if ($failed -gt 0) { exit 1 } else { exit 0 }
