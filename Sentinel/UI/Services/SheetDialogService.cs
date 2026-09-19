using System;
using System.Collections.Generic;
using Sentinel.UI.Mvvm;

namespace Sentinel.UI.Services
{
    /// <summary>One in-window sheet (message, optional details / option / path) and its buttons.</summary>
    public class SheetRequest : ObservableObject
    {
        private bool _option;
        private string _path;

        public string Title { get; set; }
        public string Message { get; set; }
        public string Details { get; set; }
        public bool IsError { get; set; }
        public string PrimaryText { get; set; } = "OK";

        /// <summary>Null = no secondary (cancel) button.</summary>
        public string SecondaryText { get; set; }

        public string OptionText { get; set; }
        public bool Option { get => _option; set => Set(ref _option, value); }

        /// <summary>Non-null = show an editable path field.</summary>
        public bool HasPath { get; set; }
        public string Path { get => _path; set => Set(ref _path, value); }

        public bool HasDetails => !string.IsNullOrWhiteSpace(Details);
        public bool HasOption => !string.IsNullOrEmpty(OptionText);
        public bool HasSecondary => SecondaryText != null;

        internal Action<bool> Completed { get; set; }
    }

    /// <summary>
    /// Sheet-based <see cref="IDialogService"/>: requests are queued and shown one at a time inside the window
    /// (dimmed content, focus kept in the sheet). Nothing here blocks the calling thread.
    /// </summary>
    public class SheetDialogService : ObservableObject, IDialogService
    {
        private readonly Queue<SheetRequest> _queue = new Queue<SheetRequest>();
        private SheetRequest _current;

        public SheetRequest Current { get => _current; private set { Set(ref _current, value); OnPropertyChanged(nameof(IsOpen)); } }
        public bool IsOpen => _current != null;

        /// <summary>Raised when a sheet opens (the view moves focus into it) or closes (focus returns).</summary>
        public event EventHandler SheetChanged;

        public void Confirm(string title, string message, string details, string yesText, string noText, Action<bool> result) =>
            Enqueue(new SheetRequest
            {
                Title = title, Message = message, Details = details, PrimaryText = yesText, SecondaryText = noText ?? "Cancel",
                Completed = ok => result?.Invoke(ok)
            });

        public void ConfirmWithOption(string title, string message, string details, string optionText, bool option,
            string yesText, string noText, Action<bool, bool> result)
        {
            var r = new SheetRequest
            {
                Title = title, Message = message, Details = details, PrimaryText = yesText, SecondaryText = noText ?? "Cancel",
                OptionText = optionText, Option = option
            };
            r.Completed = ok => result?.Invoke(ok, r.Option);
            Enqueue(r);
        }

        public void Show(string title, string message, string details = null, bool isError = false) =>
            Enqueue(new SheetRequest { Title = title, Message = message, Details = details, IsError = isError, PrimaryText = "OK" });

        public void PromptPath(string title, string message, string defaultPath, string actionText, Action<string> result)
        {
            var r = new SheetRequest
            {
                Title = title, Message = message, PrimaryText = actionText, SecondaryText = "Cancel", HasPath = true, Path = defaultPath
            };
            r.Completed = ok => result?.Invoke(ok && !string.IsNullOrWhiteSpace(r.Path) ? r.Path.Trim().Trim('"') : null);
            Enqueue(r);
        }

        private void Enqueue(SheetRequest r)
        {
            _queue.Enqueue(r);
            if (Current == null) Next();
        }

        private void Next()
        {
            Current = _queue.Count > 0 ? _queue.Dequeue() : null;
            SheetChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Closes the current sheet with the chosen answer and shows the next queued one.</summary>
        public void Complete(bool primary)
        {
            var r = Current;
            if (r == null) return;
            Next();
            r.Completed?.Invoke(primary);
        }
    }
}
