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
| 3. Building system (room templates, corridors, extensions, budget) | ✅ Done |
| 4. Departments, wards, pharmacy, surgery, finances | ✅ Done: wards, imaging, theatres, pharmacy, 38 conditions, finances, full saves |

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
| Zoom | Mouse wheel (zooms towards the cursor), or the + and − buttons bottom right |
| Pause / resume | Space |
| Speed 1x / 2x / 4x | 1 / 2 / 3 |
| Toggle building grid | G |
| Show walking routes | R (or Routes on the left-hand bar) |
| Rotate the view a quarter turn | Q / E (or the turn buttons bottom right) |
| Play or Build mode | The Play / Build switch in the top bar, or B to swap |
| Open a patient's record | Play mode: left-click the patient, a notification, or a row in the Patients list |
| Open a member of staff's panel | Play mode: left-click them, or a row in the Staff list |
| See a bed and who is in it | Play mode: left-click the bed |
| Show / hide the A&E board | Hospital on the left-hand bar |
| Patients list / Staff list | P / T (or the left-hand bar) |
| Finances | F (or click the balance in the top bar) |
| Recruitment (hire staff) | H (or Hire in the left-hand bar) |
| Build menu | Build mode (B, or Build on the left-hand bar) |
| Build / stop building | Drag with the left button / right click or Esc |
| Room details, rename, move or demolish | Build mode: left-click the room's floor |
| Furniture or equipment details | Build mode: left-click the item |
| Close a record, list or panel | Esc |
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

> Saves store everything: the hospital, date, camera, rooms, building, money and accounts, every
> patient (with their full record, where they are and what they are waiting for), staff, tasks,
> ambulances and statistics. A loaded game carries on exactly where it stopped.

## Building the hospital

The game has two modes. In **Play mode** clicking picks patients, staff and beds; rooms and
furniture can't be picked. In **Build mode** (press **B** or use the switch in the top bar) the
build menu opens and clicking picks rooms, furniture and equipment; people can't be picked.
The selected room or item is outlined in blue.

Press **B** to go into build mode and open the build menu. Every room is built from a **template** in
`game/Data/RoomTemplates`, with furniture from `game/Data/Furniture`, all at real sizes in metres.

- **Rooms** (A&E: Minors and Majors cubicles, Resus bay, Triage room, Waiting room; Staff:
  doctors' office, nurses' station, staff room) are **dragged out from corner to corner at any
  size**, in half-metre steps, above each type's minimum (shown on its button) and below its
  maximum. Either side can be the long one. The menu shows what the room holds at a few example
  sizes before you build.
- **Placing:** while you drag, a see-through preview shows the furniture and door. It is
  **green** with the price when it can be built and **red** with the reasons when it can't:
  outside the building, overlapping another room, too small or too big, not touching a corridor,
  cutting off another room or an entrance, or not enough money. Release the mouse to build.
- **Doors are automatic:** the door goes on the wall with the longest stretch touching a corridor
  (or the waiting room), centred on that stretch. A waiting room that lines up with the main
  entrance also gets a street door.
- **Furniture adapts** to the room: beds, monitors and chairs keep their real size, and a bigger
  waiting room or office simply gets more seats.
- **Corridors** are dragged out to any size, corner to corner (at least 2 m wide). Each must touch
  or cross another corridor or an entrance; where corridors overlap they simply join up, and only
  the new floor is paid for. Every room's door must open onto a corridor (or the waiting room).
- **Routes (R):** shows a dotted line from everyone who is walking to where they are going,
  patients in their triage colour and staff in their uniform colour. Whoever is selected always
  shows their route.
- **Rotate (Q / E):** turns the view a quarter turn, so the hospital can be seen from all four sides.
- **Extending the building:** drag out new floor on any side, onto grass or paving. Entrances, the
  car park and the ambulance bay must be kept clear.
- **Room panel:** in build mode, click a room's floor to see its details and who is using it, rename it, move or
  resize it (it keeps its name), or demolish it for a 50% refund. Rooms in use can't be moved or
  demolished, and the department always keeps at least one waiting room and one triage room.
- **Money:** a new hospital has £750,000 to build with, shown in the top bar.

### Money

The balance in the top bar is the hospital's money. Click it (or press **F**) for the finances:
today, yesterday and the last week by category, and what the hospital costs to run each day.

