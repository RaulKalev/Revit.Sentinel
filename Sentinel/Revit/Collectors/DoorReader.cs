using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Mechanical;
using Sentinel.Core.Geometry;
using Sentinel.Core.Models;
using Sentinel.Infrastructure;
using Sentinel.Revit.Geometry;

namespace Sentinel.Revit.Collectors
{
    /// <summary>
    /// Reads a door element from a linked document into a <see cref="DiscoveredDoor"/> (host coordinates, mm).
    /// Handles Revit family doors (FacingOrientation/HandOrientation, host wall) and IFC doors imported as
    /// DirectShapes (footprint estimate + nearest linked wall). Missing data is tolerated and reported.
    /// </summary>
    internal sealed class DoorReader
    {
        private readonly Document _hostDoc;
        private readonly RevitLinkInstance _link;
        private readonly Document _linkDoc;
        private readonly Transform _toHost;
        private readonly SentinelSettings _settings;
        private List<Level> _linkLevels;

        public DoorReader(Document hostDoc, RevitLinkInstance link, SentinelSettings settings)
        {
            _hostDoc = hostDoc;
            _link = link;
            _linkDoc = link.GetLinkDocument();
            _toHost = link.GetTotalTransform();
            _settings = settings ?? new SentinelSettings();
        }

        public DiscoveredDoor Read(Element door)
        {
            var result = new DiscoveredDoor();
            var src = new SourceDoorReference
            {
                LinkInstanceUniqueId = _link.UniqueId,
                LinkName = _link.Name,
                LinkDocumentTitle = _linkDoc.Title,
                LinkDocumentPath = SafePath(_linkDoc),
                DoorUniqueId = door.UniqueId,
                DoorElementId = RevitCompat.IdValue(door.Id),
                IfcGlobalId = ReadIfcGuid(door),
                Mark = ReadMark(door),
                LastSyncedUtc = DateTime.UtcNow.ToString("o")
            };
            ReadTypeNames(door, src);
            result.Current = src;

            DoorGeometry geometry = null;
            try
            {
                geometry = ReadGeometry(door, result.ReadWarnings);
            }
            catch (Exception ex)
            {
                result.ReadWarnings.Add("Door geometry could not be read: " + ex.Message);
                SentinelLog.Error("Door geometry read failed for " + src.DoorUniqueId, ex);
            }
            src.LastKnownGeometry = geometry;

            src.LevelName = ReadLevelName(door, geometry);
            if (geometry != null && geometry.IsValid) ReadRooms(src, geometry);
            src.LastKnownParameters = ReadParameters(door);
            return result;
        }

        // ------------------------------------------------------------------ identity

        public static string ReadIfcGuid(Element e)
        {
            var p = e.get_Parameter(BuiltInParameter.IFC_GUID);
            var v = p != null && p.HasValue ? p.AsString() : null;
            if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
            foreach (var name in new[] { "IfcGUID", "IFCGuid", "GlobalId", "IfcGlobalId" })
            {
                var q = e.LookupParameter(name);
                var s = q != null && q.HasValue && q.StorageType == StorageType.String ? q.AsString() : null;
                if (!string.IsNullOrWhiteSpace(s)) return s.Trim();
            }
            return null;
        }

        private static string ReadMark(Element e)
        {
            var p = e.get_Parameter(BuiltInParameter.ALL_MODEL_MARK);
            var v = p != null && p.HasValue ? p.AsString() : null;
            if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
            foreach (var name in new[] { "Tag", "IfcTag", "Mark", "Reference" })
            {
                var q = e.LookupParameter(name);
                var s = q != null && q.HasValue && q.StorageType == StorageType.String ? q.AsString() : null;
                if (!string.IsNullOrWhiteSpace(s)) return s.Trim();
            }

            // IFC links: the door code (IfcDoor.Name, e.g. "D-102") is the element name; Mark is often empty.
            var elementName = e.Name;
            var typeName = (e.Document.GetElement(e.GetTypeId()) as ElementType)?.Name;
            if (!string.IsNullOrWhiteSpace(elementName) && !string.Equals(elementName, typeName, StringComparison.OrdinalIgnoreCase))
                return elementName.Trim();
            return null;
        }

