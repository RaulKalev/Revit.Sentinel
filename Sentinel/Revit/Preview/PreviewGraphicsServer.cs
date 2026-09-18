using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.DirectContext3D;
using Autodesk.Revit.DB.ExternalService;
using Sentinel.Core.Geometry;
using Sentinel.Core.Models;
using Sentinel.Core.Placement;
using Sentinel.Infrastructure;
using Sentinel.UI.Services;

namespace Sentinel.Revit.Preview
{
    /// <summary>
    /// Transient preview graphics through DirectContext3D. Nothing is written to the document: the server only
    /// draws line geometry (device boxes, facing arrows, door outline, access arrow) while a scene is set.
    /// </summary>
    internal sealed class PreviewGraphicsServer : IDirectContext3DServer
    {
        private static readonly Guid ServerId = new Guid("0F6B3E52-7C1A-4D89-9E3B-5A2C8D71F406");

        private readonly object _lock = new object();
        private Document _document;
        private List<Segment> _segments = new List<Segment>();
        private Outline _bounds;

        // GPU buffers (rebuilt when the scene changes)
        private bool _dirty = true;
        private VertexBuffer _vb;
        private IndexBuffer _ib;
        private VertexFormat _format;
        private EffectInstance _effect;
        private int _vertexCount;
        private int _lineCount;

        private struct Segment
        {
            public XYZ A;
            public XYZ B;
            public ColorWithTransparency Color;
        }

        // ------------------------------------------------------------------ registration

        public static PreviewGraphicsServer Register()
        {
            var service = ExternalServiceRegistry.GetService(ExternalServices.BuiltInExternalServices.DirectContext3DService) as MultiServerService;
            if (service == null) throw new InvalidOperationException("DirectContext3D service is not available.");

            var server = new PreviewGraphicsServer();
            if (service.GetServer(ServerId) == null) service.AddServer(server);

            var active = service.GetActiveServerIds();
            if (!active.Contains(ServerId))
            {
                active.Add(ServerId);
                service.SetActiveServers(active);
            }
            return server;
        }

        public void Unregister()
        {
            try
            {
                var service = ExternalServiceRegistry.GetService(ExternalServices.BuiltInExternalServices.DirectContext3DService) as MultiServerService;
                if (service != null && service.GetServer(ServerId) != null) service.RemoveServer(ServerId);
            }
            catch (Exception ex)
            {
                SentinelLog.Error("Unregistering preview server failed", ex);
            }
        }

        // ------------------------------------------------------------------ scene

        public void SetScene(Document doc, PreviewScene scene)
        {
            var segs = new List<Segment>();
            if (scene != null)
            {
                foreach (var d in scene.Doors) AddDoor(segs, d);
            }

            lock (_lock)
            {
                _document = doc;
                _segments = segs;
                _bounds = ComputeBounds(segs);
                _dirty = true;
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _segments = new List<Segment>();
                _bounds = null;
                _dirty = true;
            }
        }

        public bool HasScene
        {
            get { lock (_lock) return _segments.Count > 0; }
        }

        public BoundingBoxXYZ SceneBox
        {
            get
            {
                lock (_lock)
                {
                    if (_bounds == null) return null;
                    return new BoundingBoxXYZ { Min = _bounds.MinimumPoint, Max = _bounds.MaximumPoint };
                }
            }
        }

