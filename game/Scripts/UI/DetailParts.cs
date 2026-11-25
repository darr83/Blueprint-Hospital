using System;
using System.Collections.Generic;
using BlueprintHospital.Simulation.Medical;
using Godot;

namespace BlueprintHospital.UI;

/// <summary>
/// One clickable line in a list (a bed on a ward, a member of staff, a clinic patient): a small badge on the
/// left, a name and a grey line under it, then an optional coloured tag and a NEWS2 square on the right.
/// Rows are kept and filled in again each refresh, so the list doesn't flicker under the mouse.
/// </summary>
public partial class DetailRow : Button
{
    private readonly PanelContainer _lead = new() { CustomMinimumSize = new Vector2(38, 38), MouseFilter = MouseFilterEnum.Ignore, SizeFlagsVertical = SizeFlags.ShrinkCenter };
    private readonly Label _leadText = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
    private readonly Label _title = new() { TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, MouseFilter = MouseFilterEnum.Ignore };
    private readonly Label _subtitle = new() { TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, MouseFilter = MouseFilterEnum.Ignore };
    private readonly PanelContainer _tag = new() { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsVertical = SizeFlags.ShrinkCenter };
    private readonly Label _tagText = new() { MouseFilter = MouseFilterEnum.Ignore };
    private readonly PanelContainer _badge = new() { CustomMinimumSize = new Vector2(30, 26), MouseFilter = MouseFilterEnum.Ignore, SizeFlagsVertical = SizeFlags.ShrinkCenter };
    private readonly Label _badgeText = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
    private Action? _onClick;

    public DetailRow()
    {
        FocusMode = FocusModeEnum.None;
        CustomMinimumSize = new Vector2(0, 54);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        ClipContents = true;
        foreach (var (state, colour, border) in new[] { ("normal", UiStyle.Surface, UiStyle.Hairline), ("hover", new Color("#F5F8FC"), new Color("#C9D8EC")), ("pressed", UiStyle.BlueTint, new Color("#C9D8EC")), ("disabled", UiStyle.Surface, UiStyle.Hairline), ("focus", UiStyle.Surface, UiStyle.Hairline) })
        {
            AddThemeStyleboxOverride(state, UiStyle.Box(colour, UiStyle.RadiusCard, border));
        }

        Pressed += () => _onClick?.Invoke();

        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.SetAnchorsPreset(LayoutPreset.FullRect);
        row.OffsetLeft = 9;
        row.OffsetRight = -9;
        row.AddThemeConstantOverride("separation", 9);
        AddChild(row);

        _leadText.AddThemeFontSizeOverride("font_size", 12);
        UiStyle.Strong(_leadText);
        _lead.AddChild(_leadText);
        row.AddChild(_lead);

        var texts = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter, MouseFilter = MouseFilterEnum.Ignore };
        texts.AddThemeConstantOverride("separation", 0);
        UiStyle.Strong(_title, UiStyle.SizeBody);
        _title.AddThemeColorOverride("font_color", UiStyle.TextDark);
        texts.AddChild(_title);
        _subtitle.AddThemeFontSizeOverride("font_size", UiStyle.SizeCaption);
        _subtitle.AddThemeColorOverride("font_color", UiStyle.TextMuted);
        texts.AddChild(_subtitle);
        row.AddChild(texts);

        _tagText.AddThemeFontSizeOverride("font_size", 11);
        UiStyle.Strong(_tagText);
        _tag.AddChild(_tagText);
        row.AddChild(_tag);

        UiStyle.Strong(_badgeText, 13);
        _badge.AddChild(_badgeText);
        row.AddChild(_badge);
    }

    /// <summary>Fills the row in. <paramref name="lead"/> is the badge text; round makes it a circle (people) rather than a square (beds, times).</summary>
    public void Set(string lead, Color leadColour, bool round, string title, string subtitle, (string Text, Color Back, Color Ink)? tag, (string Text, Color Back, Color Ink)? badge, Action? onClick, string? tooltip = null, bool dashed = false)
    {
        _leadText.Text = lead;
        _leadText.AddThemeColorOverride("font_color", leadColour.Luminance > 0.6f ? UiStyle.TextDark : Colors.White);
        _lead.AddThemeStyleboxOverride("panel", UiStyle.Box(leadColour, round ? 19 : 9));
        _title.Text = title;
        _title.AddThemeColorOverride("font_color", dashed ? UiStyle.TextMuted : UiStyle.TextDark);
        _subtitle.Text = subtitle;
        _subtitle.Visible = subtitle.Length > 0;
        _tag.Visible = tag is not null;
        if (tag is { } t)
        {
            _tagText.Text = t.Text;
            _tagText.AddThemeColorOverride("font_color", t.Ink);
            var box = UiStyle.Box(t.Back, UiStyle.RadiusChip);
            box.ContentMarginLeft = box.ContentMarginRight = 7;
            box.ContentMarginTop = box.ContentMarginBottom = 2;
            _tag.AddThemeStyleboxOverride("panel", box);
        }

        _badge.Visible = badge is not null;
        if (badge is { } b)
        {
            _badgeText.Text = b.Text;
            _badgeText.AddThemeColorOverride("font_color", b.Ink);
            _badge.AddThemeStyleboxOverride("panel", UiStyle.Box(b.Back, 7));
        }

        _onClick = onClick;
        Disabled = onClick is null;
        MouseDefaultCursorShape = onClick is null ? CursorShape.Arrow : CursorShape.PointingHand;
        TooltipText = tooltip ?? string.Empty;
        SelfModulate = dashed ? new Color(1, 1, 1, 0.75f) : Colors.White;
    }
}

