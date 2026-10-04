# Smart Room Digital Twin

Author: Mohamed Khamis

A Unity digital twin of an energy-saving smart room. It connects to the room's ESP32 over MQTT, shows the live state of the room in 3D, and lets you control it remotely. The firmware lives in [smart-room-energy](https://github.com/MrFlakey/smart-room-energy).

![Side panel with the room occupied](Screenshots/step4-panel-occupied.png)

## Features

- **Live 3D room:** lamps, fan and door follow the real devices; an occupant figure appears when the room is occupied and the room tint follows the temperature.
- **Side panel:** live power, energy used, percentage saved against "always on", temperature against target, light level and connection status.
- **Controls:** Auto/Manual mode, light and AC switches (overrides in Auto mode) and target temperature.
- **Alerts:** sensor faults show as banners, red tiles and highlights in the room, with an event log.
- **History:** a History window with daily totals, power, temperature and energy-saved charts and the day's events, served by a small logger that stores every reading.
- **Offline handling:** clear "waiting for broker" and "waiting for ESP" states; reconnects automatically.

| Controls | Fault alert | History |
|---|---|---|
| ![Controls](Screenshots/step5-switches-auto.png) | ![Fault](Screenshots/step7-fault-lm35.png) | ![History](Screenshots/step8-history.png) |

## How it fits together

```
ESP32  <--MQTT-->  Mosquitto broker (PC)  <--MQTT-->  Unity twin
                          |
                    HistoryLogger (SQLite)
```

The topics and payloads are defined in [`docs/mqtt-contract.md`](docs/mqtt-contract.md).

## Project layout

```
Assets/Scenes/Room.unity      the twin scene
Assets/Scripts/Mqtt/          MQTT connection and settings
Assets/Scripts/Model/         room state, faults, history client
Assets/Scripts/Devices/       lamps, fan, door, occupant, room tint, fault effects
Assets/Scripts/UI/            side panel, controls, event log, history window, charts
tools/HistoryLogger/          .NET + SQLite logger that answers history requests
tools/simulate-room.ps1       pretends to be the ESP32 for testing
docs/mqtt-contract.md         MQTT topics and payloads
```

## Running it

1. Open the project in **Unity 6000.4.7f1** (URP).
2. Run an MQTT broker (Mosquitto, port 1883) and set its address in `Assets/Settings/MqttSettings.asset`.
3. Open `Assets/Scenes/Room.unity` and press Play.

**3D room model:** the bedroom model is not included in this repository. Put your own room model in the scene, or import the original into `Assets/Assets/`, and assign the lamps, fan and door in the scene.

### Testing without the ESP32

`tools/simulate-room.ps1` publishes the same messages as the board and obeys commands from the twin (needs Mosquitto's `mosquitto_pub`/`mosquitto_sub`):

```
.\tools\simulate-room.ps1 -BrokerHost <broker-ip>
.\tools\simulate-room.ps1 -StayOccupied -Fault lm35
```

Options: `-DurationSeconds`, `-StayOccupied`, `-EmptyAt`, `-NoDoor`, `-Fault pir|hcsr04|lm35|light|rotation|sen0291`, `-FaultAt`, `-FaultFor`.

### History logger

`tools/HistoryLogger` is a .NET console app that saves every reading to SQLite and answers the twin's history requests. Set the broker and database path in `appsettings.json`, then `dotnet run`. The twin shows "History unavailable" when the logger isn't running.

## Built with

Unity 6 (URP), TextMesh Pro, [MQTTnet](https://github.com/dotnet/MQTTnet), .NET and SQLite, Mosquitto.
