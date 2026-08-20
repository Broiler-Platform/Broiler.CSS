using System;

namespace Broiler.CSS.Dom;

/// <summary>
/// The paged-media context a media query is evaluated in: whether the document is being formatted
/// for print, and the page area its <c>width</c>/<c>height</c> features describe.
/// </summary>
/// <remarks>
/// <para>
/// Two things about a print formatting context are not derivable from the surface the engine is
/// painting on, and both are media-query questions. The first is the media type: without this the
/// evaluator matches <c>screen</c> and <c>all</c> unconditionally, so <c>@media print</c> never
/// applies to a document that is being printed. The second is the size — Media Queries 4 evaluates
/// <c>width</c> and <c>height</c> against the page area, which is not the layout surface a paged
/// renderer happens to allocate.
/// </para>
/// <para>
/// <strong>And the page area here is the initial one the system provides, not the one
/// <c>@page</c> declares.</strong> That looks wrong until you notice the circularity: a
/// <c>@page</c> rule may itself sit inside a media query, so resolving the query against the
/// declared page would need the query already resolved. <c>css-page/media-queries-001-print</c> is
/// the test that pins it down, and its own comment is the clearest statement of the rule — it
/// declares <c>@page { size: 10in; margin: 2in }</c> and then asserts a query that only matches
/// between 4in and 5in wide and 2in and 3in tall, which is WPT's 5in × 3in initial page whether or
/// not a half-inch default margin is taken off it. The declared 10in page is deliberately not what
/// the query sees.
/// </para>
/// <para>
/// Thread-static and inert by default, like the layout engine's other render levers: a caller pins
/// it around one render on one thread, and every render that does not pin it evaluates exactly as
/// before. That makes it an ambient-state contract — a worker thread that formats for print has to
/// establish it before laying out, the same way it establishes the canvas backdrop.
/// </para>
/// </remarks>
public static class CssPagedMedia
{
    [ThreadStatic]
    private static int _width;

    [ThreadStatic]
    private static int _height;

    /// <summary>
    /// Whether this thread is formatting for paged media. False unless a <see cref="Pin"/> scope is
    /// open, which is what keeps a screen render byte-identical to one from before this existed.
    /// </summary>
    public static bool Active => _width > 0 && _height > 0;

    /// <summary>The page area's width in CSS pixels, meaningful only while <see cref="Active"/>.</summary>
    public static int Width => _width;

    /// <summary>The page area's height in CSS pixels, meaningful only while <see cref="Active"/>.</summary>
    public static int Height => _height;

    /// <summary>
    /// Formats for paged media against a page area of <paramref name="pageAreaWidth"/> by
    /// <paramref name="pageAreaHeight"/> until the returned scope is disposed. Scopes nest: the
    /// previous context is restored rather than cleared.
    /// </summary>
    public static IDisposable Pin(int pageAreaWidth, int pageAreaHeight) =>
        new Scope(pageAreaWidth, pageAreaHeight);

    /// <summary>
    /// Formats for continuous media until the returned scope is disposed, whatever context is open
    /// around it.
    /// </summary>
    /// <remarks>
    /// A <em>nested</em> browsing context is its own formatting context, and the page area of the
    /// document embedding it is not its viewport: the frame's <c>width</c>/<c>height</c> features
    /// describe the frame. <c>css-page/media-queries-002-print</c> and <c>-003-print</c> are the
    /// pair that state it — each embeds a 100 × 100 frame whose own style sheet asserts
    /// <c>@media (width: 100px) and (height: 100px)</c> — and both go red if the embedder's page
    /// area reaches them.
    /// </remarks>
    public static IDisposable Suspend() => new Scope(0, 0);

    private sealed class Scope : IDisposable
    {
        private readonly int _previousWidth;
        private readonly int _previousHeight;

        internal Scope(int width, int height)
        {
            _previousWidth = _width;
            _previousHeight = _height;
            _width = width;
            _height = height;
        }

        public void Dispose()
        {
            _width = _previousWidth;
            _height = _previousHeight;
        }
    }
}