- **Income** is a simplified NHS tariff: each A&E attendance earns £450 (Red), £250 (Amber) or
  £130 (Green) when the patient leaves A&E, and each ward admission earns £1,200 plus £300 a night
  when the patient goes home. Each operation earns £2,500. Patients who leave without being seen
  earn nothing.
- **Running costs:** wages for every post (each covered around the clock, about 4.5 people per
  post), building upkeep of £0.60 per m² a day, and every test and medicine as it is used.
  Without a pharmacy, medicines cost 50% more.
- **Building work** is paid up front. If the money runs out, building stops until it comes back.

### Conditions

The game has 38 conditions, each with its own symptoms, observations, tests, treatment and
outcome. Doctors only see what a real doctor would: symptoms, observations and test results.

| Condition | After A&E |
|---|---|
| Acute appendicitis | Operation, then admit |
| Acute asthma | Discharge |
| Acute cholecystitis | Admit |
| Acute heart failure | Admit |
| Acute kidney injury | Admit |
| Acute myocardial infarction | Transfer |
| Acute pancreatitis | Admit |
| Allergic reaction (hives) | Discharge |
| Anaphylaxis | Discharge |
| Ankle fracture | Discharge |
| Ankle sprain | Discharge |
| Atrial fibrillation with fast heart rate | Admit |
| Biliary colic | Discharge |
| Cellulitis | Discharge |
| Community-acquired pneumonia | Admit |
| COPD exacerbation | Admit |
| Deep vein thrombosis | Discharge |
| Diabetic ketoacidosis | Admit |
| Ectopic pregnancy | Operation, then admit |
| Fractured neck of femur | Operation, then admit |
| Gastro-oesophageal reflux | Discharge |
| Gastroenteritis | Discharge |
| Hypoglycaemia | Discharge |
| Migraine | Discharge |
| Musculoskeletal chest pain | Discharge |
| Pulmonary embolism | Admit |
| Pyelonephritis | Admit |
| Renal colic | Discharge |
| Seizure in known epilepsy | Discharge |
| Simple laceration | Discharge |
| Stroke | Admit |
| Subarachnoid haemorrhage | Transfer |
| Supraventricular tachycardia | Discharge |
| Threatened miscarriage | Discharge |
| Tonsillitis | Discharge |
| Upper gastrointestinal bleed | Admit |
| Urinary tract infection | Discharge |
| Vasovagal syncope (faint) | Discharge |

"Admit" goes to a ward bed when there is one, otherwise to another hospital. "Operation" goes to
theatre and then a ward bed; without a theatre the patient is transferred. "Transfer" goes to a
specialist centre elsewhere (heart attack centre, neurosurgery).

### Wards

Build a **Medical ward** (Build → Wards) to admit patients who need more than a few hours of care,
such as pneumonia. Drag it out like any room: beds go in a row along the wall opposite the door,
2.5 m apart, each with a bedside locker. From **7.5 m deep** a second row faces the first across
the aisle, so a longer or deeper ward holds more beds. The build menu shows how many beds each size
gives.

- **Ward team:** a new ward comes empty. Recruitment recommends one nurse for every 6 beds and a
  doctor; hire them onto the ward and they look after that ward's patients. Until a ward has its
  own nurse or doctor, the A&E team covers it. If the ward is demolished, its staff move to A&E.
- **Admission:** when an A&E doctor decides a patient needs admitting, the patient waits in their
  cubicle until a bed is free, then a nurse takes them up. Leaving A&E for the ward stops the
  4-hour clock. A full ward blocks A&E cubicles, as in a real hospital; the A&E board shows ward
  beds and anyone waiting for one. After 6 hours without a bed, or if there is no ward at all, the
  patient is transferred to a neighbouring hospital.
- **On the ward:** the ward doctor clerks each admission (and may spot and correct a wrong A&E
  diagnosis), nurses do observations, and a ward round decides when the patient is well enough to
  go home. Each condition sets its usual length of stay (`length_of_stay_hours`).
- A ward with a booked bed or a patient in it can't be moved or demolished.

### Imaging, theatres and pharmacy

Build these from **Build → Services**. Each room opens once you hire someone to work in it (see
**Staff and recruitment**); if it is demolished, they wait for a new room of the same kind.

- **X-ray room** (radiographer): chest X-rays and X-rays of injured limbs. **CT scanner** (CT
  radiographer): CT head and CT pulmonary angiogram. The patient is taken from their cubicle to the
  scanner and back, and a radiologist reports the scan after a short wait. Without the room, doctors
  diagnose without that scan: a broken ankle or a bleed on the brain is much easier to miss.
