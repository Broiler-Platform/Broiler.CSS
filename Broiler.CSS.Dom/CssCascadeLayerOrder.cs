using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Broiler.CSS.Cssom;

namespace Broiler.CSS.Dom;

/// <summary>
/// Maintains the cascade layer hierarchy, declaration ordering, and ordinal indices
/// for CSS Cascade 5 (§6.4 Cascade Layers).
/// </summary>
/// <remarks>
/// <para>
/// Layers are ordered by the order in which they first appear in a stylesheet or import.
/// Sublayers within a layer sort hierarchically: for two layers with the same parent,
/// the order they first appear sets their precedence; for layers with different parents,
/// their relative order is determined by their lowest common ancestor.
/// </para>
/// <para>
/// Normal declarations: unlayered styles beat all layers; later layers beat earlier layers.
/// Important declarations: layer precedence is reversed.
/// </para>
/// </remarks>
public sealed class CssCascadeLayerOrder
{
    /// <summary>
    /// The sentinel layer index assigned to unlayered style rules, which sorts higher
    /// than any cascade layer for normal declarations.
    /// </summary>
    public const int UnlayeredIndex = int.MaxValue;

    private readonly LayerNode _root = new(string.Empty, string.Empty, null);
    private readonly Dictionary<string, LayerNode> _nodesByFullName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<CssAtRule, LayerNode> _ruleToNode = [];
    private readonly List<string> _orderedLayerNames = [];

    private int _anonCounter;
    private bool _isFrozen;

    /// <summary>
    /// The total number of unique cascade layers registered.
    /// </summary>
    public int LayerCount => _nodesByFullName.Count;

    /// <summary>
    /// The canonical names of all registered layers in ascending order of normal cascade priority.
    /// </summary>
    public IReadOnlyList<string> OrderedLayerNames
    {
        get
        {
            EnsureFrozen();
            return _orderedLayerNames;
        }
    }

    /// <summary>
    /// Declares a layer by name (e.g. "base" or "framework.grid") in the given parent context.
    /// Returns the resolved canonical full name (e.g. "base" or "parent.framework.grid").
    /// </summary>
    public string DeclareLayer(string name, string? parentLayer = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        _isFrozen = false;

        var current = ResolveParentNode(parentLayer);
        var segments = CssLayerNameMetadata.GetSegments(name);
        if (segments.Length == 0)
            return string.Empty;

        foreach (var segment in segments)
        {
            if (!current.ChildrenByName.TryGetValue(segment, out var child))
            {
                var fullName = current == _root ? segment : $"{current.FullName}.{segment}";
                child = new LayerNode(segment, fullName, current);
                current.Children.Add(child);
                current.ChildrenByName[segment] = child;
                _nodesByFullName[fullName] = child;
            }
            current = child;
        }

        return current.FullName;
    }

    /// <summary>
    /// Declares a new anonymous layer in the given parent context.
    /// Returns a unique internal identifier for the anonymous layer.
    /// </summary>
    public string DeclareAnonymousLayer(string? parentLayer = null)
    {
        _isFrozen = false;
        var current = ResolveParentNode(parentLayer);

        var anonId = $"$anon_{++_anonCounter}";
        var fullName = current == _root ? anonId : $"{current.FullName}.{anonId}";
        var child = new LayerNode(anonId, fullName, current);

        current.Children.Add(child);
        _nodesByFullName[fullName] = child;
        return fullName;
    }

    /// <summary>
    /// Declares layer ordering from an @layer statement prelude (e.g. "reset, base, framework.grid;").
    /// </summary>
    public void DeclareStatement(string prelude, string? parentLayer = null)
    {
        if (string.IsNullOrWhiteSpace(prelude))
            return;

        var names = CssLayerNameMetadata.ParseStatementLayerNames(prelude);
        foreach (var name in names)
        {
            DeclareLayer(name, parentLayer);
        }
    }

    /// <summary>
    /// Returns the zero-based layer index for the given layer name, or <see cref="UnlayeredIndex"/>
    /// if <paramref name="name"/> is null, empty, or whitespace. Returns -1 if the layer is unknown.
    /// </summary>
    public int GetLayerIndex(string? name)
    {
        if (CssLayerNameMetadata.IsAnonymous(name))
            return UnlayeredIndex;

        EnsureFrozen();
        var normalized = CssLayerNameMetadata.NormalizeLayerName(name);
        return _nodesByFullName.TryGetValue(normalized, out var node) ? node.DirectRulesIndex : -1;
    }

    /// <summary>
    /// Returns the zero-based layer index for the given @layer block rule, or -1 if unknown.
    /// </summary>
    public int GetLayerIndex(CssAtRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        EnsureFrozen();
        return _ruleToNode.TryGetValue(rule, out var node) ? node.DirectRulesIndex : -1;
    }

