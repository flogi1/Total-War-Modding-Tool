using System.Reflection;
using System.Text.RegularExpressions;

namespace TotalWarModTool.Localization;

/// <summary>Compares the locale files with the texts the code actually asks for. Meant to run as a test, so a forgotten or stale text fails the build.</summary>
public static class LocaleCheck
{
    private static readonly Regex EscapedBraces = new(@"\{\{|\}\}", RegexOptions.CultureInvariant);
    private static readonly Regex Placeholder = new(@"\{(\d+)", RegexOptions.CultureInvariant);

    /// <summary>
    /// Reads every public <see langword="string"/> property and every public method returning <see langword="string"/> of each text class
    /// (numeric arguments 0, text arguments empty), then checks the file of each language in <paramref name="completeLanguages"/>
    /// (which should include <c>en</c>). One line per problem, sorted:
    /// <c>json:&lt;lang&gt;:&lt;line&gt;:&lt;position&gt;</c> (file is not valid JSON; no further check of that language),
    /// <c>missing:&lt;lang&gt;:&lt;key&gt;</c> (key missing or empty),
    /// <c>fallback-differs:&lt;key&gt;</c> (en text differs from the inline fallback),
    /// <c>placeholders:&lt;lang&gt;:&lt;key&gt;</c> (set of {n} differs from the inline fallback),
    /// <c>unused:&lt;lang&gt;:&lt;key&gt;</c> (key is in the file, but no text class asks for it).
    /// </summary>
    public static IReadOnlyList<string> FindProblems(IEnumerable<TextsBase> texts, string localesDirectory, IReadOnlyList<string> completeLanguages)
    {
        var requested = new List<KeyValuePair<string, string>>();
        foreach (var instance in texts)
        {
            ReadAllTexts(instance);
            requested.AddRange(instance.RequestedKeys);
        }

        var requestedKeys = requested.Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal);
        var problems = new HashSet<string>(StringComparer.Ordinal);
        foreach (var language in completeLanguages)
        {
            var file = LocaleFileReader.Read(Path.Combine(localesDirectory, language + ".json"));
            if (file.Error is { } error)
            {
                problems.Add($"json:{language}:{error.Line}:{error.Position}");
                continue;
            }

            foreach (var (key, fallback) in requested)
            {
                if (!file.Texts.TryGetValue(key, out var text) || text.Length == 0)
                {
                    problems.Add($"missing:{language}:{key}");
                    continue;
                }

                if (language == UiText.ReferenceLanguage && text != fallback)
                {
                    problems.Add($"fallback-differs:{key}");
                }

                if (!PlaceholderIndices(text).SetEquals(PlaceholderIndices(fallback)))
                {
                    problems.Add($"placeholders:{language}:{key}");
                }
            }

            foreach (var key in file.Texts.Keys.Where(key => !requestedKeys.Contains(key)))
            {
                problems.Add($"unused:{language}:{key}");
            }
        }

        return problems.Order(StringComparer.Ordinal).ToList();
    }

    private static void ReadAllTexts(TextsBase instance)
    {
        var type = instance.GetType();
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.PropertyType == typeof(string) && property.GetIndexParameters().Length == 0 && property.GetGetMethod() is not null)
            {
                property.GetValue(instance);
            }
        }

        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            if (method.ReturnType == typeof(string) && !method.IsSpecialName && !method.IsGenericMethodDefinition && method.DeclaringType != typeof(object))
            {
                method.Invoke(instance, method.GetParameters().Select(parameter => DefaultArgument(parameter.ParameterType)).ToArray());
            }
        }
    }

    // Numbers become 0 (the default of their type), text becomes empty.
    private static object? DefaultArgument(Type type) =>
        type == typeof(string) ? string.Empty
        : type.IsValueType ? Activator.CreateInstance(type)
        : null;

    // The set of placeholder numbers of a format pattern; "{0:N0}" counts as {0}, escaped braces "{{" and "}}" are no placeholders.
    private static HashSet<string> PlaceholderIndices(string pattern) =>
        Placeholder.Matches(EscapedBraces.Replace(pattern, string.Empty)).Select(match => match.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
}
