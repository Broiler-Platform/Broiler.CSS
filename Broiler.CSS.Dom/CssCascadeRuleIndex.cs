using System;
using System.Collections.Generic;
using Broiler.Dom;

namespace Broiler.CSS.Dom;

/// <summary>
/// A cascade-ready view of a stylesheet set: every style rule flattened into document order and
/// filed under the simple selector its subject must carry, so an element tests a handful of
/// candidate rules instead of all of them. Multithreading roadmap item #11.
/// </summary>
/// <remarks>
/// <para>
/// The cascade was a linear scan: for every element, every rule of every sheet, selector-matched.
/// The measured shape of that is in <c>docs/architecture/multithreading.md</c> — on a 900-rule,
/// 700-element page the cascade is 96.3% of a 5.0 s render, and holding the <em>matched</em> rule
/// count fixed at four while growing the sheet from 100 to 3 200 rules took the cold cascade from
/// 114.9 ms to 3 543.6 ms and the allocation from 181.7 MB to 5 817.9 MB. Cost was linear in
/// total rules; it should be linear in matched ones.
/// </para>
/// <para>
/// <b>Correctness rests on two properties, both deliberate.</b>
/// </para>
/// <list type="number">
/// <item><description>
/// A key is a <em>necessary</em> condition for a match, never a sufficient one — see
/// <see cref="CssSelectorParser.GetKey"/>. Candidates still go through the full selector matcher
/// unchanged, so the index can only ever be wrong by <em>omitting</em> a rule, and every
/// uncertain case is filed under the universal bucket instead.
/// </description></item>
/// <item><description>
/// Candidates are returned in document order, the order the linear scan visited them in. The
/// cascade's tie-break counter is relative, and a rule that never matched never advanced it, so
/// visiting only the candidates produces the same winners the full scan did.
/// </description></item>
/// </list>
/// <para>
/// <b>Conditional groups are resolved as far as they can be, once.</b> <c>@media</c> and
/// <c>@supports</c> depend on the environment and the feature oracle, both fixed for an index's
/// lifetime, so a group that does not apply is dropped at build time and costs nothing per
/// element. <c>@container</c> depends on the element being styled, so those conditions travel with
/// the entry and are evaluated per candidate — but only for candidates, which is already far fewer
/// evaluations than the scan performed.
/// </para>
/// <para>
/// An index is immutable once built. The engine rebuilds it when the sheet set or environment
/// changes, and — importantly — <em>not</em> when the DOM mutates: the index is a function of the
/// stylesheets alone, so DOM churn, which invalidates the computed-style caches constantly, must
/// not throw it away.
/// </para>
/// </remarks>
internal sealed class CssCascadeRuleIndex
{
    /// <summary>
    /// One style rule as the cascade will visit it: its origin, its position in document order,
    /// and any element-dependent conditional groups enclosing it.
    /// </summary>
    internal readonly record struct Entry(
        CssStyleRule Rule,
        CssOrigin Origin,
        CssAtRule[]? ContainerConditions);

    private readonly Entry[] _entries;
    private readonly Dictionary<string, List<int>> _byId;
    private readonly Dictionary<string, List<int>> _byClass;
    private readonly Dictionary<string, List<int>> _byType;
    private readonly List<int> _universal;

    private CssCascadeRuleIndex(
        Entry[] entries,
        Dictionary<string, List<int>> byId,
        Dictionary<string, List<int>> byClass,
        Dictionary<string, List<int>> byType,
        List<int> universal)
    {
        _entries = entries;
        _byId = byId;
        _byClass = byClass;
        _byType = byType;
        _universal = universal;
    }

    /// <summary>Total style rules in the index, after conditional-group filtering.</summary>
    internal int RuleCount => _entries.Length;

    /// <summary>Rules no key could narrow, which every element has to test.</summary>
    internal int UniversalRuleCount => _universal.Count;

