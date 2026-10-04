# Pretends to be the ESP32: publishes status, state, faults, telemetry and events
# to the local broker, and obeys commands from the twin on .../cmd,
# so the twin can be tested without the board.
# Stop it with Ctrl+C; it then publishes a retained "offline" status.
param(
    [string]$BrokerHost = "192.168.1.150",
    [int]$Port = 1883,
    [string]$RoomId = "room1",
    # 0 runs until Ctrl+C.
    [int]$DurationSeconds = 0,
    # Keeps the room occupied the whole time (useful when testing the controls).
    [switch]$StayOccupied,
    # With -StayOccupied: the room is empty from this many seconds in, for 15 s. 0 = never.
    [int]$EmptyAt = 0,
    # Never opens the door.
    [switch]$NoDoor,
    # Simulates a failed sensor: pir, hcsr04, lm35, light, rotation or sen0291.
    [ValidateSet("", "pir", "hcsr04", "lm35", "light", "rotation", "sen0291")]
    [string]$Fault = "",
    # Seconds in when the sensor fails, and how long it stays failed.
    [int]$FaultAt = 15,
    [int]$FaultFor = 20
)

$mosquitto = "C:\Program Files\mosquitto"
$pub = Join-Path $mosquitto "mosquitto_pub.exe"
$sub = Join-Path $mosquitto "mosquitto_sub.exe"
foreach ($exe in $pub, $sub) {
    if (-not (Test-Path $exe)) {
        Write-Error "$exe not found"
        exit 1
    }
}

$base = "smartroom/$RoomId"
$inv = [System.Globalization.CultureInfo]::InvariantCulture
# The payload goes to mosquitto_pub through a file (-f), so quotes and spaces arrive exactly as written
# whatever PowerShell version passes the arguments.
$payloadFile = [System.IO.Path]::GetTempFileName()
$utf8 = New-Object System.Text.UTF8Encoding($false)

function Send([string]$subtopic, [string]$payload, [switch]$Retain) {
    [System.IO.File]::WriteAllText($payloadFile, $payload, $utf8)
    $pubArgs = @("-h", $BrokerHost, "-p", $Port, "-t", "$base/$subtopic", "-f", $payloadFile)
    if ($Retain) { $pubArgs += "-r" }
    & $pub @pubArgs
    if ($LASTEXITCODE -ne 0) { Write-Warning "publish to $subtopic failed (exit $LASTEXITCODE)" }
    Write-Host ("{0}  {1,-9} {2}" -f (Get-Date -Format "HH:mm:ss"), $subtopic, $payload)
}

function F([double]$value, [string]$format) { $value.ToString($format, $inv) }

function Flag([bool]$b) { if ($b) { 1 } else { 0 } }

function StateJson {
    '{"mode":"' + $mode + '","occ":' + $occ + ',"set_c":' + (F $setC "0.0") + ',"light":' + $light + ',"fan":' + $fan +
        ',"lights_ovr":' + (Flag $lightsOvr) + ',"ac_ovr":' + (Flag $acOvr) + ',"ac_paused":' + (Flag $acPaused) +
        ',"door_open":' + $doorOpen + '}'
}

# Short reasons and what each failed sensor switches off (auto mode), as the board would report them.
$faultReasons = @{ pir = "no signal"; hcsr04 = "no echo"; lm35 = "no reading"; light = "no reading"; rotation = "no reading"; sen0291 = "not found" }
$faultOff = @{ pir = '["lights","fan"]'; hcsr04 = '[]'; lm35 = '["fan"]'; light = '["lights"]'; rotation = '[]'; sen0291 = '[]' }

function Failed([string]$key) { $script:activeFault -eq $key }

function UpdateOutputs {
    if ($script:mode -eq "manual") {
        # Manual: the twin sets the levels; nothing automatic.
        $script:light = $script:manualLight
        $script:fan = $script:manualFan
        $script:acPaused = $false
        return
    }

    if ($script:occ -eq 1) {
        $autoLight = [int][Math]::Round(100 - $script:lightPct)
        # Fan ramps from 40 % at the setpoint to 100 % at 3 °C above it.
        $over = $script:temp - $script:setC
        if ($over -gt 0) { $autoFan = [int][Math]::Min(100, [Math]::Round(40 + $over * 20)) } else { $autoFan = 0 }
    } else {
        $autoLight = 0
        $autoFan = 0
    }

    if ($script:lightsOvr) { $script:light = $script:ovrLight } else { $script:light = $autoLight }
    if ($script:acOvr) { $script:fan = $script:ovrFan } else { $script:fan = $autoFan }

    # An open door pauses the AC.
    $script:acPaused = ($script:doorOpen -eq 1)
    if ($script:acPaused) { $script:fan = 0 }

    # Failed sensors switch off whatever depends on them (auto mode only), unless the user overrides it.
    if (((Failed "lm35") -or (Failed "pir")) -and -not $script:acOvr) { $script:fan = 0 }
    if (((Failed "light") -or (Failed "pir")) -and -not $script:lightsOvr) { $script:light = 0 }
}