        private static void AddDoor(List<Segment> segs, PreviewDoor d)
        {
            if (d?.Door == null || !d.Door.IsValid) return;
            var g = d.Door;
            var n = g.Facing.Flatten().Normalize();
            var w = g.WidthAxis.Flatten().Normalize();
            var alpha = d.IsCurrent ? 0 : 120; // other doors fainter
            var doorColor = new ColorWithTransparency(90, 170, 255, (uint)alpha);
            var arrowColor = new ColorWithTransparency(40, 200, 120, (uint)alpha);

            // Door opening outline in the wall plane.
            var o = g.Origin;
            var hw = g.WidthMm / 2.0;
            var p1 = o - w * hw;
            var p2 = o + w * hw;
            var p3 = p2 + Vec3.UnitZ * g.HeightMm;
            var p4 = p1 + Vec3.UnitZ * g.HeightMm;
            Poly(segs, doorColor, p1, p2, p3, p4, p1);

            // Hinge marker: short vertical tick outside the hinge jamb.
            var hingeSign = d.Hinge == HingeSide.PositiveWidthAxis ? 1.0 : -1.0;
            var hingeBase = o + w * (hingeSign * (hw + 40));
            Line(segs, doorColor, hingeBase + Vec3.UnitZ * 300, hingeBase + Vec3.UnitZ * (g.HeightMm - 300));

            // Access direction arrow through the door (from unsecured to secured side), 1 m above floor.
            var reach = g.WallThicknessMm / 2.0 + 700;
            var z = Vec3.UnitZ * 1000;
            if (d.Direction == AccessDirection.Both)
            {
                Arrow(segs, arrowColor, o + n * reach + z, o - n * reach + z, 150);
                Arrow(segs, arrowColor, o - n * reach + z, o + n * reach + z, 150);
            }
            else
            {
                var from = d.Direction == AccessDirection.SideAToSideB ? n : -n;
                Arrow(segs, arrowColor, o + from * reach + z, o - from * reach + z, 200);
            }

            // Side A marker: small "A" square on side A floor.
            var a = o + n * (g.WallThicknessMm / 2.0 + 300);
            var s = 60.0;
            Poly(segs, doorColor, a - w * s - n * s, a + w * s - n * s, a + w * s + n * s, a - w * s + n * s, a - w * s - n * s);

            foreach (var p in d.Placements) AddPlacement(segs, p, alpha);
        }

        private static void AddPlacement(List<Segment> segs, CalculatedPlacement p, int alpha)
        {
            var color = p.HasErrors ? new ColorWithTransparency(230, 70, 70, (uint)alpha) : CategoryColor(p.Category, alpha);
            var size = Math.Max(40.0, p.PreviewSizeMm);
            var f = p.Facing.IsZero ? Vec3.UnitY : p.Facing;
            var side = Vec3.UnitZ.Cross(f).Normalize();
            var c = p.Position;
            var h = size / 2.0;
            var d = size / 4.0; // devices are shallow boxes

            var corners = new List<Vec3>();
            foreach (var sz in new[] { -h, h })
                foreach (var sy in new[] { -d, d })
                    foreach (var sx in new[] { -h, h })
                        corners.Add(c + side * sx + f * sy + Vec3.UnitZ * sz);
            // box edges (index = z*4 + y*2 + x)
            int[,] edges = { { 0, 1 }, { 2, 3 }, { 4, 5 }, { 6, 7 }, { 0, 2 }, { 1, 3 }, { 4, 6 }, { 5, 7 }, { 0, 4 }, { 1, 5 }, { 2, 6 }, { 3, 7 } };
            for (int i = 0; i < edges.GetLength(0); i++) Line(segs, color, corners[edges[i, 0]], corners[edges[i, 1]]);

            // Facing arrow
            Arrow(segs, color, c, c + f * (size * 1.6), size * 0.35);
        }

        private static ColorWithTransparency CategoryColor(ComponentCategory c, int alpha)
        {
            switch (c)
            {
                case ComponentCategory.CardReader:
                case ComponentCategory.Keypad:
                    return new ColorWithTransparency(0, 150, 255, (uint)alpha);
                case ComponentCategory.DoorContact:
                    return new ColorWithTransparency(32, 201, 151, (uint)alpha);
                case ComponentCategory.ElectricLock:
                    return new ColorWithTransparency(255, 170, 0, (uint)alpha);
                case ComponentCategory.RexPir:
                case ComponentCategory.RexButton:
                    return new ColorWithTransparency(190, 110, 255, (uint)alpha);
                case ComponentCategory.EmergencyRelease:
                case ComponentCategory.PanicButton:
                    return new ColorWithTransparency(255, 90, 160, (uint)alpha);
                default:
                    return new ColorWithTransparency(200, 200, 200, (uint)alpha);
            }
        }

        private static void Line(List<Segment> segs, ColorWithTransparency c, Vec3 a, Vec3 b) =>
            segs.Add(new Segment { A = RevitUnits.ToXyzFt(a), B = RevitUnits.ToXyzFt(b), Color = c });

