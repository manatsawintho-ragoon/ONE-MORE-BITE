using System.Text;
using KruaImDee.Models;

namespace KruaImDee.Services;

// Compare spelling and Thai sounds without an external service.
public static class MenuSearch
{
    public static List<MenuSuggestion> Suggest(List<MenuItem> items, string query, int limit = 8)
    {
        var results = new List<MenuSuggestion>();
        query = (query ?? "").Trim();
        if (query.Length > 80)
        {
            query = query.Substring(0, 80);
        }

        foreach (MenuItem item in items)
        {
            double score = query.Length == 0 ? 1 : Score(query, item.Name);
            if (score < 0.28)
            {
                continue;
            }

            results.Add(
                new MenuSuggestion
                {
                    Id = item.Id,
                    Name = item.Name,
                    ImageUrl = item.ImageUrl,
                    Price = item.EffectivePrice,
                    OriginalPrice = item.Price,
                    DiscountPercent = item.DiscountPercent,
                    Score = Math.Round(score, 3),
                }
            );
        }

        return results
            .OrderByDescending(result => result.Score)
            .ThenBy(result => result.Id)
            .Take(limit)
            .ToList();
    }

    public static double Score(string query, string name)
    {
        string normalizedQuery = Normalize(query, false);
        string normalizedName = Normalize(name, false);
        if (normalizedQuery.Length == 0 || normalizedName.Length == 0)
        {
            return 0;
        }

        if (normalizedName == normalizedQuery)
        {
            return 1;
        }

        if (normalizedName.Contains(normalizedQuery))
        {
            return 0.98;
        }

        string phoneticQuery = Normalize(query, true);
        string phoneticName = Normalize(name, true);
        if (phoneticName.Contains(phoneticQuery))
        {
            return 0.92;
        }

        double vectorScore = Cosine(normalizedQuery, normalizedName);
        double phoneticScore = Cosine(phoneticQuery, phoneticName) * 0.85;
        double editScore =
            1.0
            - (double)EditDistance(normalizedQuery, normalizedName)
                / Math.Max(normalizedQuery.Length, normalizedName.Length);
        return Math.Max(vectorScore, Math.Max(phoneticScore, editScore * 0.8));
    }

    private static string Normalize(string text, bool phonetic)
    {
        var normalized = new StringBuilder();
        string[] consonantGroups = new string[]
        {
            "ก",
            "ขฃคฅฆ",
            "จ",
            "ฉชฌ",
            "ซศษส",
            "ญย",
            "ฎฏดต",
            "ฐฑฒถทธ",
            "ณน",
            "ผพภ",
            "ฝฟ",
            "ลฬ",
        };
        foreach (char original in text.ToLowerInvariant())
        {
            if (char.IsWhiteSpace(original) || char.IsPunctuation(original))
            {
                continue;
            }

            if ("่้๊๋์็".Contains(original))
            {
                continue;
            }

            char value = original;
            if (phonetic)
            {
                foreach (string group in consonantGroups)
                {
                    if (!group.Contains(value))
                    {
                        continue;
                    }

                    value = group[0];
                    break;
                }
            }

            normalized.Append(value);
        }

        return normalized.ToString();
    }

    private static Dictionary<string, int> Bigrams(string text)
    {
        var counts = new Dictionary<string, int>();
        for (int index = 0; index < text.Length - 1; index++)
        {
            string pair = text.Substring(index, 2);
            if (!counts.ContainsKey(pair))
            {
                counts[pair] = 0;
            }

            counts[pair]++;
        }

        return counts;
    }

    // Character-pair counts form local vectors; cosine compares their direction.
    private static double Cosine(string first, string second)
    {
        var left = Bigrams(first);
        var right = Bigrams(second);
        double dot = 0;
        double leftLength = 0;
        double rightLength = 0;
        foreach (var pair in left)
        {
            leftLength += pair.Value * pair.Value;
            if (right.ContainsKey(pair.Key))
            {
                dot += pair.Value * right[pair.Key];
            }
        }

        foreach (int count in right.Values)
        {
            rightLength += count * count;
        }

        if (leftLength == 0 || rightLength == 0)
        {
            return 0;
        }

        return dot / Math.Sqrt(leftLength * rightLength);
    }

    private static int EditDistance(string first, string second)
    {
        int[,] distance = new int[first.Length + 1, second.Length + 1];
        for (int row = 0; row <= first.Length; row++)
        {
            distance[row, 0] = row;
        }

        for (int column = 0; column <= second.Length; column++)
        {
            distance[0, column] = column;
        }

        for (int row = 1; row <= first.Length; row++)
        {
            for (int column = 1; column <= second.Length; column++)
            {
                int change = first[row - 1] == second[column - 1] ? 0 : 1;
                int deletion = distance[row - 1, column] + 1;
                int insertion = distance[row, column - 1] + 1;
                int replacement = distance[row - 1, column - 1] + change;
                distance[row, column] = Math.Min(deletion, Math.Min(insertion, replacement));
            }
        }

        return distance[first.Length, second.Length];
    }
}
