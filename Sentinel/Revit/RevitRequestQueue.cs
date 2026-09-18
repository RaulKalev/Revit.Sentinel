using System;
using System.Collections.Generic;
using Autodesk.Revit.UI;
using Sentinel.Infrastructure;

namespace Sentinel.Revit
{
    /// <summary>
    /// ExternalEvent handler with a FIFO queue of work items. The modeless UI enqueues closures and raises the
    /// event; Revit runs them in a valid API context. Unlike a single "request" field, queued requests never
    /// overwrite each other.
    /// </summary>
    internal sealed class RevitRequestQueue : IExternalEventHandler
    {
        private readonly Queue<KeyValuePair<string, Action<UIApplication>>> _queue =
            new Queue<KeyValuePair<string, Action<UIApplication>>>();
        private readonly object _lock = new object();
        private ExternalEvent _event;

        /// <summary>Must be called in a valid API context (e.g. from IExternalCommand.Execute).</summary>
        public void Initialize()
        {
            if (_event == null) _event = ExternalEvent.Create(this);
        }

        public void Enqueue(string name, Action<UIApplication> work)
        {
            lock (_lock) _queue.Enqueue(new KeyValuePair<string, Action<UIApplication>>(name, work));
            var result = _event != null ? _event.Raise() : ExternalEventRequest.Denied;
            if (result == ExternalEventRequest.Denied || result == ExternalEventRequest.TimedOut)
                SentinelLog.Warn("ExternalEvent.Raise returned " + result + " for " + name);
        }

        public void Execute(UIApplication app)
        {
            while (true)
            {
                KeyValuePair<string, Action<UIApplication>> item;
                lock (_lock)
                {
                    if (_queue.Count == 0) return;
                    item = _queue.Dequeue();
                }

                try
                {
                    item.Value(app);
                }
                catch (Exception ex)
                {
                    // Individual work items report their own failures; this is the last line of defence.
                    SentinelLog.Error("Request '" + item.Key + "' failed", ex);
                }
            }
        }

        public string GetName() => "Sentinel.RequestQueue";

        public void Dispose()
        {
            try { _event?.Dispose(); } catch { }
            _event = null;
        }
    }
}
