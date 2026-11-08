// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 A-common-Chinese
// This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3 or later.

using WtHud.UI.Mfd.Controls;

namespace WtHud.UI.Mfd.Pages;

/// <summary>
/// Second-level page that samples attitude for a few seconds and derives the pitch sign.
/// Fully self-contained: it owns its buttons, its repaint timer and its result handling.
/// </summary>
internal sealed class PitchDetectPage : MfdPageBase
{
    private readonly Action _onFinished;         // form: leave page, show toast
    private readonly Action _onCancelled;
    private System.Windows.Forms.Timer? _timer;

    public PitchDetectPage(Action onFinished, Action onCancelled) : base("pitchdetect")
    {
        _onFinished = onFinished;
        _onCancelled = onCancelled;
    }

    public override string TitleKey => "row.pitchDetect";
    public override string HintKey => "pitchdetect.hint";

    public string? ResultToast { get; private set; }

    public override void Build()
    {
        Buttons.Clear();
        Buttons.Add(new VirtualButton("pitchdetect.finish", VirtualButtonKind.Action)
        {
            Slot = 0,
            Text = () => L10n.Get(LangScope.Screen, "confirm.ok"),
            Value = () => WtHud.Hud.PitchDetect.SampleCount.ToString(),
        });
        Buttons.Add(new VirtualButton("pitchdetect.cancel", VirtualButtonKind.Action)
        {
            Slot = 3,
            Text = () => L10n.Get(LangScope.Screen, "confirm.cancel"),
        });
    }

    public void StartRepaint(Action invalidate)
    {
        _timer ??= new System.Windows.Forms.Timer { Interval = 120 };
        _timer.Tick += (_, _) => invalidate();
        _timer.Start();
    }

    public void StopRepaint() => _timer?.Stop();

    public override bool Handle(MfdCommand cmd)
    {
        switch (cmd.Id)
        {
            case "confirm.ok":
            case "pitchdetect.finish":
                Finish();
                _onFinished();
                return true;
            case "nav.back":
            case "confirm.cancel":
            case "pitchdetect.cancel":
                WtHud.Hud.PitchDetect.Abort();
                StopRepaint();
                _onCancelled();
                return true;
        }
        return false;
    }

    private void Finish()
    {
        StopRepaint();
        var ok = WtHud.Hud.PitchDetect.Finish();
        if (ok)
        {
            var veh = WtHud.Hud.AirframeDb.CurrentVehicle;
            if (!string.IsNullOrEmpty(veh))
            {
                WtHud.Hud.PitchDetect.SaveFor(veh);
                ResultToast = L10n.Get(LangScope.Screen, "pitchdetect.ok") + " " +
                              (WtHud.Hud.PitchDetect.Sign > 0 ? "+1" : "-1") +
                              $" ({WtHud.Hud.PitchDetect.Confidence:F2})";
            }
        }
        else
        {
            ResultToast = L10n.Get(LangScope.Screen, "pitchdetect.fail");
        }
    }

    public override void Draw(MfdRenderContext ctx)
    {
        var g = ctx.G;
        var s = ctx.Scale;
        using var head = MfdTheme.CjkAt(22f * s);
        using var body = MfdTheme.MonoAt(19f * s);
        using var text = new SolidBrush(MfdTheme.Text);
        using var accent = new SolidBrush(MfdTheme.Green);

        var x = ctx.Glass.Left + ctx.Pad;
        var y = ctx.Glass.Top + ctx.Glass.Height * 0.30f;

        g.DrawString(L10n.Get(LangScope.Screen, "pitchdetect.running"), head, text, x, y);
        y += 46 * s;

        g.DrawString($"{WtHud.Hud.PitchDetect.Seconds:F1}s   " +
                     $"{L10n.Get(LangScope.Screen, "pitchdetect.samples")} {WtHud.Hud.PitchDetect.SampleCount}",
                     body, accent, x, y);
        y += 46 * s;

        g.DrawString("CONFIRM = " + L10n.Get(LangScope.Screen, "pitchdetect.ok"), body, text, x, y);
    }
}
