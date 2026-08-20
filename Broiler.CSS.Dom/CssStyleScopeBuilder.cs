using System;
using System.Collections.Generic;

namespace Broiler.CSS.Dom;

/// <summary>
/// Keeps a <see cref="CssStyleEngine"/>'s registered stylesheet set in sync with a
/// host-supplied, document-ordered list of stylesheet sources. It owns the neutral
/// scope-assembly the engine needs — evaluating each source's <c>media</c> attribute
/// against the environment, preserving cascade-origin/document order, and re-syncing
/// only when the effective input changes — while the host keeps the parts that need the
/// DOM and resource loading: discovering <c>&lt;style&gt;</c>/<c>&lt;link&gt;</c> elements,
/// fetching external sheets, and extracting each sheet's CSS text.
/// </summary>
/// <remarks>
/// This replaces the ad-hoc bridge assembly that concatenated every collected sheet into
/// one blob and parsed it as a single sheet, ignoring per-sheet <c>media</c>. Parsing each
/// source as its own sheet also isolates a malformed sheet from the next and scopes
/// <c>@import</c>/<c>@namespace</c> correctly. Only the assembly is here; cascade,
/// inheritance, and computed style stay in <see cref="CssStyleEngine"/>.
/// </remarks>
public sealed class CssStyleScopeBuilder(CssStyleEngine engine)
{
    private readonly CssStyleEngine _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    private bool _synced;
    private int _syncHash;

    /// <summary>The engine whose stylesheet set this builder maintains.</summary>
    public CssStyleEngine Engine => _engine;

    /// <summary>
    /// A host-supplied stylesheet in document order. <paramref name="CssText"/> is the
    /// already-extracted CSS text, <paramref name="Origin"/> its cascade origin, and
    /// <paramref name="Media"/> the element's <c>media</c> attribute — <c>null</c>, empty,
    /// or whitespace meaning "applies to all media".
    /// </summary>
    public readonly record struct StyleSource(string CssText, CssOrigin Origin, string? Media = null);

    /// <summary>
    /// Re-syncs <see cref="Engine"/>'s registered stylesheets from <paramref name="sources"/>,
    /// in order, keeping only those whose <c>media</c> matches <paramref name="environment"/>,
    /// but only when the effective (media-filtered) input changed since the last call. The
    /// engine environment is always updated so <c>@media</c> rules inside the sheets and
    /// viewport-relative lengths resolve against the current viewport. Returns the engine.
    /// </summary>
    public CssStyleEngine Sync(IReadOnlyList<StyleSource> sources, CssEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(sources);

        var included = new List<StyleSource>(sources.Count);
        foreach (var source in sources)
        {
            if (string.IsNullOrWhiteSpace(source.Media)
                || CssStyleEngine.MatchesMediaQuery(source.Media, environment))
                included.Add(source);
        }

        var hash = ComputeHash(included);
        if (!_synced || hash != _syncHash)
        {
            _engine.ClearStyleSheets();
            foreach (var source in included)
            {
                if (!string.IsNullOrEmpty(source.CssText))
                    _engine.AddStyleSheet(new CssParser().ParseStyleSheet(source.CssText), source.Origin);
            }

            _syncHash = hash;
            _synced = true;
        }

        _engine.UpdateEnvironment(environment);
        return _engine;
    }

    // Identity of the media-filtered source set: order-sensitive over (text, origin). The
    // media gate already ran, so a viewport change that does not flip any media match leaves
    // this unchanged and skips the (re-parse) resync; one that flips a match changes the
    // included set and therefore the hash.
    private static int ComputeHash(List<StyleSource> included)
    {
        var hash = new HashCode();
        hash.Add(included.Count);
        foreach (var source in included)
        {
            hash.Add(source.CssText, StringComparer.Ordinal);
            hash.Add(source.Origin);
        }
        return hash.ToHashCode();
    }
}