/// <summary>A list of <see cref="DetailRow"/>s that keeps its rows between refreshes.</summary>
public sealed class DetailRows
{
    private readonly VBoxContainer _box;
    private readonly List<DetailRow> _rows = new();
    private int _used;

    public DetailRows(VBoxContainer box) => _box = box;

    public void Begin() => _used = 0;

    public DetailRow Next()
    {
        if (_used == _rows.Count)
        {
            var row = new DetailRow();
            _rows.Add(row);
            _box.AddChild(row);
        }

        var next = _rows[_used++];
        next.Visible = true;
        return next;
    }

    /// <summary>Hides the rows not used this time.</summary>
    public void End()
    {
        for (var i = _used; i < _rows.Count; i++)
        {
            _rows[i].Visible = false;
        }
    }
}

/// <summary>A strip with one rounded block per bed, each coloured by the patient's NEWS2 band (grey when empty).</summary>
public partial class BedStrip : Control
{
    private Color[] _colours = Array.Empty<Color>();

    public BedStrip()
    {
        CustomMinimumSize = new Vector2(0, 12);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public void Set(IReadOnlyList<Color> colours)
    {
        var same = colours.Count == _colours.Length;
        for (var i = 0; same && i < colours.Count; i++)
        {
            same = colours[i] == _colours[i];
        }

        if (same)
        {
            return;
        }

        _colours = new Color[colours.Count];
        for (var i = 0; i < colours.Count; i++)
        {
            _colours[i] = colours[i];
        }

        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_colours.Length == 0)
        {
            return;
        }

        var gap = _colours.Length > 20 ? 2f : 4f;
        var w = (Size.X - (gap * (_colours.Length - 1))) / _colours.Length;
        for (var i = 0; i < _colours.Length; i++)
        {
            DrawStyleBox(UiStyle.Box(_colours[i], 3), new Rect2(i * (w + gap), 0, Math.Max(2, w), Size.Y));
        }
    }
}

/// <summary>Colours shared by the detail panels.</summary>
public static class DetailColours
{
    public static readonly Color Red = new("#E53935");
    public static readonly Color Amber = new("#F9A825");
    public static readonly Color Green = new("#43A047");
    public static readonly Color Empty = new("#E3E8ED");
    public static readonly Color Purple = new("#6B4FBB");
    public static readonly Color PurpleTint = new("#EFEBFA");
    public static readonly Color AmberInk = new("#2B1D00");

    /// <summary>The NEWS2 square for a patient: the monitor's colour band, with the score in it.</summary>
    public static (string Text, Color Back, Color Ink) News2Badge(int total, TriageCategory? band) => band switch
    {
        TriageCategory.Red => (total.ToString(), Red, Colors.White),
        TriageCategory.Amber => (total.ToString(), Amber, AmberInk),
        TriageCategory.Green => (total.ToString(), Green, Colors.White),
        _ => ("?", Empty, UiStyle.TextMuted),
    };

    public static Color Band(TriageCategory? band) => band switch
    {
        TriageCategory.Red => Red,
        TriageCategory.Amber => Amber,
        TriageCategory.Green => Green,
        _ => new Color("#B9C4CE"),
    };

    public static (string, Color, Color) Tag(string text, Color back, Color ink) => (text, back, ink);

    public static (string, Color, Color) Blue(string text) => (text, UiStyle.BlueTint, UiStyle.Blue);

    public static (string, Color, Color) GreenTag(string text) => (text, UiStyle.GreenTint, UiStyle.Green);

    public static (string, Color, Color) AmberTag(string text) => (text, UiStyle.AmberTint, UiStyle.AmberText);

    public static (string, Color, Color) RedTag(string text) => (text, UiStyle.RedTint, UiStyle.Red);

    public static (string, Color, Color) PurpleTag(string text) => (text, PurpleTint, Purple);

    public static (string, Color, Color) GreyTag(string text) => (text, UiStyle.Ground, UiStyle.TextMuted);
}
