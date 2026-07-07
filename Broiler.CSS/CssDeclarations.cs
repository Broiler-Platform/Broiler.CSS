using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Broiler.CSS;

public sealed class CssDeclaration(string name, CssValue value, bool important)
{
    public string Name { get; } = name;
    public CssValue Value { get; } = value;
    public bool Important { get; } = important;
}

public sealed class CssDeclarationBlock(IEnumerable<CssDeclaration> declarations)
{
    private readonly ReadOnlyCollection<CssDeclaration> _declarations = declarations.ToList().AsReadOnly();

    public IReadOnlyList<CssDeclaration> Declarations => _declarations;

    public CssDeclaration? GetLastDeclaration(string propertyName)
    {
        // Manual reverse scan avoids the LINQ iterator/closure allocation of
        // LastOrDefault on this per-lookup hot path.
        for (var i = _declarations.Count - 1; i >= 0; i--)
        {
            if (string.Equals(_declarations[i].Name, propertyName, StringComparison.OrdinalIgnoreCase))
                return _declarations[i];
        }

        return null;
    }

    public string? GetPropertyValue(string propertyName) => GetLastDeclaration(propertyName)?.Value.Text;
}
