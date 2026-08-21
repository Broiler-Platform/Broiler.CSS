using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Broiler.Dom;

namespace Broiler.CSS.Dom.Tests;

/// <summary>
/// The engine is re-entered from multiple threads: the HtmlBridge's <c>GetComputedProps</c> routes
/// through the engine's sparse projection, and JS continuations dispatched on ThreadPool threads run
/// that computed-style/geometry work concurrently with the main-thread layout pass. Before the
/// engine's caches were synchronised, that race corrupted the plain <see cref="Dictionary{TKey,TValue}"/>
/// backing stores and aborted the process with "Operations that change non-concurrent collections
/// must have exclusive access" — the crash that gated the css-view-transitions WPT tests in issue
/// #1445. These tests hammer every public query path concurrently with cache-invalidating stylesheet
/// mutations; they must complete without any collection-corruption throw.
/// </summary>
public sealed class CssStyleEngineConcurrencyTests
{
    [Fact(Timeout = 600000)]
    public void Concurrent_Style_Queries_And_Invalidations_Do_Not_Corrupt_Caches()
    {
        var document = new DomDocument();
        var html = document.CreateElement("html");
        var body = document.CreateElement("body");
        document.AppendChild(html);
        html.AppendChild(body);

        var elements = new List<DomElement>();
        for (var i = 0; i < 64; i++)
        {
            var div = document.CreateElement("div");
            div.ClassName = i % 2 == 0 ? "even" : "odd";
            div.Id = $"n{i}";
            body.AppendChild(div);
            elements.Add(div);
        }

        var engine = new CssStyleEngine();
        engine.AddStyleSheet(new CssParser().ParseStyleSheet(
            "div { color: red; } .even { color: green; } .odd { color: blue; } #n0 { color: purple; }"));

        const int workers = 12;
        const int iterations = 150;
        var errors = new ConcurrentQueue<Exception>();
        using var start = new Barrier(workers);

        Parallel.For(0, workers, worker =>
        {
            start.SignalAndWait();
            try
            {
                for (var iteration = 0; iteration < iterations; iteration++)
                {
                    foreach (var element in elements)
                    {
                        // Exercise all four cache-backed public paths: the computed-style cache,
                        // the sparse projection cache, and the declared-cascade cache (twice).
                        engine.GetComputedStyle(element);
                        engine.GetSparseComputedStyle(element, sparseInheritance: true);
                        engine.GetCascadedStyle(element, includeInlineStyle: true);
                        engine.GetCascadedDeclaredValues(element);
                    }

                    // A minority of workers keep clearing and re-populating the caches mid-query,
                    // which is what turns concurrent reads/writes into a corrupting race.
                    if (worker % 4 == 0)
                    {
                        engine.AddStyleSheet(new CssParser().ParseStyleSheet(
                            $".even {{ background-color: rgb(0, 0, {iteration % 256}); }}"));
                        engine.InvalidateComputedStyleCaches();
                    }
                }
            }
            catch (Exception ex)
            {
                errors.Enqueue(ex);
            }
        });

        Assert.Empty(errors);
    }

    /// <summary>
    /// Multithreading item #12's exit gate at the engine level: resolving a document's cascade on
    /// many threads must produce exactly what resolving it on one thread produces.
    /// </summary>
    /// <remarks>
    /// The crash test above says the caches survive concurrency; it does not say the <em>answers</em>
    /// do, and it cannot — it never compares a value. That distinction is the whole risk of the
    /// renderer's warm pass: the cascade recurses into ancestors, so two threads can race the same
    /// ancestor, and "both computed something valid" is only good enough if they computed the same
    /// thing. Here the sequential result is taken first, on a pristine engine, and a second engine
    /// with an identical sheet set is driven from every thread at once and compared property by
    /// property.
    /// </remarks>
    [Fact(Timeout = 600000)]
    public void Parallel_Cascade_Matches_The_Sequential_Cascade_Property_For_Property()
    {
        const string sheet =
            "div { color: red; padding: 2px } .even { color: green; font-weight: bold }"
            + ".odd { color: blue } #n0 { color: purple } section div { margin-left: 3px }"
            + "div[data-k='1'] { border-left: 1px solid black } section > div em { font-style: italic }";

        var (sequentialDocument, sequentialElements) = BuildDocument();
        var sequentialEngine = new CssStyleEngine();
        sequentialEngine.AddStyleSheet(new CssParser().ParseStyleSheet(sheet));

        var expected = new List<IReadOnlyDictionary<string, string>>(sequentialElements.Count);
        foreach (var element in sequentialElements)
            expected.Add(sequentialEngine.GetCascadedStyle(element, includeInlineStyle: true));

        // A second, structurally identical document: reusing the first would compare a parallel run
        // against a cache the sequential run had already filled, which proves nothing.
        var (parallelDocument, parallelElements) = BuildDocument();
        var parallelEngine = new CssStyleEngine();
        parallelEngine.AddStyleSheet(new CssParser().ParseStyleSheet(sheet));

        // Eight, which is the thread count the roadmap's exit gate names, and deliberately more than
        // this machine's core count: oversubscription is what interleaves the ancestor recursion.
        var actual = new IReadOnlyDictionary<string, string>[parallelElements.Count];
        Parallel.For(
            0,
            parallelElements.Count,
            new ParallelOptions { MaxDegreeOfParallelism = 8 },
            i => actual[i] = parallelEngine.GetCascadedStyle(parallelElements[i], includeInlineStyle: true));

        Assert.Equal(expected.Count, actual.Length);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal(
                expected[i].OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray(),
                actual[i].OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray());
        }

        // The sheet has to be doing something, or the comparison above is a tautology over empty maps.
        Assert.Contains(expected, style => style.ContainsKey("color"));
        GC.KeepAlive(sequentialDocument);
        GC.KeepAlive(parallelDocument);
    }

    private static (DomDocument Document, List<DomElement> Elements) BuildDocument()
    {
        var document = new DomDocument();
        var html = document.CreateElement("html");
        var body = document.CreateElement("body");
        document.AppendChild(html);
        html.AppendChild(body);

        var elements = new List<DomElement> { html, body };
        for (var section = 0; section < 12; section++)
        {
            var host = document.CreateElement("section");
            body.AppendChild(host);
            elements.Add(host);

            for (var i = 0; i < 24; i++)
            {
                var div = document.CreateElement("div");
                div.ClassName = i % 2 == 0 ? "even" : "odd";
                div.Id = $"s{section}n{i}";
                div.SetAttribute("data-k", (i % 3).ToString(System.Globalization.CultureInfo.InvariantCulture));
                host.AppendChild(div);
                elements.Add(div);

                var em = document.CreateElement("em");
                div.AppendChild(em);
                elements.Add(em);
            }
        }

        return (document, elements);
    }
}