- **Operating theatre** (consultant surgeon): appendicectomy, ectopic pregnancy and hip fracture
  operations. The patient waits in their cubicle for theatre, has the operation, then needs a ward
  bed to recover in. If the A&E diagnosis was wrong, the surgeon finds out in theatre.
- **Pharmacy** (pharmacist): buys medicines at list price and dispenses ward patients' medicines to
  take home. Without one, medicines cost 50% more and every ward discharge waits about 4 hours for
  medicines from an outside pharmacy.

The simulation works with whatever you build: any number of cubicles, triage rooms, offices and
staff rooms.

### Starting a new game

**New Game** asks how to start:

- **Small A&E with a team:** cubicles, resus, triage, offices and a starting team of 9 (a receptionist,
  4 nurses, 2 doctors and 2 porters). Patients arrive straight away.
- **Empty building:** just the building, the roads and a waiting room with reception, plus the value
  of the starter rooms in the bank. You build and hire everything. A&E stays closed to new patients
  (the A&E board says what it still needs) until it has a triage room, a cubicle or resus bay, an A&E
  nurse and an A&E doctor. It also closes if you take any of these away.

### Staff and recruitment

- **Grades:** nurses are Band 5, 6, 7, 8 or 9 by how good they are (Band 7 and above wear navy);
  doctors are Junior Doctor, Doctor, Registrar or Consultant. Radiographers, surgeons, pharmacists,
  porters and receptionists have one grade each. Any nurse or doctor can work in A&E or on any ward.
- **Recruitment** (H) lists eight candidates, refreshed every morning at 08:00, with their grade,
  skill, experience, pace and salary. Choose where each will work and press Hire. Rooms never come
  with staff: everyone is hired by you.
- **Recommended team:** the same panel shows, for A&E, porters and each room, how many of each kind
  of staff it recommends and how many you have. It's a guide only.
- **Letting staff go:** open their card and press Let go twice. Whatever they were doing goes back
  on the board for someone else.

### Moving patients

- Patients who can walk, walk. Patients who can't wait where they are until a **porter** pushes
  them: on a **trolley** if they are Red, are going to theatre, or came by ambulance and aren't fit
  to sit; in a **wheelchair** if they came by ambulance and are fit to sit (Green), are Amber and
  aged 75 or over, or are being admitted. Everyone walks out when they go home.
- Ambulance crews wheel their own patients in from the bay on their stretcher.
- The game starts with two porters. With none, a nurse does the pushing; a nurse also steps in
  if a move has waited 10 minutes for a porter.

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
- `disposition` is `discharge`, `transfer`, `admit` or `surgery`, with a note shown in the record.
  `surgery` also needs `"operation": { "name": "Laparoscopic appendicectomy", "minutes": 60 }`.

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
`turnaround_minutes`. Imaging tests also name their `room` (`x_ray` or `ct`): they are done there
by a radiographer and reported after `turnaround_minutes`, and can't be ordered without that room. `applies_to_sex` can restrict a test (e.g. pregnancy test: `female`).

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

### Room templates and furniture

- `Furniture/` lists each item with its real size (`width_m`, `depth_m`, `height_m`), how it is
  drawn (`box`, `chair`, `bed` or `monitor`), colour, cost and whether it is medical equipment.
- `RoomTemplates/` lists each room: `kind` (what the simulation uses it for), `layout` (how it is
  furnished: `bed_space`, `triage`, `waiting`, `office`, `staff_room` or `none` for corridors),
  example `sizes` (shown in the menu), minimum and maximum width and length in metres, door width, costs, staffing text
  and the furniture it may use. Loading checks every furniture id exists and that each layout has
  the items it needs.

### Adding a new content type (for developers)

1. Add a model class in `src/BlueprintHospital.Simulation/Data/Models/` implementing `IDataRecord`,
   with a `Validate` method that checks each field.
2. Add a folder constant and a `RecordLoader.LoadFolder<T>` call in `GameDatabase.Load`, plus any
   cross-reference checks.
3. Create the folder under `game/Data/` and add tests in `tests/`.

## Measurements

All sizes are stored in metres. One grid tile is **0.5 m × 0.5 m** (`Units.MetresPerTile`).
The starter hospital building is 30 m × 20 m on an 80 m × 60 m site.
