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
| 2. A&E vertical slice (arrivals, triage, staff, first conditions) | ✅ Done |
| 3. Building system (room templates, corridors) | Next |
| 4. Departments, wards, pharmacy, surgery, finances | Planned |

## Repository layout

```
game/                          Godot project (open this folder in Godot)
  project.godot
  BlueprintHospital.csproj     Game scripts; references the simulation library
  BlueprintHospital.sln
  Data/                        All data-driven content (JSON)
    Departments/  Symptoms/  Conditions/  Investigations/  Medications/
    Names/  Arrivals/  PatientBackgrounds/
  Scenes/                      Main menu and hospital scenes
  Scripts/
    Core/                      GameManager (autoload), session, settings, saves, smoke test
    Hospital/                  Site, rooms and people rendering, isometric camera, tile cursor
    UI/                        Main menu, HUD, A&E board, notifications, patient record,
                               patient/staff lists, pause/settings/save/load panels
src/BlueprintHospital.Simulation/   Engine-independent game rules (no Godot references)
  Core/                        Simulation clock, event bus, save format
  Data/                        JSON loading and validation, data models
  World/                       Grid, metres <-> tiles, isometric projection, site map, A* pathfinding
  Medical/                     Observations and NEWS2, triage, investigations, diagnosis engine
  Patients/                    Patient record and patient generator
  Staff/                       Staff members and the clinical task board
  Hospital/                    Care spaces and the A&E layout
  Transport/                   Ambulances
  Simulation/                  HospitalSimulation: arrivals, patient flow, staff AI, clinical work
tests/BlueprintHospital.Simulation.Tests/  xUnit tests, including whole patient journeys
tools/SimulationRunner/        Console tool: runs the A&E for days and prints the results
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
| Open a patient's record | Left-click the patient, a notification, or a row in the Patients list |
| Patients list / Staff list | P / T (or the top-bar buttons) |
| Close a record or list | Esc |
| Menu (save, load, settings, quit) | Esc (when nothing is open) |

## How the A&E works

Time runs at **15 game seconds per real second** at 1x (a game hour takes 4 real minutes at 1x,
1 minute at 4x). Patients arrive on a realistic daily pattern, about 35 a day: quiet overnight,
busiest from late morning to evening.

- **Walk-ins** queue at reception, are registered, then wait to be called to triage.
  **Ambulance** patients wait on the crew's trolley in the bay until a nurse takes handover.
- **Triage** uses observations and **NEWS2** to give a **Red / Amber / Green** category. Some
  complaints trigger protocol tests at triage (an ECG for chest pain); an abnormal ECG upgrades the
  patient to Red.
- Patients go to **Resus** (Red), **Majors** (Amber) or **Minors** (Green) when a space is free.
  Sicker patients take priority: a Red patient goes to a Majors cubicle if Resus is full, and
  lower-priority patients are kept out of spaces a sicker patient is waiting for.
- **Staff choose their own work** from a task board, by clinical priority: Red emergencies,
  deteriorating Amber patients, ongoing Red care, handovers, triage, routine Amber, Green, then
  admin. They walk to the patient to do each task, get tired, take breaks, and are refreshed at
  the 08:00 and 20:00 shift changes.
- **Doctors** take a history, build a differential diagnosis from what they know (symptoms,
  observations and results, never the hidden true condition), order tests, wait for results,
  and commit to a diagnosis. Tired, junior or overloaded doctors make more mistakes, and a missed
  diagnosis can bring the patient back.
- **Treatment** follows the diagnosis, with allergy checks before every medicine. Patients are
  then discharged or transferred (e.g. a heart attack to a heart attack centre for PCI).
- Untreated patients **deteriorate**. Repeat observations pick this up and upgrade their category.

The **A&E board** (left) shows patients by category, waits, ambulances, cubicle use and the
4-hour figure. Click any notification to open that patient's record.

> Saves currently store the hospital, date and camera. Patients and staff in the department are
> not saved yet; a loaded game starts with an empty department at the saved time.

## Building an executable

1. In Godot: **Editor → Manage Export Templates → Download and Install** (once).
2. **Project → Export…**, choose the **Windows Desktop** preset, then **Export Project**.
   The preset writes to `builds/windows/BlueprintHospital.exe` and already includes the
   `Data/*.json` files.

## Tests

```bash
# Unit tests for the simulation library
dotnet test tests/BlueprintHospital.Simulation.Tests

# Launch check: starts a game, checks patients arrive and are triaged and staff work,
# opens a patient record, saves, reloads and verifies, then exits 0/1
godot --headless --path game -- --smoke-test

# Same, with screenshots (needs a display, not --headless)
godot --path game -- --smoke-test --screenshots=/some/folder

# Run the A&E without graphics: hours, random seed, arrival multiplier, print N patient journeys
dotnet run --project tools/SimulationRunner -- 72 1 1.0 3
```

The simulation runner is the quickest way to check balance after changing content: it prints the
department each hour, then arrivals, outcomes, 4-hour performance, diagnostic accuracy and how
hard each member of staff worked.

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

Add `"triage_investigations": ["ecg"]` to a symptom to make triage nurses request those tests
whenever a patient has it.

### How to add a condition

Add an entry to any file in `game/Data/Conditions/`. This is a complete example:

```json
{
  "id": "simple_laceration",
  "name": "Simple laceration",
  "department": "emergency",
  "description": "A clean cut to the skin that needs cleaning and closing.",
  "prevalence": 6,
  "age_range": [4, 85],
  "female_fraction": 0.4,
  "ambulance_probability": 0.03,
  "presenting_complaint": "bleeding_wound",
  "symptoms": [ { "symptom": "bleeding_wound", "probability": 1.0 } ],
  "observations": {
    "heart_rate": [84, 9], "systolic_bp": [126, 11], "diastolic_bp": [78, 8],
    "respiratory_rate": [15, 2], "oxygen_saturation": [98.5, 0.8],
    "temperature": [36.7, 0.3], "pain_score": [2, 5]
  },
  "initial_severity": [0.05, 0.12],
  "deterioration_per_hour": 0,
  "deterioration_effects": { "heart_rate": 0, "systolic_bp": 0, "respiratory_rate": 0, "oxygen_saturation": 0, "temperature": 0 },
  "can_be_fatal": false,
  "key_investigations": [],
  "treatment": [
    { "type": "procedure", "name": "Wound cleaning and closure", "performed_by": "doctor", "minutes": 20, "essential": true },
    { "type": "medication", "medication": "paracetamol", "essential": false }
  ],
  "recovery_per_hour": 0.5,
  "disposition": "discharge",
  "disposition_note": "Discharged after wound closure with wound care advice."
}
```

- `prevalence` is how common the condition is relative to the others (it is also the doctors'
  prior belief). `symptoms` lists each symptom with the chance a patient has it; the
  `presenting_complaint` must be one of them, with probability 1.
- `observations` give the mean and spread of each vital sign; `pain_score` is a min/max range.
- `initial_severity` (0–1) and `deterioration_per_hour` control how ill patients are and how fast
  they get worse untreated; `deterioration_effects` is how much each vital sign shifts at full severity.
  Severity 1 is fatal if `can_be_fatal` is true.
- `key_investigations` are the tests doctors order to confirm or rule out this condition.
- `treatment` steps are done in order. `essential` steps must be given for the patient to recover.
- `disposition` is `discharge`, `transfer` or `admit`, with a note shown in the record.

### How to add an investigation

Add an entry to any file in `game/Data/Investigations/`:

```json
{
  "id": "troponin",
  "name": "High-sensitivity troponin",
  "category": "blood",
  "performed_by": "nurse",
  "procedure_minutes": 5,
  "turnaround_minutes": 60,
  "cost": 25,
  "applies_to_sex": "any",
  "unit": "ng/L",
  "normal": { "value_range": [2, 14], "summaries": ["Troponin {value} ng/L (normal, below 14)."] },
  "false_positive_rate": 0.03,
  "false_positive": { "value_range": [15, 40], "summaries": ["Troponin {value} ng/L (mildly raised)."] },
  "abnormal": [
    { "condition": "acute_myocardial_infarction", "probability": 0.95,
      "value_range": [60, 3000], "summaries": ["Troponin {value} ng/L (markedly raised)."] }
  ],
  "escalate_to_red_if_abnormal": false
}
```

Results are generated from the patient's **true** condition: for each `abnormal` entry,
`probability` is the test's sensitivity for that condition; otherwise the result is normal, apart
from `false_positive_rate`. `{value}` in a summary is replaced by a number from `value_range`.
`category` is `bedside`, `blood` or `imaging`; blood tests are sent to the lab and come back after
`turnaround_minutes`. `applies_to_sex` can restrict a test (e.g. pregnancy test: `female`).

### How to add a medication

Add an entry to any file in `game/Data/Medications/`:

```json
{
  "id": "aspirin",
  "name": "Aspirin",
  "drug_class": "Antiplatelet",
  "route": "oral",
  "typical_dose": "300 mg loading dose",
  "indications": "Suspected acute coronary syndrome.",
  "contraindications": "Aspirin allergy, active bleeding.",
  "allergy_groups": ["aspirin", "nsaid"],
  "common_side_effects": ["Indigestion", "Bleeding"],
  "cost_per_dose": 1
}
```

Before giving a medicine, staff check the patient's allergies against `allergy_groups` and
withhold it if they match. Patient allergies come from `game/Data/PatientBackgrounds/`.

### Names, arrivals and patient backgrounds

- `Names/` holds first names and surnames used for patients and staff.
- `Arrivals/` sets the expected arrivals for each hour of the day (`hourly_arrivals`, 24 numbers)
  and a multiplier per weekday.
- `PatientBackgrounds/` lists past medical history, regular medications and allergies, with how
  likely each is at different ages.

Room templates and furniture arrive with the building system.

### Adding a new content type (for developers)

1. Add a model class in `src/BlueprintHospital.Simulation/Data/Models/` implementing `IDataRecord`,
   with a `Validate` method that checks each field.
2. Add a folder constant and a `RecordLoader.LoadFolder<T>` call in `GameDatabase.Load`, plus any
   cross-reference checks.
3. Create the folder under `game/Data/` and add tests in `tests/`.

## Measurements

All sizes are stored in metres. One grid tile is **0.5 m × 0.5 m** (`Units.MetresPerTile`).
The starter hospital building is 30 m × 20 m on an 80 m × 60 m site.
