using System;
using System.Collections.Generic;
using System.Linq;
using Sentinel.Core.Models;
using Sentinel.Core.Rules;
using Sentinel.UI.Mvvm;

namespace Sentinel.UI.ViewModels
{
    /// <summary>Display texts for enums (single source for combo boxes and read-only text).</summary>
    public static class UiChoices
    {
        public static readonly List<Option<PlacementReference>> References = new List<Option<PlacementReference>>
        {
            new Option<PlacementReference>(PlacementReference.LatchJamb, "Latch jamb (opening edge)"),
            new Option<PlacementReference>(PlacementReference.HingeJamb, "Hinge jamb"),
            new Option<PlacementReference>(PlacementReference.DoorCenter, "Door centre")
        };

        public static readonly List<Option<PlacementSide>> Sides = new List<Option<PlacementSide>>
        {
            new Option<PlacementSide>(PlacementSide.UnsecuredSide, "Unsecured side"),
            new Option<PlacementSide>(PlacementSide.SecuredSide, "Secured side"),
            new Option<PlacementSide>(PlacementSide.SideA, "Side A"),
            new Option<PlacementSide>(PlacementSide.SideB, "Side B"),
            new Option<PlacementSide>(PlacementSide.InWall, "In wall / frame")
        };

        public static readonly List<Option<HeightReference>> HeightReferences = new List<Option<HeightReference>>
        {
            new Option<HeightReference>(HeightReference.DoorBottom, "from door bottom"),
            new Option<HeightReference>(HeightReference.DoorTop, "from door head")
        };

        public static readonly List<Option<OrientationMode>> Orientations = new List<Option<OrientationMode>>
        {
            new Option<OrientationMode>(OrientationMode.FaceAwayFromWall, "Face away from wall"),
            new Option<OrientationMode>(OrientationMode.FaceTowardDoor, "Face toward door"),
            new Option<OrientationMode>(OrientationMode.FollowWall, "Follow wall"),
            new Option<OrientationMode>(OrientationMode.Fixed, "Fixed rotation")
        };

        public static readonly List<Option<AccessDirection>> Directions = new List<Option<AccessDirection>>
        {
            new Option<AccessDirection>(AccessDirection.SideAToSideB, "Side A → Side B"),
            new Option<AccessDirection>(AccessDirection.SideBToSideA, "Side B → Side A"),
            new Option<AccessDirection>(AccessDirection.Both, "Both directions")
        };

        public static readonly List<Option<HostBehavior>> HostBehaviors = new List<Option<HostBehavior>>
        {
            new Option<HostBehavior>(HostBehavior.Auto, "Auto (face-based → wall face, else unhosted)"),
            new Option<HostBehavior>(HostBehavior.LinkedWallFace, "Host on linked wall face"),
            new Option<HostBehavior>(HostBehavior.Unhosted, "Unhosted / work plane")
        };

        public static readonly List<Option<ComponentCategory>> Categories =
            Enum.GetValues(typeof(ComponentCategory)).Cast<ComponentCategory>()
                .Select(c => new Option<ComponentCategory>(c, CategoryText(c))).ToList();

        public static readonly List<Option<DiscoveryScope>> Scopes = new List<Option<DiscoveryScope>>
        {
            new Option<DiscoveryScope>(DiscoveryScope.AllDoors, "All linked doors"),
            new Option<DiscoveryScope>(DiscoveryScope.ActiveView, "Doors in active view"),
            new Option<DiscoveryScope>(DiscoveryScope.SelectedLevels, "Selected levels")
        };

        public static readonly List<Option<HingeSide>> HingeSides = new List<Option<HingeSide>>
        {
            new Option<HingeSide>(HingeSide.NegativeWidthAxis, "Negative width axis"),
            new Option<HingeSide>(HingeSide.PositiveWidthAxis, "Positive width axis")
        };

        public static readonly List<Option<RuleOperator>> Operators =
            Enum.GetValues(typeof(RuleOperator)).Cast<RuleOperator>()
                .Select(o => new Option<RuleOperator>(o, AssignmentRuleEvaluator.OperatorText(o))).ToList();

        public static readonly List<Option<RuleMatchMode>> MatchModes = new List<Option<RuleMatchMode>>
        {
            new Option<RuleMatchMode>(RuleMatchMode.All, "All conditions (AND)"),
            new Option<RuleMatchMode>(RuleMatchMode.Any, "Any condition (OR)")
        };

        public static string CategoryText(ComponentCategory c)
        {
            switch (c)
            {
                case ComponentCategory.CardReader: return "Card Reader";
                case ComponentCategory.DoorContact: return "Door Contact";
                case ComponentCategory.ElectricLock: return "Electric Lock";
                case ComponentCategory.RexPir: return "REX PIR";
                case ComponentCategory.RexButton: return "REX Button";
                case ComponentCategory.EmergencyRelease: return "Emergency Release";
                case ComponentCategory.GlassBreakDetector: return "Glass Break Detector";
                case ComponentCategory.PirDetector: return "PIR Detector";
                case ComponentCategory.PanicButton: return "Panic Button";
                default: return c.ToString();
            }
        }

        public static string Text<T>(IEnumerable<Option<T>> options, T value)
        {
            var o = options.FirstOrDefault(x => EqualityComparer<T>.Default.Equals(x.Value, value));
            return o != null ? o.Text : value?.ToString();
        }

        public static Option<T> Find<T>(IEnumerable<Option<T>> options, T value) =>
            options.FirstOrDefault(x => EqualityComparer<T>.Default.Equals(x.Value, value));
    }
}
