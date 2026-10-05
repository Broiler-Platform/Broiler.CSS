using System;

namespace Broiler.CSS.Dom;

/// <summary>
/// Whether this thread's selector matching is computing a visited link's visited style, the one cascade
/// in which <c>:visited</c> matches.
/// </summary>
/// <remarks>
/// <para>
/// <b>Chromium styles a visited link twice, and this is the switch between the two.</b> Its style, the
/// one every query answers from, is computed as if no link were visited: <c>:link</c> matches every link
/// and <c>:visited</c> none. A visited link, and what is inside it, also get a visited style, in which
/// <c>:visited</c> matches a visited link and <c>:link</c> does not; only its colours are used
/// (<see cref="CssStyleEngine.GetCascadedStyle"/>). The engine turns this on while it computes that
/// second cascade.
/// </para>
/// <para>
/// Thread-static, as quirks mode and paged media are: the engine's caches are shared by the threads
/// that style a page, and each result is keyed by the mode it was computed under.
/// </para>
/// </remarks>
internal static class CssVisitedLinkMatching
{
    [ThreadStatic]
    private static bool t_active;

    /// <summary>Whether <c>:visited</c> matches a visited link on this thread.</summary>
    internal static bool Active => t_active;

    /// <summary>Turns visited-link matching on until the returned scope is disposed.</summary>
    internal static Scope Enter()
    {
        var previous = t_active;
        t_active = true;
        return new Scope(previous);
    }

    /// <summary>Restores the matching the thread had before <see cref="Enter"/>.</summary>
    internal readonly struct Scope(bool previous) : IDisposable
    {
        public void Dispose() => t_active = previous;
    }
}
