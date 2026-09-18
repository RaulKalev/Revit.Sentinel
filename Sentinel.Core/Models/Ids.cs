using System;

namespace Sentinel.Core.Models
{
    /// <summary>Stable string identifiers. GUID "N" format keeps them compact and safe in Revit storage.</summary>
    public static class Ids
    {
        public static string New() => Guid.NewGuid().ToString("N");
    }
}
