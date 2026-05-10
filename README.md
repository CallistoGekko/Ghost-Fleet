!!!WARNING!!!
EVERY SINGLE LINE OF THIS CODE WAS VIBECODED. BEWARE OF BAD THINGS HAPPENING
!!!WARNING!!!

# Logistics Tab — Solar Expanse Mod

A [BepInEx](https://github.com/BepInEx/BepInEx) mod for the game **Solar Expanse** that adds a logistics management system inspired by Factorio. Automate resource transportation between celestial bodies using your existing spacecraft and launch vehicles — no more manual mission planning for routine supply runs!

## Features

- **Automated interplanetary logistics** — set up "GET" requests and "SEND" providers on any celestial body, and the mod handles the rest.
- **Spacecraft (SC) delivery** — assign quotas to ship types (e.g. "use up to 3 TALOS for logistics"). The mod finds idle ships at the provider, creates cyclical missions, and delivers resources automatically.
- **Launch Vehicle (LV) delivery** — enable specific LV types for surface-to-orbit or surface-to-other-body deliveries. Uses orbital containers for same-body orbit, regular spacecraft for interplanetary.
- **Quota-based ship management** — no ship locking or hiding. Quotas are soft limits: ships remain visible and usable in the vanilla mission planner. Logistics simply won't exceed the quota.
- **Persistent save/load** — all requests, providers, and quotas are saved per save file. Active deliveries are reconciled on load.
- **Button-based amount input** — no text fields, just `+10` `+100` `+1K` buttons for quick resource amount entry.

## Installation

1. Install [BepInEx 5.x](https://github.com/BepInEx/BepInEx) for Solar Expanse.
2. Download the latest `LogisticsMod.dll` from [Releases](https://github.com/yourusername/logistics-mod/releases).
3. Place it in `BepInEx/plugins/logisticsmod/LogisticsMod.dll`.
4. Launch the game. Open any object's info window — the Logistics tab appears at the bottom.

## How It Works

### Quick Start

1. Open any celestial body's info window (planet, moon, orbit, asteroid).
2. Scroll to the **Logistics** section at the bottom.
3. **SPACECRAFT tab** — add quotas for ship types you want to use: `+ Add spacecraft quota` → select a type, set the number of ships to reserve.
4. **LAUNCH VEHICLE tab** — click LV types to enable them (ON/OFF toggle).
5. **SEND tab** — add resources this body provides to the network, with a minimum keep amount.
6. **GET tab** — request resources from the network. The mod will deliver them using available ships.

### Sections

| Section | Purpose |
|---------|---------|
| **GET — Request Resources** | List resources you want delivered to this body. Shows status: pending, in transit, satisfied. |
| **SEND — Provide Resources** | List resources this body exports to the network. Set minimum keep to reserve local stock. |
| **SPACECRAFT — Logistics Vessels** | Assign quotas to interplanetary ship types. Format: `free/total TYPENAME`. |
| **LAUNCH VEHICLE — Surface Shuttles** | Toggle launch vehicle types ON/OFF for surface-to-orbit transfers. |

### Status Types
- **pending** — waiting for available ships or resources
- **in transit** — delivery mission is active
- **satisfied** — requested amount is present on the body
- **failed** — delivery could not be completed

Requests that cannot be fulfilled stay `pending` and retry each day.

## Requirements

- **Solar Expanse** (Steam version)
- **BepInEx 5.x** (x64)
- **Newtonsoft.Json** (included with the game)

## Building from Source

```powershell
dotnet build "C:\temp\logiModDevRoot\NewVersion\LogisticsMod\LogisticsMod.csproj" -c Debug
```

The built DLL is placed directly into the game's BepInEx plugins folder (configured in `.csproj`).

### Dependencies
Update the `.csproj` hint paths to match your game installation:
- `BepInEx/core/BepInEx.dll`
- `BepInEx/core/0Harmony.dll`
- `Solar Expanse_Data/Managed/Assembly-CSharp.dll`
- `Solar Expanse_Data/Managed/UnityEngine.CoreModule.dll`
- `Solar Expanse_Data/Managed/Unity.TextMeshPro.dll`
- (and other UnityEngine modules, listed in `.csproj`)

## Architecture

```
ObjectInfoWindow (game)
  └─ LogisticsUI (MonoBehaviour)
       ├─ GET section (requests)
       ├─ SEND section (providers)
       ├─ SPACECRAFT section (SC quotas)
       └─ LAUNCH VEHICLE section (LV toggles)

LogisticsObserver (static)
  ├─ OnDayChange() — main loop
  ├─ CountActiveLogisticsCycles() — query game state
  ├─ TryCreateDeliveries() — SC + LV delivery creation
  └─ SetupCycleMission() — create game cycles

LogisticsNetwork (static)
  └─ Dictionary<int, LogisticsObjectData> — keyed by oi.id

LogisticsPersistence (static)
  └─ JSON save/load per save file
```

## Known Limitations

- **Return trip fuel** — spacecraft with fuel requirements may not have fuel for the return leg. Cycle missions cannot buy fuel at destination. Ensure fuel stockpiles at both ends for non-solar ships.
- **Keyboard input** — vanilla text input fields may stop working after interacting with mod UI. The mod uses button-based input as a workaround, but the root cause may affect other mods.
- **Ships idle at wrong locations** — logistics only uses ships at the provider body. Ships elsewhere won't be called back (request stays pending).
