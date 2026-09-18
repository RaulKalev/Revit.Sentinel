using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Sentinel.Core.Persistence;
using Sentinel.Infrastructure;

namespace Sentinel.Revit.Storage
{
    /// <summary>
    /// Project-level persistence: one DataStorage element carrying the <see cref="StoragePayload"/> in an
    /// Extensible Storage entity. The schema is intentionally tiny and generic (version + header + JSON items);
    /// all domain evolution happens inside the JSON with <see cref="SentinelProjectSerializer.CurrentDataVersion"/>
    /// and migrations, so this schema should never need to change.
    ///
    /// DO NOT change the GUID or field names once shipped.
    /// </summary>
    internal static class SentinelProjectStorage
    {
        public static readonly Guid SchemaGuid = new Guid("5E3C1A7B-9D24-4F6E-8B13-2A7C9E0D4F61");
        public const string VendorId = "RKTL";
        public const string DataStorageName = "Sentinel.ProjectData";

        private const string F_DataVersion = "DataVersion";
        private const string F_Header = "Header";
        private const string F_Items = "Items";

        public static Schema GetSchema()
        {
            var schema = Schema.Lookup(SchemaGuid);
            if (schema != null) return schema;

            var sb = new SchemaBuilder(SchemaGuid);
            sb.SetSchemaName("SentinelProjectData");
            sb.SetVendorId(VendorId);
            sb.SetReadAccessLevel(AccessLevel.Public);
            sb.SetWriteAccessLevel(AccessLevel.Public);
            sb.SetDocumentation("Sentinel (RK Tools) project data: versioned JSON items for component definitions, " +
                                "door set types, door set instances and assignment rules.");
            sb.AddSimpleField(F_DataVersion, typeof(int));
            sb.AddSimpleField(F_Header, typeof(string));
            sb.AddArrayField(F_Items, typeof(string));
            return sb.Finish();
        }

        public static DataStorage Find(Document doc)
        {
            if (doc == null) return null;
            return new FilteredElementCollector(doc)
                .OfClass(typeof(DataStorage))
                .WherePasses(new ExtensibleStorageFilter(SchemaGuid))
                .Cast<DataStorage>()
                .FirstOrDefault();
        }

        /// <summary>Reads the payload, or null when the project has no Sentinel data yet.</summary>
        public static StoragePayload Read(Document doc)
        {
            var ds = Find(doc);
            if (ds == null) return null;

            var ent = ds.GetEntity(GetSchema());
            if (ent == null || !ent.IsValid()) return null;

            return new StoragePayload
            {
                DataVersion = ent.Get<int>(F_DataVersion),
                Header = ent.Get<string>(F_Header),
                Items = (ent.Get<IList<string>>(F_Items) ?? new List<string>()).ToList()
            };
        }

        /// <summary>Writes the payload. Caller must have an open transaction.</summary>
        public static ElementId Write(Document doc, StoragePayload payload)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            var schema = GetSchema();
            var ds = Find(doc);
            if (ds == null)
            {
                ds = DataStorage.Create(doc);
                try { ds.Name = DataStorageName; } catch { /* cosmetic */ }
                SentinelLog.Info("Created Sentinel DataStorage " + RevitCompat.IdValue(ds.Id));
            }

            var ent = new Entity(schema);
            ent.Set(F_DataVersion, payload.DataVersion);
            ent.Set(F_Header, payload.Header ?? "");
            IList<string> items = payload.Items ?? new List<string>();
            ent.Set(F_Items, items);
            ds.SetEntity(ent);
            return ds.Id;
        }
    }
}
