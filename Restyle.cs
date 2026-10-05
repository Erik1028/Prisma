namespace RGBCommander;

/// <summary>
/// Re-skins a live control tree. Most of the app reads <see cref="Theme"/> inside OnPaint, so a skin
/// change is already visible there after one Invalidate — but ~67 places BAKE a token into a control
/// property (<c>ForeColor = Theme.Subtle</c>) and every themed font carries the old skin's typeface.
/// This walks the tree and brings those two along, so the look can be switched without restarting.
///
/// Restarting is what the sibling apps do, but Prisma must not: a restart re-detects every device,
/// which means a fresh SMBus/I2C probe round on hardware this app goes out of its way not to disturb.
/// </summary>
internal static class Restyle
{
    /// <summary>Re-applies the current skin to <paramref name="root"/> and everything under it.</summary>
    public static void Apply(Control root)
    {
        Walk(root);
        root.Invalidate(true);
    }

    private static void Walk(Control c)
    {
        var refont = Theme.Respec(c.Font);
        if (refont != null && !ReferenceEquals(refont, c.Font)) c.Font = refont;

        // Only colours that ARE a theme token move; a swatch's red or a device's own colour
        // matches nothing in the token table and is left exactly as it is.
        var fore = Theme.Remap(c.ForeColor);
        if (fore.ToArgb() != c.ForeColor.ToArgb()) c.ForeColor = fore;

        if (c.BackColor != Color.Transparent)
        {
            var back = Theme.Remap(c.BackColor);
            if (back.ToArgb() != c.BackColor.ToArgb()) c.BackColor = back;
        }

        // A colour well (hover == fill) IS its colour - a saved swatch that happens to equal the
        // accent must not be remapped to the other skin's accent. Only chrome moves.
        if (c is RoundedButton rb && rb.HoverFillColor.ToArgb() != rb.FillColor.ToArgb())
        {
            rb.FillColor = Theme.Remap(rb.FillColor);
            rb.HoverFillColor = Theme.Remap(rb.HoverFillColor);
            rb.BorderColor = Theme.Remap(rb.BorderColor);
        }

        foreach (Control child in c.Controls) Walk(child);
    }
}
