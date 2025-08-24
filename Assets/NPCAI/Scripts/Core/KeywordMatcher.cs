using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;

[Serializable]
public class KeywordSet
{
	[TextArea] public string[] entries = Array.Empty<string>();
	[Range(0.5f, 1f)] public float minSimilarity = 0.78f;

	public bool Matches(string text) => KeywordMatcher.Matches(entries, text, minSimilarity);
}

public static class KeywordMatcher
{
	public static bool Matches(IEnumerable<string> entries, string text, float minSimilarity = 0.78f)
	{
		if (entries == null) return false;
		var normText = Normalize(text);
		if (string.IsNullOrEmpty(normText)) return false;

		var tokens = Tokenize(normText);
		foreach (var raw in entries)
		{
			if (string.IsNullOrWhiteSpace(raw)) continue;
			var term = Normalize(raw);

			if (term.Contains(' '))
			{
				if (normText.Contains(term)) return true;
				continue;
			}

			foreach (var tok in tokens)
			{
				if (tok == term) return true;
				var sim = Similarity(tok, term);
				if (sim >= minSimilarity) return true;
				if (tok.Contains(term) || term.Contains(tok)) return true;
			}
		}
		return false;
	}

	public static string Normalize(string s)
	{
		if (string.IsNullOrEmpty(s)) return string.Empty;
		s = s.ToLowerInvariant().Normalize(NormalizationForm.FormD);

		var sb = new StringBuilder(s.Length);
		foreach (var ch in s)
		{
			var uc = CharUnicodeInfo.GetUnicodeCategory(ch);
			if (uc != UnicodeCategory.NonSpacingMark && uc != UnicodeCategory.EnclosingMark)
				sb.Append(ch);
		}
		s = sb.ToString().Normalize(NormalizationForm.FormC);

		var arr = s.Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray();
		s = new string(arr);
		while (s.Contains("  ")) s = s.Replace("  ", " ");
		return s.Trim();
	}

	public static List<string> Tokenize(string norm) =>
		string.IsNullOrEmpty(norm) ? new List<string>() : norm.Split(' ').Where(t => t.Length > 0).ToList();

	public static float Similarity(string a, string b)
	{
		if (a == b) return 1f;
		if (a.Length == 0 || b.Length == 0) return 0f;
		int dist = Levenshtein(a, b);
		int m = Math.Max(a.Length, b.Length);
		return 1f - (float)dist / m;
	}

	public static int Levenshtein(string a, string b)
	{
		int n = a.Length, m = b.Length;
		var d = new int[n + 1, m + 1];
		for (int i = 0; i <= n; i++) d[i, 0] = i;
		for (int j = 0; j <= m; j++) d[0, j] = j;
		for (int i = 1; i <= n; i++)
		{
			for (int j = 1; j <= m; j++)
			{
				int cost = a[i - 1] == b[j - 1] ? 0 : 1;
				d[i, j] = Math.Min(
					Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
					d[i - 1, j - 1] + cost
				);
			}
		}
		return d[n, m];
	}
}
