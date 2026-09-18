using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Sentinel.Core.Geometry;
using Sentinel.Core.Models;

namespace Sentinel.Core.Status
{
    public class SourceComparison
    {
        public bool Moved { get; set; }
        public bool Rotated { get; set; }
        public bool Resized { get; set; }
        public bool ParametersChanged { get; set; }
        public List<string> Details { get; set; } = new List<string>();

        public bool HasChanges => Moved || Rotated || Resized || ParametersChanged;
    }

    /// <summary>Compares a stored source door snapshot with the current state from the link.</summary>
    public static class SourceChangeDetector
    {
        public static SourceComparison Compare(SourceDoorReference stored, DoorGeometry current,
            IDictionary<string, string> currentParameters, SentinelSettings settings)
        {
            var result = new SourceComparison();
            settings = settings ?? new SentinelSettings();
            var inv = CultureInfo.InvariantCulture;
            var old = stored?.LastKnownGeometry;

            if (old != null && current != null)
            {
                var d = old.Origin.DistanceTo(current.Origin);
                if (d > settings.SourceMoveToleranceMm)
                {
                    result.Moved = true;
                    result.Details.Add("Door moved " + d.ToString("0", inv) + " mm");
                }

                var r = Angles.Difference(old.RotationDeg, current.RotationDeg);
                if (r > settings.SourceRotationToleranceDeg)
                {
                    result.Rotated = true;
                    result.Details.Add("Door rotated " + r.ToString("0.#", inv) + "°" + (r > 179 ? " (facing flipped)" : ""));
                }

                if (Math.Abs(old.WidthMm - current.WidthMm) > settings.SourceSizeToleranceMm)
                {
                    result.Resized = true;
                    result.Details.Add("Width " + old.WidthMm.ToString("0", inv) + " → " + current.WidthMm.ToString("0", inv) + " mm");
                }
                if (Math.Abs(old.HeightMm - current.HeightMm) > settings.SourceSizeToleranceMm)
                {
                    result.Resized = true;
                    result.Details.Add("Height " + old.HeightMm.ToString("0", inv) + " → " + current.HeightMm.ToString("0", inv) + " mm");
                }
                if (old.Hinge != current.Hinge && old.Hinge != HingeSide.Unknown && current.Hinge != HingeSide.Unknown)
                {
                    result.Rotated = true;
                    result.Details.Add("Door hand flipped");
                }
            }

            if (stored?.LastKnownParameters != null && currentParameters != null)
            {
                foreach (var kv in stored.LastKnownParameters)
                {
                    string now;
                    currentParameters.TryGetValue(kv.Key, out now);
                    if (!string.Equals(kv.Value ?? "", now ?? "", StringComparison.Ordinal))
                    {
                        result.ParametersChanged = true;
                        result.Details.Add(kv.Key + ": \"" + kv.Value + "\" → \"" + now + "\"");
                    }
                }
            }

            return result;
        }
    }

    /// <summary>Detects components that were moved/rotated in Revit after placement.</summary>
    public static class ComponentDriftDetector
    {
        public static bool IsDrifted(PlacedComponentInstance c, Vec3 actualPosition, double actualRotationDeg,
            SentinelSettings settings, out string detail)
        {
            detail = null;
            settings = settings ?? new SentinelSettings();
            if (c == null || !c.PlacedPosition.HasValue) return false;

            var accepted = c.ManualPositionAccepted && c.ManualPositionAcceptedAt.HasValue;
            var reference = accepted ? c.ManualPositionAcceptedAt.Value : c.PlacedPosition.Value;
            var referenceRot = accepted ? c.ManualRotationAcceptedDeg : c.PlacedRotationDeg;

            var d = reference.DistanceTo(actualPosition);
            var r = Angles.Difference(referenceRot, actualRotationDeg);
            var inv = CultureInfo.InvariantCulture;

            if (d > settings.ComponentMoveToleranceMm || r > settings.ComponentRotationToleranceDeg)
            {
                var parts = new List<string>();
                if (d > settings.ComponentMoveToleranceMm) parts.Add("moved " + d.ToString("0", inv) + " mm");
                if (r > settings.ComponentRotationToleranceDeg) parts.Add("rotated " + r.ToString("0.#", inv) + "°");
                detail = string.Join(", ", parts);
                return true;
            }
            return false;
        }
    }
}