        private void ReadTypeNames(Element door, SourceDoorReference src)
        {
            var fi = door as FamilyInstance;
            if (fi?.Symbol != null)
            {
                src.FamilyName = fi.Symbol.Family?.Name;
                src.TypeName = fi.Symbol.Name;
                return;
            }
            var type = _linkDoc.GetElement(door.GetTypeId()) as ElementType;
            src.FamilyName = type?.FamilyName;
            src.TypeName = type?.Name;
            if (string.IsNullOrWhiteSpace(src.TypeName))
            {
                foreach (var name in new[] { "IfcObjectType", "ObjectType", "Type" })
                {
                    var q = door.LookupParameter(name);
                    var s = q != null && q.HasValue && q.StorageType == StorageType.String ? q.AsString() : null;
                    if (!string.IsNullOrWhiteSpace(s)) { src.TypeName = s; break; }
                }
            }
            if (string.IsNullOrWhiteSpace(src.TypeName)) src.TypeName = door.Name;
        }

        // ------------------------------------------------------------------ geometry

        private DoorGeometry ReadGeometry(Element door, List<string> warnings)
        {
            var fi = door as FamilyInstance;
            var lp = door.Location as LocationPoint;
            var box = GeometryUtils.GetBox(door);

            XYZ origin, facing, widthAxis;
            double width = 0, height = 0, thickness = 0;
            var hinge = HingeSide.Unknown;
            var source = DoorGeometrySource.FamilyInstance;
            var hingeFromHandle = false;
            var wallMeasured = false;

            if (fi != null && lp != null && IsHorizontal(fi.FacingOrientation) && IsHorizontal(fi.HandOrientation))
            {
                facing = Flat(fi.FacingOrientation);
                widthAxis = Flat(fi.HandOrientation);
                origin = lp.Point;
                width = ReadLength(fi, BuiltInParameter.DOOR_WIDTH, BuiltInParameter.FAMILY_WIDTH_PARAM, "Width");
                height = ReadLength(fi, BuiltInParameter.DOOR_HEIGHT, BuiltInParameter.FAMILY_HEIGHT_PARAM, "Height");
                hinge = _settings.HingeOnHandOrientationSide ? HingeSide.PositiveWidthAxis : HingeSide.NegativeWidthAxis;

                var hostWall = fi.Host as Wall;
                double offset, t;
                if (hostWall != null && GeometryUtils.MeasureWall(hostWall, origin, facing, out offset, out t))
                {
                    origin = origin + facing * offset;
                    thickness = t;
                    wallMeasured = true;
                }
                else if (hostWall != null)
                {
                    thickness = hostWall.Width;
                    wallMeasured = true;
                }
            }
            else
            {
                // IFC DirectShape (or family without orientation): estimate from the plan footprint.
                source = DoorGeometrySource.EstimatedFromGeometry;
                var pts = GeometryUtils.GetPoints(door);
                if (pts.Count < 3 || box == null)
                {
                    warnings.Add("Door has no usable geometry.");
                    return null;
                }
                var rect = FootprintAnalyzer.MinimumAreaRectangle(pts.Select(p => new Vec3(p.X, p.Y, 0)));
                if (rect == null)
                {
                    warnings.Add("Door footprint could not be analysed.");
                    return null;
                }
                facing = new XYZ(rect.ShortAxis.X, rect.ShortAxis.Y, 0);
                widthAxis = new XYZ(rect.LongAxis.X, rect.LongAxis.Y, 0);
                var minZ = pts.Min(p => p.Z);
                var maxZ = pts.Max(p => p.Z);
                origin = new XYZ(rect.CenterX, rect.CenterY, minZ);
                width = ReadLength(door, null, null, "OverallWidth", "Width");
                height = ReadLength(door, null, null, "OverallHeight", "Height");
                if (width <= 0) width = rect.LongLength;
                if (height <= 0) height = maxZ - minZ;
                thickness = rect.ShortLength;

                // Hinge side from the handle: it sticks out of the leaf at handle height, on the latch side.
                var handle = DetectHandle(door, new XYZ(rect.CenterX, rect.CenterY, minZ), widthAxis, facing);
                if (handle.Found)
                {
                    hinge = handle.Hinge;
                    hingeFromHandle = true;
                    warnings.Add("Door orientation estimated from geometry; hinge side read from the door handle (" + handle.Note + ").");
                }
                else
                {
                    warnings.Add("Door orientation estimated from geometry; hinge side unknown (" + handle.Note + ").");
                }
            }

            // No host wall measured yet (IFC, or a family door without a Wall host): find the nearest linked wall.
            if (!wallMeasured)
            {
                double offset, t;
                if (MeasureNearestWall(origin, facing, width, out offset, out t))
                {
                    origin = origin + facing * offset;
                    thickness = t;
                }
                else
                {
                    warnings.Add("Host wall not found; wall thickness taken from the door geometry.");
                    if (thickness <= 0 && box != null) thickness = ExtentAlong(box, facing);
                }
            }

            if (width <= 0 && box != null)
            {
                width = ExtentAlong(box, widthAxis);
                warnings.Add("Door width parameter missing; estimated from the bounding box.");
            }
            if (height <= 0 && box != null)
            {
                height = box.Max.Z - box.Min.Z;
                warnings.Add("Door height parameter missing; estimated from the bounding box.");
            }

            // Link → host.
            var hOrigin = _toHost.OfPoint(origin);
            var hFacing = _toHost.OfVector(facing);
            var hWidth = _toHost.OfVector(widthAxis);

            return new DoorGeometry
            {
                Origin = RevitUnits.ToVec3Mm(hOrigin),
                Facing = RevitUnits.ToVec3Dir(hFacing).Flatten().Normalize(),
                WidthAxis = RevitUnits.ToVec3Dir(hWidth).Flatten().Normalize(),
                WidthMm = RevitUnits.FtToMm(width),
                HeightMm = RevitUnits.FtToMm(height),
                WallThicknessMm = RevitUnits.FtToMm(thickness),
                Hinge = hinge,
                HingeFromHandle = hingeFromHandle,
                Source = source
            };
        }