    /// <summary>
    /// Builds an index over the sheets in cascade order. <paramref name="mediaApplies"/> and
    /// <paramref name="supportsApplies"/> decide the environment-fixed conditional groups now;
    /// <c>@container</c> groups are kept for per-element evaluation.
    /// </summary>
    internal static CssCascadeRuleIndex Build(
        IReadOnlyList<(CssStyleSheet Sheet, CssOrigin Origin)> sheets,
        Func<string, bool> mediaApplies,
        Func<string, bool> supportsApplies)
    {
        var entries = new List<Entry>();
        var byId = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        var byClass = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        var byType = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        var universal = new List<int>();

        void File(int entryIndex, CssStyleRule rule)
        {
            var filed = false;

            foreach (var selector in rule.Selectors.Selectors)
            {
                var key = CssSelectorParser.GetKey(selector.Text);
                var bucket = key.Kind switch
                {
                    CssSelectorKeyKind.Id => byId,
                    CssSelectorKeyKind.Class => byClass,
                    CssSelectorKeyKind.Type => byType,
                    _ => null,
                };

                if (bucket is null)
                {
                    // One unkeyable selector in the list makes the whole rule universal: the rule
                    // is visited as a unit, and dropping it from the universal bucket because a
                    // *different* selector in the same list was keyable would lose the match.
                    universal.Add(entryIndex);
                    return;
                }

                var list = bucket.TryGetValue(key.Name, out var existing)
                    ? existing
                    : bucket[key.Name] = [];

                // A selector list may name the same key twice (`.a p, .b p`); one entry per bucket
                // is enough, and duplicates would make the candidate merge do needless work.
                if (list.Count == 0 || list[^1] != entryIndex)
                    list.Add(entryIndex);

                filed = true;
            }

            // A rule with no selectors at all can never match; filing it universally would only
            // make every element pay for it.
            if (!filed)
                return;
        }

        void Walk(IReadOnlyList<CssRule> rules, CssOrigin origin, List<CssAtRule>? containerConditions)
        {
            foreach (var rule in rules)
            {
                switch (rule)
                {
                    case CssStyleRule styleRule:
                        entries.Add(new Entry(
                            styleRule,
                            origin,
                            containerConditions is { Count: > 0 } ? containerConditions.ToArray() : null));
                        File(entries.Count - 1, styleRule);
                        break;

                    case CssAtRule atRule when atRule.Name.Equals("media", StringComparison.OrdinalIgnoreCase):
                        if (mediaApplies(atRule.Prelude))
                            Walk(atRule.Rules, origin, containerConditions);
                        break;

                    case CssAtRule atRule when atRule.Name.Equals("supports", StringComparison.OrdinalIgnoreCase):
                        if (supportsApplies(atRule.Prelude))
                            Walk(atRule.Rules, origin, containerConditions);
                        break;

                    case CssAtRule atRule when atRule.Name.Equals("container", StringComparison.OrdinalIgnoreCase):
                    {
                        // Element-dependent: kept, not decided.
                        var nested = containerConditions is null
                            ? [atRule]
                            : new List<CssAtRule>(containerConditions) { atRule };
                        Walk(atRule.Rules, origin, nested);
                        break;
                    }
                }
            }
        }

        foreach (var (sheet, origin) in sheets)
            Walk(sheet.Rules, origin, containerConditions: null);

        return new CssCascadeRuleIndex([.. entries], byId, byClass, byType, universal);
    }

    /// <summary>
    /// Appends the entry indices <paramref name="element"/> could match, in document order, to
    /// <paramref name="candidates"/> (which is cleared first).
    /// </summary>
    /// <remarks>
    /// The buckets are each already in ascending document order, so this is a k-way merge rather
    /// than a sort — and the merge is what restores the visiting order the linear scan had, which
    /// is what keeps the cascade's tie-breaking identical.
    /// </remarks>
    internal void CollectCandidates(DomElement element, List<int> candidates)
    {
        candidates.Clear();

        // At most: universal + type + id + one per class.
        var lists = new List<List<int>>(4);

        if (_universal.Count > 0)
            lists.Add(_universal);

        if (_byType.Count > 0 && _byType.TryGetValue(element.LocalName, out var typeBucket))
            lists.Add(typeBucket);

        if (_byId.Count > 0 &&
            element.GetAttribute("id") is { Length: > 0 } id &&
            _byId.TryGetValue(id, out var idBucket))
        {
            lists.Add(idBucket);
        }

        if (_byClass.Count > 0 && element.GetAttribute("class") is { Length: > 0 } classAttribute)
        {
            foreach (var className in classAttribute.Split(
                         (char[]?)null,
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (_byClass.TryGetValue(className, out var classBucket))
                    lists.Add(classBucket);
            }
        }

        switch (lists.Count)
        {
            case 0:
                return;

            case 1:
                candidates.AddRange(lists[0]);
                return;
        }

        Merge(lists, candidates);
    }

    /// <summary>Merges ascending index lists into one ascending list, dropping duplicates.</summary>
    private static void Merge(List<List<int>> lists, List<int> candidates)
    {
        var cursors = new int[lists.Count];
        var previous = -1;

        while (true)
        {
            var smallest = int.MaxValue;
            var from = -1;

            for (var i = 0; i < lists.Count; i++)
            {
                var cursor = cursors[i];
                if (cursor < lists[i].Count && lists[i][cursor] < smallest)
                {
                    smallest = lists[i][cursor];
                    from = i;
                }
            }

            if (from < 0)
                return;

            cursors[from]++;

            // A rule reachable through two keys (`.a.b` on an element with both) must be visited
            // once, exactly as the linear scan visited it once.
            if (smallest != previous)
            {
                candidates.Add(smallest);
                previous = smallest;
            }
        }
    }

    /// <summary>The entry at <paramref name="index"/>, as returned by <see cref="CollectCandidates"/>.</summary>
    internal Entry this[int index] => _entries[index];
}
