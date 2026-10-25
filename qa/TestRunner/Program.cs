using System.Reflection;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

// Minimal xUnit runner usable when `dotnet test` cannot start its testhost
// (the DSH sandbox denies testhost's parent-process handle watch).
// Supports [Fact], [Theory]+[InlineData], and skips.

var assemblyPath = Path.Combine(AppContext.BaseDirectory, "FusionNet.Tests.dll");
string? filter = null;
foreach (var arg in args)
{
    if (arg.StartsWith("--assembly=", StringComparison.OrdinalIgnoreCase))
        assemblyPath = Path.GetFullPath(arg["--assembly=".Length..]);
    else if (arg.StartsWith("--filter=", StringComparison.OrdinalIgnoreCase))
        filter = arg["--filter=".Length..];
}

var assembly = Assembly.LoadFrom(assemblyPath);
var types = assembly.GetTypes()
    .Where(t => t is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false })
    .OrderBy(t => t.FullName, StringComparer.Ordinal)
    .ToList();

int passed = 0, failed = 0, skipped = 0;
var failures = new List<string>();

foreach (var type in types)
{
    var factMethods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
        .Where(m => m.GetCustomAttribute<FactAttribute>() is not null ||
                    m.GetCustomAttribute<TheoryAttribute>() is not null)
        .OrderBy(m => m.Name, StringComparer.Ordinal)
        .ToList();
    if (factMethods.Count == 0) continue;

    foreach (var method in factMethods)
    {
        var fact = method.GetCustomAttribute<FactAttribute>();
        var theory = method.GetCustomAttribute<TheoryAttribute>();
        var dataRows = new List<object?[]?>();

        if (theory is not null)
        {
            foreach (var attr in method.GetCustomAttributes<DataAttribute>())
            {
                IEnumerable<object?[]> rows;
                rows = attr.GetData(method);
                foreach (var row in rows)
                {
                    if (row is null) { dataRows.Add(new object?[] { null }); continue; }
                    dataRows.Add(row);
                }
            }
            if (dataRows.Count == 0) dataRows.Add(Array.Empty<object?>());
        }
        else
        {
            dataRows.Add(Array.Empty<object?>());
        }

        var skipReason = fact?.Skip;
        if (!string.IsNullOrEmpty(skipReason))
        {
            skipped++;
            Console.WriteLine($"SKIP  {type.Name}.{method.Name}  ({skipReason})");
            continue;
        }

        foreach (var row in dataRows)
        {
            var label = row is { Length: > 0 }
                ? $"{type.Name}.{method.Name}({string.Join(", ", row.Select(Format))})"
                : $"{type.Name}.{method.Name}";

            if (!string.IsNullOrEmpty(filter) && label.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;

            object? instance = null;
            try
            {
                instance = Activator.CreateInstance(type);
                if (row is null)
                {
                    failed++;
                    failures.Add($"{label}: theory data row was null");
                    continue;
                }
                var result = method.Invoke(instance, row);
                if (result is Task task) task.GetAwaiter().GetResult();
                else if (result is ValueTask vt) vt.GetAwaiter().GetResult();
                passed++;
            }
            catch (Exception ex)
            {
                failed++;
                var root = ex;
                while (root is TargetInvocationException { InnerException: not null } tie) root = tie.InnerException!;
                failures.Add($"{label}{Environment.NewLine}      {root.GetType().Name}: {root.Message}");
                Console.WriteLine($"FAIL  {label}");
            }
            finally
            {
                (instance as IDisposable)?.Dispose();
            }
        }
    }
}

Console.WriteLine();
Console.WriteLine($"Total: {passed + failed + skipped}  Passed: {passed}  Failed: {failed}  Skipped: {skipped}");
if (failures.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine("Failures:");
    foreach (var f in failures) Console.WriteLine("  - " + f);
    return 1;
}
return 0;

static string Format(object? value) => value switch
{
    null => "null",
    string s => $"\"{s}\"",
    _ => value.ToString() ?? "null"
};
