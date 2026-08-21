using System.Reflection;

namespace Broiler.CSS.Tests;

public sealed class CssArchitectureTests
{
    [Fact(Timeout = 600000)]
    public void Kernel_Has_No_NonFramework_Assembly_Dependencies()
    {
        var dependencies = typeof(CssParser).Assembly
            .GetReferencedAssemblies()
            .Select(static assembly => assembly.Name)
            .Where(static name =>
                name is not null &&
                !name.StartsWith("System", StringComparison.Ordinal) &&
                !string.Equals(name, "mscorlib", StringComparison.Ordinal) &&
                !string.Equals(name, "netstandard", StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(dependencies);
    }

    [Fact(Timeout = 600000)]
    public void Project_Has_No_Project_Or_Package_References()
    {
        var projectPath = FindProjectPath();
        var projectText = File.ReadAllText(projectPath);

        Assert.DoesNotContain("<ProjectReference", projectText, StringComparison.Ordinal);
        Assert.DoesNotContain("<PackageReference", projectText, StringComparison.Ordinal);
    }

    [Fact(Timeout = 600000)]
    public void Public_Surface_Does_Not_Leak_Other_Broiler_Types()
    {
        var leaks = typeof(CssParser).Assembly
            .GetExportedTypes()
            .SelectMany(static type => type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            .SelectMany(GetMemberTypes)
            .Where(static type =>
                type.Namespace?.StartsWith("Broiler.", StringComparison.Ordinal) == true &&
                type.Namespace != "Broiler.CSS" &&
                // The CSS component's own sub-namespaces (e.g. Broiler.CSS.Cssom) are
                // not foreign-component leaks. The kernel has no project references,
                // so it can only ever expose its own namespaces here.
                !type.Namespace.StartsWith("Broiler.CSS.", StringComparison.Ordinal))
            .Select(static type => type.FullName)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(leaks);
    }

    [Fact(Timeout = 600000)]
    public void Mutable_Collections_Are_Not_Publicly_Exposed()
    {
        var leaks = typeof(CssParser).Assembly
            .GetExportedTypes()
            .SelectMany(static type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            .Where(static property =>
                property.PropertyType.IsGenericType &&
                property.PropertyType.GetGenericTypeDefinition() is var definition &&
                (definition == typeof(List<>) ||
                 definition == typeof(Dictionary<,>) ||
                 definition == typeof(HashSet<>)))
            .Select(static property => $"{property.DeclaringType!.Name}.{property.Name}")
            .ToArray();

        Assert.Empty(leaks);
    }

    private static string FindProjectPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string[] candidates =
            [
                Path.Combine(directory.FullName, "Broiler.CSS", "Broiler.CSS.csproj"),
                Path.Combine(directory.FullName, "Broiler.CSS", "Broiler.CSS", "Broiler.CSS.csproj"),
                Path.Combine(directory.FullName, "src", "Broiler.CSS", "Broiler.CSS.csproj"),
            ];

            var project = candidates.FirstOrDefault(File.Exists);
            if (project is not null)
                return project;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate Broiler.CSS.csproj.");
    }

    private static IEnumerable<Type> GetMemberTypes(MemberInfo member) => member switch
    {
        MethodInfo method => [method.ReturnType, .. method.GetParameters().Select(static parameter => parameter.ParameterType)],
        PropertyInfo property => [property.PropertyType],
        FieldInfo field => [field.FieldType],
        EventInfo eventInfo when eventInfo.EventHandlerType is not null => [eventInfo.EventHandlerType],
        _ => [],
    };
}
