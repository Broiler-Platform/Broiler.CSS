using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Broiler.CSS;

public abstract class CssRule
{
}

public sealed class CssStyleRule(CssSelectorList selectors, CssDeclarationBlock declarations, CssSourceRange range) : CssRule
{
    public CssSelectorList Selectors { get; } = selectors;

    public CssSourceRange Range { get; } = range;

    public CssDeclarationBlock Declarations { get; } = declarations;
}

public sealed class CssAtRule(string name, string prelude, string? blockText, CssDeclarationBlock? declarations,
    IEnumerable<CssRule>? rules, CssSourceRange range) : CssRule
{
    private readonly ReadOnlyCollection<CssRule> _rules = (rules ?? []).ToList().AsReadOnly();

    public string Name { get; } = name;

    public CssSourceRange Range { get; } = range;

    public string Prelude { get; } = prelude;

    public string? BlockText { get; } = blockText;

    public CssDeclarationBlock? Declarations { get; } = declarations;

    public IReadOnlyList<CssRule> Rules => _rules;

    public bool HasBlock => BlockText is not null;
}
