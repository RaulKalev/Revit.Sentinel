using System;
using System.Collections.Generic;
using System.Linq;
using Sentinel.Core.Models;

namespace Sentinel.Core.Geometry
{
    /// <summary>A stretch of wall material along the outward wall normal, relative to a component point (mm).</summary>
    public struct MaterialSpan
    {
        public MaterialSpan(double from, double to)
        {
            From = Math.Min(from, to);
            To = Math.Max(from, to);
        }

        public double From { get; }
        public double To { get; }
    }

    /// <summary>
    /// Keeps wall-side components out of wall material. Walls are often split across models (core wall in one link,
    /// lining in another), so the face of the door's own wall is not always the visible face: a component calculated
    /// on it can end up inside the other model's wall. Revit measures the material along the wall normal through the
    /// calculated point; this decides how far the component must move out.
    /// </summary>
    public static class WallClearance
    {
        /// <summary>Material thinner than this at the point (touching faces, rounding) is not a collision.</summary>
        public const double ToleranceMm = 2;

        /// <summary>Largest push-out accepted; more means the measurement hit something else (e.g. a crossing wall).</summary>
        public const double MaxMm = 400;

        /// <summary>Key of a measurement: component slot and door side (a flipped set is measured again).</summary>
        public static string Key(string slotKey, string side) => slotKey + "@" + side;

        /// <summary>
        /// Distance (mm) the point must move along the outward normal to leave the material it is in: 0 when the point
        /// is in free air (or only touches a face). Touching / overlapping spans (lining on a core wall) are merged.
        /// Returns -1 when the material goes on further than <see cref="MaxMm"/>.
        /// </summary>
        public static double ExitDistance(IEnumerable<MaterialSpan> spans, double toleranceMm = ToleranceMm)
        {
            var list = (spans ?? Enumerable.Empty<MaterialSpan>()).Where(s => s.To - s.From > 1e-6).OrderBy(s => s.From).ToList();
            var exit = 0.0;
            var inside = false;
            bool grew;
            do
            {
                grew = false;
                foreach (var s in list)
                {
                    // The point (or the face it was pushed to) is within this span, with a small tolerance at the start.
                    if (s.From <= exit + toleranceMm && s.To > exit + (inside ? 1e-6 : toleranceMm))
                    {
                        exit = s.To;
                        inside = true;
                        grew = true;
                    }
                }
            } while (grew && exit <= MaxMm);
            if (exit > MaxMm) return -1;
            return inside ? exit : 0.0;
        }

        /// <summary>
        /// Stores the measured push-outs (slot → mm, rounded to 1 mm; zeros dropped). Returns true when they changed.
        /// </summary>
        public static bool Store(DoorSetInstance inst, IDictionary<string, double> measured)
        {
            if (inst == null) return false;
            var next = (measured ?? new Dictionary<string, double>())
                .Where(kv => !string.IsNullOrEmpty(kv.Key) && kv.Value >= 1)
                .ToDictionary(kv => kv.Key, kv => Math.Round(kv.Value));
            var old = inst.WallClearances ?? new Dictionary<string, double>();
            var same = old.Count == next.Count && next.All(kv => old.ContainsKey(kv.Key) && Math.Abs(old[kv.Key] - kv.Value) < 0.5);
            if (same) return false;
            inst.WallClearances = next;
            inst.Touch();
            return true;
        }
    }
}
