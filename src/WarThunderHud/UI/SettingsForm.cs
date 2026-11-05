// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 A-common-Chinese
// This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3.0 or later.

using System.Windows.Forms;
using WtHud.Hud;

namespace WtHud.UI;

/// <summary>
/// Small settings window (F1). Lives on its own STA thread because the overlay itself is a
/// click-through fullscreen window with a custom message loop.
/// </summary>
public sealed class SettingsForm : Form
{
    private readonly CheckBox _extrapolate = new() { Text = "启用外推（仅速度/高度，姿态始终用原始数据）", AutoSize = true };
    private readonly CheckBox _visible = new() { Text = "显示 HUD（等同 F10）", AutoSize = true };
    private readonly CheckBox _bodyFixed = new() { Text = "纯机体固连模式（等同 F9）", AutoSize = true };
    private readonly CheckBox _rollCoupling = new() { Text = "相机随滚转转动（第三人称请勿勾选）", AutoSize = true };
    private readonly CheckBox _bankFlip = new() { Text = "反转坡度方向", AutoSize = true };
    private readonly CheckBox _hideGround = new() { Text = "停在地面时隐藏 HUD", AutoSize = true };
    private readonly NumericUpDown _groundDelay = new() { Minimum = 0, Maximum = 3000, DecimalPlaces = 0, Increment = 100, Width = 80 };
    private readonly CheckBox _hideCockpit = new() { Text = "座舱视角隐藏 HUD（按一次 V）", AutoSize = true };
    private readonly CheckBox _hideNose = new() { Text = "机头视角隐藏 HUD（按两次 V）", AutoSize = true };
    private readonly TextBox _viewKey = new() { Width = 90 };
    private readonly TextBox _zoomKey = new() { Width = 90 };
    private readonly TextBox _mapKey = new() { Width = 90 };
    private readonly TextBox _scoreKey = new() { Width = 90 };
    private readonly TextBox _holdKey = new() { Width = 90 };
    private readonly TextBox _resetKey = new() { Width = 90 };
    private readonly TextBox _settingsKey = new() { Width = 90 };
    private readonly TextBox _pitchZeroKey = new() { Width = 90 };
    private readonly TextBox _bankFlipKey = new() { Width = 90 };
    private readonly TextBox _bodyFixedKey = new() { Width = 90 };
    private readonly TextBox _showKey = new() { Width = 90 };
    private readonly TextBox _exitKey = new() { Width = 90 };
    private readonly TextBox _saveKey = new() { Width = 90 };
    private readonly TextBox _holeKey = new() { Width = 90 };
    private readonly CheckBox _aimRmb = new() { Text = "长按鼠标右键进入瞄准布局", AutoSize = true };
    private readonly CheckBox _tapeRadar = new() { Text = "高度带显示雷达高度（仅机型支持时；否则显示海拔）", AutoSize = true };
    private readonly NumericUpDown _aoaWarn = new() { Minimum = 5, Maximum = 45, DecimalPlaces = 1, Increment = 0.5m, Width = 80 };
    private readonly NumericUpDown _bingoMin = new() { Minimum = 1, Maximum = 60, DecimalPlaces = 0, Increment = 1, Width = 80 };
    private readonly CheckBox _showLadder = new() { Text = "显示俯仰梯", AutoSize = true };
    private readonly CheckBox _showFpm = new() { Text = "显示飞行矢量（速度矢量）", AutoSize = true };
    private readonly NumericUpDown _hole = new() { Minimum = 0, Maximum = 240, DecimalPlaces = 0, Increment = 8, Width = 80 };
    private readonly NumericUpDown _endScale = new() { Minimum = 0.5m, Maximum = 3.0m, DecimalPlaces = 2, Increment = 0.05m, Width = 80 };
    private readonly NumericUpDown _overspeed = new() { Minimum = 200, Maximum = 3000, DecimalPlaces = 0, Increment = 10, Width = 80 };
    private readonly NumericUpDown _overspeedMach = new() { Minimum = 0.3m, Maximum = 5.0m, DecimalPlaces = 2, Increment = 0.05m, Width = 80 };
    private readonly CheckBox _overspeedMachMode = new() { Text = "超速按马赫数判定（取消则按 IAS）", AutoSize = true };
    private readonly NumericUpDown _fov = new() { Minimum = 15, Maximum = 120, DecimalPlaces = 0, Increment = 1, Width = 80 };
    private readonly NumericUpDown _offsetY = new() { Minimum = -10, Maximum = 10, DecimalPlaces = 1, Increment = 0.5m, Width = 80 };
    private readonly NumericUpDown _pitchZero = new() { Minimum = -40, Maximum = 40, DecimalPlaces = 1, Increment = 0.5m, Width = 80 };

