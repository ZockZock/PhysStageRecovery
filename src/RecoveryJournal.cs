using System;
using System.Collections.Generic;
using System.Globalization;

namespace BoosterWatch
{
    public sealed class JournalEntry
    {
        public Guid Id;
        public string Name, Status;
        public double Time, Funds;
        public int StageCursor = -1;
        public uint AnchorPart;
        public readonly HashSet<uint> FailedParts = new HashSet<uint>();

        public static JournalEntry Read(ConfigNode n)
        {
            Guid id;
            if (!Guid.TryParse(n.GetValue("id"), out id)) return null;
            double time, funds; int cursor; uint anchor;
            double.TryParse(n.GetValue("time"), NumberStyles.Float, CultureInfo.InvariantCulture, out time);
            double.TryParse(n.GetValue("funds"), NumberStyles.Float, CultureInfo.InvariantCulture, out funds);
            JournalEntry entry = new JournalEntry { Id = id, Name = n.GetValue("name") ?? "Booster",
                Status = n.GetValue("status") ?? "Tracking", Time = time, Funds = funds };
            if (int.TryParse(n.GetValue("stageCursor"), out cursor) && cursor >= 0) entry.StageCursor = cursor;
            if (uint.TryParse(n.GetValue("anchorPart"), out anchor)) entry.AnchorPart = anchor;
            foreach (string part in n.GetValues("failedPart"))
                if (uint.TryParse(part, out anchor)) entry.FailedParts.Add(anchor);
            return entry;
        }
        public void Write(ConfigNode n)
        {
            n.AddValue("id", Id.ToString()); n.AddValue("name", Name); n.AddValue("status", Status);
            n.AddValue("time", Time.ToString("R", CultureInfo.InvariantCulture));
            n.AddValue("funds", Funds.ToString("R", CultureInfo.InvariantCulture));
            n.AddValue("stageCursor", StageCursor); n.AddValue("anchorPart", AnchorPart);
            foreach (uint part in FailedParts) n.AddValue("failedPart", part);
        }
    }

    [KSPScenario(ScenarioCreationOptions.AddToAllGames, new[] { GameScenes.FLIGHT, GameScenes.SPACECENTER, GameScenes.TRACKSTATION })]
    public sealed class RecoveryJournal : ScenarioModule
    {
        public static RecoveryJournal Instance;
        public readonly Dictionary<Guid, JournalEntry> Entries = new Dictionary<Guid, JournalEntry>();
        public bool Enabled = true, AutoRecovery = true;

        public override void OnAwake() { base.OnAwake(); Instance = this; }
        public void OnDestroy() { if (Instance == this) Instance = null; }

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            Entries.Clear();
            bool value;
            if (bool.TryParse(node.GetValue("enabled"), out value)) Enabled = value;
            if (bool.TryParse(node.GetValue("autoRecovery"), out value)) AutoRecovery = value;
            foreach (ConfigNode n in node.GetNodes("BOOSTER"))
            {
                JournalEntry entry = JournalEntry.Read(n);
                if (entry != null) Entries[entry.Id] = entry;
            }
        }

        public override void OnSave(ConfigNode node)
        {
            base.OnSave(node);
            node.AddValue("enabled", Enabled);
            node.AddValue("autoRecovery", AutoRecovery);
            foreach (JournalEntry e in Entries.Values)
            {
                e.Write(node.AddNode("BOOSTER"));
            }
        }
    }
}
