// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 A-common-Chinese
// This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3.0 or later.

using System.Runtime.InteropServices;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DirectComposition;
using Vortice.DirectWrite;
using Vortice.DXGI;
using Vortice.Mathematics;
using WtHud.Graphics;
using WtHud.Hud;

namespace WtHud.Overlay;

/// <summary>
/// Fullscreen, click-through, topmost overlay window.
///
/// Composition path: D3D11 device -> DXGI flip swapchain created for composition
/// -> DirectComposition visual with premultiplied alpha. No injection, no hooks:
/// this is an ordinary window owned by our process and composed by DWM on top of
/// the game's borderless window.
/// </summary>
public sealed class OverlayWindow : IDisposable
{
    private const string ClassName = "WtHudOverlayClass";

    private readonly NativeMethods.WndProc _wndProc;
    private IntPtr _hwnd;
    private ID3D11Device? _device;
    private ID3D11DeviceContext? _context;
    private IDXGISwapChain1? _swapchain;
    private IDCompositionDevice? _dcomp;
    private IDCompositionTarget? _target;
    private IDCompositionVisual? _visual;
    private ID2D1Factory1? _d2dFactory;
    private IDWriteFactory? _dwrite;
    private ID2D1RenderTarget? _renderTarget;
    private HudDrawer? _drawer;
    private ID3D11Texture2D? _backBuffer;

    public int Width { get; private set; }
    public int Height { get; private set; }
    public bool Running { get; private set; } = true;

    /// <summary>Raised with the hotkey id: 1 = F10 toggle HUD, 2 = F9 toggle body-fixed mode.</summary>
    public Action<int>? HotkeyPressed;

    public OverlayWindow(int width, int height)
    {
        Width = width;
        Height = height;

        _wndProc = WndProcImpl;
        var wc = new NativeMethods.WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.WNDCLASSEX>(),
            style = 0,
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = NativeMethods.GetModuleHandle(null),
            hCursor = NativeMethods.CreateInvisibleCursor(),
            lpszClassName = ClassName,
        };
        NativeMethods.RegisterClassEx(ref wc);

        int exStyle = NativeMethods.WS_EX_TOPMOST | NativeMethods.WS_EX_TRANSPARENT |
                      NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW |
                      NativeMethods.WS_EX_NOREDIRECTIONBITMAP;

        _hwnd = NativeMethods.CreateWindowEx(exStyle, ClassName, "WtHud Overlay",
            NativeMethods.WS_POPUP | NativeMethods.WS_VISIBLE, 0, 0, width, height,
            IntPtr.Zero, IntPtr.Zero, wc.hInstance, IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
            throw new InvalidOperationException("CreateWindowEx failed: " + Marshal.GetLastWin32Error());

        NativeMethods.RegisterHotKey(_hwnd, 1, NativeMethods.MOD_NOREPEAT, NativeMethods.VK_F10);
        NativeMethods.RegisterHotKey(_hwnd, 2, NativeMethods.MOD_NOREPEAT, NativeMethods.VK_F9);
        NativeMethods.RegisterHotKey(_hwnd, 3, NativeMethods.MOD_NOREPEAT, NativeMethods.VK_F11);
        NativeMethods.RegisterHotKey(_hwnd, 4, NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT, NativeMethods.VK_UP);
        NativeMethods.RegisterHotKey(_hwnd, 5, NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT, NativeMethods.VK_DOWN);
        NativeMethods.RegisterHotKey(_hwnd, 6, NativeMethods.MOD_NOREPEAT, NativeMethods.VK_F7);
        NativeMethods.RegisterHotKey(_hwnd, 7, NativeMethods.MOD_NOREPEAT, NativeMethods.VK_F8);
        NativeMethods.RegisterHotKey(_hwnd, 8, NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT, NativeMethods.VK_PRIOR);
        NativeMethods.RegisterHotKey(_hwnd, 9, NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT, NativeMethods.VK_NEXT);
        NativeMethods.RegisterHotKey(_hwnd, 10, NativeMethods.MOD_NOREPEAT, NativeMethods.VK_F12);
        NativeMethods.RegisterHotKey(_hwnd, 11, NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT, NativeMethods.VK_R);
        NativeMethods.RegisterHotKey(_hwnd, 12, NativeMethods.MOD_NOREPEAT, NativeMethods.VK_F1);
        NativeMethods.RegisterHotKey(_hwnd, 13, NativeMethods.MOD_NOREPEAT, NativeMethods.VK_F5);
        NativeMethods.RegisterHotKey(_hwnd, 14, NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT, NativeMethods.VK_H);

        NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_SHOWNOACTIVATE);
        NativeMethods.SetWindowPos(_hwnd, (IntPtr)NativeMethods.HWND_TOPMOST, 0, 0, width, height,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);