    public bool Extrapolate => _extrapolate.Checked;
    public bool HudVisible => _visible.Checked;
    public bool BodyFixedOnly => _bodyFixed.Checked;
    public double RollCoupling => _rollCoupling.Checked ? 1.0 : 0.0;
    public int BankSign => _bankFlip.Checked ? -1 : 1;
    public bool HideCockpit => _hideCockpit.Checked;
    public bool HideNose => _hideNose.Checked;
    public double Fov => (double)_fov.Value;
    public double OffsetY => (double)_offsetY.Value;
    public double PitchZero => (double)_pitchZero.Value;
    public string ViewKey => _viewKey.Text;
    public string ZoomKey => _zoomKey.Text;
    public string MapKey => _mapKey.Text;
    public string ScoreboardKey => _scoreKey.Text;
    public string HoldHideKey => _holdKey.Text;
    public string ResetKey => _resetKey.Text;
    public string SettingsKey => _settingsKey.Text;
    public string PitchZeroKey => _pitchZeroKey.Text;
    public string BankFlipKey => _bankFlipKey.Text;
    public string BodyFixedKey => _bodyFixedKey.Text;
    public string ShowKey => _showKey.Text;
    public string ExitKey => _exitKey.Text;
    public string SaveKey => _saveKey.Text;
    public string CursorHoleKey => _holeKey.Text;
    public bool AimOnRightMouse => _aimRmb.Checked;
    public bool HideOnGround => _hideGround.Checked;
    public double GroundHideDelayMs => (double)_groundDelay.Value;
    public bool TapeRadarAltitude => _tapeRadar.Checked;
    public double AoaWarnDeg => (double)_aoaWarn.Value;
    public double BingoFuelMinutes => (double)_bingoMin.Value;
    public bool ShowPitchLadder => _showLadder.Checked;
    public bool ShowFpm => _showFpm.Checked;
    public double OverspeedKmh => (double)_overspeed.Value;
    public double OverspeedMach => (double)_overspeedMach.Value;
    public bool OverspeedByMach => _overspeedMachMode.Checked;
    public double EnduranceScale => (double)_endScale.Value;
    public int CursorHoleHalfPx => (int)_hole.Value;

