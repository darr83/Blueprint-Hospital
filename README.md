# Blueprint Hospital

A realistic, NHS-inspired hospital management simulation for Windows, built with
**Godot 4.7 (.NET)** and **C#**. Players build a hospital, run an A&E with Red / Amber / Green
triage, and follow real patients through investigation, diagnosis, treatment and discharge.

> Educational simulation only. Medical content is informed by UK clinical practice but is not
> clinical guidance.

## Status

| Milestone | State |
|---|---|
| 1. Project foundation (menu, camera, grid, data loading, save/load) | ✅ Done |
| 2. A&E vertical slice (arrivals, triage, staff, first conditions) | Next |
| 3. Building system (room templates, corridors) | Planned |
| 4. Departments, wards, pharmacy, surgery, finances | Planned |

## Repository layout

```
game/                          Godot project (open this folder in Godot)
  project.godot
  BlueprintHospital.csproj     Game scripts; references the simulation library
  BlueprintHospital.sln
  Data/                        All data-driven content (JSON)
    Departments/
    Symptoms/
  Scenes/                      Main menu and hospital scenes
  Scripts/
    Core/                      GameManager (autoload), session, settings, saves, smoke test
    Hospital/                  Site rendering, isometric camera, tile cursor
    UI/                        Main menu, HUD, pause/settings/save/load panels
src/BlueprintHospital.Simulation/   Engine-independent game rules (no Godot references)
  Core/                        Simulation clock, event bus, save format
  Data/                        JSON loading and validation, data models
  World/                       Grid, metres <-> tiles, isometric projection, site map
  Medical/                     Triage categories (and, later, the whole medical model)
tests/BlueprintHospital.Simulation.Tests/  xUnit tests for the simulation library
```

The rules of the game live in `src/` as plain C#, so they can be tested quickly without
starting Godot. The `game/` project only draws the world and handles input.

## Opening and running the game

You need:

- **Godot 4.7.2 .NET** (the "mono" download), e.g. `Godot_v4.7.2-stable_mono_win64.exe`.
- **.NET 8 SDK** – https://dotnet.microsoft.com/download/dotnet/8.0

Then:

1. Start Godot, choose **Import**, and select `game/project.godot`.
2. Press **Build** (hammer icon, top right) once, then **Run Project** (F5).

### Controls

| Action | Control |
|---|---|
| Move camera | Middle mouse drag, or WASD / arrow keys |
| Zoom | Mouse wheel (zooms towards the cursor) |
| Pause / resume | Space |
| Speed 1x / 2x / 4x | 1 / 2 / 3 |
| Toggle building grid | G |
| Menu (save, load, settings, quit) | Esc |

## Building an executable

1. In Godot: **Editor → Manage Export Templates → Download and Install** (once).
2. **Project → Export…**, choose the **Windows Desktop** preset, then **Export Project**.
   The preset writes to `builds/windows/BlueprintHospital.exe` and already includes the
   `Data/*.json` files.

## Tests

```bash
# Unit tests for the simulation library
dotnet test tests/BlueprintHospital.Simulation.Tests

# Launch check: starts a game, runs the clock, saves, reloads and verifies, then exits 0/1
godot --headless --path game -- --smoke-test

# Same, with screenshots of the main menu and hospital (needs a display, not --headless)
godot --path game -- --smoke-test --screenshots=/some/folder
```

GitHub Actions runs both on every pull request (`.github/workflows/ci.yml`).

## Adding content

All content is loaded from `game/Data/` when the game starts and is checked before play.
If anything is wrong, the main menu lists every problem with its file, record and field,
for example:

```
Symptoms/core_symptoms.json record 'fever', field 'name': is required and must not be empty.
```

General rules for every data folder:

- Each `.json` file holds an **array** of records. Split content across as many files as you like;
  every `.json` file in the folder is loaded.
- Every record needs a unique `id`: lowercase letters, digits and underscores, starting with a letter
  (e.g. `chest_pain`). Other data refers to records by this id.
- Field names are `snake_case`. Misspelt or unknown fields are rejected rather than ignored.

### How to add a symptom

Add an entry to any file in `game/Data/Symptoms/`:

```json
{
  "id": "cough",
  "name": "Cough",
  "patient_description": "I can't stop coughing.",
  "body_system": "respiratory",
  "is_red_flag": false
}
```

`body_system` is one of: `general`, `cardiovascular`, `respiratory`, `gastrointestinal`,
`neurological`, `gynaecological`, `musculoskeletal`, `skin`.

### How to create a new department

Add an entry to any file in `game/Data/Departments/`:

```json
{
  "id": "respiratory",
  "name": "Respiratory Medicine",
  "short_name": "RESP",
  "description": "Treats conditions of the lungs and airways.",
  "colour": "#00897B",
  "starts_unlocked": false,
  "is_shared_service": false,
  "opening_cost": 220000
}
```

Exactly one department (A&E) has `starts_unlocked: true`. Set `is_shared_service` for services such
as Radiology that serve other departments.

### Conditions, investigations, medications, room templates, furniture

These content types arrive with the milestones that use them (conditions and investigations in
the A&E vertical slice, room templates and furniture with the building system, medications with
the pharmacy). Each will get its own section here, following the same rules as above.

### Adding a new content type (for developers)

1. Add a model class in `src/BlueprintHospital.Simulation/Data/Models/` implementing `IDataRecord`,
   with a `Validate` method that checks each field.
2. Add a folder constant and a `RecordLoader.LoadFolder<T>` call in `GameDatabase.Load`, plus any
   cross-reference checks.
3. Create the folder under `game/Data/` and add tests in `tests/`.

## Measurements

All sizes are stored in metres. One grid tile is **0.5 m × 0.5 m** (`Units.MetresPerTile`).
The starter hospital building is 30 m × 20 m on an 80 m × 60 m site.
