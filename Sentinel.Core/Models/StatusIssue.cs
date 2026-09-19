namespace Sentinel.Core.Models
{
    /// <summary>Machine-readable issue codes so the UI can group and filter reasons.</summary>
    public static class IssueCodes
    {
        public const string FamilyNotConfigured = "FamilyNotConfigured";
        public const string FamilyNotLoaded = "FamilyNotLoaded";
        public const string SourceMissing = "SourceMissing";
        public const string LinkUnavailable = "LinkUnavailable";
        public const string SourceChanged = "SourceChanged";
        public const string WallNotFound = "WallNotFound";
        public const string InvalidPlacementPoint = "InvalidPlacementPoint";
        public const string CreationFailed = "CreationFailed";
        public const string UnsupportedGeometry = "UnsupportedGeometry";
        public const string UnsupportedFamily = "UnsupportedFamily";
        public const string DefinitionMissing = "DefinitionMissing";
        public const string ComponentDefinitionMissing = "ComponentDefinitionMissing";
        public const string ComponentMissing = "ComponentMissing";
        public const string ManuallyModified = "ManuallyModified";
        public const string ConfigurationChanged = "ConfigurationChanged";
        public const string HingeAssumed = "HingeAssumed";
        public const string GeometryEstimated = "GeometryEstimated";
        public const string DuplicateElement = "DuplicateElement";
        public const string HostingFallback = "HostingFallback";
        public const string ParameterNotWritten = "ParameterNotWritten";
        public const string BuiltInFallback = "BuiltInFallback";
        public const string BuiltInUnavailable = "BuiltInUnavailable";
    }

    public class StatusIssue
    {
        public IssueSeverity Severity { get; set; }
        public string Code { get; set; }
        public string Message { get; set; }

        /// <summary>Optional component slot the issue refers to.</summary>
        public string SlotKey { get; set; }

        public StatusIssue() { }

        public StatusIssue(IssueSeverity severity, string code, string message, string slotKey = null)
        {
            Severity = severity;
            Code = code;
            Message = message;
            SlotKey = slotKey;
        }

        public override string ToString() => Severity + ": " + Message;
    }
}