function ClampLevel($v) { [int][Math]::Max(0, [Math]::Min(100, [Math]::Round([double]$v))) }

# Applies one command from the twin. Returns $true if anything changed.
function HandleCommand([string]$json) {
    try { $cmd = $json | ConvertFrom-Json } catch { Write-Warning "bad cmd: $json"; return $false }
    $names = $cmd.PSObject.Properties.Name

    if ($names -contains "mode" -and ($cmd.mode -eq "auto" -or $cmd.mode -eq "manual")) {
        if ($cmd.mode -eq "manual" -and $script:mode -ne "manual") {
            # Start manual from whatever the room is doing now.
            $script:manualLight = $script:light
            $script:manualFan = $script:fan
        }
        # Overrides only mean something in auto; switching mode either way clears them.
        if ($cmd.mode -ne $script:mode) {
            $script:lightsOvr = $false
            $script:acOvr = $false
        }
        $script:mode = $cmd.mode
    }
    if ($names -contains "lights_ovr" -or $names -contains "ac_ovr") {
        Write-Host "  (ignored lights_ovr / ac_ovr: removed from the contract, use light / fan)"
    }
    # light / fan: manual just sets the device; auto sets it and starts the matching override.
    if ($names -contains "light") {
        $level = ClampLevel $cmd.light
        if ($script:mode -eq "manual") { $script:manualLight = $level }
        else { $script:lightsOvr = $true; $script:ovrLight = $level }
    }
    if ($names -contains "fan") {
        $level = ClampLevel $cmd.fan
        if ($script:mode -eq "manual") { $script:manualFan = $level }
        else { $script:acOvr = $true; $script:ovrFan = $level }
    }
    if ($names -contains "set_c") {
        $script:setC = [Math]::Max(18.0, [Math]::Min(30.0, [double]$cmd.set_c))
    }
    if ($names -contains "reset_energy" -and [int]$cmd.reset_energy -ne 0) {
        $script:energyWh = 0.0
    }
    return $true
}

function TelemetryJson($elapsed) {
    $powerW = 0.15 + $light * 0.004 + $fan * 0.018 + ($rand.NextDouble() - 0.5) * 0.04
    $script:energyWh += $powerW * 2 / 3600
    $savedPct = 37.5 + 7 * [Math]::Sin($elapsed / 70)
    if ($occ -eq 1) { $dist = 80 + ($rand.NextDouble() - 0.5) * 10 } else { $dist = 240 + ($rand.NextDouble() - 0.5) * 6 }

    # A failed sensor reports null.
    $occJson = "$occ"; $pirJson = "$occ"; $distJson = F $dist "0.0"; $tempJson = F $temp "0.0"
    $lightPctJson = F $lightPct "0.0"; $powerJson = F $powerW "0.00"
    if (Failed "pir") { $occJson = "null"; $pirJson = "null" }
    if (Failed "hcsr04") { $distJson = "null" }
    if (Failed "lm35") { $tempJson = "null" }
    if (Failed "light") { $lightPctJson = "null" }
    if (Failed "sen0291") { $powerJson = "null" }

    '{"ts":' + [long]($elapsed * 1000) + ',"occ":' + $occJson + ',"pir":' + $pirJson + ',"door_open":' + $doorOpen +
        ',"dist_cm":' + $distJson + ',"temp_c":' + $tempJson + ',"set_c":' + (F $setC "0.0") +
        ',"light_pct":' + $lightPctJson + ',"light":' + $light + ',"fan":' + $fan +
        ',"power_w":' + $powerJson + ',"energy_wh":' + (F $script:energyWh "0.0000") +
        ',"saved_pct":' + (F $savedPct "0.0") + ',"mode":"' + $mode + '","lights_ovr":' + (Flag $lightsOvr) +
        ',"ac_ovr":' + (Flag $acOvr) + ',"ac_paused":' + (Flag $acPaused) + '}'
}

$start = Get-Date
$mode = "auto"
$occ = 1
$setC = 25.0
$temp = 26.0
$lightPct = 40.0
$light = 0
$fan = 0
$manualLight = 0
$manualFan = 0
$lightsOvr = $false
$acOvr = $false
$ovrLight = 100
$ovrFan = 100
$acPaused = $false
$energyWh = 0.0
$lastToggle = $start
$lastTelemetry = $start.AddSeconds(-10)
$doorOpen = 0
# The first door opening comes about 8 s in, then about every 25 s while the room is occupied.
$lastDoorClose = $start.AddSeconds(-17)
$doorOpenedAt = $start
$rand = New-Object System.Random
$activeFault = ""
$faultDone = $false

