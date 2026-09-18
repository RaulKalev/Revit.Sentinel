using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Sentinel.Core.Geometry;

namespace Sentinel.Core.Models
{
    /// <summary>
    /// Persistent identity + last known state of a source door in a linked model.
    /// Identity is resolved in this order: door UniqueId inside the link, then IFC GlobalId. ElementIds are
    /// convenience only (they are not stable across link reloads).
    /// </summary>
    public class SourceDoorReference
    {
        // ---- link identity ----
        /// <summary>UniqueId of the RevitLinkInstance in the host document (SourceLinkIdentifier).</summary>
        public string LinkInstanceUniqueId { get; set; }
        public string LinkName { get; set; }
        public string LinkDocumentTitle { get; set; }
        public string LinkDocumentPath { get; set; }

        // ---- door identity ----
        /// <summary>UniqueId of the door element inside the linked document (SourceDoorIdentifier).</summary>
        public string DoorUniqueId { get; set; }
        public long DoorElementId { get; set; }
        public string IfcGlobalId { get; set; }

        // ---- descriptive data (last known) ----
        public string Mark { get; set; }
        public string FamilyName { get; set; }
        public string TypeName { get; set; }
        public string LevelName { get; set; }
        public string SideARoom { get; set; }
        public string SideBRoom { get; set; }

        // ---- geometry snapshot (host coordinates, mm) ----
        public DoorGeometry LastKnownGeometry { get; set; }

        /// <summary>Selected source parameters (IFC properties etc.) captured at the last sync.</summary>
        public Dictionary<string, string> LastKnownParameters { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public string LastSyncedUtc { get; set; }

        [JsonIgnore]
        public string DisplayName => !string.IsNullOrWhiteSpace(Mark) ? Mark :
            !string.IsNullOrWhiteSpace(TypeName) ? TypeName + " [" + DoorElementId + "]" : "Door " + DoorElementId;

        /// <summary>Key used to match live doors with stored references.</summary>
        [JsonIgnore]
        public string Key => MakeKey(LinkInstanceUniqueId, DoorUniqueId);

        public static string MakeKey(string linkUid, string doorUid) => (linkUid ?? "") + "|" + (doorUid ?? "");

        public SourceDoorReference Clone()
        {
            var c = (SourceDoorReference)MemberwiseClone();
            c.LastKnownGeometry = LastKnownGeometry != null ? LastKnownGeometry.Clone() : null;
            c.LastKnownParameters = new Dictionary<string, string>(
                LastKnownParameters ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
            return c;
        }
    }
}
