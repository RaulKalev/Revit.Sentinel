using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Sentinel.Core.Models;
using Sentinel.Core.Rules;

namespace Sentinel.Core.Persistence
{
    /// <summary>
    /// Storage-agnostic representation of the persisted data: a version number, a header document and one JSON
    /// envelope per item. The Revit layer stores exactly these three values in Extensible Storage, so adding new
    /// item types or fields never requires a new Revit schema.
    /// </summary>
    public class StoragePayload
    {
        public int DataVersion { get; set; }
        public string Header { get; set; }
        public List<string> Items { get; set; } = new List<string>();
    }

    public class ProjectHeader
    {
        public int DataVersion { get; set; }
        public string ProjectId { get; set; }
        public SentinelSettings Settings { get; set; }
        public string SavedUtc { get; set; }
        public string SavedBy { get; set; }
        public string PluginVersion { get; set; }
    }

    public class LoadResult
    {
        public SentinelProject Project { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();

        /// <summary>Data was written by a newer Sentinel. The project must be treated as read-only.</summary>
        public bool IsNewerThanSupported { get; set; }

        public int StoredDataVersion { get; set; }
        public bool WasMigrated { get; set; }
    }

    public static class SentinelProjectSerializer
    {
        /// <summary>
        /// SentinelDataVersion. Bump whenever the persisted shape changes (new/renamed/removed fields, changed
        /// meaning) and add a step to <see cref="SentinelDataMigrator"/>.
        /// </summary>
        public const int CurrentDataVersion = 1;

        public const string TypeComponent = "ComponentDefinition";
        public const string TypeDoorSetDefinition = "DoorSetDefinition";
        public const string TypeDoorSetInstance = "DoorSetInstance";
        public const string TypeAssignmentRule = "AssignmentRule";

        public static readonly JsonSerializerSettings JsonSettings = CreateSettings();

        private static JsonSerializerSettings CreateSettings()
        {
            var s = new JsonSerializerSettings
            {
                ObjectCreationHandling = ObjectCreationHandling.Replace,
                MissingMemberHandling = MissingMemberHandling.Ignore,
                NullValueHandling = NullValueHandling.Ignore,
                Formatting = Formatting.None,
                TypeNameHandling = TypeNameHandling.None,
                FloatFormatHandling = FloatFormatHandling.DefaultValue
            };
            s.Converters.Add(new StringEnumConverter());
            return s;
        }

        private static JsonSerializer Serializer => JsonSerializer.Create(JsonSettings);

        // ------------------------------------------------------------------ save

        public static StoragePayload ToPayload(SentinelProject project, string savedBy, string pluginVersion)
        {
            if (project == null) throw new ArgumentNullException(nameof(project));

            var header = new ProjectHeader
            {
                DataVersion = CurrentDataVersion,
                ProjectId = project.ProjectId,
                Settings = project.Settings ?? new SentinelSettings(),
                SavedUtc = DateTime.UtcNow.ToString("o"),
                SavedBy = savedBy,
                PluginVersion = pluginVersion
            };

            var payload = new StoragePayload
            {
                DataVersion = CurrentDataVersion,
                Header = JsonConvert.SerializeObject(header, JsonSettings)
            };

            foreach (var c in project.ComponentDefinitions) payload.Items.Add(Envelope(TypeComponent, c.Id, c));
            foreach (var d in project.DoorSetDefinitions) payload.Items.Add(Envelope(TypeDoorSetDefinition, d.Id, d));
            foreach (var i in project.DoorSetInstances) payload.Items.Add(Envelope(TypeDoorSetInstance, i.Id, i));
            foreach (var r in project.AssignmentRules) payload.Items.Add(Envelope(TypeAssignmentRule, r.Id, r));

            // Items this version could not interpret are written back untouched.
            foreach (var raw in project.PreservedRawItems ?? new List<string>())
                if (!string.IsNullOrWhiteSpace(raw)) payload.Items.Add(raw);

            return payload;
        }

        private static string Envelope(string type, string id, object data)
        {
            var env = new JObject
            {
                ["Type"] = type,
                ["Id"] = id,
                ["Data"] = JToken.FromObject(data, Serializer)
            };
            return env.ToString(Formatting.None);
        }

        // ------------------------------------------------------------------ load

