using System;

namespace AstroHybrid.App.Services
{
    // For scaling/chaining progress inside one worker. The UI-facing Progress<T> remains in the view model.
    internal sealed class InlineProgress<T> : IProgress<T>
    {
        private readonly Action<T> _report;

        public InlineProgress(Action<T> report)
        {
            _report = report ?? throw new ArgumentNullException(nameof(report));
        }

        public void Report(T value) => _report(value);
    }
}
