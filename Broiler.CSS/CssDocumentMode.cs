namespace Broiler.CSS;

/// <summary>
/// The document-mode (quirks vs standards) flag for the render on this thread, as far as the
/// CSS layer is concerned.
/// </summary>
/// <remarks>
/// <para>
/// A handful of CSS rules are relaxed in quirks mode, and the cascade has to know which mode it
/// is running in to apply them — most visibly the "hashless colour and unitless length" quirk
/// (https://quirks.spec.whatwg.org/#the-hashless-hex-color-quirk), under which a declaration
/// such as <c>width: 200</c> is a valid length instead of a parse error.
/// </para>
/// <para>
/// It is <c>[ThreadStatic]</c> in this assembly for the same reason
/// <see cref="CssLengthParser.ViewportEstablishedOnThisThread"/> is: the flag's canonical home is
/// <c>Broiler.Layout.DocumentModeContext</c>, but Broiler.CSS cannot reference Broiler.Layout —
/// the dependency runs the other way. <c>DocumentModeContext.CurrentQuirksMode</c>'s setter
/// mirrors every write here, so the two hosts that already publish the flag on each parse (the
/// DOM-bridge path and <c>HtmlContainerInt.SetHtmlWithStyleSet</c>) need no change.
/// </para>
/// <para>
/// The default is standards mode. A thread that never published a document — a bare
/// <c>CssParser</c> or cascade-engine caller with no page behind it — is parsing plain CSS, which
/// is exactly the strict reading.
/// </para>
/// </remarks>
public static class CssDocumentMode
{
    [System.ThreadStatic]
    private static bool _quirksMode;

    /// <summary>Whether the document being styled on this thread is in quirks mode.</summary>
    public static bool QuirksMode
    {
        get => _quirksMode;
        set => _quirksMode = value;
    }
}