        _winW = width; _winH = height;
        ApplyCursorHoleRegion(width, height);
        InitGraphics(width, height);
    }

    /// <summary>
    /// Cuts a small hole out of the window region at the screen centre. War Thunder clips the
    /// OS cursor there; with the hole that pixel region belongs to the game window again, so the
    /// game keeps hiding its own cursor. The HUD already leaves a gap at the boresight, so the
    /// hole does not remove any symbology.
    /// </summary>
    private int _winW, _winH;

    /// <summary>Half-size in pixels of the transparent centre hole (0 = no hole).</summary>
    public int CursorHoleHalfPx { get; private set; } = 70;

    /// <summary>Re-assert the invisible cursor right after a region change (Windows re-evaluates
    /// the cursor owner whenever the window shape changes, which flashes the arrow).</summary>
    public void ForceInvisibleCursor()
    {
        if (_invisibleCursor != IntPtr.Zero) NativeMethods.SetCursor(_invisibleCursor);
    }

    /// <summary>
    /// Ask the foreground window (the game) to re-run its WM_SETCURSOR handler. Changing the
    /// overlay region makes Windows re-evaluate which window owns the cursor and it can fall back
    /// to the game class cursor (an arrow), which then never clears because the game pins the
    /// pointer to the screen centre. A single posted WM_SETCURSOR makes the game hide it again.
    /// </summary>
    public void NudgeForegroundCursor()
    {
        IntPtr fg = NativeMethods.GetForegroundWindow();
        if (fg == IntPtr.Zero || fg == _hwnd) return;
        IntPtr lParam = (IntPtr)(NativeMethods.HTCLIENT | (NativeMethods.WM_MOUSEMOVE << 16));
        NativeMethods.PostMessage(fg, NativeMethods.WM_SETCURSOR, fg, lParam);
    }

    /// <summary>Resize the centre hole; smaller values occlude less HUD symbology.</summary>
    public void SetCursorHole(int halfPx)
    {
        CursorHoleHalfPx = Math.Clamp(halfPx, 0, 240);
        ApplyCursorHoleRegion(_winW, _winH);
    }

    /// <summary>
    /// Cuts a small hole out of the window region at the screen centre. War Thunder clips the
    /// OS cursor there; with the hole that pixel region belongs to the game window again, so the
    /// game keeps hiding its own cursor. Keep it as small as the game still accepts, otherwise it
    /// swallows HUD symbols (notably the flight-path marker near the boresight).
    /// </summary>
    private void ApplyCursorHoleRegion(int width, int height)
    {
        int hole = CursorHoleHalfPx;
        if (width <= 0 || height <= 0) return;

        if (hole <= 0)
        {
            IntPtr whole = NativeMethods.CreateRectRgn(0, 0, width, height);
            NativeMethods.SetWindowRgn(_hwnd, whole, true);
            Console.WriteLine("overlay region: full screen (no cursor hole)");
            return;
        }

        int cx = width / 2, cy = height / 2;
        IntPtr full = NativeMethods.CreateRectRgn(0, 0, width, height);
        IntPtr holeRgn = NativeMethods.CreateRectRgn(cx - hole, cy - hole, cx + hole, cy + hole);
        NativeMethods.CombineRgn(full, full, holeRgn, NativeMethods.RGN_DIFF);
        NativeMethods.SetWindowRgn(_hwnd, full, true);
        NativeMethods.DeleteObject(holeRgn);
        Console.WriteLine($"overlay region: full screen minus {hole * 2}x{hole * 2} at centre");
    }

    private void InitGraphics(int width, int height)
    {
        D3D11.D3D11CreateDevice(null!, DriverType.Hardware, DeviceCreationFlags.BgraSupport, null!,
            out ID3D11Device device, out ID3D11DeviceContext context).CheckError();
        _device = device;
        _context = context;

        D2D1.D2D1CreateFactory(Vortice.Direct2D1.FactoryType.SingleThreaded, out ID2D1Factory factory).CheckError();
        _d2dFactory = factory.QueryInterface<ID2D1Factory1>();

        DWrite.DWriteCreateFactory(Vortice.DirectWrite.FactoryType.Shared, out IDWriteFactory write).CheckError();
        _dwrite = write;

        using var dxgiDevice = _device.QueryInterface<IDXGIDevice>();
        using var adapter = dxgiDevice.GetAdapter();
        using var factory2 = adapter.GetParent<IDXGIFactory2>();

        var desc = new SwapChainDescription1
        {
            Width = (uint)width,
            Height = (uint)height,
            Format = Format.B8G8R8A8_UNorm,
            Stereo = false,
            SampleDescription = new SampleDescription(1, 0),
            BufferUsage = Usage.RenderTargetOutput,
            BufferCount = 2,
            Scaling = Scaling.Stretch,
            SwapEffect = SwapEffect.FlipSequential,
            AlphaMode = Vortice.DXGI.AlphaMode.Premultiplied,
            Flags = SwapChainFlags.None,
        };
        _swapchain = factory2.CreateSwapChainForComposition(_device, desc);

        _dcomp = DComp.DCompositionCreateDevice<IDCompositionDevice>(dxgiDevice);
        _dcomp.CreateTargetForHwnd(_hwnd, true, out var compTarget);
        _target = compTarget;
        _visual = _dcomp.CreateVisual();
        _visual.SetContent(_swapchain);
        _target.SetRoot(_visual);
        _dcomp.Commit();

        CreateRenderTarget();
    }

    private void CreateRenderTarget()
    {
        DisposeRenderTarget();
        _backBuffer = _swapchain!.GetBuffer<ID3D11Texture2D>(0);
        using var surface = _backBuffer.QueryInterface<IDXGISurface>();
        var props = new RenderTargetProperties(
            RenderTargetType.Default,
            new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied),
            96, 96, RenderTargetUsage.None, Vortice.Direct2D1.FeatureLevel.Default);
        _renderTarget = _d2dFactory!.CreateDxgiSurfaceRenderTarget(surface, props);
        _drawer = new HudDrawer(_d2dFactory, _dwrite!, _renderTarget);
    }

    /// <summary>Draws one transparent frame and presents it.</summary>
    public void Present(IReadOnlyList<HudPrim> prims)
    {
        if (_drawer == null || _swapchain == null) return;
        _drawer.RenderFrame(prims, 0, 0, 0, 0);
        _swapchain.Present(0, PresentFlags.None);
    }

    /// <summary>Processes pending window messages; returns false once the window is gone.</summary>
    public bool PumpMessages()
    {
        while (NativeMethods.PeekMessage(out var msg, IntPtr.Zero, 0, 0, NativeMethods.PM_REMOVE))
        {
            if (msg.message == NativeMethods.WM_QUIT)
            {
                Running = false;
                return false;
            }
            NativeMethods.TranslateMessage(ref msg);
            NativeMethods.DispatchMessage(ref msg);
        }
        return Running;
    }

    public void RequestClose() => Running = false;

    private bool _visible = true;
    private readonly IntPtr _invisibleCursor = NativeMethods.CreateInvisibleCursor();

    /// <summary>
    /// Show/hide the overlay window. Hiding it while the game is not focused is what makes
    /// clicks pass through to other applications: a topmost, transparent-by-hit-test window
    /// still owns the mouse over its whole area in some compositor states.
    /// </summary>
    public void SetVisible(bool visible)
    {
        if (visible == _visible) return;
        _visible = visible;
        NativeMethods.ShowWindow(_hwnd, visible ? NativeMethods.SW_SHOWNOACTIVATE : NativeMethods.SW_HIDE);
        if (visible)
        {
            // A child window must not be pushed to the topmost band.
            IntPtr insertAfter = _parent != IntPtr.Zero ? IntPtr.Zero : (IntPtr)NativeMethods.HWND_TOPMOST;
            NativeMethods.SetWindowPos(_hwnd, insertAfter, 0, 0, Width, Height,
                NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
        }
    }

    public bool IsVisible => _visible;

    private IntPtr _parent;

    /// <summary>
    /// Re-parents the overlay under the game's window. A topmost popup covering the game makes
    /// the game believe the pointer left its client area, so the game re-shows the OS cursor;
    /// as a child of the game window the overlay is part of it and that check keeps working.
    /// </summary>
    public void AttachTo(IntPtr parentHwnd)
    {
        if (parentHwnd == IntPtr.Zero || parentHwnd == _parent) return;
        _parent = parentHwnd;

        var style = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GWL_STYLE).ToInt64();
        style = (style | NativeMethods.WS_CHILD) & ~(long)NativeMethods.WS_POPUP;
        NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GWL_STYLE, new IntPtr(style));
        NativeMethods.SetParent(_hwnd, parentHwnd);

        int w = Width, h = Height;
        if (NativeMethods.GetClientRect(parentHwnd, out var rc) && rc.Right > 0 && rc.Bottom > 0)
        {
            w = rc.Right - rc.Left;
            h = rc.Bottom - rc.Top;
        }
        NativeMethods.SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, w, h,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
        Console.WriteLine($"overlay attached to game window {parentHwnd} client {w}x{h}");
    }

    private IntPtr WndProcImpl(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case NativeMethods.WM_NCHITTEST:
                // Click-through: never claim the mouse.
                return (IntPtr)NativeMethods.HTTRANSPARENT;
            case NativeMethods.WM_SETCURSOR:
                // Own an invisible cursor so nothing is painted over the game, regardless of
                // which window Windows decides is the cursor owner.
                if (_invisibleCursor != IntPtr.Zero) NativeMethods.SetCursor(_invisibleCursor);
                return (IntPtr)1;
            case NativeMethods.WM_HOTKEY:
                HotkeyPressed?.Invoke(wParam.ToInt32());
                return IntPtr.Zero;
            case NativeMethods.WM_DESTROY:
                NativeMethods.PostQuitMessage(0);
                return IntPtr.Zero;
        }
        return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void DisposeRenderTarget()
    {
        _drawer?.Dispose();
        _drawer = null;
        _renderTarget?.Dispose();
        _renderTarget = null;
        _backBuffer?.Dispose();
        _backBuffer = null;
    }

    public void Dispose()
    {
        DisposeRenderTarget();
        _visual?.Dispose();
        _target?.Dispose();
        _dcomp?.Dispose();
        _swapchain?.Dispose();
        _d2dFactory?.Dispose();
        _dwrite?.Dispose();
        _context?.Dispose();
        _device?.Dispose();
        if (_hwnd != IntPtr.Zero)
        {
            NativeMethods.UnregisterHotKey(_hwnd, 1);
            NativeMethods.UnregisterHotKey(_hwnd, 2);
            NativeMethods.UnregisterHotKey(_hwnd, 3);
            NativeMethods.UnregisterHotKey(_hwnd, 4);
            NativeMethods.UnregisterHotKey(_hwnd, 5);
            NativeMethods.UnregisterHotKey(_hwnd, 6);
            NativeMethods.UnregisterHotKey(_hwnd, 7);
            NativeMethods.UnregisterHotKey(_hwnd, 8);
            NativeMethods.UnregisterHotKey(_hwnd, 9);
            NativeMethods.UnregisterHotKey(_hwnd, 10);
            NativeMethods.UnregisterHotKey(_hwnd, 11);
            NativeMethods.UnregisterHotKey(_hwnd, 12);
            NativeMethods.UnregisterHotKey(_hwnd, 13);
            NativeMethods.DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }
    }
}




















