using Godot;

namespace BlueprintHospital.UI;

/// <summary>Line icons for the left-hand navigation, drawn in code so they stay crisp at any size.</summary>
public partial class NavIcon : Control
{
    public enum Kind
    {
        Hospital,
        Build,
        Patients,
        Staff,
        Hire,
        Finances,
        Routes,
        Grid,
    }

    private readonly Kind _kind;
    private readonly BaseButton _owner;

    public NavIcon(Kind kind, BaseButton owner)
    {
        _kind = kind;
        _owner = owner;
        CustomMinimumSize = new Vector2(24, 24);
        Size = CustomMinimumSize;
        MouseFilter = MouseFilterEnum.Ignore;
        owner.Toggled += _ => QueueRedraw();
        owner.MouseEntered += QueueRedraw;
        owner.MouseExited += QueueRedraw;
    }

    public override void _Draw()
    {
        var c = _owner.ButtonPressed ? UiStyle.Blue : UiStyle.TextMuted;
        const float w = 2f;
        void L(float x1, float y1, float x2, float y2) => DrawLine(new Vector2(x1, y1), new Vector2(x2, y2), c, w, true);
        void Box(float x, float y, float bw, float bh) => DrawRect(new Rect2(x, y, bw, bh), c, false, w);

        switch (_kind)
        {
            case Kind.Hospital:
                DrawPolyline(new[] { new Vector2(5, 21), new Vector2(5, 8), new Vector2(12, 4), new Vector2(19, 8), new Vector2(19, 21) }, c, w, true);
                L(3, 21, 21, 21);
                L(9, 12, 15, 12);
                L(12, 9, 12, 15);
                break;
            case Kind.Build:
                Box(3, 3, 8, 8);
                Box(13, 3, 8, 8);
                Box(3, 13, 8, 8);
                L(17, 13, 17, 21);
                L(13, 17, 21, 17);
                break;
            case Kind.Patients:
                DrawArc(new Vector2(12, 7), 4, 0, Mathf.Tau, 24, c, w, true);
                DrawArc(new Vector2(12, 22), 8, Mathf.Pi, Mathf.Tau, 24, c, w, true);
                break;
            case Kind.Staff:
                DrawRect(new Rect2(4, 3, 16, 18), c, false, w);
                DrawArc(new Vector2(12, 10), 3, 0, Mathf.Tau, 20, c, w, true);
                L(8, 17, 16, 17);
                break;
            case Kind.Hire:
                // A person with a plus beside them.
                DrawArc(new Vector2(9, 8), 3.5f, 0, Mathf.Tau, 20, c, w, true);
                DrawArc(new Vector2(9, 21), 7, Mathf.Pi, Mathf.Tau, 20, c, w, true);
                L(19, 6, 19, 14);
                L(15, 10, 23, 10);
                break;
            case Kind.Finances:
                L(4, 20, 4, 10);
                L(10, 20, 10, 4);
                L(16, 20, 16, 13);
                L(2, 20, 22, 20);
                break;
            case Kind.Routes:
                for (var i = 0; i < 5; i++)
                {
                    var t0 = i / 5f;
                    var t1 = t0 + 0.12f;
                    L(Mathf.Lerp(4, 20, t0), Mathf.Lerp(20, 5, t0), Mathf.Lerp(4, 20, t1), Mathf.Lerp(20, 5, t1));
                }

                DrawArc(new Vector2(20, 5), 2.5f, 0, Mathf.Tau, 16, c, w, true);
                break;
            case Kind.Grid:
                L(3, 9, 21, 9);
                L(3, 15, 21, 15);
                L(9, 3, 9, 21);
                L(15, 3, 15, 21);
                break;
        }
    }
}
