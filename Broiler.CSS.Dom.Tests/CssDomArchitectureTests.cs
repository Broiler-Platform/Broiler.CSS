using System.Reflection;
using System.Xml.Linq;

namespace Broiler.CSS.Dom.Tests;

public sealed class CssDomArchitectureTests
{
    [Fact(Timeout = 600000)]
    public void Production_Project_References_Only_Css_And_Dom()
    {
        var project = XDocument.Load(FindProjectPath());
        var references = project
            .Descendants("ProjectReference")
            .Select(static element => Path.GetFileNameWithoutExtension((string?)element.Attribute("Include")))
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
