# MQTT contract (ESP32 ↔ digital twin)

Author: Mohamed Khamis

Changes: 2026-10-03, `light` and `fan` are on/off commands (0 or 100) accepted in both modes; in auto mode they also start the matching override. The separate `lights_ovr` and `ac_ovr` commands are removed (state and telemetry still report both flags). Buttons B1 and B2 work the same way. 2026-10-04, `light`/`fan` and B1/B2 keep working during sensor faults (as overrides in auto mode); while the motion sensor (pir) is failed, overrides don't end when the room empties. 2026-10-04, history requests and answers defined (PC history logger).

Base topic: `smartroom/room1/` (`room1` is the room id).

Broker: local Mosquitto at `192.168.1.150:1883` first. HiveMQ Cloud (TLS, port 8883, user + password) is the fallback, to be added later.

Every payload is JSON except `status`, which is plain text.

| Topic | Direction | Retained | When |
|---|---|---|---|
| `smartroom/room1/telemetry` | ESP32 → twin | no | Every 2 s |
| `smartroom/room1/state` | ESP32 → twin | yes | On any change and on reconnect |
| `smartroom/room1/status` | ESP32 → twin | yes | `online` on connect, `offline` as the Last Will |
| `smartroom/room1/faults` | ESP32 → twin | yes | When a sensor fails or recovers |
| `smartroom/room1/event` | ESP32 → twin | no | Occupancy, door, buttons, faults |
| `smartroom/room1/cmd` | twin → ESP32 | no | Commands |
| `smartroom/room1/history/req` | twin → PC logger | no | History request |
| `smartroom/room1/history/resp/<id>` | PC logger → twin | no | History answer |

## telemetry

```json
{"ts":123456,"occ":1,"pir":1,"door_open":0,"dist_cm":82.4,"temp_c":26.4,"set_c":25.0,"light_pct":41.2,"light":60,"fan":35,"power_w":1.84,"energy_wh":12.7,"saved_pct":38.5,"mode":"auto","lights_ovr":0,"ac_ovr":0,"ac_paused":0}
```

- `ts` is milliseconds since boot.
- `light` and `fan` are output levels, 0 to 100 %.
- `light_pct` is ambient light, 0 to 100.
- `temp_c`, `dist_cm` and `power_w` are `null` when that sensor has failed.

## state (retained)

```json
{"mode":"auto","occ":1,"set_c":25.0,"light":60,"fan":35,"lights_ovr":0,"ac_ovr":0,"ac_paused":0,"door_open":0}
```

## status (retained)

Plain text. `online` on connect; `offline` is set as the ESP32's Last Will.

## faults (retained)

```json
{"lm35":"no reading","sen0291":"not found"}
```

One key per failed sensor, with a short reason. `{}` when everything is OK.

Sensor keys: `pir`, `hcsr04`, `lm35`, `light`, `rotation`, `sen0291`.

In auto mode the board switches off what depends on a failed sensor: the fan for `lm35`, the lights for `light`, both for `pir`. The `light`/`fan` commands and buttons B1/B2 still work during a fault and act as overrides.

## event

```json
{"type":"occupancy","value":1}
{"type":"door","value":1}
{"type":"button","id":1}
{"type":"fault","sensor":"lm35","off":["fan"]}
{"type":"recovered","sensor":"lm35"}
```

Button ids: 1 is the lights button (B1), 2 is the fan/AC button (B2), 3 switches auto/manual (B3). B1 and B2 toggle their device on or off exactly like the `light` and `fan` commands below, including starting the override in auto mode.

## cmd

One or more keys in a single JSON object:

```json
{"mode":"auto"}        {"mode":"manual"}
{"light":100}          {"light":0}
{"fan":100}            {"fan":0}
{"set_c":24.5}
{"reset_energy":1}
```

- `light` and `fan` turn that device on (100) or off (0). The twin only sends 0 or 100, because the room's controls are push buttons, not dimmers.
- Manual mode: the board just sets the device on or off.
- Auto mode: the board sets the device on or off and also sets the matching override flag (`lights_ovr` or `ac_ovr` = 1). The override holds until the room becomes empty (then auto control resumes and the flag clears) or the mode is switched.
- While the fan is overridden in auto mode it runs at 100 % (on) or 0 % (off). An open door still pauses the AC, even during an override. In manual mode the door does not pause the fan.
- Without an override, auto mode dims the lights by daylight and sets the fan speed from the temperature on its own.
- Switching mode clears both override flags.
- During a sensor fault, `light`/`fan` (and B1/B2) are still accepted; in auto mode they override what the fault switched off.
- While the motion sensor (`pir`) is failed, occupancy is unknown, so an override does not end when the room empties: it lasts until it is switched off, the mode changes, or the sensor recovers (then the normal rules apply again). There is no time limit.
- When a failed sensor recovers, an active override stays until its normal end condition.
- `set_c` is 18 to 30. The last change wins, whether it came from the knob or the app.

## history (PC logger)

The history logger (`tools/HistoryLogger`, started with Windows) records every telemetry message, event, status change and faults change into `D:\Smart Room Data\history.db`. Nothing is deleted automatically. Days are local dates.

Request on `smartroom/room1/history/req`:

```json
{"day":"2026-10-04","client":"<id>"}
{"day":"list","client":"<id>"}
```

The answer comes on `smartroom/room1/history/resp/<id>` (QoS 1, not retained). `<id>` must not contain `/`, `#` or `+`.

Day list:

```json
{"days":["2026-10-03","2026-10-04"]}
```

One day:

```json
{"day":"2026-10-04",
 "totals":{"energy_wh":12.7,"saved_pct":38.5,"occupied_s":15300,"faults":1},
 "series":[{"t":"14:05","power_w":1.62,"temp_c":26.4,"set_c":25.0,"saved_pct":38.9,"occ":1.0}],
 "events":[{"t":"14:06:12","type":"door","value":1},{"t":"14:20:00","type":"status","value":"offline"}]}
```

- `totals.energy_wh`: energy used that day, the sum of the increases of `energy_wh` (a drop means the board reset its counter).
- `totals.saved_pct`: time-weighted average of `saved_pct`. `totals.occupied_s`: seconds with `occ` = 1. `totals.faults`: number of fault events.
- `series`: 5-minute buckets that have data. `power_w`, `temp_c` and `set_c` are averages, `saved_pct` is the last value, `occ` is the fraction of readings that were occupied. Any of them is `null` when no reading had it.
- `events`: that day's events as received (same fields as the `event` topic) plus `{"type":"status","value":"online"|"offline"}` for ESP connection changes, each with a local time `t`. At most the newest 500.

## Timeouts

The timeouts (15 s empty, 10 s door) are fixed in firmware and can't be set from the twin.
