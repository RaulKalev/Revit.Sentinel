using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Sentinel.Core.Geometry;
using Sentinel.Core.Models;
using Sentinel.UI.Services;

namespace Sentinel.UiHarness
{
    /// <summary>
    /// Draws a preview scene in plan (what the DirectContext3D preview shows in Revit, flattened) so placement
    /// semantics can be checked visually: wall, opening, hinge, sides, access arrow, devices + facing, heights.
    /// </summary>
    public static class PlanRenderer
    {
        public static void Render(PreviewScene scene, string title, string path)
        {
            var d = scene?.Doors.FirstOrDefault();
            if (d?.Door == null) return;
            var g = d.Door;
            var n = g.Facing.Flatten().Normalize();
            var w = g.WidthAxis.Flatten().Normalize();
            const double sizePx = 640, margin = 40, spanMm = 4200;
            var scale = (sizePx - 2 * margin) / spanMm;

            // Local plan frame centred on the door: screen X = along W, screen Y = along -N (side A up).
            Func<Vec3, Point> P = v =>
            {
                var r = v - g.Origin;
                return new Point(sizePx / 2 + r.Dot(w) * scale, sizePx / 2 - r.Dot(n) * scale);
            };

            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(31, 31, 31)), null, new Rect(0, 0, sizePx, sizePx + 60));
                var wallBrush = new SolidColorBrush(Color.FromRgb(90, 90, 90));
                var hw = g.WidthMm / 2;
                var ht = g.WallThicknessMm / 2;
                // Wall on both sides of the opening
                dc.DrawGeometry(wallBrush, null, Quad(P, g.Origin + w * -2000 + n * ht, g.Origin + w * -hw + n * ht, g.Origin + w * -hw - n * ht, g.Origin + w * -2000 - n * ht));
                dc.DrawGeometry(wallBrush, null, Quad(P, g.Origin + w * hw + n * ht, g.Origin + w * 2000 + n * ht, g.Origin + w * 2000 - n * ht, g.Origin + w * hw - n * ht));

                // Hinge
                var hingeSign = d.Hinge == HingeSide.PositiveWidthAxis ? 1.0 : -1.0;
                var hinge = g.Origin + w * (hingeSign * hw);
                dc.DrawEllipse(Brushes.Orange, null, P(hinge), 5, 5);
                // Door leaf (open 90° towards side A)
                dc.DrawLine(new Pen(Brushes.Orange, 2), P(hinge), P(hinge + n * g.WidthMm));
                Text(dc, "hinge", P(hinge) + new Vector(-14, 10), Brushes.Orange, 10);

                // Sides
                Text(dc, "SIDE A (facing)", P(g.Origin + n * 1600) + new Vector(-50, 0), Brushes.LightGray, 13);
                Text(dc, "SIDE B", P(g.Origin - n * 1600) + new Vector(-25, -10), Brushes.LightGray, 13);

                // Access arrow
                var green = new Pen(new SolidColorBrush(Color.FromRgb(40, 200, 120)), 3);
                if (d.Direction == AccessDirection.Both)
                {
                    Arrow(dc, green, P(g.Origin + n * 1100 + w * 300), P(g.Origin - n * 1100 + w * 300));
                    Arrow(dc, green, P(g.Origin - n * 1100 - w * 300), P(g.Origin + n * 1100 - w * 300));
                }
                else
                {
                    var from = d.Direction == AccessDirection.SideAToSideB ? n : -n;
                    Arrow(dc, green, P(g.Origin + from * 1100), P(g.Origin - from * 1100));
                }

                foreach (var p in d.Placements.Where(x => !x.IsBuiltIn))
                {
                    var c = p.HasErrors ? Brushes.IndianRed : ColorFor(p.Category);
                    var pt = P(p.Position);
                    var s = Math.Max(6, p.PreviewSizeMm * scale);
                    dc.DrawRectangle(null, new Pen(c, 2), new Rect(pt.X - s / 2, pt.Y - s / 2, s, s));
                    var tip = P(p.Position + p.Facing * 350);
                    Arrow(dc, new Pen(c, 1.5), pt, tip);
                    Text(dc, p.Label + " h=" + p.Position.Z.ToString("0", CultureInfo.InvariantCulture) + (p.IsMirrorCopy ? " (B copy)" : ""),
                        pt + new Vector(8, -18), c, 11);
                }

                Text(dc, title, new Point(12, sizePx + 8), Brushes.White, 14);
                Text(dc, "Access: " + d.Direction + "   hinge: " + d.Hinge + "   (plan, side A up)", new Point(12, sizePx + 32), Brushes.LightGray, 11);
            }

            var rtb = new RenderTargetBitmap((int)sizePx, (int)sizePx + 60, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            Save(rtb, path);
        }

        private static Brush ColorFor(ComponentCategory c)
        {
            switch (c)
            {
                case ComponentCategory.CardReader:
                case ComponentCategory.Keypad: return new SolidColorBrush(Color.FromRgb(0, 150, 255));
                case ComponentCategory.DoorContact: return new SolidColorBrush(Color.FromRgb(32, 201, 151));
                case ComponentCategory.ElectricLock: return new SolidColorBrush(Color.FromRgb(255, 170, 0));
                case ComponentCategory.RexPir:
                case ComponentCategory.RexButton: return new SolidColorBrush(Color.FromRgb(190, 110, 255));
                default: return Brushes.LightGray;
            }
        }

        private static Geometry Quad(Func<Vec3, Point> P, Vec3 a, Vec3 b, Vec3 c, Vec3 d)
        {
            var sg = new StreamGeometry();
            using (var ctx = sg.Open())
            {
                ctx.BeginFigure(P(a), true, true);
                ctx.LineTo(P(b), true, false);
                ctx.LineTo(P(c), true, false);
                ctx.LineTo(P(d), true, false);
            }
            return sg;
        }

        private static void Arrow(DrawingContext dc, Pen pen, Point a, Point b)
        {
            dc.DrawLine(pen, a, b);
            var v = b - a;
            if (v.Length < 1) return;
            v.Normalize();
            var perp = new Vector(-v.Y, v.X);
            dc.DrawLine(pen, b, b - v * 10 + perp * 5);
            dc.DrawLine(pen, b, b - v * 10 - perp * 5);
        }

        private static void Text(DrawingContext dc, string text, Point at, Brush brush, double size)
        {
            var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, brush, 1.0);
            dc.DrawText(ft, at);
        }

        public static void Save(BitmapSource bmp, string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using (var fs = File.Create(path)) enc.Save(fs);
        }
    }
}
