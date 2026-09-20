using System;
using System.Linq;

namespace Sentinel.Core.Models
{
    /// <summary>
    /// Zoom amounts for "Zoom to door" (percent of the standard framing: 100 = door plus 1.5 m around it,
    /// 200 = twice as close). The settings stepper moves through <see cref="Steps"/>.
    /// </summary>
    public static class ZoomLevels
    {
        public const double Default = 100;
        public const double Min = 25;
        public const double Max = 400;

        public static readonly double[] Steps = { 25, 50, 75, 100, 125, 150, 200, 300, 400 };

        /// <summary>Stored value made safe (missing / zero / out of range in older or edited data).</summary>
        public static double Clamp(double percent) =>
            double.IsNaN(percent) || percent <= 0 ? Default : Math.Max(Min, Math.Min(Max, percent));

        /// <summary>The next step up (+1) or down (-1) from the current value.</summary>
        public static double Step(double current, int direction)
        {
            var c = Clamp(current);
            return direction > 0
                ? Steps.Where(s => s > c + 0.01).DefaultIfEmpty(Max).First()
                : Steps.Where(s => s < c - 0.01).DefaultIfEmpty(Min).Last();
        }

        /// <summary>
        /// Factor the framed area is multiplied by: 1 at 100 %, 0.5 at 200 % (half the area width = twice as close),
        /// 2 at 50 %.
        /// </summary>
        public static double AreaFactor(double percent) => Default / Clamp(percent);
    }
}