        private static void Poly(List<Segment> segs, ColorWithTransparency c, params Vec3[] pts)
        {
            for (int i = 0; i + 1 < pts.Length; i++) Line(segs, c, pts[i], pts[i + 1]);
        }

        private static void Arrow(List<Segment> segs, ColorWithTransparency c, Vec3 from, Vec3 to, double head)
        {
            Line(segs, c, from, to);
            var dir = (to - from).Normalize();
            if (dir.IsZero) return;
            var side = Vec3.UnitZ.Cross(dir).Normalize();
            if (side.IsZero) side = Vec3.UnitX;
            Line(segs, c, to, to - dir * head + side * (head * 0.5));
            Line(segs, c, to, to - dir * head - side * (head * 0.5));
        }

        private static Outline ComputeBounds(List<Segment> segs)
        {
            if (segs.Count == 0) return null;
            var pts = segs.SelectMany(s => new[] { s.A, s.B }).ToList();
            var pad = 0.5;
            return new Outline(
                new XYZ(pts.Min(p => p.X) - pad, pts.Min(p => p.Y) - pad, pts.Min(p => p.Z) - pad),
                new XYZ(pts.Max(p => p.X) + pad, pts.Max(p => p.Y) + pad, pts.Max(p => p.Z) + pad));
        }

        // ------------------------------------------------------------------ IDirectContext3DServer

        public Guid GetServerId() => ServerId;
        public string GetVendorId() => "RKTL";
        public ExternalServiceId GetServiceId() => ExternalServices.BuiltInExternalServices.DirectContext3DService;
        public string GetName() => "Sentinel Preview";
        public string GetDescription() => "Transient preview of Sentinel door set placements.";
        public string GetApplicationId() => "";
        public string GetSourceId() => "";
        public bool UsesHandles() => false;
        public bool UseInTransparentPass(View view) => false;

        public bool CanExecute(View view)
        {
            lock (_lock)
            {
                if (_segments.Count == 0 || _document == null || view == null) return false;
                try
                {
                    if (!view.Document.Equals(_document)) return false;
                }
                catch { return false; }
                return view is View3D || view is ViewPlan || view is ViewSection;
            }
        }

        public Outline GetBoundingBox(View view)
        {
            lock (_lock) return _bounds;
        }

        public void RenderScene(View view, DisplayStyle displayStyle)
        {
            try
            {
                lock (_lock)
                {
                    if (_segments.Count == 0) return;
                    if (_dirty || _vb == null || !_vb.IsValid() || !_ib.IsValid()) Rebuild();
                    if (_vb == null || _lineCount == 0) return;
                    DrawContext.FlushBuffer(_vb, _vertexCount, _ib, IndexLine.GetSizeInShortInts() * _lineCount,
                        _format, _effect, PrimitiveType.LineList, 0, _lineCount);
                }
            }
            catch (Exception ex)
            {
                SentinelLog.Error("Preview render failed", ex);
            }
        }

        private void Rebuild()
        {
            _dirty = false;
            // 16-bit indices: keep well below 65535 vertices.
            var segs = _segments.Take(30000).ToList();
            _vertexCount = segs.Count * 2;
            _lineCount = segs.Count;
            if (_lineCount == 0) { _vb = null; return; }

            var vertexFloats = VertexPositionColored.GetSizeInFloats() * _vertexCount;
            _vb = new VertexBuffer(vertexFloats);
            _vb.Map(vertexFloats);
            var vs = _vb.GetVertexStreamPositionColored();
            foreach (var s in segs)
            {
                vs.AddVertex(new VertexPositionColored(s.A, s.Color));
                vs.AddVertex(new VertexPositionColored(s.B, s.Color));
            }
            _vb.Unmap();

            var indexShorts = IndexLine.GetSizeInShortInts() * _lineCount;
            _ib = new IndexBuffer(indexShorts);
            _ib.Map(indexShorts);
            var istream = _ib.GetIndexStreamLine();
            for (int i = 0; i < _lineCount; i++) istream.AddLine(new IndexLine(i * 2, i * 2 + 1));
            _ib.Unmap();

            _format = new VertexFormat(VertexFormatBits.PositionColored);
            _effect = new EffectInstance(VertexFormatBits.PositionColored);
        }
    }
}
