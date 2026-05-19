using System.Drawing;
using TmrOverlay.Core.Settings;

namespace TmrOverlay.App.Overlays.Abstractions;

internal abstract class PersistentOverlayForm : Form
{
    private const int WsExToolWindow = 0x00000080;
    private const int WsExTransparent = 0x00000020;
    private const int WsExNoActivate = 0x08000000;
    private const int WmNcHitTest = 0x0084;
    private const int HtClient = 1;
    private const int HtTransparent = -1;
    private const int FadeIntervalMilliseconds = 50;
    private const double FadeInSeconds = 0.22d;
    private const double FadeOutSeconds = 0.65d;
    private const double InputTransparentOpacityThreshold = 0.01d;

    private readonly OverlaySettings _settings;
    private readonly Action _saveSettings;
    private readonly System.Windows.Forms.Timer _fadeTimer;
    private Point _dragCursorOrigin;
    private Point _dragFormOrigin;
    private bool _dragging;
    private bool _inputTransparentOverride;
    private bool _framePersistenceSuppressed;
    private double _baseOpacity;
    private double _telemetryFadeAlpha = 1d;
    private double _telemetryFadeTarget = 1d;
    private DateTimeOffset _lastFadeStepAtUtc = DateTimeOffset.UtcNow;

    protected PersistentOverlayForm(
        OverlaySettings settings,
        Action saveSettings,
        int defaultWidth,
        int defaultHeight,
        bool enableWholeFormDrag = true)
    {
        _settings = settings;
        _saveSettings = saveSettings;
        _baseOpacity = Math.Clamp(_settings.Opacity, 0d, 1d);

        AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(
            _settings.Width > 0 ? _settings.Width : defaultWidth,
            _settings.Height > 0 ? _settings.Height : defaultHeight);
        DoubleBuffered = true;
        FormBorderStyle = FormBorderStyle.None;
        Location = new Point(_settings.X, _settings.Y);
        MaximizeBox = false;
        MinimizeBox = false;
        Opacity = _baseOpacity;
        ShowIcon = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = _settings.AlwaysOnTop;

        if (enableWholeFormDrag)
        {
            RegisterDragSurface(this);
        }

        _fadeTimer = new System.Windows.Forms.Timer
        {
            Interval = FadeIntervalMilliseconds
        };
        _fadeTimer.Tick += (_, _) => StepTelemetryFade();
    }

    public double LiveTelemetryFadeAlpha => _telemetryFadeAlpha;

    public double EffectiveOverlayOpacity => EffectiveOpacity();

    public bool IsEffectivelyInputTransparent =>
        _inputTransparentOverride || EffectiveOverlayOpacity <= InputTransparentOpacityThreshold;

    public virtual bool IsIntrinsicallyInputTransparentOverlay => false;

    protected bool IsOverlayFramePersistenceSuppressed => _framePersistenceSuppressed;

    public void SetInputTransparentOverride(bool enabled)
    {
        if (_inputTransparentOverride == enabled)
        {
            return;
        }

        _inputTransparentOverride = enabled;
        if (IsHandleCreated)
        {
            RecreateHandle();
        }
    }

    public void SetFramePersistenceSuppressed(bool suppressed)
    {
        _framePersistenceSuppressed = suppressed;
    }

    public void SetBaseOverlayOpacity(double opacity)
    {
        _baseOpacity = Math.Clamp(opacity, 0d, 1d);
        ApplyEffectiveOpacity();
    }

    protected void DisableOverlayAndClose()
    {
        _settings.Enabled = false;
        _saveSettings();
        Close();
    }