# Listen for commands: mosquitto_sub writes each payload as a line to a temp file, which the loop reads.
$cmdFile = [System.IO.Path]::GetTempFileName()
$cmdErr = [System.IO.Path]::GetTempFileName()
$subProc = Start-Process -FilePath $sub -ArgumentList @("-h", $BrokerHost, "-p", $Port, "-t", "$base/cmd", "-q", "1") `
    -RedirectStandardOutput $cmdFile -RedirectStandardError $cmdErr -NoNewWindow -PassThru
$cmdLinesRead = 0

try {
    UpdateOutputs
    Send "status" "online" -Retain
    Send "faults" "{}" -Retain
    Send "state" (StateJson) -Retain
    Send "event" ('{"type":"occupancy","value":' + $occ + '}')

    while ($true) {
        $now = Get-Date
        $elapsed = ($now - $start).TotalSeconds
        if ($DurationSeconds -gt 0 -and $elapsed -ge $DurationSeconds) { break }
        $stateChanged = $false

        # Commands from the twin.
        $lines = @(Get-Content -LiteralPath $cmdFile -ErrorAction SilentlyContinue)
        for ($i = $cmdLinesRead; $i -lt $lines.Count; $i++) {
            $line = $lines[$i].Trim()
            if ($line -eq "") { continue }
            Write-Host ("{0}  cmd       {1}" -f (Get-Date -Format "HH:mm:ss"), $line)
            if (HandleCommand $line) { $stateChanged = $true }
        }
        $cmdLinesRead = $lines.Count

        # Occupancy flips about every 30 s (unless -StayOccupied, where -EmptyAt can empty it once).
        # Overrides end when the room empties.
        $wantOcc = $occ
        if ($StayOccupied) {
            if ($EmptyAt -gt 0 -and $elapsed -ge $EmptyAt -and $elapsed -lt $EmptyAt + 15) { $wantOcc = 0 } else { $wantOcc = 1 }
        } elseif (($now - $lastToggle).TotalSeconds -ge 30) {
            $wantOcc = 1 - $occ
        }
        if ($wantOcc -ne $occ) {
            $occ = $wantOcc
            $lastToggle = $now
            # Overrides end when the room empties, except while the motion sensor is failed:
            # occupancy is unknown then, so they last until switched off, the mode changes, or it recovers.
            if ($occ -eq 0 -and -not (Failed "pir")) {
                $lightsOvr = $false
                $acOvr = $false
            }
            Send "event" ('{"type":"occupancy","value":' + $occ + '}')
            $stateChanged = $true
        }

        # The door opens for about 10 s.
        if (-not $NoDoor -and -not (Failed "hcsr04") -and $doorOpen -eq 0 -and $occ -eq 1 -and ($now - $lastDoorClose).TotalSeconds -ge 25) {
            $doorOpen = 1
            $doorOpenedAt = $now
            Send "event" '{"type":"door","value":1}'
            $stateChanged = $true
        } elseif ($doorOpen -eq 1 -and ($now - $doorOpenedAt).TotalSeconds -ge 10) {
            $doorOpen = 0
            $lastDoorClose = $now
            Send "event" '{"type":"door","value":0}'
            $stateChanged = $true
        }

        # Simulated sensor fault: fails at -FaultAt, recovers -FaultFor seconds later.
        if ($Fault -ne "" -and -not $faultDone) {
            if ($activeFault -eq "" -and $elapsed -ge $FaultAt) {
                $activeFault = $Fault
                Send "faults" ('{"' + $Fault + '":"' + $faultReasons[$Fault] + '"}') -Retain
                Send "event" ('{"type":"fault","sensor":"' + $Fault + '","off":' + $faultOff[$Fault] + '}')
                $stateChanged = $true
            } elseif ($activeFault -ne "" -and $elapsed -ge $FaultAt + $FaultFor) {
                $activeFault = ""
                $faultDone = $true
                Send "faults" "{}" -Retain
                Send "event" ('{"type":"recovered","sensor":"' + $Fault + '"}')
                $stateChanged = $true
            }
        }

        # Temperature drifts between 24 and 28 °C, ambient light between 25 and 55 %.
        $temp = 26 + 2 * [Math]::Sin($elapsed / 40) + ($rand.NextDouble() - 0.5) * 0.2
        $lightPct = 40 + 15 * [Math]::Sin($elapsed / 55) + ($rand.NextDouble() - 0.5) * 2

        if ($stateChanged) {
            UpdateOutputs
            Send "state" (StateJson) -Retain
        }

        if (($now - $lastTelemetry).TotalSeconds -ge 2) {
            $lastTelemetry = $now
            UpdateOutputs
            Send "telemetry" (TelemetryJson $elapsed)
        }

        Start-Sleep -Milliseconds 250
    }
}
finally {
    if ($activeFault -ne "") { Send "faults" "{}" -Retain }
    if ($subProc -and -not $subProc.HasExited) { $subProc.Kill() }
    Remove-Item -LiteralPath $cmdFile, $cmdErr -ErrorAction SilentlyContinue
    Send "status" "offline" -Retain
    Remove-Item -LiteralPath $payloadFile -ErrorAction SilentlyContinue
}
