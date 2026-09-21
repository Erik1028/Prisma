using System.Runtime.InteropServices;

namespace RGBCommander;

/// <summary>Win10/11 acrylic blur-behind ("liquid glass") via the undocumented but
/// long-stable SetWindowCompositionAttribute accent policy. The window background
/// becomes a blurred, dark-tinted pane of whatever sits behind it; custom-painted
/// cards stay opaque on top.</summary>
public static class Acrylic
{
    private const int WcaAccentPolicy = 19;
    private const int AccentDisabled = 0;
    private const int AccentEnableAcrylicBlurBehind = 4;

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public uint GradientColor; // AABBGGRR
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    public static void Apply(Form form, bool enabled)
    {
        if (!form.IsHandleCreated) return;
        var bg = Theme.Bg;
        var accent = new AccentPolicy
        {
            AccentState = enabled ? AccentEnableAcrylicBlurBehind : AccentDisabled,
            // Dark tint, but light enough that the blur actually reads. The window's own
            // background paints alpha-0 black while glass is on (Backdrop.Glass), so the
            // blurred desktop shows wherever cards/text don't cover it.
            GradientColor = enabled ? 0xB4000000u | (uint)(bg.B << 16 | bg.G << 8 | bg.R) : 0
        };
        int size = Marshal.SizeOf<AccentPolicy>();
        IntPtr ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(accent, ptr, false);
            var data = new WindowCompositionAttributeData { Attribute = WcaAccentPolicy, Data = ptr, SizeOfData = size };
            try { SetWindowCompositionAttribute(form.Handle, ref data); } catch { }
        }
        finally { Marshal.FreeHGlobal(ptr); }
    }
}
