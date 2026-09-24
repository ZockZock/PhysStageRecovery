using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace BoosterWatch
{
    // The English text lives twice on purpose - once in the DLL (LocalizationTable) and once in
    // packaging/Localization/en-us.cfg, because KSP looks a mod's text up in files, not in the DLL.
    // This test is what keeps the two honest: table against file, German against English and the
    // tags used in src against the table. A typo in a tag can therefore not reach a flight.
    internal static class LocalizationTests
    {
        private static int checks;
        private static string root;

        private static int Main(string[] args)
        {
            root = args.Length > 0 ? args[0] : ".";
            try
            {
                Dictionary<string, string> english = Read(Path.Combine(root, "packaging", "Localization", "en-us.cfg"), "en-us");
                Dictionary<string, string> german = Read(Path.Combine(root, "packaging", "Localization", "de-de.cfg"), "de-de");

                EnglishMatchesTable(english);
                EveryTableTagIsInTheFile(english);
                StructureIsIntact(english, german);
                PlaceholdersMatch(english, german);
                NoTagIsUsedWithoutText(english);
            }
            catch (Exception e)
            {
                Console.WriteLine("FAIL: " + e.Message);
                return 1;
            }
            Console.WriteLine(checks + " localization checks passed.");
            return 0;
        }

        private static void Check(bool value, string name)
        {
            if (!value) throw new Exception(name);
            checks++;
            Console.WriteLine("PASS: " + name);
        }

        private static void EnglishMatchesTable(Dictionary<string, string> english)
        {
            int same = 0, wrong = 0;
            string firstBad = null;
            foreach (KeyValuePair<string, string> pair in LocalizationTable.English)
            {
                string text;
                if (!english.TryGetValue(pair.Key, out text)) { wrong++; if (firstBad == null) firstBad = pair.Key + " fehlt"; continue; }
                if (text != pair.Value) { wrong++; if (firstBad == null) firstBad = pair.Key + ": '" + text + "' statt '" + pair.Value + "'"; }
                else same++;
            }
            Check(wrong == 0, "en-us.cfg carries exactly the table's English text (" + same + " tags"
                + (wrong == 0 ? ")" : ", " + wrong + " wrong, first: " + firstBad + ")"));
        }

        private static void EveryTableTagIsInTheFile(Dictionary<string, string> english)
        {
            List<string> extra = english.Keys.Where(tag => !LocalizationTable.English.ContainsKey(tag)).ToList();
            Check(extra.Count == 0, "en-us.cfg has no tag the table does not know (" + english.Count + " tags"
                + (extra.Count == 0 ? ")" : ", extra: " + string.Join(", ", extra.ToArray()) + ")"));
        }

        // Every tag must appear in both files, no tag twice, and no text may carry whitespace that the
        // config parser would silently cut off - a leading space is exactly how " - engine" would lose
        // its separator.
        private static void StructureIsIntact(Dictionary<string, string> english, Dictionary<string, string> german)
        {
            List<string> missingGerman = english.Keys.Where(tag => !german.ContainsKey(tag)).ToList();
            Check(missingGerman.Count == 0, "every English tag is translated into German (" + german.Count + " tags"
                + (missingGerman.Count == 0 ? ")" : ", missing: " + string.Join(", ", missingGerman.Take(5).ToArray()) + ")"));

            List<string> extraGerman = german.Keys.Where(tag => !english.ContainsKey(tag)).ToList();
            Check(extraGerman.Count == 0, "de-de.cfg has no tag that en-us.cfg does not define"
                + (extraGerman.Count == 0 ? "" : " (extra: " + string.Join(", ", extraGerman.ToArray()) + ")"));

            List<string> untrimmed = english.Concat(german)
                .Where(pair => pair.Value != pair.Value.Trim()).Select(pair => pair.Key).ToList();
            Check(untrimmed.Count == 0, "no text begins or ends with whitespace"
                + (untrimmed.Count == 0 ? "" : " (" + string.Join(", ", untrimmed.ToArray()) + ")"));

            Check(english.Values.All(text => text.IndexOf('=') < 0),
                "no English text contains '=', which the config parser would split on");
            Check(german.Values.All(text => text.IndexOf('=') < 0),
                "no German text contains '=', which the config parser would split on");
        }

        // The placeholders are the numbers a status line fills in. A translation that drops one would
        // show the wrong value or none at all.
        private static void PlaceholdersMatch(Dictionary<string, string> english, Dictionary<string, string> german)
        {
            List<string> broken = new List<string>();
            foreach (KeyValuePair<string, string> pair in english)
            {
                List<string> left = Placeholders(pair.Value);
                List<string> right = Placeholders(german[pair.Key]);
                if (!left.SequenceEqual(right)) broken.Add(pair.Key + " en(" + string.Join(",", left.ToArray())
                    + ") de(" + string.Join(",", right.ToArray()) + ")");
                else if (left.Count > 0 && left[0] != "1") broken.Add(pair.Key + " starts at " + left[0]);
                else if (left.Count > 0 && left[left.Count - 1] != left.Count.ToString()) broken.Add(pair.Key + " is not numbered from 1 upwards");
            }
            Check(broken.Count == 0, "German and English use the same <<1>>, <<2>> placeholders"
                + (broken.Count == 0 ? "" : " (" + string.Join("; ", broken.ToArray()) + ")"));
        }

        private static List<string> Placeholders(string text)
        {
            List<string> found = new List<string>();
            foreach (Match match in Regex.Matches(text, @"<<(\d+)>>")) found.Add(match.Groups[1].Value);
            found.Sort();
            return found;
        }

        // Every #PSR_ tag written in the sources must have a text, or the window would show the raw
        // tag. This is what catches a tag that is only misspelled in one call site.
        private static void NoTagIsUsedWithoutText(Dictionary<string, string> english)
        {
            SortedSet<string> used = new SortedSet<string>();
            int files = 0;
            foreach (string file in Directory.GetFiles(Path.Combine(root, "src"), "*.cs"))
            {
                files++;
                foreach (Match match in Regex.Matches(File.ReadAllText(file), "\"#PSR_[A-Za-z0-9_]+\""))
                    used.Add(match.Value.Trim('"'));
            }
            List<string> unknown = used.Where(tag => !english.ContainsKey(tag)
                && !LocalizationTable.English.ContainsKey(tag)).ToList();
            Check(unknown.Count == 0, "every tag used in " + files + " source files has a text (" + used.Count + " tags"
                + (unknown.Count == 0 ? ")" : ", unknown: " + string.Join(", ", unknown.ToArray()) + ")"));

            List<string> unused = LocalizationTable.English.Keys.Where(tag => !used.Contains(tag)
                && tag.IndexOf("#PSR_Journal_", StringComparison.Ordinal) != 0).ToList();
            Check(unused.Count == 0, "no text is left over that nothing shows"
                + (unused.Count == 0 ? "" : " (" + string.Join(", ", unused.ToArray()) + ")"));
        }

        // Reads one Localization file: 'Localization { <id> { #tag = text } }', which is the shape KSP's
        // AddTagValuesForLanguage walks.
        private static Dictionary<string, string> Read(string path, string expectedLanguage)
        {
            Dictionary<string, string> tags = new Dictionary<string, string>();
            string language = null;
            int depth = 0;
            int lineNumber = 0;
            foreach (string raw in File.ReadAllLines(path))
            {
                lineNumber++;
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal)) continue;
                if (line == "{") { depth++; continue; }
                if (line == "}") { depth--; continue; }
                if (line.IndexOf('{') >= 0 || line.IndexOf('}') >= 0)
                    throw new Exception(path + ":" + lineNumber + ": braces must stand on their own line");
                if (depth == 1)
                {
                    if (language != null) throw new Exception(path + ": more than one language block (" + language + ", " + line + ")");
                    language = line;
                    continue;
                }
                if (depth == 2)
                {
                    int split = line.IndexOf('=');
                    if (!line.StartsWith("#", StringComparison.Ordinal) || split < 0)
                        throw new Exception(path + ":" + lineNumber + ": expected '#tag = text', found '" + line + "'");
                    string tag = line.Substring(0, split).Trim();
                    string text = line.Substring(split + 1).Trim();
                    if (tags.ContainsKey(tag)) throw new Exception(path + ": tag " + tag + " appears twice");
                    if (text.Length == 0) throw new Exception(path + ": tag " + tag + " has no text");
                    tags[tag] = text;
                }
            }
            if (language != expectedLanguage)
                throw new Exception(path + ": language block is '" + language + "', expected '" + expectedLanguage + "'");
            if (depth != 0) throw new Exception(path + ": unbalanced braces");
            if (tags.Count == 0) throw new Exception(path + ": no tags");
            Check(true, Path.GetFileName(path) + " reads as one '" + language + "' block with " + tags.Count + " tags");
            return tags;
        }
    }
}