    /// <summary>
    /// Scans a sequence of rules, registering all @layer statement rules, @layer blocks,
    /// and @import layers in their source declaration order.
    /// </summary>
    public void Scan(IEnumerable<CssRule> rules, string? parentLayer = null)
    {
        ArgumentNullException.ThrowIfNull(rules);
        _isFrozen = false;

        foreach (var rule in rules)
        {
            if (rule is not CssAtRule atRule)
                continue;

            if (atRule.Name.Equals("layer", StringComparison.OrdinalIgnoreCase))
            {
                if (atRule.HasBlock)
                {
                    var layerNode = CssLayerNameMetadata.IsAnonymous(atRule.Prelude)
                        ? GetOrCreateAnonymousNode(atRule, parentLayer)
                        : GetOrCreateNamedNode(atRule, parentLayer);

                    _ruleToNode[atRule] = layerNode;
                    if (atRule.Rules.Count > 0)
                        Scan(atRule.Rules, layerNode.FullName);
                }
                else
                {
                    DeclareStatement(atRule.Prelude, parentLayer);
                }
            }
            else if (atRule.Name.Equals("import", StringComparison.OrdinalIgnoreCase))
            {
                var meta = CssomRuleMetadata.GetImport(atRule);
                if (meta.Layer == CssImportLayer.Anonymous)
                {
                    var layerNode = GetOrCreateAnonymousNode(atRule, parentLayer);
                    _ruleToNode[atRule] = layerNode;
                }
                else if (meta.Layer == CssImportLayer.Named && meta.LayerName is not null)
                {
                    var layerNode = GetOrCreateNamedNode(atRule, parentLayer, meta.LayerName);
                    _ruleToNode[atRule] = layerNode;
                }
            }
            else if (atRule.HasBlock && atRule.Rules.Count > 0)
            {
                // Conditional group rules (@media, @supports, @container) do not form layers,
                // but any @layer rules declared inside them participate in the global layer order.
                Scan(atRule.Rules, parentLayer);
            }
        }
    }

    /// <summary>
    /// Freezes the registry and assigns linear ordinal indices to all registered layers.
    /// </summary>
    public void Freeze()
    {
        if (_isFrozen)
            return;

        var nextIndex = 0;
        AssignIndices(_root, ref nextIndex);

        _orderedLayerNames.Clear();
        var allNodes = _nodesByFullName.Values.OrderBy(static node => node.DirectRulesIndex);
        foreach (var node in allNodes)
        {
            _orderedLayerNames.Add(node.FullName);
        }

        _isFrozen = true;
    }

    /// <summary>
    /// Builds a frozen layer order registry by scanning the rules across the given stylesheets for a specific origin.
    /// </summary>
    public static CssCascadeLayerOrder Build(
        IReadOnlyList<(CssStyleSheet Sheet, CssOrigin Origin)> sheets,
        CssOrigin origin = CssOrigin.Author)
    {
        ArgumentNullException.ThrowIfNull(sheets);
        var order = new CssCascadeLayerOrder();

        foreach (var (sheet, sheetOrigin) in sheets)
        {
            if (sheetOrigin == origin)
                order.Scan(sheet.Rules);
        }

        order.Freeze();
        return order;
    }

    /// <summary>
    /// Builds a frozen layer order registry by scanning the given rules.
    /// </summary>
    public static CssCascadeLayerOrder Build(IReadOnlyList<CssRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var order = new CssCascadeLayerOrder();
        order.Scan(rules);
        order.Freeze();
        return order;
    }

    private void EnsureFrozen()
    {
        if (!_isFrozen)
            Freeze();
    }

    private LayerNode ResolveParentNode(string? parentLayer)
    {
        if (string.IsNullOrEmpty(parentLayer))
            return _root;

        var normalized = CssLayerNameMetadata.NormalizeLayerName(parentLayer);
        return _nodesByFullName.TryGetValue(normalized, out var node) ? node : _root;
    }

    private LayerNode GetOrCreateNamedNode(CssAtRule atRule, string? parentLayer, string? explicitName = null)
    {
        var name = explicitName ?? atRule.Prelude;
        var fullName = DeclareLayer(name, parentLayer);
        return _nodesByFullName[fullName];
    }

    private LayerNode GetOrCreateAnonymousNode(CssAtRule atRule, string? parentLayer)
    {
        if (_ruleToNode.TryGetValue(atRule, out var existing))
            return existing;

        var fullName = DeclareAnonymousLayer(parentLayer);
        return _nodesByFullName[fullName];
    }

    private void AssignIndices(LayerNode node, ref int nextIndex)
    {
        // First, assign indices to all sublayers in their declaration order.
        foreach (var child in node.Children)
        {
            AssignIndices(child, ref nextIndex);
        }

        // Direct rules inside this layer sort after all its sublayers.
        if (node != _root)
        {
            node.DirectRulesIndex = nextIndex++;
        }
    }

    private sealed class LayerNode(string name, string fullName, LayerNode? parent)
    {
        public string Name { get; } = name;
        public string FullName { get; } = fullName;
        public LayerNode? Parent { get; } = parent;
        public List<LayerNode> Children { get; } = [];
        public Dictionary<string, LayerNode> ChildrenByName { get; } = new(StringComparer.OrdinalIgnoreCase);
        public int DirectRulesIndex { get; set; } = -1;
    }
}
