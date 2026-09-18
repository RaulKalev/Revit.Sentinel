using System.Collections.Generic;
using System.Linq;
using Sentinel.Core.Models;

namespace Sentinel.Core.Library
{
    /// <summary>
    /// Starter content for a new project: a component library without family mappings (the user maps their own
    /// families on the Components page) and two example door set types. Nothing here is project specific.
    /// </summary>
    public static class DefaultLibrary
    {
        public static List<ComponentDefinition> CreateComponents()
        {
            return new List<ComponentDefinition>
            {
                Component("Card Reader", ComponentCategory.CardReader, 1000,
                    Rule(PlacementReference.LatchJamb, PlacementSide.UnsecuredSide, 150, 0, null, HeightReference.DoorBottom, OrientationMode.FaceAwayFromWall, duplicate: true),
                    "Reader on the unsecured side next to the latch jamb."),
                Component("Door Contact", ComponentCategory.DoorContact, 50,
                    Rule(PlacementReference.LatchJamb, PlacementSide.SecuredSide, -100, 0, null, HeightReference.DoorTop, OrientationMode.FaceAwayFromWall),
                    "Contact at the door head over the latch edge, secured side."),
                Component("Electric Lock", ComponentCategory.ElectricLock, 1050,
                    Rule(PlacementReference.LatchJamb, PlacementSide.InWall, 0, 0, null, HeightReference.DoorBottom, OrientationMode.FollowWall),
                    "Lock in the latch jamb."),
                Component("REX PIR", ComponentCategory.RexPir, 150,
                    Rule(PlacementReference.DoorCenter, PlacementSide.SecuredSide, 0, 0, null, HeightReference.DoorTop, OrientationMode.FaceAwayFromWall),
                    "Request-to-exit detector above the door on the secured side."),
                Component("REX Button", ComponentCategory.RexButton, 1000,
                    Rule(PlacementReference.LatchJamb, PlacementSide.SecuredSide, 150, 0, null, HeightReference.DoorBottom, OrientationMode.FaceAwayFromWall),
                    "Exit button on the secured side next to the latch jamb."),
                Component("Emergency Release", ComponentCategory.EmergencyRelease, 1200,
                    Rule(PlacementReference.LatchJamb, PlacementSide.SecuredSide, 300, 0, null, HeightReference.DoorBottom, OrientationMode.FaceAwayFromWall),
                    "Emergency door release on the secured side."),
                Component("Keypad", ComponentCategory.Keypad, 1000,
                    Rule(PlacementReference.LatchJamb, PlacementSide.UnsecuredSide, 150, 0, null, HeightReference.DoorBottom, OrientationMode.FaceAwayFromWall, duplicate: true),
                    "Keypad on the unsecured side."),
                Component("Intercom", ComponentCategory.Intercom, 1300,
                    Rule(PlacementReference.LatchJamb, PlacementSide.UnsecuredSide, 350, 0, null, HeightReference.DoorBottom, OrientationMode.FaceAwayFromWall),
                    "Intercom panel on the unsecured side."),
                Component("Glass Break Detector", ComponentCategory.GlassBreakDetector, 2400,
                    Rule(PlacementReference.DoorCenter, PlacementSide.SecuredSide, 0, 0, null, HeightReference.DoorBottom, OrientationMode.FaceAwayFromWall),
                    "Room device (room sets are planned for a later milestone)."),
                Component("PIR Detector", ComponentCategory.PirDetector, 2400,
                    Rule(PlacementReference.DoorCenter, PlacementSide.SecuredSide, 0, 0, null, HeightReference.DoorBottom, OrientationMode.FaceAwayFromWall),
                    "Room device (room sets are planned for a later milestone)."),
                Component("Panic Button", ComponentCategory.PanicButton, 1000,
                    Rule(PlacementReference.LatchJamb, PlacementSide.SecuredSide, 500, 0, null, HeightReference.DoorBottom, OrientationMode.FaceAwayFromWall),
                    "Hold-up / panic button."),
                Component("Custom", ComponentCategory.Custom, 1000,
                    Rule(PlacementReference.DoorCenter, PlacementSide.SideA, 0, 0, null, HeightReference.DoorBottom, OrientationMode.FaceAwayFromWall),
                    "User-defined component.")
            };
        }

        public static List<DoorSetDefinition> CreateDoorSets(IList<ComponentDefinition> components)
        {
            ComponentDefinition Find(ComponentCategory c) => components.FirstOrDefault(x => x.Category == c);

            var reader = Find(ComponentCategory.CardReader);
            var contact = Find(ComponentCategory.DoorContact);
            var lockDef = Find(ComponentCategory.ElectricLock);
            var rex = Find(ComponentCategory.RexPir);

            var ds01 = new DoorSetDefinition
            {
                Code = "DS-01",
                Name = "Standard Office Access",
                Description = "Example set: reader, door contact, electric lock.",
                DefaultAccessDirection = AccessDirection.SideAToSideB
            };
            AddSlot(ds01, reader, "Reader");
            AddSlot(ds01, contact, "Door Contact");
            AddSlot(ds01, lockDef, "Lock");

            var ds02 = new DoorSetDefinition
            {
                Code = "DS-02",
                Name = "Secure Technical Door",
                Description = "Example set: reader, door contact, electric lock, REX PIR.",
                DefaultAccessDirection = AccessDirection.SideAToSideB
            };
            AddSlot(ds02, reader, "Reader");
            AddSlot(ds02, contact, "Door Contact");
            AddSlot(ds02, lockDef, "Lock");
            AddSlot(ds02, rex, "REX");

            return new List<DoorSetDefinition> { ds01, ds02 };
        }

        /// <summary>Creates a project populated with the starter library.</summary>
        public static SentinelProject CreateProject()
        {
            var p = new SentinelProject();
            p.ComponentDefinitions = CreateComponents();
            p.DoorSetDefinitions = CreateDoorSets(p.ComponentDefinitions);
            return p;
        }

        private static void AddSlot(DoorSetDefinition set, ComponentDefinition component, string label)
        {
            if (component == null) return;
            set.Components.Add(new SetComponentRule
            {
                ComponentDefinitionId = component.Id,
                Label = label,
                Rule = component.DefaultPlacement.Clone()
            });
        }

        private static ComponentDefinition Component(string name, ComponentCategory category, double height, PlacementRule rule, string description)
        {
            return new ComponentDefinition
            {
                Name = name,
                Category = category,
                DefaultMountingHeightMm = height,
                DefaultPlacement = rule,
                Description = description,
                HostBehavior = HostBehavior.Auto
            };
        }

        private static PlacementRule Rule(PlacementReference reference, PlacementSide side, double along, double fromWall,
            double? height, HeightReference heightRef, OrientationMode orientation, bool duplicate = false)
        {
            return new PlacementRule
            {
                Reference = reference,
                Side = side,
                AlongWallOffsetMm = along,
                FromWallOffsetMm = fromWall,
                MountingHeightMm = height,
                HeightReference = heightRef,
                Orientation = orientation,
                DuplicateWhenBothDirections = duplicate
            };
        }
    }
}
