using System.Drawing;
using System.Drawing.Drawing2D;

namespace OwaWidget.App.Services;

public static class TrayIconPainter
{
    private static readonly Color Ink = Color.FromArgb(0x20, 0x1E, 0x1D);
    private static readonly Color Accent = Color.FromArgb(0xEC, 0x30, 0x13);
    private static readonly Color Idle = Color.FromArgb(0xA3, 0x9D, 0x99);
    private static readonly Color Muted = Color.FromArgb(0x57, 0x52, 0x4F);

    public static Bitmap Paint(TrayPresentation presentation, int size)
    {
        var scale = size / 16f;
        var border = 2f * scale;
        var radius = 4f * scale;

        var bitmap = new Bitmap(size, size);

        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);

        if (presentation.State == TrayState.Running)
        {
            using var path = Rounded(new RectangleF(0, 0, size, size), radius);
            using var brush = new SolidBrush(Accent);
            graphics.FillPath(brush, path);
            return bitmap;
        }

        var outer = new RectangleF(border / 2f, border / 2f, size - border, size - border);
        var inner = RectangleF.Inflate(outer, -border / 2f, -border / 2f);

        using (var innerPath = Rounded(inner, Math.Max(1f, radius - border)))
        using (var white = new SolidBrush(Color.White))
        {
            graphics.FillPath(white, innerPath);

            var height = presentation.State switch
            {
                TrayState.Free => 2f * scale,
                TrayState.Soon => Math.Max(1f, (float)Math.Round(12 * scale * presentation.Fill)),
                _ => 0f
            };

            if (height > 0)
            {
                var saved = graphics.Clip;
                graphics.SetClip(innerPath);

                using (var brush = new SolidBrush(presentation.State == TrayState.Free ? Idle : Accent))
                {
                    graphics.FillRectangle(
                        brush,
                        new RectangleF(inner.Left, inner.Bottom - height, inner.Width, height));
                }

                graphics.Clip = saved;
            }
        }

        using (var pen = new Pen(presentation.State == TrayState.Offline ? Muted : Ink, border))
        {
            if (presentation.State == TrayState.Offline)
            {
                pen.DashStyle = DashStyle.Dash;
                pen.DashPattern = new[] { 1.6f, 1.2f };
            }

            using var outerPath = Rounded(outer, Math.Max(1f, radius - border / 2f));
            graphics.DrawPath(pen, outerPath);
        }

        return bitmap;
    }

    private static GraphicsPath Rounded(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        var diameter = Math.Min(radius * 2f, Math.Min(rect.Width, rect.Height));

        if (diameter <= 0)
        {
            path.AddRectangle(rect);
            return path;
        }

        path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();

        return path;
    }
}