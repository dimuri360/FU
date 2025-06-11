using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace EinstellungenApp
{
    public class RssReader
    {
        private readonly Action<string> _log;

        public RssReader(Action<string> log)
        {
            _log = log;
        }

        public async Task RunAsync(CancellationToken token = default)
        {
            const string inputFile = "RSS_Links.txt";
            const string outputFile = "RSS_OUT.txt";
            const string outputTitlesFile = "RSS_OUT2.txt";
            const string outputNgramsFile = "RSS_OUT3.txt";

            if (!File.Exists(inputFile))
            {
                _log($"Die Datei \"{inputFile}\" wurde nicht gefunden.");
                return;
            }

            File.WriteAllText(outputFile, string.Empty);
            File.WriteAllText(outputTitlesFile, string.Empty);
            File.WriteAllText(outputNgramsFile, string.Empty);

            var unigramCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var bigramCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var trigramCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            var links = File.ReadAllLines(inputFile);

            using (HttpClient client = new HttpClient())
            {
                foreach (var link in links)
                {
                    if (token.IsCancellationRequested)
                        break;

                    if (string.IsNullOrWhiteSpace(link))
                        continue;

                    try
                    {
                        string content = await client.GetStringAsync(link, token);
                        XDocument doc = XDocument.Parse(content);
                        var items = doc.Descendants("item");

                        string formattedOutput = $"----- Inhalt von {link} -----{Environment.NewLine}";
                        string formattedTitles = $"----- Title & Description von {link} -----{Environment.NewLine}";

                        foreach (var item in items)
                        {
                            string title = RemoveImageTags(item.Element("title")?.Value ?? "N/A");
                            string description = RemoveImageTags(item.Element("description")?.Value ?? "N/A");
                            string itemLink = RemoveImageTags(item.Element("link")?.Value ?? "N/A");
                            string category = RemoveImageTags(item.Element("category")?.Value ?? "N/A");
                            string pubDate = RemoveImageTags(item.Element("pubDate")?.Value ?? "N/A");

                            formattedOutput += $"Title: {title}{Environment.NewLine}";
                            formattedOutput += $"Description: {description}{Environment.NewLine}";
                            formattedOutput += $"Link: {itemLink}{Environment.NewLine}";
                            formattedOutput += $"Category: {category}{Environment.NewLine}";
                            formattedOutput += $"PubDate: {pubDate}{Environment.NewLine}";
                            formattedOutput += "--------------------------------" + Environment.NewLine;

                            formattedTitles += $"Title: {title}{Environment.NewLine}";
                            formattedTitles += $"Description: {description}{Environment.NewLine}";
                            formattedTitles += "--------------------------------" + Environment.NewLine;

                            string combinedText = title + " " + description;
                            CountNGrams(combinedText, 1, unigramCounts);
                            CountNGrams(combinedText, 2, bigramCounts);
                            CountNGrams(combinedText, 3, trigramCounts);
                        }

                        File.AppendAllText(outputFile, formattedOutput);
                        File.AppendAllText(outputTitlesFile, formattedTitles);

                        _log($"Inhalt von {link} wurde gespeichert.");
                    }
                    catch (Exception ex)
                    {
                        _log($"Fehler beim Abrufen von {link}: {ex.Message}");
                    }
                }
            }

            string ngramOutput = "----- Wortfrequenz (Unigrams) -----" + Environment.NewLine;
            ngramOutput += FormatNGramDictionary(unigramCounts) + Environment.NewLine;
            ngramOutput += "----- Wortfrequenz (Bigrams) -----" + Environment.NewLine;
            ngramOutput += FormatNGramDictionary(bigramCounts) + Environment.NewLine;
            ngramOutput += "----- Wortfrequenz (Trigrams) -----" + Environment.NewLine;
            ngramOutput += FormatNGramDictionary(trigramCounts) + Environment.NewLine;

            File.WriteAllText(outputNgramsFile, ngramOutput);

            _log("Alle Inhalte wurden verarbeitet.");
        }

        private static string RemoveImageTags(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;
            return Regex.Replace(input, "<img\\s+[^>]*\\/?>", "", RegexOptions.IgnoreCase);
        }

        private static void CountNGrams(string text, int n, Dictionary<string, int> dict)
        {
            if (string.IsNullOrEmpty(text))
                return;

            string[] words = Regex.Split(text.ToLowerInvariant(), @"\W+")
                                  .Where(w => !string.IsNullOrWhiteSpace(w))
                                  .ToArray();
            for (int i = 0; i <= words.Length - n; i++)
            {
                string ngram = string.Join(" ", words.Skip(i).Take(n));
                if (dict.ContainsKey(ngram))
                    dict[ngram]++;
                else
                    dict[ngram] = 1;
            }
        }

        private static string FormatNGramDictionary(Dictionary<string, int> dict)
        {
            var sorted = dict.Where(kvp => kvp.Value > 1)
                             .OrderByDescending(kvp => kvp.Value)
                             .ThenBy(kvp => kvp.Key);
            string result = string.Empty;
            foreach (var kvp in sorted)
            {
                result += $"{kvp.Key}: {kvp.Value}{Environment.NewLine}";
            }
            return result;
        }
    }
}
