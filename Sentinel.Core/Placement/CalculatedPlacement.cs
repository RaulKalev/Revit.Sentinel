using System.Collections.Generic;
using System.Linq;
using Sentinel.Core.Geometry;
using Sentinel.Core.Models;

namespace Sentinel.Core.Placement
{
    public enum ResolvedSide
    {
        SideA,
        SideB,
        InWall
    }

    /// <summary>Where and how one component should be placed. Pure data — used by preview and executor alike.</summary>
    public class CalculatedPlacement
    {
        public string SlotKey { get; set; }
        public string RuleId { get; set; }
        public bool IsMirrorCopy { get; set; }

        public string ComponentDefinitionId { get; set; }
        public string ComponentName { get; set; }
        public ComponentCategory Category { get; set; }
        public string Label { get; set; }
        public string FamilyName { get; set; }
        public string TypeName { get; set; }
        public HostBehavior HostBehavior { get; set; }
        public double PreviewSizeMm { get; set; } = 120;

        /// <summary>Insertion point in host project coordinates (mm).</summary>
        public Vec3 Position { get; set; }

        /// <summary>Horizontal unit vector the device front should point to.</summary>
        public Vec3 Facing { get; set; }

        /// <summary>Plan angle of <see cref="Facing"/> (degrees).</summary>
        public double FacingAngleDeg { get; set; }

        /// <summary>Rotation to apply to the family instance: facing angle + family calibration offset.</summary>
        public double InstanceRotationDeg { get; set; }

        /// <summary>Outward normal of the wall face the component sits on (zero vector for InWall).</summary>
        public Vec3 WallNormal { get; set; }

        public ResolvedSide Side { get; set; }

        /// <summary>Human readable reason for this location (traceability).</summary>
        public string Explanation { get; set; }

        public List<StatusIssue> Issues { get; set; } = new List<StatusIssue>();

        public bool HasErrors => Issues.Any(i => i.Severity == IssueSeverity.Error);
        public bool CanPlace => !HasErrors;
    }

    public class PlacementPlan
    {
        public List<CalculatedPlacement> Placements { get; set; } = new List<CalculatedPlacement>();

        /// <summary>Plan-level issues (geometry, hinge assumptions, missing definition...).</summary>
        public List<StatusIssue> Issues { get; set; } = new List<StatusIssue>();

        public HingeSide ResolvedHinge { get; set; }
        public bool HingeAssumed { get; set; }

        /// <summary>Stable hash of what would be placed; compared with the hash stored at placement time.</summary>
        public string ConfigurationHash { get; set; }

        public IEnumerable<StatusIssue> AllIssues => Issues.Concat(Placements.SelectMany(p => p.Issues));
        public bool HasBlockingErrors => Issues.Any(i => i.Severity == IssueSeverity.Error);
    }
}
