using System;
using System.Collections.Generic;
using KSP.Localization;
using UnityEngine;

namespace BoosterWatch
{
    // Every text the player can see goes through here, and KSP's own language setting decides which
    // one comes out.
    //
    // Measured in KSP 1.12.5, not assumed:
    //  * The language comes from <KSP>\buildID64.txt, line `language = <id>`; Localizer reads it in
    //    GetLanguageIdFromFile and exposes it as Localizer.CurrentLanguage.
    //  * A mod's file is any .cfg in GameData with a node `Localization { <id> { #tag = text } }`.
    //    Localizer.AddTagValuesForLanguage collects exactly those (GameDatabase.GetConfigNodes).
    //  * RefreshTagValues loads en-us from every file first and then the current language on top, so
    //    an untranslated tag falls back to the English one - which is why this mod ships en-us.cfg
    //    as well as de-de.cfg.
    //  * Localizer.Format has no null guard: it calls Instance._Format, and Instance is only set once
    //    GameDatabase has run Localizer.Init. Formatting before that throws. Every call here is
    //    therefore guarded and falls back to the English text compiled into the DLL, which keeps the
    //    window readable even if the Localization folder is missing.
    public static class Loc
    {
        private static readonly HashSet<string> warned = new HashSet<string>();

        public static string Get(string tag)
        {
            return Get(tag, (object[])null);
        }

        // args fill the <<1>>, <<2>> placeholders of the text, in KSP's own syntax.
        public static string Get(string tag, params object[] args)
        {
            if (string.IsNullOrEmpty(tag)) return "";
            string text;
            if (FromGame(tag, out text))
            {
                if (args == null || args.Length == 0) return text;
                try { return Localizer.Format(tag, args); }
                catch (Exception) { return LocalizationTable.Substitute(text, args); }
            }
            return LocalizationTable.Substitute(Fallback(tag), args);
        }

        // The journal stores its status words in English so a save keeps its meaning; only the display
        // is translated.
        public static string Journal(string status)
        {
            string tag = LocalizationTable.JournalTag(status);
            return tag == null ? status : Get(tag);
        }

        public static string Language
        {
            get
            {
                try { return Localizer.Instance == null ? "" : Localizer.CurrentLanguage; }
                catch (Exception) { return ""; }
            }
        }

        // One line for the log: how many of the mod's texts the running game actually delivered, and
        // how many came from the English fallback. A missing or half-installed language file is
        // therefore visible at a glance instead of showing up as a raw #PSR_ tag in the window.
        public static string Audit()
        {
            int translated = 0, english = 0, missing = 0;
            foreach (KeyValuePair<string, string> pair in LocalizationTable.English)
            {
                string text;
                if (!FromGame(pair.Key, out text)) missing++;
                else if (text == pair.Value) english++;
                else translated++;
            }
            string language = Language;
            return "Sprache '" + (language.Length > 0 ? language : "unbekannt") + "': "
                + LocalizationTable.English.Count + " Texte, davon " + translated + " uebersetzt und "
                + english + " wie im englischen Ersatz, " + missing + " fehlend.";
        }

        private static bool FromGame(string tag, out string text)
        {
            text = null;
            try
            {
                if (Localizer.Instance == null) return false;
                return Localizer.TryGetStringByTag(tag, out text) && !string.IsNullOrEmpty(text);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string Fallback(string tag)
        {
            string text;
            if (LocalizationTable.English.TryGetValue(tag, out text)) return text;
            Warn(tag);
            return tag;
        }

        private static void Warn(string tag)
        {
            if (!warned.Add(tag)) return;
            // Unity's logger is not available in every harness the sources are compiled into, so a
            // missing text must never be the reason something fails.
            try { Debug.LogWarning("[PhysStageRecovery] Text fehlt: " + tag); }
            catch (Exception) { }
        }
    }

    // One line per start with the language and how many texts it delivered.
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public sealed class LocalizationCheck : MonoBehaviour
    {
        private void Start()
        {
            Debug.Log("[PhysStageRecovery] " + Loc.Audit());
        }
    }
}
