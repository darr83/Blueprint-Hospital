using System;
using System.Linq;
using BlueprintHospital.Hospital;
using BlueprintHospital.Simulation.Core;
using BlueprintHospital.World3D;
using Godot;

namespace BlueprintHospital.Core;

/// <summary>
/// Review shots of the Blender-made models, run with: godot --path game -- --model-shots=DIR (needs a display).
/// Stands every model in res://Art/Models in a row on a plain floor, with today's code-built bed at
/// the end for scale, and photographs the row from all four sides.
/// </summary>
public partial class ModelShots : Node
{
    private static readonly (double X, double Y) Centre = (16, 9);

    private string _directory = "";
    private int _frame;
    private int _shot;
    private int _count;
    private HospitalScene? _hospital;

    public static bool Begin()
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--model-shots=", StringComparison.Ordinal))
            {
                GameManager.Instance.AddChild(new ModelShots { Name = "ModelShots", ProcessMode = ProcessModeEnum.Always, _directory = arg["--model-shots=".Length..] });
                Callable.From(() => GameManager.Instance.StartNewGame("Model Review General")).CallDeferred();
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
                h.Hud.Visible = false;
                if (h.GetNodeOrNull<CanvasItem>("TileCursor") is { } cursor)
                {
                    cursor.Visible = false;
                }

                h.Session.Clock.SetSpeed(SimulationSpeed.Paused);
                Build(h);
                _frame = 0;
            }

            return;
        }

        if (_frame % 8 != 0)
        {
            return;
        }

        var view = _shot / 2;
        if (view >= 4)
        {
            GD.Print("[models] Done.");
            GetTree().Quit(0);
            return;
        }

        if (_shot % 2 == 0)
        {
            _hospital.RotateView(view - IsoDraw.Rotation);
            _hospital.Camera.SnapTo(IsoDraw.ToScreen(Centre.X + (_count * 1.5), Centre.Y - (_count * 1.5)), 3f);
        }
        else
        {
            DirAccess.MakeDirRecursiveAbsolute(_directory);
            GetViewport().GetTexture().GetImage().SavePng($"{_directory}/models_{view}.png");
            GD.Print($"[models] Saved view {view}");
        }

        _shot++;
    }

    private void Build(HospitalScene h)
    {
        var root = h.View3D.Root;
        // Models run up the screen's right-hand diagonal, three tiles apart; part kits are left out.
        var names = Models3D.All().Where(n => !Models3D.IsKit(n)).ToList();
        _count = names.Count;
        for (var i = 0; i < names.Count; i++)
        {
            var holder = new Node3D { Position = Kit3D.At(Centre.X + (i * 3), Centre.Y - (i * 3)) };
            var model = Models3D.Make(names[i])!;
            holder.AddChild(model);
            GD.Print($"[models] {names[i]}: {Models3D.Triangles(model)} triangles");
            root.AddChild(holder);
        }

        var span = 14 + (names.Count * 3);
        root.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(span, 0.02f, span) },
            Position = Kit3D.At(Centre.X + (names.Count * 1.5), Centre.Y - (names.Count * 1.5)),
            MaterialOverride = Kit3D.Mat(new Color("#D9D4C7")),
        });
    }
}