        /// <summary>Runs <see cref="HandleDetector"/> on the door's geometry pieces, in the door frame (mm).</summary>
        private HandleDetection DetectHandle(Element door, XYZ centre, XYZ widthAxis, XYZ facing)
        {
            try
            {
                var pieces = GeometryUtils.GetPointGroups(door)
                    .Select(g => (IList<Vec3>)g.Select(p =>
                    {
                        var d = p - centre;
                        return new Vec3(RevitUnits.FtToMm(d.DotProduct(widthAxis)), RevitUnits.FtToMm(d.DotProduct(facing)), RevitUnits.FtToMm(d.Z));
                    }).ToList())
                    .ToList();
                var result = HandleDetector.Detect(pieces);
                SentinelLog.Info("Handle check " + (ReadMark(door) ?? door.UniqueId) + ": " + pieces.Count + " piece(s), " + result.Note);
                return result;
            }
            catch (Exception ex)
            {
                SentinelLog.Error("Handle check failed for " + door.UniqueId, ex);
                return new HandleDetection { Note = "handle check failed" };
            }
        }

        private bool MeasureNearestWall(XYZ origin, XYZ facing, double widthFt, out double offset, out double thickness)
        {
            offset = 0;
            thickness = 0;
            try
            {
                var reach = Math.Max(1.0, widthFt);
                var outline = new Outline(
                    new XYZ(origin.X - reach, origin.Y - reach, origin.Z),
                    new XYZ(origin.X + reach, origin.Y + reach, origin.Z + 7.0));
                var probe = origin + XYZ.BasisZ * 3.0; // ~0.9 m above the door bottom
                var walls = new FilteredElementCollector(_linkDoc)
                    .OfCategory(BuiltInCategory.OST_Walls)
                    .WhereElementIsNotElementType()
                    .WherePasses(new BoundingBoxIntersectsFilter(outline))
                    .ToElements();

                var best = double.MaxValue;
                foreach (var w in walls)
                {
                    double o, t;
                    if (!GeometryUtils.MeasureWall(w, probe, facing, out o, out t)) continue;
                    if (t > 3.5 || Math.Abs(o) > t) continue; // point must lie within the wall thickness
                    var score = Math.Abs(o);
                    if (score < best)
                    {
                        best = score;
                        offset = o;
                        thickness = t;
                    }
                }
                return best < double.MaxValue;
            }
            catch (Exception ex)
            {
                SentinelLog.Error("Nearest wall search failed", ex);
                return false;
            }
        }

        private static bool IsHorizontal(XYZ v) => v != null && v.GetLength() > 1e-6 && Math.Abs(v.Normalize().Z) < 0.1;
        private static XYZ Flat(XYZ v) => new XYZ(v.X, v.Y, 0).Normalize();

