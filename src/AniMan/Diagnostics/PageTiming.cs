using System.Diagnostics;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;

namespace AniMan.Diagnostics;

/// <summary>
/// Measures how long a tab takes to appear, written to the log at Debug level (Debug builds
/// only, see App.ConfigureSerilog). Create it in a field initializer so "since created"
/// includes building the page's XAML; "visit" counts from the page's Loaded event.
/// </summary>
public sealed class PageTiming
{
    private readonly Stopwatch _sinceCreated = Stopwatch.StartNew();
    private bool _firstVisitLogged;

    /// <summary>Name in the log line, e.g. "Library Anime".</summary>
    public string Page { get; set; } = "?";

    /// <summary>Nothing is measured or logged until this is set.</summary>
    public ILogger? Logger { get; set; }

    /// <summary>Starts timing one visit; call when the page is loaded.</summary>
    public Visit BeginVisit() => new(this);

    public sealed class Visit
    {
        private readonly PageTiming _owner;
        private readonly Stopwatch _visit = Stopwatch.StartNew();
        private long _dataMs = -1;

        internal Visit(PageTiming owner) => _owner = owner;

        /// <summary>The page's data is loaded; what follows is building and drawing it.</summary>
        public void DataLoaded() => _dataMs = _visit.ElapsedMilliseconds;

        /// <summary>Waits until layout and rendering are done, then logs the times.</summary>
        public async Task LogWhenRenderedAsync(Dispatcher dispatcher)
        {
            var logger = _owner.Logger;
            if (logger is null || !logger.IsEnabled(LogLevel.Debug)) return;

            // ContextIdle runs after the layout, render and data-binding work queued so far.
            await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            var visitMs = _visit.ElapsedMilliseconds;

            if (!_owner._firstVisitLogged)
            {
                _owner._firstVisitLogged = true;
                logger.LogDebug(
                    "Page timing {Page}: first visit {Visit} ms (data {Data} ms), {Created} ms since the page was created",
                    _owner.Page, visitMs, _dataMs, _owner._sinceCreated.ElapsedMilliseconds);
            }
            else
            {
                logger.LogDebug("Page timing {Page}: visit {Visit} ms (data {Data} ms)", _owner.Page, visitMs, _dataMs);
            }
        }
    }
}
