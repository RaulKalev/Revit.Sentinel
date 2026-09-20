using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace Sentinel.Core.Models
{
    /// <summary>
    /// Per-rule override on one instance. Only non-null values override the template; everything else keeps
    /// following the base set definition.
    /// </summary>
    public class ComponentRuleOverride
    {
        public string RuleId { get; set; }

        /// <summary>Change the component type (e.g. swap reader model) for this door only.</summary>
        public string ComponentDefinitionId { get; set; }

        public PlacementReference? Reference { get; set; }
        public PlacementSide? Side { get; set; }
        public double? AlongWallOffsetMm { get; set; }
        public double? FromWallOffsetMm { get; set; }
        public double? MountingHeightMm { get; set; }
        public HeightReference? HeightReference { get; set; }
        public OrientationMode? Orientation { get; set; }
        public double? RotationDeg { get; set; }

        /// <summary>For a built-in component: the set component that carries it on this door (see PlacementRule.CarrierRuleId).</summary>
        public string CarrierRuleId { get; set; }

        [JsonIgnore]
        public bool IsEmpty =>
            string.IsNullOrEmpty(ComponentDefinitionId) && !Reference.HasValue && !Side.HasValue &&
            !AlongWallOffsetMm.HasValue && !FromWallOffsetMm.HasValue && !MountingHeightMm.HasValue &&
            !HeightReference.HasValue && !Orientation.HasValue && !RotationDeg.HasValue && string.IsNullOrEmpty(CarrierRuleId);

        public ComponentRuleOverride Clone() => (ComponentRuleOverride)MemberwiseClone();
    }

    /// <summary>
    /// Per-instance differences from the base set definition, stored separately from the template:
    /// added components, removed template components and rule overrides.
    /// </summary>
    public class SetOverrides
    {
        public List<SetComponentRule> AddedComponents { get; set; } = new List<SetComponentRule>();
        public List<string> RemovedRuleIds { get; set; } = new List<string>();
        public List<ComponentRuleOverride> RuleOverrides { get; set; } = new List<ComponentRuleOverride>();

        [JsonIgnore]
        public bool IsEmpty =>
            (AddedComponents == null || AddedComponents.Count == 0) &&
            (RemovedRuleIds == null || RemovedRuleIds.Count == 0) &&
            (RuleOverrides == null || RuleOverrides.All(o => o.IsEmpty));

        [JsonIgnore]
        public int Count =>
            (AddedComponents?.Count ?? 0) + (RemovedRuleIds?.Count ?? 0) +
            (RuleOverrides?.Count(o => !o.IsEmpty) ?? 0);

        public ComponentRuleOverride GetOrCreate(string ruleId)
        {
            var o = RuleOverrides.FirstOrDefault(x => x.RuleId == ruleId);
            if (o == null)
            {
                o = new ComponentRuleOverride { RuleId = ruleId };
                RuleOverrides.Add(o);
            }
            return o;
        }

        public ComponentRuleOverride Find(string ruleId) =>
            RuleOverrides == null ? null : RuleOverrides.FirstOrDefault(x => x.RuleId == ruleId);

        public SetOverrides Clone() => new SetOverrides
        {
            AddedComponents = (AddedComponents ?? new List<SetComponentRule>()).Select(x => x.Clone()).ToList(),
            RemovedRuleIds = new List<string>(RemovedRuleIds ?? new List<string>()),
            RuleOverrides = (RuleOverrides ?? new List<ComponentRuleOverride>()).Select(x => x.Clone()).ToList()
        };
    }
}
