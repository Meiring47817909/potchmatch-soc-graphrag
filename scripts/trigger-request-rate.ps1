# Sends unauthenticated GET /api/bookings requests through the gateway to trigger the
# SOC "POSSIBLE_DOS" rule (>100 requests within 60 s from one source). The default
# Count is 250 so the threshold is crossed comfortably even on a slow machine.
#
# Every request is expected to get a 401 (no token). NOTE: these 401s also raise a
# Rule 2 alert ("Unauthorised Endpoint Access") for this IP - that is expected.
#
# Uses one shared HttpClient and inspects StatusCode (no exception per 401), and
# 127.0.0.1 rather than localhost (avoids a slow ::1 attempt first).
# Works on Windows PowerShell 5.1+.
param(
    [string]$Url = "http://127.0.0.1:5132/api/bookings",
    [int]$Count = 250
)

Add-Type -AssemblyName System.Net.Http

$client = New-Object System.Net.Http.HttpClient
$client.Timeout = [TimeSpan]::FromSeconds(10)

$unauthorised = 0
$otherStatus = 0
$failed = 0
$clock = [System.Diagnostics.Stopwatch]::StartNew()

try {
    for ($i = 1; $i -le $Count; $i++) {
        try {
            $response = $client.GetAsync($Url).GetAwaiter().GetResult()
            if ([int]$response.StatusCode -eq 401) { $unauthorised++ } else { $otherStatus++ }
            $response.Dispose()
        }
        catch {
            $failed++
        }
    }
}
finally {
    $client.Dispose()
}
$clock.Stop()

Write-Host ("Sent {0} requests to {1} in {2:N1} s" -f $Count, $Url, $clock.Elapsed.TotalSeconds)
Write-Host ("  401 Unauthorized:   {0}" -f $unauthorised)
Write-Host ("  Other status codes: {0}" -f $otherStatus)
Write-Host ("  Connection failures: {0}" -f $failed)

if ($unauthorised -eq 0) {
    Write-Error "No 401 responses received - is the stack running at $Url?"
    exit 1
}
if ($clock.Elapsed.TotalSeconds -gt 60) {
    Write-Warning "Took longer than 60 s; the >100 requests/min threshold may not have been reached."
}
