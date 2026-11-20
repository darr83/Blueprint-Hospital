using System;
using System.Collections.Generic;
using System.Linq;
using BlueprintHospital.Hospital;
using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Data.Models;
using BlueprintHospital.Simulation.Medical;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.UI.Results;
using Godot;

namespace BlueprintHospital.Core;

/// <summary>
/// Review shots of test result documents, run with: godot --path game -- --result-shots=DIR [--3d] (needs a display).
/// Gives a few patients finished tests, then photographs a patient record and each result opened from it.
/// </summary>
public partial class ResultShots : Node
{
    private string _directory = "";
    private int _frame;
    private int _shot;
    private HospitalScene? _hospital;
    private readonly List<(string Name, Patient Patient, InvestigationOrder? Order)> _plan = new();

    public static bool Begin()
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--result-shots=", StringComparison.Ordinal))
            {
                GameManager.Instance.Settings.View3D = Array.IndexOf(OS.GetCmdlineUserArgs(), "--3d") >= 0;
                var shots = new ResultShots { Name = "ResultShots", ProcessMode = ProcessModeEnum.Always, _directory = arg["--result-shots=".Length..] };
                GameManager.Instance.AddChild(shots);
                Callable.From(() => GameManager.Instance.StartNewGame("St Aldric's General")).CallDeferred();
                return true;
            }
        }

        return false;
    }

    public override void _Process(double delta)
    {
        _frame++;
        if (_hospital is null)
        {
            if (GetTree().CurrentScene is HospitalScene h && h.IsNodeReady())
            {
                _hospital = h;
                h.Session.Clock.SetSpeed(SimulationSpeed.Paused);
                Prepare(h.Session.Simulation);
                _frame = 0;
            }

            return;
        }

        if (_frame < 20 || _frame % 12 != 0)
        {
            return;
        }

        var index = _shot / 2;
        if (index >= _plan.Count)
        {
            GD.Print("[results] Done.");
            GetTree().Quit(0);
            return;
        }

        var (name, patient, order) = _plan[index];
        if (_shot % 2 == 0)
        {
            ResultViewer.Current?.Close();
            _hospital.SelectPatient(patient, centre: true);
            if (order is not null)
            {
                ResultViewer.Open(_hospital.Hud, _hospital.Session.Simulation.Database, patient, order, _hospital.Session.HospitalName);
            }
        }
        else
        {
            var image = GetViewport().GetTexture().GetImage();
            DirAccess.MakeDirRecursiveAbsolute(_directory);
            var path = $"{_directory}/{name}.png";
            image.SavePng(path);
            GD.Print($"[results] Saved {path}");
        }

        _shot++;
    }

    private void Prepare(HospitalSimulation sim)
    {
        var db = sim.Database;
        InvestigationOrder Add(Patient p, string testId, bool abnormal, int ago)
        {
            var test = db.Investigations[testId];
            ResultTemplate template = abnormal ? test.Abnormal.First(a => a.Condition == p.Condition.Id) : test.Normal;
            double? value = template.ValueRange is { } r ? Math.Round((r[0] + r[1]) / 2, 1) : null;
            var text = template.Summaries[0];
            if (value is { } v)
            {
                text = text.Replace("{value}", InvestigationSystem.FormatValue(v), StringComparison.Ordinal);
            }

            var order = new InvestigationOrder
            {
                InvestigationId = testId,
                RequestedAt = sim.Now.AddMinutes(-ago - 60),
                Status = InvestigationStatus.Resulted,
                ResultedAt = sim.Now.AddMinutes(-ago),
                Abnormal = abnormal,
                Value = value,
                Summary = text,
            };
            p.Investigations.Add(order);
            return order;
        }

        var chest = sim.SpawnPatient(ArrivalMethod.WalkIn, "community_acquired_pneumonia");
        var fbc = Add(chest, "full_blood_count", true, 50);
        Add(chest, "c_reactive_protein", true, 48);
        var ue = Add(chest, "urea_electrolytes", false, 47);
        var cxr = Add(chest, "chest_xray", true, 30);
        var ecgNormal = Add(chest, "ecg", false, 70);

        var ankle = sim.SpawnPatient(ArrivalMethod.WalkIn, "ankle_fracture");
        var ankleXray = Add(ankle, "limb_xray", true, 20);

        var heart = sim.SpawnPatient(ArrivalMethod.WalkIn, "acute_myocardial_infarction");
        var stemi = Add(heart, "ecg", true, 15);
        Add(heart, "troponin", true, 5);

        var head = sim.SpawnPatient(ArrivalMethod.WalkIn, "subarachnoid_haemorrhage");
        var cth = Add(head, "ct_head", true, 10);

        var ectopic = sim.SpawnPatient(ArrivalMethod.WalkIn, "ectopic_pregnancy");
        var preg = Add(ectopic, "pregnancy_test", true, 40);
        var scan = Add(ectopic, "pelvic_ultrasound", true, 12);

        _plan.Add(("01_record", chest, null));
        _plan.Add(("02_fbc_sheet", chest, fbc));
        _plan.Add(("03_ue_normal_sheet", chest, ue));
        _plan.Add(("04_chest_xray", chest, cxr));
        _plan.Add(("05_ecg_normal", chest, ecgNormal));
        _plan.Add(("06_ankle_xray", ankle, ankleXray));
        _plan.Add(("07_ecg_stemi", heart, stemi));
        _plan.Add(("08_ct_head", head, cth));
        _plan.Add(("09_pregnancy_test", ectopic, preg));
        _plan.Add(("10_pelvic_ultrasound", ectopic, scan));
    }
}
