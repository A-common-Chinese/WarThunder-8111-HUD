// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 A-common-Chinese
// This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3.0 or later.

namespace WtHud.Hud;

/// <summary>
/// Pixel/degree calibration. The game does not expose its camera FOV, so the
/// scale has to come from the two-point hotkey calibration (F7 zero, F8 gain)
/// or from a manually entered FOV.
/// </summary>
public sealed class HudCalibration
{
    /// <summary>Pixels per degree of view angle. 0 means "derive from field of view".</summary>
    public double PixelsPerDegree { get; set; }

    /// <summary>Vertical field of view in degrees, used when PixelsPerDegree is 0.</summary>
    public double VerticalFovDeg { get; set; } = 45.0;

    /// <summary>Boresight offset from the screen centre, in pixels (positive = down/right).</summary>
    /// <summary>Viewport height used for the degrees-to-pixels scale (0 = full height).</summary>
    public double PxReferenceHeight { get; set; }

    public double CenterOffsetX { get; set; }
    public double CenterOffsetY { get; set; }

    /// <summary>
    /// Moves the ENTIRE HUD (attitude group, tapes, heading tape, data block) up by this
    /// many degrees of view angle; positive = up. This is a pure layout knob, tuned live
    /// with Ctrl+Alt+Up/Down and then baked as the default.
    ///
    /// Note: shifting the HUD away from the camera axis means the ladder no longer lines
    /// up with the real horizon; the F7 zero-calibration is the knob for that, and it is
    /// separate from this placement offset.
    /// </summary>
    public double LayoutOffsetYDeg { get; set; } = 1.8;

    /// <summary>
    /// How much of the aircraft's roll the camera actually performs.
    ///
    /// Measured in War Thunder's third-person chase view: the camera keeps the world
    /// "up" (horizon stays level) while the aircraft visibly rolls, so the correct value
    /// for that camera is 0. A cockpit view with a 1:1 camera would need 1.0.
    /// </summary>
    public double RollCoupling { get; set; } = 0.0;

    /// <summary>Vertical field of view implied by the current calibration.</summary>
    public double EffectivePxPerDeg(int viewportHeight) =>
        PixelsPerDegree > 0.01
            ? PixelsPerDegree
            : (PxReferenceHeight > 0 ? PxReferenceHeight : viewportHeight) / Math.Max(5.0, VerticalFovDeg);
}



