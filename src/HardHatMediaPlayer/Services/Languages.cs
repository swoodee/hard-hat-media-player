using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace HardHatMediaPlayer.Services;

/// <summary>Turns the many ways tracks name their language ("eng", "English", "en-US", "rum") into two-letter codes.</summary>
public static class Languages
{
    private static readonly Lazy<Dictionary<string, string>> Lookup = new(Build);

    private static Dictionary<string, string> Build()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in CultureInfo.GetCultures(CultureTypes.NeutralCultures))
        {
            var two = c.TwoLetterISOLanguageName;
            if (two.Length != 2) continue;
            map.TryAdd(two, two);
            map.TryAdd(c.ThreeLetterISOLanguageName, two);
            map.TryAdd(c.EnglishName, two);
            map.TryAdd(c.NativeName, two);
        }
        // ISO 639-2/B codes, which Matroska files often use instead of the /T ones .NET knows.
        foreach (var (b, two) in new[]
                 {
                     ("rum", "ro"), ("fre", "fr"), ("ger", "de"), ("dut", "nl"), ("chi", "zh"), ("cze", "cs"), ("gre", "el"),
                     ("per", "fa"), ("alb", "sq"), ("arm", "hy"), ("baq", "eu"), ("bur", "my"), ("geo", "ka"), ("ice", "is"),
                     ("mac", "mk"), ("mao", "mi"), ("may", "ms"), ("slo", "sk"), ("tib", "bo"), ("wel", "cy"), ("scc", "sr"),
                     ("scr", "hr"), ("pob", "pt"), ("moldavian", "ro"),
                 })
            map[b] = two;
        return map;
    }

    /// <summary>The two-letter code for a language name or code, or null when it isn't recognised.</summary>
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var v = value.Trim();
        if (Lookup.Value.TryGetValue(v, out var code)) return code;
        // "en-US", "pt_BR", "English (SDH)"
        var head = v.Split('-', '_', '(', ' ')[0];
        return head != v && Lookup.Value.TryGetValue(head, out code) ? code : null;
    }

    /// <summary>Guesses the language from a libVLC track name such as "Track 2 - [Romanian]" or "English (SDH)".</summary>
    public static string? FromTrackName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        foreach (Match m in Regex.Matches(name, @"\[([^\]]+)\]").Reverse())
            if (Normalize(m.Groups[1].Value) is { } code) return code;
        foreach (var word in Regex.Split(name, @"[^\p{L}]+").Where(w => w.Length >= 4))
            if (Normalize(word) is { } code) return code;
        return null;
    }

    /// <summary>"ro, en" → ["ro", "en"]; unknown entries are kept as typed (lower case).</summary>
    public static List<string> ParseList(string? list) =>
        (list ?? "").Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(l => Normalize(l) ?? l.ToLowerInvariant())
            .Distinct()
            .ToList();

    /// <summary>"ro" → "Romanian".</summary>
    public static string DisplayName(string code)
    {
        try { return CultureInfo.GetCultureInfo(code).EnglishName; }
        catch (CultureNotFoundException) { return code; }
    }
}