        public static LoadResult FromPayload(StoragePayload payload)
        {
            var result = new LoadResult { Project = new SentinelProject() };
            if (payload == null)
            {
                result.Warnings.Add("No Sentinel data found.");
                return result;
            }

            result.StoredDataVersion = payload.DataVersion;
            if (payload.DataVersion > CurrentDataVersion)
            {
                result.IsNewerThanSupported = true;
                result.Warnings.Add("This project was saved with a newer Sentinel (data version " + payload.DataVersion +
                                    "). It is opened read-only to avoid losing data.");
            }

            // Header
            try
            {
                if (!string.IsNullOrWhiteSpace(payload.Header))
                {
                    var headerObj = JObject.Parse(payload.Header);
                    if (payload.DataVersion < CurrentDataVersion)
                        SentinelDataMigrator.MigrateHeader(headerObj, payload.DataVersion);
                    var header = headerObj.ToObject<ProjectHeader>(Serializer);
                    if (header != null)
                    {
                        if (!string.IsNullOrWhiteSpace(header.ProjectId)) result.Project.ProjectId = header.ProjectId;
                        if (header.Settings != null) result.Project.Settings = header.Settings;
                    }
                }
            }
            catch (Exception ex)
            {
                result.Warnings.Add("Project settings could not be read and were reset: " + ex.Message);
            }

            foreach (var raw in payload.Items ?? new List<string>())
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                try
                {
                    var env = JObject.Parse(raw);
                    var type = (string)env["Type"];
                    var data = env["Data"] as JObject;
                    if (data == null) throw new JsonException("Item has no data.");

                    if (payload.DataVersion < CurrentDataVersion)
                    {
                        SentinelDataMigrator.MigrateItem(type, data, payload.DataVersion);
                        result.WasMigrated = true;
                    }

                    switch (type)
                    {
                        case TypeComponent:
                            result.Project.ComponentDefinitions.Add(Required(data.ToObject<ComponentDefinition>(Serializer)));
                            break;
                        case TypeDoorSetDefinition:
                            result.Project.DoorSetDefinitions.Add(Required(data.ToObject<DoorSetDefinition>(Serializer)));
                            break;
                        case TypeDoorSetInstance:
                            result.Project.DoorSetInstances.Add(Normalize(Required(data.ToObject<DoorSetInstance>(Serializer))));
                            break;
                        case TypeAssignmentRule:
                            result.Project.AssignmentRules.Add(Required(data.ToObject<AssignmentRule>(Serializer)));
                            break;
                        default:
                            result.Project.PreservedRawItems.Add(raw);
                            result.Warnings.Add("Unknown stored item type \"" + type + "\" kept unchanged (written by a newer Sentinel?).");
                            break;
                    }
                }
                catch (Exception ex)
                {
                    result.Project.PreservedRawItems.Add(raw);
                    result.Warnings.Add("A stored item could not be read and was kept unchanged: " + ex.Message);
                }
            }

            return result;
        }

        private static T Required<T>(T value) where T : class
        {
            if (value == null) throw new JsonException("Empty " + typeof(T).Name + ".");
            return value;
        }

        private static DoorSetInstance Normalize(DoorSetInstance i)
        {
            if (i.Overrides == null) i.Overrides = new SetOverrides();
            if (i.Overrides.AddedComponents == null) i.Overrides.AddedComponents = new List<SetComponentRule>();
            if (i.Overrides.RemovedRuleIds == null) i.Overrides.RemovedRuleIds = new List<string>();
            if (i.Overrides.RuleOverrides == null) i.Overrides.RuleOverrides = new List<ComponentRuleOverride>();
            if (i.Components == null) i.Components = new List<PlacedComponentInstance>();
            if (i.Source == null) i.Source = new SourceDoorReference();
            if (i.Source.LastKnownParameters == null)
                i.Source.LastKnownParameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            else if (!(i.Source.LastKnownParameters.Comparer is StringComparer))
                i.Source.LastKnownParameters = new Dictionary<string, string>(i.Source.LastKnownParameters, StringComparer.OrdinalIgnoreCase);
            return i;
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>Deep copy through the persisted representation (what you get is what would be saved).</summary>
        public static SentinelProject DeepClone(SentinelProject project)
        {
            var copy = FromPayload(ToPayload(project, null, null)).Project;
            return copy;
        }

        public static string ToJson(object value, bool indented = false) =>
            JsonConvert.SerializeObject(value, indented ? Formatting.Indented : Formatting.None, JsonSettings);

        public static T FromJson<T>(string json) => JsonConvert.DeserializeObject<T>(json, JsonSettings);
    }

    /// <summary>
    /// Upgrades stored JSON from older data versions. Version 1 is the first release, so there are no steps yet;
    /// each future bump adds a "from N to N+1" step here that edits the JObject in place.
    /// </summary>
    public static class SentinelDataMigrator
    {
        public static void MigrateHeader(JObject header, int fromVersion)
        {
            for (var v = Math.Max(fromVersion, 0); v < SentinelProjectSerializer.CurrentDataVersion; v++)
            {
                // Example for a future step:
                // if (v == 1) { /* rename/move fields in header */ }
            }
            header["DataVersion"] = SentinelProjectSerializer.CurrentDataVersion;
        }

        public static void MigrateItem(string type, JObject data, int fromVersion)
        {
            for (var v = Math.Max(fromVersion, 0); v < SentinelProjectSerializer.CurrentDataVersion; v++)
            {
                // Example for a future step:
                // if (v == 1 && type == SentinelProjectSerializer.TypeDoorSetInstance) { ... }
            }
        }
    }
}
