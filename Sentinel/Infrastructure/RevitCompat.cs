using Autodesk.Revit.DB;
using Sentinel.Core.Geometry;

namespace Sentinel.Infrastructure
{
    /// <summary>
    /// Single place for Revit-version-sensitive API usage.
    ///
    /// Supported targets: Revit 2024 (net48, symbol REVIT2024) and Revit 2026 (net8.0-windows, symbol REVIT2026).
    ///  - ElementId: <c>ElementId.Value</c> (long) and <c>new ElementId(long)</c> exist in both 2024 and 2026
    ///    (IntegerValue was removed in 2026). Revit 2021–2023 would need IntegerValue here.
    ///  - FilteredElementCollector(document, viewId, linkId) (linked elements visible in a host view) is used by
    ///    door discovery and exists since Revit 2024; older versions would need a crop-box filter fallback.
    ///  - Selection.SetReferences (select linked elements) exists since Revit 2023.
    /// No other version-specific paths are used; shared code is C# 7.3.
    /// </summary>
    internal static class RevitCompat
    {
        public static long IdValue(ElementId id) => id == null ? -1 : id.Value;

        public static ElementId ToElementId(long value) => new ElementId(value);

        public static bool IsValid(ElementId id) => id != null && id != ElementId.InvalidElementId;
    }

    /// <summary>Unit and type conversion between Revit (feet, XYZ) and Sentinel.Core (millimetres, Vec3).</summary>
    internal static class RevitUnits
    {
        public const double MmPerFoot = 304.8;

        public static double FtToMm(double ft) => ft * MmPerFoot;
        public static double MmToFt(double mm) => mm / MmPerFoot;

        public static Vec3 ToVec3Mm(XYZ p) => p == null ? Vec3.Zero : new Vec3(FtToMm(p.X), FtToMm(p.Y), FtToMm(p.Z));
        public static Vec3 ToVec3Dir(XYZ v) => v == null ? Vec3.Zero : new Vec3(v.X, v.Y, v.Z);

        public static XYZ ToXyzFt(Vec3 p) => new XYZ(MmToFt(p.X), MmToFt(p.Y), MmToFt(p.Z));
        public static XYZ ToXyzDir(Vec3 v) => new XYZ(v.X, v.Y, v.Z);
    }
}
