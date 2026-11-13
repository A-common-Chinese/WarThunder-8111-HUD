// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 A-common-Chinese
// This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3 or later.

namespace WtHud.Hud;

/// <summary>
/// 一份「编队配置」——能在机器之间传的最小集合。
/// <para>不含按键、显示、面板、节点 ID：那些是每台机器自己的事，传过去只会互相打架。</para>
/// <para>★ 不含「我是长机还是僚机」：长机把自己的配置广播给僚机时，僚机不该因此变成长机。
/// <see cref="SenderLead"/> 只用于两件事：确认页上标明来源角色、以及不把长机的编号套给对方。</para>
/// </summary>
public readonly record struct FormationProfile(
    bool SenderLead, int Slot, string Shape, int Side,
    double LateralM, double BackM, double UpM, double SpacingM,
    double DonePct, double HoldS, double DropPct,
    bool MapOnly, bool Assist, string LeadIcon)
{
    /// <summary>把当前配置抽成一份可传输的档案。</summary>
    public static FormationProfile From(HudConfig c)
    {
        var shape = FormationGeometry.IsKnownShape(c.FormationShape)
            ? c.FormationShape : FormationGeometry.ShapeCustom;
        return new FormationProfile(
            c.LeadMode,
            Math.Clamp(c.FormationSlot, 0, FormationGeometry.MaxSlots - 1),
            shape,
            Math.Clamp(c.FormationWingSide, -1, 1),
            c.FormationWingOffsetM, c.FormationStationBackM, c.FormationUpM,
            FormationGeometry.Spacing(c, shape),
            c.FormationDonePct, c.FormationDoneHoldS, c.FormationDropPct,
            c.FormationMapOnly, c.FormationAssist, c.LeadIcon ?? "");
    }

    /// <summary>
    /// 写进配置。语义与面板「套预设」一致：本来在自定义就先存快照，再覆盖四个数值。
    /// <para>★ 只改草稿，落盘仍然靠 SAVE —— 别人发一包就替你把正在飞的设置写进磁盘，是另一种事故。</para>
    /// </summary>
    public void ApplyTo(HudConfig c)
    {
        if (c.FormationShape == FormationGeometry.ShapeCustom) c.SnapshotCustom();

        c.FormationShape = FormationGeometry.IsKnownShape(Shape) ? Shape : FormationGeometry.ShapeCustom;
        // 长机的编号没有几何含义：把它套到僚机头上会跟 1 号僚机撞位
        if (!SenderLead) c.FormationSlot = Math.Clamp(Slot, 0, FormationGeometry.MaxSlots - 1);
        c.FormationWingSide = Math.Clamp(Side, -1, 1);
        c.FormationWingOffsetM = Math.Max(0, LateralM);
        c.FormationStationBackM = Math.Max(10, BackM);
        c.FormationUpM = UpM;
        if (c.FormationShape != FormationGeometry.ShapeCustom)
            FormationGeometry.SetSpacing(c, c.FormationShape, Math.Max(10, SpacingM));
        c.FormationDonePct = Math.Clamp(DonePct, 1, 50);
        c.FormationDoneHoldS = Math.Clamp(HoldS, 0.5, 10);
        c.FormationDropPct = Math.Clamp(DropPct, 5, 100);
        c.FormationMapOnly = MapOnly;
        c.FormationAssist = Assist;
        c.LeadIcon = LeadIcon ?? "";
    }
}
