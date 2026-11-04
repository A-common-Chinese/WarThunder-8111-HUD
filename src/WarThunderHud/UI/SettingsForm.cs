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
    private readonly CheckBox _hideCockpit = new() { Text = "座舱视角隐藏 HUD（按一次 V）", AutoSize = true };
    private readonly CheckBox _hideNose = new() { Text = "机头视角隐藏 HUD（按两次 V）", AutoSize = true };
    private readonly TextBox _viewKey = new() { Width = 90 };
    private readonly TextBox _zoomKey = new() { Width = 90 };
    private readonly TextBox _mapKey = new() { Width = 90 };
    private readonly TextBox _scoreKey = new() { Width = 90 };
    private readonly NumericUpDown _endScale = new() { Minimum = 0.5m, Maximum = 3.0m, DecimalPlaces = 2, Increment = 0.05m, Width = 80 };
    private readonly NumericUpDown _overspeed = new() { Minimum = 200, Maximum = 3000, DecimalPlaces = 0, Increment = 10, Width = 80 };
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
    public double OverspeedKmh => (double)_overspeed.Value;
    public double EnduranceScale => (double)_endScale.Value;

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
        _overspeed.Value = (decimal)Math.Clamp(cfg.OverspeedKmh, 200, 3000);
        _endScale.Value = (decimal)Math.Clamp(cfg.EnduranceScale, 0.5, 3.0);

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
        AddRow(new Label { Text = "超速阈值（IAS km/h，超过则变红）", AutoSize = true }, _overspeed);
        AddRow(new Label { Text = "续航标定系数（对齐游戏读数，1.3≈实测）", AutoSize = true }, _endScale);
        AddRow(new Label { Text = "视角切换键（默认 V）", AutoSize = true }, _viewKey);
        AddRow(new Label { Text = "瞄准/放大键（默认 Z）", AutoSize = true }, _zoomKey);
        AddRow(new Label { Text = "地图键（默认 M）", AutoSize = true }, _mapKey);
        AddRow(new Label { Text = "计分板键（默认 TAB）", AutoSize = true }, _scoreKey);

        var hotkeys = new Label
        {
            AutoSize = true,
            MaximumSize = new System.Drawing.Size(420, 0),
            Margin = new Padding(3, 12, 3, 3),
            Text = "快捷键：" + Environment.NewLine +
                   "F1 设置面板   F7 俯仰零位   F8 反转坡度   F9 纯机体固连   F10 显示/隐藏   F11 连按两次退出   F12 保存" + Environment.NewLine +
                   "Ctrl+Alt+↑/↓ 整体上下移   Ctrl+Alt+PgUp/PgDn 视场角   Ctrl+Alt+R 相机滚转" + Environment.NewLine +
                                      "按键支持组合，如 CTRL+N / SHIFT+TAB / ALT+F1；不会吞掉游戏按键" + Environment.NewLine +
                   "Z 瞄准布局（两侧数据带外推、俯仰梯下移、航向带置顶、滚转圆弧放大）",
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






