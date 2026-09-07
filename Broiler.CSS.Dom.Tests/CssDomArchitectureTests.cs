using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Broiler.CSS.Dom.Tests;

public sealed class CssDomArchitectureTests
{
    [Fact(Timeout = 600000)]
    public void Production_Project_References_Only_Css_And_Dom()
    {
        var projectPath = FindProjectPath();
        var project = XDocument.Load(projectPath);
        var references = project
            .Descendants("ProjectReference")
            .Select(element => ReferenceName((string?)element.Attribute("Include"), projectPath))
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["Broiler.CSS", "Broiler.Dom"], references);
        Assert.Empty(project.Descendants("PackageReference"));
    }

    [Fact(Timeout = 600000)]
    public void Public_Surface_Does_Not_Leak_Consumer_Types()
    {
        var assembly = typeof(CssSelectorMatcher).Assembly;
        var forbidden = assembly.GetExportedTypes()
            .SelectMany(GetMemberTypes)
            .Where(static type => type.Namespace is not null)
            .Where(static type =>
                type.Namespace.StartsWith("Broiler.HtmlBridge", StringComparison.Ordinal) ||
                type.Namespace.StartsWith("Broiler.HTML", StringComparison.Ordinal) ||
                type.Namespace.StartsWith("Broiler.JavaScript", StringComparison.Ordinal) ||
                type.Namespace.StartsWith("Broiler.Graphics", StringComparison.Ordinal))
            .Distinct()
            .ToArray();

        Assert.Empty(forbidden);
    }

    [Fact(Timeout = 600000)]
    public void Public_Surface_Does_Not_Expose_Mutable_Collections()
    {
        var assembly = typeof(CssStyleEngine).Assembly;
        var mutable = assembly.GetExportedTypes()
            .SelectMany(GetMemberTypes)
            .Where(static type => type.IsGenericType)
            .Select(static type => type.GetGenericTypeDefinition())
            .Where(static definition =>
                definition == typeof(List<>) ||
                definition == typeof(Dictionary<,>) ||
                definition == typeof(HashSet<>))
            .Distinct()
            .ToArray();

        Assert.Empty(mutable);
    }

    /// <summary>
    /// The referenced project's name, resolving an Include that is a bare MSBuild property.
    /// </summary>
    /// <remarks>
    /// The Dom kernel is referenced as <c>$(BroilerDomPath)</c> rather than by path, so that a
    /// standalone build takes the nested checkout while the aggregate solution points every
    /// component at one root checkout and does not create a second Broiler.Dom project node.
    /// This test reads the csproj as XML, which does no MSBuild evaluation, so without this the
    /// reference reads as its own literal and the assertion compares "$(BroilerDomPath)" against
    /// "Broiler.Dom". Resolving the property from the same Directory.Build.props the build uses
    /// keeps the assertion meaningful: it still fails if the property stops naming Broiler.Dom.
    /// </remarks>
    private static string? ReferenceName(string? include, string projectPath)
    {
        if (include is null)
            return null;

        var property = Regex.Match(include, @"^\$\((?<name>[A-Za-z_][A-Za-z0-9_]*)\)$");
        if (property.Success)
            include = ResolveProperty(property.Groups["name"].Value, projectPath) ?? include;

        // MSBuild accepts either separator; Path.GetFileNameWithoutExtension only splits on the
        // host's, so a props file written with backslashes would otherwise survive whole on Linux.
        return Path.GetFileNameWithoutExtension(include.Replace('\\', '/'));
    }

    /// <summary>
    /// The first definition of <paramref name="name"/> in a Directory.Build.props at or above the
    /// project, which is the one MSBuild's own evaluation reaches first from here.
    /// </summary>
    private static string? ResolveProperty(string name, string projectPath)
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(projectPath)!);
        while (directory is not null)
        {
            var props = Path.Combine(directory.FullName, "Directory.Build.props");
            if (File.Exists(props))
            {
                var value = XDocument.Load(props)
                    .Descendants()
                    .FirstOrDefault(element => element.Name.LocalName == name)?
                    .Value;
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }
            directory = directory.Parent;
        }
        return null;
    }

    private static IEnumerable<Type> GetMemberTypes(Type type)
    {
        yield return type;
        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
        {
            yield return method.ReturnType;
            foreach (var parameter in method.GetParameters())
                yield return parameter.ParameterType;
        }
        foreach (var property in type.GetProperties())
            yield return property.PropertyType;
    }

    private static string FindProjectPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string[] candidates =
            [
                Path.Combine(directory.FullName, "Broiler.CSS.Dom", "Broiler.CSS.Dom.csproj"),
                Path.Combine(directory.FullName, "Broiler.CSS", "Broiler.CSS.Dom", "Broiler.CSS.Dom.csproj"),
                Path.Combine(directory.FullName, "src", "Broiler.CSS.Dom", "Broiler.CSS.Dom.csproj"),
            ];

            var path = candidates.FirstOrDefault(File.Exists);
            if (path is not null)
                return path;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException();
    }
}
