using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
    [Fact]
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
}
