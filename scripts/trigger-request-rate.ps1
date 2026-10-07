# Sends 120 unauthenticated GET /api/bookings requests as fast as possible to
# trigger the SOC "POSSIBLE_DOS" rule (>100 requests per minute from one source).
# The 401 responses are expected and are swallowed so the loop continues.
# Works on Windows PowerShell 5.1+.
param(
    [string]$Url = "http://localhost:5132/api/bookings",
    [int]$Count = 120
)

$sent = 0
for ($i = 1; $i -le $Count; $i++) {
    try {
        Invoke-WebRequest -Uri $Url -Method Get -UseBasicParsing | Out-Null
    }
    catch {
        # Expected: 401 Unauthorized (no token supplied). Keep going.
    }
    $sent++
}
Write-Host "Sent $sent requests to $Url"