    public void SetLiveTelemetryAvailable(bool available, bool immediate = false)
    {
        var target = available ? 1d : 0d;
        if (immediate)
        {
            _telemetryFadeTarget = target;
            _telemetryFadeAlpha = target;
            _fadeTimer.Stop();
            ApplyEffectiveOpacity();
            return;
        }

        if (Math.Abs(_telemetryFadeTarget - target) < 0.001d)
        {
            return;
        }

        _telemetryFadeTarget = target;
        _lastFadeStepAtUtc = DateTimeOffset.UtcNow;
        if (!_fadeTimer.Enabled)
        {
            _fadeTimer.Start();
        }
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var createParams = base.CreateParams;
            if (UseToolWindowStyle)
            {
                createParams.ExStyle |= WsExToolWindow;
            }

            if (UseNoActivateStyle)
            {
                createParams.ExStyle |= WsExNoActivate;
            }

            if (_inputTransparentOverride && UseInputTransparentExtendedWindowStyle)
            {
                createParams.ExStyle |= WsExTransparent;
            }

            return createParams;
        }
    }

    protected virtual bool UseToolWindowStyle => true;

    protected virtual bool UseNoActivateStyle => true;

    protected virtual bool UseInputTransparentExtendedWindowStyle => true;

    protected virtual bool ShouldReceiveInputWhileTransparent(Point clientPoint) => false;

    protected override bool ShowWithoutActivation => UseToolWindowStyle;

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmNcHitTest && IsEffectivelyInputTransparent)
        {
            var clientPoint = PointToClient(ScreenPointFromLParam(m.LParam));
            if (ShouldReceiveInputWhileTransparent(clientPoint))
            {
                m.Result = new IntPtr(HtClient);
                return;
            }

            m.Result = new IntPtr(HtTransparent);
            return;
        }

        base.WndProc(ref m);
    }

    private static Point ScreenPointFromLParam(IntPtr lParam)
    {
        var value = lParam.ToInt64();
        return new Point(unchecked((short)(value & 0xffff)), unchecked((short)((value >> 16) & 0xffff)));
    }

    protected void RegisterDragSurfaces(params Control[] controls)
    {
        foreach (var control in controls)
        {
            RegisterDragSurface(control);
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        PersistOverlayFrame();
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _fadeTimer.Stop();
            _fadeTimer.Dispose();
        }

        base.Dispose(disposing);
    }

    protected virtual Size GetPersistedOverlaySize()
    {
        return Size;
    }

    private void RegisterDragSurface(Control control)
    {
        control.Cursor = Cursors.SizeAll;
        control.MouseDown += BeginDrag;
        control.MouseMove += DragOverlay;
        control.MouseUp += EndDrag;
    }

    private void BeginDrag(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        _dragging = true;
        _dragCursorOrigin = Cursor.Position;
        _dragFormOrigin = Location;
    }

    private void DragOverlay(object? sender, MouseEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        var cursor = Cursor.Position;
        Location = new Point(
            _dragFormOrigin.X + cursor.X - _dragCursorOrigin.X,
            _dragFormOrigin.Y + cursor.Y - _dragCursorOrigin.Y);
    }

    private void EndDrag(object? sender, MouseEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        PersistOverlayFrame();
    }

    protected virtual void PersistOverlayFrame()
    {
        if (_framePersistenceSuppressed)
        {
            return;
        }

        var persistedSize = GetPersistedOverlaySize();
        _settings.X = Location.X;
        _settings.Y = Location.Y;
        _settings.Width = persistedSize.Width;
        _settings.Height = persistedSize.Height;
        _settings.Opacity = _baseOpacity;
        _saveSettings();
    }

    private void StepTelemetryFade()
    {
        var now = DateTimeOffset.UtcNow;
        var elapsedSeconds = Math.Max(0d, (now - _lastFadeStepAtUtc).TotalSeconds);
        _lastFadeStepAtUtc = now;
        var duration = _telemetryFadeTarget > _telemetryFadeAlpha ? FadeInSeconds : FadeOutSeconds;
        var delta = duration <= 0d ? 1d : elapsedSeconds / duration;
        _telemetryFadeAlpha = MoveToward(_telemetryFadeAlpha, _telemetryFadeTarget, delta);
        ApplyEffectiveOpacity();

        if (Math.Abs(_telemetryFadeAlpha - _telemetryFadeTarget) < 0.001d)
        {
            _telemetryFadeAlpha = _telemetryFadeTarget;
            ApplyEffectiveOpacity();
            _fadeTimer.Stop();
        }
    }

    private void ApplyEffectiveOpacity()
    {
        var effective = EffectiveOpacity();
        if (Math.Abs(Opacity - effective) > 0.001d)
        {
            Opacity = effective;
        }
    }

    private double EffectiveOpacity()
    {
        return Math.Clamp(_baseOpacity * _telemetryFadeAlpha, 0d, 1d);
    }

    private static double MoveToward(double current, double target, double delta)
    {
        if (current < target)
        {
            return Math.Min(target, current + delta);
        }

        return Math.Max(target, current - delta);
    }
}
