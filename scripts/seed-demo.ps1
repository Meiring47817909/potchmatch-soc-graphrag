<#
.SYNOPSIS
  Seeds PotchMatch with demo users and drives realistic traffic through the API Gateway, so the
  SOC pipeline produces real events, alerts and (in Neo4j) incidents.

.DESCRIPTION
  Nothing is written to a database directly: every step is an ordinary API call, so events flow
  through the real pipeline (services -> SocService -> detection rules -> alerts -> GraphService
  sync -> Neo4j incidents).

  Phases:
    1. Users      - registers 12 players and coaches across the North-West clubs (or logs in if they
                    already exist) and tops up most wallets.
    2. Business   - coaches host coaching sessions, players host challenges, others accept them
                    (bookings, escrow holds and releases), plus an insufficient-funds attempt,
                    an attempt to accept your own booking and a wrong current password.
    3. Attacks    - scenarios that trigger every detection rule:
                    a. Brute force on two accounts           -> Multiple Failed Logins (HIGH) x2
                       (all from one IP, so also an IP-based Multiple Failed Logins alert)
                    b. A player probing admin endpoints       -> Unauthorised Endpoint Access (MEDIUM)
                       and then the knowledge graph / RAG     -> GRAPH_ACCESS (HIGH)
                       Same user, two rules                   -> ESCALATED incident in the graph
                    c. Anonymous graph probing                -> GRAPH_ACCESS (HIGH) for the source IP
                    d. Request flood (unless -SkipFlood)      -> POSSIBLE_DOS (HIGH)
                    e. payment-service outage (unless -SkipOutage, needs docker)
                                                              -> SERVICE_FAILURE (HIGH), auto-resolved
    4. Summary    - waits for detection and graph sync, then lists the alerts and incidents.

  Re-running is safe: existing users are logged in instead of registered. While an alert is still
  OPEN, new matching events are added to it instead of creating a duplicate, so resolve the alerts on
  the SOC page first if you want a fresh set.

  All demo users have the password  Demo@12345  and an @potchmatch.demo email address.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts\seed-demo.ps1
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts\seed-demo.ps1 -SkipOutage -SkipFlood
#>
param(
    [string]$BaseUrl = "http://127.0.0.1:5132",
    [string]$ComposeFile = (Join-Path $PSScriptRoot "..\backend\docker-compose.yml"),
    [string]$AdminEmail = "admin@potchmatch.local",
    [string]$AdminPassword = "Admin@12345",
    [switch]$SkipOutage,
    [switch]$SkipFlood,
    [switch]$NoWait
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Net.Http

$DemoPassword = "Demo@12345"
$client = New-Object System.Net.Http.HttpClient
$client.Timeout = [TimeSpan]::FromSeconds(30)

function Write-Step([string]$text) { Write-Host "`n== $text" -ForegroundColor Cyan }
function Write-Ok([string]$text) { Write-Host "   $text" -ForegroundColor Gray }

# One HTTP call through the gateway. Never throws on 4xx/5xx: returns @{ Status; Body }.
function Invoke-Api {
    param([string]$Method, [string]$Path, $Body = $null, [string]$Token = $null)
    $request = New-Object System.Net.Http.HttpRequestMessage([System.Net.Http.HttpMethod]::new($Method), "$BaseUrl$Path")
    if ($Token) { $request.Headers.Authorization = New-Object System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", $Token) }
    if ($null -ne $Body) {
        $json = $Body | ConvertTo-Json -Depth 5 -Compress
        $request.Content = New-Object System.Net.Http.StringContent($json, [System.Text.Encoding]::UTF8, "application/json")
    }
    try {
        $response = $client.SendAsync($request).GetAwaiter().GetResult()
        $text = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        $parsed = $null
        if ($text -and ($text.TrimStart().StartsWith("{") -or $text.TrimStart().StartsWith("["))) { $parsed = $text | ConvertFrom-Json }
        return @{ Status = [int]$response.StatusCode; Body = $parsed; Text = $text }
    }
    catch {
        return @{ Status = 0; Body = $null; Text = $_.Exception.Message }
    }
    finally { $request.Dispose() }
}

function Get-FutureDate([int]$days, [int]$hour) {
    (Get-Date).ToUniversalTime().Date.AddDays($days).AddHours($hour).ToString("yyyy-MM-ddTHH:mm:ssZ")
}

# ---------------------------------------------------------------------------------------------
Write-Step "Checking the gateway at $BaseUrl"
$probe = Invoke-Api GET "/api/bookings"
if ($probe.Status -eq 0) { throw "The API Gateway is not reachable at $BaseUrl. Start the stack first (docker compose up -d in backend/)." }
Write-Ok "gateway is up"

# ---------------------------------------------------------------------------------------------
Write-Step "1. Registering demo users"
$people = @(
    @{ Name = "Sarah";   Surname = "Naidoo";          Club = "pcc";        Coach = $false; Deposit = 600 },
    @{ Name = "Kyle";    Surname = "Botha";           Club = "pcc";        Coach = $false; Deposit = 400 },
    @{ Name = "Werner";  Surname = "Steyn";           Club = "pcc";        Coach = $false; Deposit = 300 },
    @{ Name = "David";   Surname = "Reddy";           Club = "pcc";        Coach = $true;  Deposit = 0 },
    @{ Name = "Michael"; Surname = "Erasmus";         Club = "defence";    Coach = $false; Deposit = 500 },
    @{ Name = "Riaan";   Surname = "Coetzee";         Club = "defence";    Coach = $true;  Deposit = 0 },
    @{ Name = "Thabo";   Surname = "Mokoena";         Club = "klerksdorp"; Coach = $false; Deposit = 700 },
    @{ Name = "Tumi";    Surname = "Sithole";         Club = "klerksdorp"; Coach = $false; Deposit = 0 },
    @{ Name = "Lindiwe"; Surname = "Dlamini";         Club = "klerksdorp"; Coach = $true;  Deposit = 0 },
    @{ Name = "Emma";    Surname = "van der Merwe";   Club = "rustenburg"; Coach = $false; Deposit = 450 },
    @{ Name = "Pieter";  Surname = "Human";           Club = "rustenburg"; Coach = $true;  Deposit = 0 },
    @{ Name = "Anja";    Surname = "Fourie";          Club = "coligny";    Coach = $true;  Deposit = 0 }
)

$users = @{}
foreach ($p in $people) {
    $email = ("{0}.{1}@potchmatch.demo" -f $p.Name, ($p.Surname -replace ' ', '')).ToLower()
    $reg = Invoke-Api POST "/api/auth/register" @{ name = $p.Name; surname = $p.Surname; email = $email; password = $DemoPassword; clubId = $p.Club; isCoach = $p.Coach }
    if ($reg.Status -eq 200) {
        $auth = $reg.Body; $how = "registered"
    }
    elseif ($reg.Status -eq 409) {
        $login = Invoke-Api POST "/api/auth/login" @{ email = $email; password = $DemoPassword }
        if ($login.Status -ne 200) { throw "User $email exists but login failed ($($login.Status))." }
        $auth = $login.Body; $how = "already existed, logged in"
    }
    else { throw "Registering $email failed: $($reg.Status) $($reg.Text)" }

    $users[$p.Name] = @{ Email = $email; Token = $auth.token; Id = $auth.playerId; Club = $p.Club; Coach = $p.Coach }
    Write-Ok ("{0,-28} {1,-6} {2,-11} {3}" -f $email, ($(if ($p.Coach) { "coach" } else { "player" })), $p.Club, $how)

    if ($p.Deposit -gt 0) {
        $dep = Invoke-Api POST "/api/payments/wallet/deposit" @{ amountZAR = $p.Deposit } $auth.token
        if ($dep.Status -ne 200) { Write-Warning "  deposit for $email returned $($dep.Status)" }
    }
}

# ---------------------------------------------------------------------------------------------
Write-Step "2. Normal business activity"
function New-Booking($host_, [decimal]$fee, [int]$days, [int]$hour, [string]$location, [bool]$coaching) {
    $r = Invoke-Api POST "/api/bookings" @{ clubId = $host_.Club; feeZAR = $fee; scheduledAt = (Get-FutureDate $days $hour); proposedLocation = $location; isCoachingSession = $coaching } $host_.Token
    if ($r.Status -ne 201 -and $r.Status -ne 200) { Write-Warning "  create booking returned $($r.Status) $($r.Text)"; return $null }
    return $r.Body.id
}
function Approve-Booking($guest, $bookingId, [string]$label) {
    $r = Invoke-Api PUT "/api/bookings/$bookingId/accept" $null $guest.Token
    Write-Ok ("{0,-55} -> HTTP {1}" -f $label, $r.Status)
    return $r.Status
}

$s1 = New-Booking $users.David   250 3 17 "PCC Court 2"         $true
$s2 = New-Booking $users.Lindiwe 200 4 18 "Klerksdorp Court 1"  $true
$s3 = New-Booking $users.Pieter  220 5 9  "Rustenburg Court 3"  $true
$s4 = New-Booking $users.Riaan   180 2 16 "Defence Court 1"     $true
$c1 = New-Booking $users.Michael 100 2 19 "Defence Court 2"     $false
$c2 = New-Booking $users.Thabo   150 6 10 "Klerksdorp Court 2"  $false
$c3 = New-Booking $users.Werner   80 3 18 "PCC Court 1"         $false
$c4 = New-Booking $users.Emma    120 7 11 "Rustenburg Court 1"  $false
$c5 = New-Booking $users.Sarah   200 8 12 "PCC Court 3"         $false
Write-Ok "created 4 coaching sessions and 5 challenges"

if ($s1) { [void](Approve-Booking $users.Sarah   $s1 "Sarah books David's coaching session") }
if ($s2) { [void](Approve-Booking $users.Thabo   $s2 "Thabo books Lindiwe's coaching session") }
if ($s4) { [void](Approve-Booking $users.Michael $s4 "Michael books Riaan's coaching session") }
if ($c1) { [void](Approve-Booking $users.Kyle    $c1 "Kyle accepts Michael's challenge") }
if ($c4) { [void](Approve-Booking $users.Sarah   $c4 "Sarah accepts Emma's challenge") }
if ($c2) { [void](Approve-Booking $users.Tumi    $c2 "Tumi (empty wallet) tries Thabo's challenge [402 expected]") }
if ($c3) { [void](Approve-Booking $users.Werner  $c3 "Werner tries to accept his own challenge [400 expected]") }

$pw = Invoke-Api PUT "/api/players/$($users.Werner.Id)/password" @{ currentPassword = "not-my-password"; newPassword = "Whatever@123" } $users.Werner.Token
Write-Ok ("{0,-55} -> HTTP {1}" -f "Werner changes password with the wrong current one", $pw.Status)

# ---------------------------------------------------------------------------------------------
Write-Step "3a. Brute force against two accounts (Rule 1)"
foreach ($victim in @($users.Sarah, $users.Thabo)) {
    for ($i = 1; $i -le 6; $i++) { [void](Invoke-Api POST "/api/auth/login" @{ email = $victim.Email; password = "guess-$i" }) }
    Write-Ok "6 failed logins for $($victim.Email)"
}

Write-Step "3b. Kyle probes admin endpoints, then the knowledge graph (Rules 2 and 5 -> escalated incident)"
foreach ($path in @("/api/soc/alerts", "/api/soc/events", "/api/soc/rules", "/api/soc/alerts")) { [void](Invoke-Api GET $path $null $users.Kyle.Token) }
[void](Invoke-Api PUT "/api/players/$($users.Sarah.Id)" @{ name = "Hacked"; surname = "Account"; email = $users.Sarah.Email; clubId = "pcc" } $users.Kyle.Token)
Write-Ok "4 x 403 on /api/soc/* and 1 x 403 editing another player's profile"
foreach ($path in @("/api/graph/users/high-severity", "/api/graph/services/payment-service/impact", "/api/graph/incidents")) { [void](Invoke-Api GET $path $null $users.Kyle.Token) }
[void](Invoke-Api POST "/api/rag/ask" @{ question = "Which users have admin rights?" } $users.Kyle.Token)
Write-Ok "3 x 403 on /api/graph/* and 1 x 403 on /api/rag/ask"

Write-Step "3c. Anonymous probing of the knowledge graph (Rule 5 by source IP)"
foreach ($path in @("/api/graph/incidents", "/api/graph/users/high-severity", "/api/graph/services/identity-service/threats", "/api/rag/status")) { [void](Invoke-Api GET $path) }
Write-Ok "4 x 401 on /api/graph/* and /api/rag/* without a token"

if (-not $SkipFlood) {
    Write-Step "3d. Request flood (Rule 3 POSSIBLE_DOS)"
    $flood = 0
    for ($i = 1; $i -le 150; $i++) { if ((Invoke-Api GET "/api/bookings").Status -ne 0) { $flood++ } }
    Write-Ok "$flood unauthenticated requests sent"
}

if (-not $SkipOutage) {
    Write-Step "3e. payment-service outage (Rule 4 SERVICE_FAILURE)"
    $docker = Get-Command docker -ErrorAction SilentlyContinue
    if (-not $docker) {
        Write-Warning "docker was not found on PATH - skipping the outage scenario."
    }
    else {
        & docker compose -f $ComposeFile stop payment-service | Out-Null
        Write-Ok "payment-service stopped; waiting 25 s for Consul to report it critical..."
        Start-Sleep -Seconds 25
        if ($s3) { [void](Approve-Booking $users.Emma $s3 "Emma books Pieter's session while payments are down") }
        if ($c5) { [void](Approve-Booking $users.Michael $c5 "Michael accepts Sarah's challenge while payments are down") }
        Write-Ok "waiting 20 s so the rule fires while the service is still down..."
        Start-Sleep -Seconds 20
        & docker compose -f $ComposeFile start payment-service | Out-Null
        Write-Ok "payment-service started again (the alert resolves itself once Consul reports it healthy)"
    }
}

# ---------------------------------------------------------------------------------------------
Write-Step "4. Summary"
$admin = Invoke-Api POST "/api/auth/login" @{ email = $AdminEmail; password = $AdminPassword }
if ($admin.Status -ne 200) { Write-Warning "Admin login failed ($($admin.Status)); open the SOC page to see the results."; return }
$adminToken = $admin.Body.token

if (-not $NoWait) {
    Write-Ok "waiting 60 s for detection (every 15 s) and graph sync (every 15 s + settle time)..."
    Start-Sleep -Seconds 60
}

$alerts = Invoke-Api GET "/api/soc/alerts" $null $adminToken
if ($alerts.Status -eq 200) {
    Write-Host "`nAlerts ($(@($alerts.Body).Count)):" -ForegroundColor Green
    @($alerts.Body) | Sort-Object timestamp | ForEach-Object {
        Write-Host ("   {0,-6} {1,-13} {2,-30} {3}" -f $_.severity, $_.status, $_.ruleName, $_.description)
    }
}
$incidents = Invoke-Api GET "/api/graph/incidents" $null $adminToken
if ($incidents.Status -eq 200) {
    $rows = @($incidents.Body.rows)
    Write-Host "`nIncidents in the knowledge graph ($($rows.Count)):" -ForegroundColor Green
    $rows | ForEach-Object {
        $flag = if ($_.escalated) { "ESCALATED" } else { "" }
        Write-Host ("   {0,-6} {1,-9} {2,-9} {3}  [{4}]" -f $_.severity, $_.status, $flag, $_.title, $_.subject)
    }
    if ($rows.Count -eq 0) { Write-Ok "(none yet - the graph sync can lag; refresh the Incidents tab in a minute)" }
}

Write-Host "`nDone. Log in as $AdminEmail and open the SOC page. Demo users log in with password $DemoPassword." -ForegroundColor Cyan
$client.Dispose()