        private static double ExtentAlong(BoundingBoxXYZ box, XYZ axis)
        {
            var d = box.Max - box.Min;
            return Math.Abs(d.X * axis.X) + Math.Abs(d.Y * axis.Y);
        }

        private double ReadLength(Element e, BuiltInParameter? bip1, BuiltInParameter? bip2, params string[] names)
        {
            var type = _linkDoc.GetElement(e.GetTypeId());
            foreach (var el in new[] { e, type })
            {
                if (el == null) continue;
                foreach (var bip in new[] { bip1, bip2 })
                {
                    if (!bip.HasValue) continue;
                    var p = el.get_Parameter(bip.Value);
                    if (p != null && p.HasValue && p.StorageType == StorageType.Double && p.AsDouble() > 1e-6) return p.AsDouble();
                }
                foreach (var n in names)
                {
                    var p = el.LookupParameter(n);
                    if (p != null && p.HasValue && p.StorageType == StorageType.Double && p.AsDouble() > 1e-6) return p.AsDouble();
                }
            }
            return 0;
        }

        // ------------------------------------------------------------------ context

        private string ReadLevelName(Element door, DoorGeometry g)
        {
            var level = _linkDoc.GetElement(door.LevelId) as Level;
            if (level != null) return level.Name;

            if (_linkLevels == null)
                _linkLevels = new FilteredElementCollector(_linkDoc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.Elevation).ToList();
            if (g == null || _linkLevels.Count == 0) return null;

            var zLink = _toHost.Inverse.OfPoint(RevitUnits.ToXyzFt(g.Origin)).Z;
            var below = _linkLevels.LastOrDefault(l => l.Elevation <= zLink + 0.05);
            return (below ?? _linkLevels.First()).Name;
        }

        private void ReadRooms(SourceDoorReference src, DoorGeometry g)
        {
            var probe = g.WallThicknessMm / 2.0 + _settings.RoomProbeDistanceMm;
            var up = Vec3.UnitZ * 1000;
            src.SideARoom = RoomAt(g.Origin + g.Facing * probe + up);
            src.SideBRoom = RoomAt(g.Origin - g.Facing * probe + up);
        }

        private string RoomAt(Vec3 hostPointMm)
        {
            try
            {
                var hostPt = RevitUnits.ToXyzFt(hostPointMm);
                var linkPt = _toHost.Inverse.OfPoint(hostPt);

                var room = _linkDoc.GetRoomAtPoint(linkPt) ?? _hostDoc.GetRoomAtPoint(hostPt);
                if (room != null) return RoomText(room);

                var space = _hostDoc.GetSpaceAtPoint(hostPt);
                if (space != null) return RoomText(space);
            }
            catch (Exception ex)
            {
                SentinelLog.Warn("Room probe failed: " + ex.Message);
            }
            return null;
        }

        private static string RoomText(SpatialElement s)
        {
            var number = s.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString();
            var name = s.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString();
            var text = ((number ?? "") + " " + (name ?? "")).Trim();
            return string.IsNullOrEmpty(text) ? s.Name : text;
        }

        private Dictionary<string, string> ReadParameters(Element door)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var type = _linkDoc.GetElement(door.GetTypeId());
            foreach (var name in _settings.CapturedParameterNames ?? new List<string>())
            {
                if (string.IsNullOrWhiteSpace(name) || dict.ContainsKey(name)) continue;
                var p = door.LookupParameter(name) ?? type?.LookupParameter(name);
                if (p == null || !p.HasValue) continue;
                var v = ParameterText(p);
                if (!string.IsNullOrEmpty(v)) dict[name] = v;
            }
            return dict;
        }

        public static string ParameterText(Parameter p)
        {
            switch (p.StorageType)
            {
                case StorageType.String: return p.AsString();
                case StorageType.Integer: return p.AsValueString() ?? p.AsInteger().ToString(CultureInfo.InvariantCulture);
                case StorageType.Double: return p.AsValueString() ?? p.AsDouble().ToString(CultureInfo.InvariantCulture);
                case StorageType.ElementId: return p.AsValueString();
                default: return null;
            }
        }

        private static string SafePath(Document d)
        {
            try { return d.PathName; } catch { return null; }
        }
    }
}
