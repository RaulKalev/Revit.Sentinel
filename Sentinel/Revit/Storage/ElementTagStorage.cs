using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Sentinel.Core.Persistence;
using Sentinel.Infrastructure;

namespace Sentinel.Revit.Storage
{
    /// <summary>Identity written onto every Sentinel-placed family instance.</summary>
    internal class ElementTag
    {
        public int DataVersion { get; set; }
        public string ProjectId { get; set; }
        public string SetId { get; set; }
        public string ComponentId { get; set; }
        public string SlotKey { get; set; }
        public string DefinitionId { get; set; }
        public string ComponentDefinitionId { get; set; }
        public string SourceLinkUniqueId { get; set; }
        public string SourceDoorUniqueId { get; set; }
        public string SourceIfcGlobalId { get; set; }
    }

    /// <summary>
    /// Per-element identity (SentinelSetId / SentinelComponentId + source door ids). Lets Sentinel rebuild the
    /// relationship graph from the model itself, detect copies and recover sets if project data is lost.
    /// DO NOT change the GUID or field names once shipped.
    /// </summary>
    internal static class ElementTagStorage
    {
        public static readonly Guid SchemaGuid = new Guid("B8F2D6A4-3C71-4E95-A0D8-6F1B2E7C9A53");

        private static readonly string[] StringFields =
        {
            "ProjectId", "SetId", "ComponentId", "SlotKey", "DefinitionId", "ComponentDefinitionId",
            "SourceLinkUniqueId", "SourceDoorUniqueId", "SourceIfcGlobalId"
        };

        public static Schema GetSchema()
        {
            var schema = Schema.Lookup(SchemaGuid);
            if (schema != null) return schema;

            var sb = new SchemaBuilder(SchemaGuid);
            sb.SetSchemaName("SentinelElementTag");
            sb.SetVendorId(SentinelProjectStorage.VendorId);
            sb.SetReadAccessLevel(AccessLevel.Public);
            sb.SetWriteAccessLevel(AccessLevel.Public);
            sb.SetDocumentation("Sentinel (RK Tools) identity of a placed security component.");
            sb.AddSimpleField("DataVersion", typeof(int));
            foreach (var f in StringFields) sb.AddSimpleField(f, typeof(string));
            return sb.Finish();
        }

        /// <summary>Caller must have an open transaction.</summary>
        public static void Write(Element element, ElementTag tag)
        {
            var e = new Entity(GetSchema());
            e.Set("DataVersion", SentinelProjectSerializer.CurrentDataVersion);
            e.Set("ProjectId", tag.ProjectId ?? "");
            e.Set("SetId", tag.SetId ?? "");
            e.Set("ComponentId", tag.ComponentId ?? "");
            e.Set("SlotKey", tag.SlotKey ?? "");
            e.Set("DefinitionId", tag.DefinitionId ?? "");
            e.Set("ComponentDefinitionId", tag.ComponentDefinitionId ?? "");
            e.Set("SourceLinkUniqueId", tag.SourceLinkUniqueId ?? "");
            e.Set("SourceDoorUniqueId", tag.SourceDoorUniqueId ?? "");
            e.Set("SourceIfcGlobalId", tag.SourceIfcGlobalId ?? "");
            element.SetEntity(e);
        }

        public static ElementTag TryRead(Element element)
        {
            if (element == null) return null;
            try
            {
                var e = element.GetEntity(GetSchema());
                if (e == null || !e.IsValid()) return null;
                return new ElementTag
                {
                    DataVersion = e.Get<int>("DataVersion"),
                    ProjectId = e.Get<string>("ProjectId"),
                    SetId = e.Get<string>("SetId"),
                    ComponentId = e.Get<string>("ComponentId"),
                    SlotKey = e.Get<string>("SlotKey"),
                    DefinitionId = e.Get<string>("DefinitionId"),
                    ComponentDefinitionId = e.Get<string>("ComponentDefinitionId"),
                    SourceLinkUniqueId = e.Get<string>("SourceLinkUniqueId"),
                    SourceDoorUniqueId = e.Get<string>("SourceDoorUniqueId"),
                    SourceIfcGlobalId = e.Get<string>("SourceIfcGlobalId")
                };
            }
            catch (Exception ex)
            {
                SentinelLog.Error("Reading Sentinel tag failed for element " + RevitCompat.IdValue(element.Id), ex);
                return null;
            }
        }

        public static void Remove(Element element)
        {
            try { element.DeleteEntity(GetSchema()); }
            catch (Exception ex) { SentinelLog.Error("Removing Sentinel tag failed", ex); }
        }

        /// <summary>All tagged elements in the document (quick Extensible Storage filter).</summary>
        public static List<KeyValuePair<Element, ElementTag>> ReadAll(Document doc)
        {
            return new FilteredElementCollector(doc)
                .WherePasses(new ExtensibleStorageFilter(SchemaGuid))
                .WhereElementIsNotElementType()
                .ToElements()
                .Select(el => new KeyValuePair<Element, ElementTag>(el, TryRead(el)))
                .Where(kv => kv.Value != null)
                .ToList();
        }
    }
}
