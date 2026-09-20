using System;
using System.Collections.Generic;
using System.Linq;

namespace Sentinel.Core.Models
{
    /// <summary>
    /// Keywords that make a floor plan preferred for "Zoom to door" (Settings, e.g. "Security, EL"). Earlier keywords
    /// win over later ones; matching ignores case.
    /// </summary>
    public static class PlanKeywords
    {
        public static List<string> Parse(string text) =>
            (text ?? "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(k => k.Trim()).Where(k => k.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        /// <summary>Index of the first keyword the name contains; <see cref="int.MaxValue"/> when none (or no keywords).</summary>
        public static int Rank(string planName, IList<string> keywords)
        {
            if (string.IsNullOrEmpty(planName) || keywords == null) return int.MaxValue;
            for (var i = 0; i < keywords.Count; i++)
                if (planName.IndexOf(keywords[i], StringComparison.OrdinalIgnoreCase) >= 0) return i;
            return int.MaxValue;
        }

        /// <summary>The candidates with the best keyword rank; all of them when none matches a keyword.</summary>
        public static List<T> Preferred<T>(IEnumerable<T> candidates, Func<T, string> name, IList<string> keywords)
        {
            var list = (candidates ?? Enumerable.Empty<T>()).ToList();
            if (keywords == null || keywords.Count == 0 || list.Count == 0) return list;
            var best = list.Min(c => Rank(name(c), keywords));
            return best == int.MaxValue ? list : list.Where(c => Rank(name(c), keywords) == best).ToList();
        }
    }
}