    public SettingsForm(HudConfig cfg, bool hudVisible, bool bodyFixedOnly)
    {
        Text = "WtHud 设置";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        TopMost = true;
        ShowInTaskbar = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new System.Drawing.Size(470, 430);
        Font = new System.Drawing.Font("Microsoft YaHei UI", 9f);

        _extrapolate.Checked = cfg.Extrapolate;
        _visible.Checked = hudVisible;
        _bodyFixed.Checked = bodyFixedOnly;
        _rollCoupling.Checked = cfg.RollCoupling > 0.5;
        _bankFlip.Checked = cfg.BankSign < 0;
        _hideCockpit.Checked = cfg.HideInCockpit;
        _hideNose.Checked = cfg.HideInNose;
        _fov.Value = (decimal)Math.Clamp(cfg.VerticalFovDeg, 15, 120);
        _offsetY.Value = (decimal)Math.Clamp(cfg.LayoutOffsetYDeg, -10, 10);
        _pitchZero.Value = (decimal)Math.Clamp(cfg.PitchZeroDeg, -40, 40);
        _viewKey.Text = cfg.ViewKey;
        _zoomKey.Text = cfg.ZoomKey;
        _mapKey.Text = cfg.MapKey;
        _scoreKey.Text = cfg.ScoreboardKey;
        _holdKey.Text = cfg.HoldHideKey;
        _resetKey.Text = cfg.ResetKey;
        _settingsKey.Text = cfg.SettingsKey;
        _pitchZeroKey.Text = cfg.PitchZeroKey;
        _bankFlipKey.Text = cfg.BankFlipKey;
        _bodyFixedKey.Text = cfg.BodyFixedKey;
        _showKey.Text = cfg.ShowKey;
        _exitKey.Text = cfg.ExitKey;
        _saveKey.Text = cfg.SaveKey;
        _holeKey.Text = cfg.CursorHoleKey;
        _aimRmb.Checked = cfg.AimOnRightMouse;
        _hideGround.Checked = cfg.HideOnGround;
        _groundDelay.Value = (decimal)Math.Clamp(cfg.GroundHideDelayMs, 0, 3000);
        _tapeRadar.Checked = cfg.TapeRadarAltitude;
        _aoaWarn.Value = (decimal)Math.Clamp(cfg.AoaWarnDeg, 5, 45);
        _bingoMin.Value = (decimal)Math.Clamp(cfg.BingoFuelSeconds / 60.0, 1, 60);
        _showLadder.Checked = cfg.ShowPitchLadder;
        _showFpm.Checked = cfg.ShowFpm;
        _overspeed.Value = (decimal)Math.Clamp(cfg.OverspeedKmh, 200, 3000);
        _overspeedMach.Value = (decimal)Math.Clamp(cfg.OverspeedMach, 0.3, 5.0);
        _overspeedMachMode.Checked = cfg.OverspeedByMach;
        _endScale.Value = (decimal)Math.Clamp(cfg.EnduranceScale, 0.5, 3.0);
        _hole.Value = Math.Clamp(cfg.CursorHoleHalfPx, 0, 240);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            Padding = new Padding(12),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 64));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36));

        void AddRow(Control left, Control? right = null)
        {
            layout.RowCount++;
            layout.Controls.Add(left);
            if (right != null) layout.Controls.Add(right);
            else layout.SetColumnSpan(left, 2);
        }

        AddRow(_extrapolate);
        AddRow(_visible);
        AddRow(_bodyFixed);
        AddRow(_rollCoupling);
        AddRow(_bankFlip);
        AddRow(_hideCockpit);
        AddRow(_hideNose);
        AddRow(new Label { Text = "垂直视场角 FOV（度）", AutoSize = true }, _fov);
        AddRow(new Label { Text = "HUD 垂直偏移（度，正=上移）", AutoSize = true }, _offsetY);
        AddRow(new Label { Text = "俯仰零位（度，F7 自动填入）", AutoSize = true }, _pitchZero);
        AddRow(new Label { Text = "AOA 告警阈值（度，超过则闪 AOA）", AutoSize = true }, _aoaWarn);
        AddRow(new Label { Text = "BINGO 油量阈值（分钟，低于则闪 BINGO）", AutoSize = true }, _bingoMin);
        AddRow(new Label { Text = "超速阈值（IAS km/h，有 vne 的机型自动用 vne）", AutoSize = true }, _overspeed);
        AddRow(new Label { Text = "超速阈值（马赫数 M）", AutoSize = true }, _overspeedMach);
        AddRow(_overspeedMachMode);
        AddRow(new Label { Text = "续航标定系数（对齐游戏读数，1.3≈实测）", AutoSize = true }, _endScale);
        AddRow(new Label { Text = "光标洞半径 px（0=关闭，越小越不挡图标）", AutoSize = true }, _hole);
        AddRow(new Label { Text = "长按隐藏键（默认 U）", AutoSize = true }, _holdKey);
        AddRow(new Label { Text = "状态复位键（默认 F5）", AutoSize = true }, _resetKey);
        AddRow(new Label { Text = "设置面板键（默认 F1）", AutoSize = true }, _settingsKey);
        AddRow(new Label { Text = "俯仰零位键（默认 F7）", AutoSize = true }, _pitchZeroKey);
        AddRow(new Label { Text = "坡度翻转键（默认 F8）", AutoSize = true }, _bankFlipKey);
        AddRow(new Label { Text = "纯机体固连键（默认 F9）", AutoSize = true }, _bodyFixedKey);
        AddRow(new Label { Text = "显示/隐藏键（默认 F10）", AutoSize = true }, _showKey);
        AddRow(new Label { Text = "退出键（默认 F11，连按两次）", AutoSize = true }, _exitKey);
        AddRow(new Label { Text = "保存配置键（默认 F12）", AutoSize = true }, _saveKey);
        AddRow(new Label { Text = "光标洞循环键（默认 CTRL+ALT+H）", AutoSize = true }, _holeKey);
        AddRow(_aimRmb);
        AddRow(_hideGround);
        AddRow(new Label { Text = "地面隐藏延迟（毫秒，0=立即）", AutoSize = true }, _groundDelay);
        AddRow(_tapeRadar);
        AddRow(_showLadder);
        AddRow(_showFpm);
        AddRow(new Label { Text = "视角切换键（默认 V）", AutoSize = true }, _viewKey);
        AddRow(new Label { Text = "瞄准/放大键（默认 Z）", AutoSize = true }, _zoomKey);
        AddRow(new Label { Text = "地图键（默认 M）", AutoSize = true }, _mapKey);
        AddRow(new Label { Text = "计分板键（默认 TAB）", AutoSize = true }, _scoreKey);

        var hotkeys = new Label
        {
            AutoSize = true,
            MaximumSize = new System.Drawing.Size(440, 0),
            Margin = new Padding(3, 12, 3, 3),
            Text = "固定快捷键（不可改）：" + Environment.NewLine +
                   "  Ctrl+Alt+↑ / ↓         HUD 整体上移 / 下移" + Environment.NewLine +
                   "  Ctrl+Alt+PgUp / PgDn   垂直视场角（角度尺度）" + Environment.NewLine +
                   "  Ctrl+Alt+R             相机滚转耦合" + Environment.NewLine +
                   Environment.NewLine +
                   "其余键位在上方逐项可改，支持组合键（如 CTRL+N / SHIFT+TAB / ALT+F1）；" + Environment.NewLine +
                   "按键均为被动轮询或系统热键，不会吞掉游戏里的按键。",
        };
        AddRow(hotkeys);

        var ok = new Button { Text = "保存", DialogResult = DialogResult.OK, Width = 92, Height = 30 };
        var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Width = 92, Height = 30 };
        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Bottom,
            Height = 52,
            Padding = new Padding(8),
        };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);

        // Auto-scrolling wrapper: at 150% DPI the row list does not fit on one screen.
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        scroll.Controls.Add(layout);

        Controls.Add(scroll);
        Controls.Add(buttons);
        AcceptButton = ok;
        CancelButton = cancel;
    }
}

















