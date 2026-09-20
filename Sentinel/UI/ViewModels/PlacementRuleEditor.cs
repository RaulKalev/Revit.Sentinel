using System;
using System.Collections.Generic;
using System.ComponentModel;
using Sentinel.Core.Models;
using Sentinel.UI.Mvvm;

namespace Sentinel.UI.ViewModels
{
    /// <summary>
    /// Editable form of a <see cref="PlacementRule"/>. Numbers are edited as text so blanks (e.g. "use default
    /// height") and invalid input can be represented; invalid values are reported through IDataErrorInfo and never
    /// written to the model.
    /// </summary>
    public class PlacementRuleEditor : ObservableObject, IDataErrorInfo
    {
        private Option<PlacementReference> _reference;
        private Option<PlacementSide> _side;
        private string _along = "0";
        private string _fromWall = "0";
        private string _height = "";
        private Option<HeightReference> _heightRef;
        private Option<OrientationMode> _orientation;
        private string _rotation = "0";
        private bool _duplicateBoth;
        private string _carrierRuleId; // not edited here: kept so saving the fields never drops it

        /// <summary>Which set component carries a built-in component (chosen next to the editor, not in it).</summary>
        public string CarrierRuleId { get => _carrierRuleId; set => _carrierRuleId = value; }

        public event EventHandler Edited;

        public List<Option<PlacementReference>> References => UiChoices.References;
        public List<Option<PlacementSide>> Sides => UiChoices.Sides;
        public List<Option<HeightReference>> HeightReferences => UiChoices.HeightReferences;
        public List<Option<OrientationMode>> Orientations => UiChoices.Orientations;

        /// <summary>When false the height field is required (component defaults); when true blank = component default.</summary>
        public bool HeightMayBeBlank { get; set; } = true;

        public Option<PlacementReference> Reference { get => _reference; set { if (Set(ref _reference, value)) RaiseEdited(); } }
        public Option<PlacementSide> Side { get => _side; set { if (Set(ref _side, value)) RaiseEdited(); } }
        public string AlongWall { get => _along; set { if (Set(ref _along, value)) RaiseEdited(); } }
        public string FromWall { get => _fromWall; set { if (Set(ref _fromWall, value)) RaiseEdited(); } }
        public string Height { get => _height; set { if (Set(ref _height, value)) RaiseEdited(); } }
        public Option<HeightReference> HeightRef { get => _heightRef; set { if (Set(ref _heightRef, value)) RaiseEdited(); } }
        public Option<OrientationMode> Orientation { get => _orientation; set { if (Set(ref _orientation, value)) RaiseEdited(); } }
        public string Rotation { get => _rotation; set { if (Set(ref _rotation, value)) RaiseEdited(); } }
        public bool DuplicateBoth { get => _duplicateBoth; set { if (Set(ref _duplicateBoth, value)) RaiseEdited(); } }

        private bool _loading;
        private bool _showAdvanced;

        /// <summary>Progressive disclosure: from-wall offset, height reference, orientation, rotation, duplication.</summary>
        public bool ShowAdvanced { get => _showAdvanced; set => Set(ref _showAdvanced, value); }

        /// <summary>All current field errors as one readable line (shown next to the fields, not only in tooltips).</summary>
        public string ValidationSummary
        {
            get
            {
                var parts = new System.Collections.Generic.List<string>();
                if (this[nameof(AlongWall)] != null) parts.Add("Along wall: " + this[nameof(AlongWall)]);
                if (this[nameof(FromWall)] != null) parts.Add("From wall: " + this[nameof(FromWall)]);
                if (this[nameof(Height)] != null) parts.Add("Height: " + this[nameof(Height)]);
                if (this[nameof(Rotation)] != null) parts.Add("Rotation: " + this[nameof(Rotation)]);
                return string.Join("  ", parts);
            }
        }

        public bool HasErrors => !IsValid;

        private void RaiseEdited()
        {
            OnPropertiesChanged(nameof(ValidationSummary), nameof(HasErrors));
            // Advanced fields with errors stay visible so the problem is never hidden behind a disclosure.
            if ((this[nameof(FromWall)] != null || this[nameof(Rotation)] != null) && !ShowAdvanced) ShowAdvanced = true;
            if (!_loading) Edited?.Invoke(this, EventArgs.Empty);
        }

        public void Load(PlacementRule rule)
        {
            _loading = true;
            rule = rule ?? new PlacementRule();
            Reference = UiChoices.Find(UiChoices.References, rule.Reference);
            Side = UiChoices.Find(UiChoices.Sides, rule.Side);
            AlongWall = NumberText.Format(rule.AlongWallOffsetMm);
            FromWall = NumberText.Format(rule.FromWallOffsetMm);
            Height = NumberText.Format(rule.MountingHeightMm);
            HeightRef = UiChoices.Find(UiChoices.HeightReferences, rule.HeightReference);
            Orientation = UiChoices.Find(UiChoices.Orientations, rule.Orientation);
            Rotation = NumberText.Format(rule.RotationDeg);
            DuplicateBoth = rule.DuplicateWhenBothDirections;
            _carrierRuleId = rule.CarrierRuleId;
            _loading = false;
            RefreshAll();
        }

        public bool IsValid =>
            this[nameof(AlongWall)] == null && this[nameof(FromWall)] == null &&
            this[nameof(Height)] == null && this[nameof(Rotation)] == null;

        /// <summary>Returns the edited rule, or null when any field is invalid.</summary>
        public PlacementRule ToRule()
        {
            if (!IsValid) return null;
            double along, from, rot, h;
            NumberText.TryParse(AlongWall, out along);
            NumberText.TryParse(FromWall, out from);
            NumberText.TryParse(Rotation, out rot);
            double? height = null;
            if (NumberText.TryParse(Height, out h)) height = h;

            return new PlacementRule
            {
                Reference = Reference?.Value ?? PlacementReference.LatchJamb,
                Side = Side?.Value ?? PlacementSide.UnsecuredSide,
                AlongWallOffsetMm = along,
                FromWallOffsetMm = from,
                MountingHeightMm = height,
                HeightReference = HeightRef?.Value ?? HeightReference.DoorBottom,
                Orientation = Orientation?.Value ?? OrientationMode.FaceAwayFromWall,
                RotationDeg = rot,
                DuplicateWhenBothDirections = DuplicateBoth,
                CarrierRuleId = _carrierRuleId
            };
        }

        public string Error => null;

        public string this[string columnName]
        {
            get
            {
                double v;
                switch (columnName)
                {
                    case nameof(AlongWall):
                        return NumberText.TryParse(AlongWall, out v) ? null : "Enter a number (mm).";
                    case nameof(FromWall):
                        return NumberText.TryParse(FromWall, out v) ? null : "Enter a number (mm).";
                    case nameof(Rotation):
                        return NumberText.TryParse(Rotation, out v) ? null : "Enter a number (degrees).";
                    case nameof(Height):
                        if (string.IsNullOrWhiteSpace(Height)) return HeightMayBeBlank ? null : "Enter a height (mm).";
                        return NumberText.TryParse(Height, out v) ? null : "Enter a number (mm) or leave blank for the component default.";
                    default:
                        return null;
                }
            }
        }
    }
}
