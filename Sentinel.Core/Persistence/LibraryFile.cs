using System;
using System.Collections.Generic;
using System.Linq;
using Sentinel.Core.Models;
using Sentinel.Core.Rules;

namespace Sentinel.Core.Persistence
{
    /// <summary>
    /// Portable library (components, door set types, assignment rules) for reuse between projects.
    /// Door set instances are project data and are never exported.
    /// </summary>
    public class LibraryFile
    {
        public const int CurrentFormatVersion = 1;

        public int FormatVersion { get; set; } = CurrentFormatVersion;
        public string ExportedUtc { get; set; }
        public string ExportedBy { get; set; }
        public List<ComponentDefinition> ComponentDefinitions { get; set; } = new List<ComponentDefinition>();
        public List<DoorSetDefinition> DoorSetDefinitions { get; set; } = new List<DoorSetDefinition>();
        public List<AssignmentRule> AssignmentRules { get; set; } = new List<AssignmentRule>();

        public static LibraryFile FromProject(SentinelProject project, string user)
        {
            return new LibraryFile
            {
                ExportedUtc = DateTime.UtcNow.ToString("o"),
                ExportedBy = user,
                ComponentDefinitions = project.ComponentDefinitions.Select(c => c.Clone()).ToList(),
                DoorSetDefinitions = project.DoorSetDefinitions.Select(d => d.Clone()).ToList(),
                AssignmentRules = project.AssignmentRules.Select(r => r.Clone()).ToList()
            };
        }

        public string ToJson() => SentinelProjectSerializer.ToJson(this, true);

        public static LibraryFile Parse(string json)
        {
            var lib = SentinelProjectSerializer.FromJson<LibraryFile>(json);
            if (lib == null) throw new FormatException("The file is not a Sentinel library.");
            if (lib.FormatVersion > CurrentFormatVersion)
                throw new FormatException("The library was written by a newer Sentinel (format " + lib.FormatVersion + ").");
            return lib;
        }

        /// <summary>
        /// Merges the library into the project: items with the same id are replaced, others added.
        /// Returns a human readable summary.
        /// </summary>
        public string MergeInto(SentinelProject project)
        {
            int cAdd = 0, cRep = 0, sAdd = 0, sRep = 0, rAdd = 0, rRep = 0;

            foreach (var c in ComponentDefinitions ?? new List<ComponentDefinition>())
            {
                var i = project.ComponentDefinitions.FindIndex(x => x.Id == c.Id);
                if (i >= 0) { project.ComponentDefinitions[i] = c.Clone(); cRep++; }
                else { project.ComponentDefinitions.Add(c.Clone()); cAdd++; }
            }
            foreach (var d in DoorSetDefinitions ?? new List<DoorSetDefinition>())
            {
                var i = project.DoorSetDefinitions.FindIndex(x => x.Id == d.Id);
                var copy = d.Clone();
                if (i >= 0) { copy.Touch(); project.DoorSetDefinitions[i] = copy; sRep++; }
                else { project.DoorSetDefinitions.Add(copy); sAdd++; }
            }
            foreach (var r in AssignmentRules ?? new List<AssignmentRule>())
            {
                var i = project.AssignmentRules.FindIndex(x => x.Id == r.Id);
                if (i >= 0) { project.AssignmentRules[i] = r.Clone(); rRep++; }
                else { project.AssignmentRules.Add(r.Clone()); rAdd++; }
            }

            return "Components: " + cAdd + " added, " + cRep + " updated. " +
                   "Door set types: " + sAdd + " added, " + sRep + " updated. " +
                   "Rules: " + rAdd + " added, " + rRep + " updated.";
        }
    }
}
