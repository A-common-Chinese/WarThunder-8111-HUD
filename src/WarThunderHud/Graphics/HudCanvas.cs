using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DirectWrite;
using Vortice.DXGI;
using WtHud.Hud;

namespace WtHud.Graphics;

/// <summary>
/// Offscreen Direct3D11/Direct2D target used for PNG previews (and unit visuals).
/// </summary>
public sealed class HudCanvas : IDisposable
{
    public ID3D11Device Device { get; }
    public ID2D1Factory1 D2DFactory { get; }
    public IDWriteFactory WriteFactory { get; }
    public ID3D11Texture2D Target { get; }
    public HudDrawer Drawer { get; }
    public int Width { get; }
    public int Height { get; }

    private readonly ID3D11DeviceContext _context;
    private readonly ID2D1RenderTarget _renderTarget;

    public HudCanvas(int width, int height)
    {
        Width = width;
        Height = height;

        D3D11.D3D11CreateDevice(null!, DriverType.Hardware, DeviceCreationFlags.BgraSupport, null!,
            out ID3D11Device device, out ID3D11DeviceContext context).CheckError();
        Device = device;
        _context = context;

        var desc = new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
            CPUAccessFlags = CpuAccessFlags.None,
            MiscFlags = ResourceOptionFlags.None,
        };
        Target = Device.CreateTexture2D(desc);
        using var surface = Target.QueryInterface<IDXGISurface>();

        D2D1.D2D1CreateFactory(Vortice.Direct2D1.FactoryType.SingleThreaded, out ID2D1Factory d2dFactory).CheckError();
        D2DFactory = d2dFactory.QueryInterface<ID2D1Factory1>();

        DWrite.DWriteCreateFactory(Vortice.DirectWrite.FactoryType.Shared, out IDWriteFactory writeFactory).CheckError();
        WriteFactory = writeFactory;

        var props = new RenderTargetProperties(
            RenderTargetType.Default,
            new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied),
            96, 96,
            RenderTargetUsage.None,
            Vortice.Direct2D1.FeatureLevel.Default);

        _renderTarget = D2DFactory.CreateDxgiSurfaceRenderTarget(surface, props);
        Drawer = new HudDrawer(D2DFactory, WriteFactory, _renderTarget);
    }

    public void RenderFrame(byte r, byte g, byte b, byte a, IReadOnlyList<HudPrim> prims) =>
        Drawer.RenderFrame(prims, r, g, b, a);

    public byte[] ReadBackRgba()
    {
        var desc = new Texture2DDescription
        {
            Width = (uint)Width,
            Height = (uint)Height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            BindFlags = BindFlags.None,
            CPUAccessFlags = CpuAccessFlags.Read,
            MiscFlags = ResourceOptionFlags.None,
        };
        using var staging = Device.CreateTexture2D(desc);
        _context.CopyResource(staging, Target);
        var map = _context.Map(staging, 0, MapMode.Read);
        try
        {
            var rgba = new byte[Width * Height * 4];
            for (int y = 0; y < Height; y++)
            {
                IntPtr row = map.DataPointer + y * (int)map.RowPitch;
                int dst = y * Width * 4;
                for (int x = 0; x < Width; x++)
                {
                    int src = x * 4;
                    byte b = System.Runtime.InteropServices.Marshal.ReadByte(row, src + 0);
                    byte g = System.Runtime.InteropServices.Marshal.ReadByte(row, src + 1);
                    byte r = System.Runtime.InteropServices.Marshal.ReadByte(row, src + 2);
                    byte a = System.Runtime.InteropServices.Marshal.ReadByte(row, src + 3);
                    if (a > 0 && a < 255)
                    {
                        r = (byte)Math.Min(255, r * 255 / a);
                        g = (byte)Math.Min(255, g * 255 / a);
                        b = (byte)Math.Min(255, b * 255 / a);
                    }
                    rgba[dst + x * 4 + 0] = r;
                    rgba[dst + x * 4 + 1] = g;
                    rgba[dst + x * 4 + 2] = b;
                    rgba[dst + x * 4 + 3] = a;
                }
            }
            return rgba;
        }
        finally
        {
            _context.Unmap(staging, 0);
        }
    }

    public void Dispose()
    {
        Drawer.Dispose();
        _renderTarget.Dispose();
        D2DFactory.Dispose();
        WriteFactory.Dispose();
        Target.Dispose();
        _context.Dispose();
        Device.Dispose();
    }
}
