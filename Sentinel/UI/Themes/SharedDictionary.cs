using System;
using System.Collections.Generic;
using System.Windows;

namespace Sentinel.UI
{
    /// <summary>
    /// A merged dictionary that is loaded once per source and then shared. Every view (and templated rows such as the
    /// component rows) merges SentinelStyles.xaml; without sharing each instance would parse it again.
    /// Styles are sealed and use DynamicResource for colours, so sharing them across windows and themes is safe.
    /// </summary>
    public class SharedDictionary : ResourceDictionary
    {
        private static readonly Dictionary<Uri, ResourceDictionary> Cache = new Dictionary<Uri, ResourceDictionary>();

        public new Uri Source
        {
            get => _source;
            set
            {
                _source = value;
                ResourceDictionary shared;
                if (!Cache.TryGetValue(value, out shared))
                {
                    shared = new ResourceDictionary { Source = value };
                    Cache[value] = shared;
                }
                MergedDictionaries.Add(shared);
            }
        }

        private Uri _source;
    }
}
