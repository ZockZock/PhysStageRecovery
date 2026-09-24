using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace BoosterWatch
{
    // The whole flight, every number, into one file.
    //
    // The `Landing check` and `Landing predict` lines are written once a second and are meant to be
    // read by a person; that is not enough to find a sign error that only shows for a few ticks, and
    // every one of the last four flights cost a launch to answer a question the log could not. This
    // records every tick - the raw readings from KSP alongside every value the law derived from them -
    // so a single flight answers all of them at once.
    //
    // Sampling: every tick from 0.1 s before ignition, every 0.1 s before that, and every tick in the
    // last kilometre. The file is written out as it goes, so a crash does not lose it.
    public sealed class FlightRecorder
    {
        private const double FastInterval = 0.1;
        private const double LowAltitude = 1000;

        private readonly List<string> rows = new List<string>();
        private readonly string path;
        private double lastSample = double.NegativeInfinity;
        private double lastFlush = double.NegativeInfinity;
        private bool headerWritten;
        private int dropped;

        public string Path { get { return path; } }
        public int Rows { get { return rows.Count; } }
        public bool Active { get { return path != null; } }

        public FlightRecorder(string vesselId, string tag)
        {
            try
            {
                string plugin = System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string dir = System.IO.Path.Combine(plugin, "..", "PluginData", "Flights");
                Directory.CreateDirectory(dir);
                string name = "flight-" + Short(vesselId) + "-" + tag + ".csv";
                path = System.IO.Path.GetFullPath(System.IO.Path.Combine(dir, name));
            }
            catch (Exception)
            {
                path = null;
            }
        }

        private static string Short(string id)
        {
            if (string.IsNullOrEmpty(id)) return "unbekannt";
            return id.Length <= 8 ? id : id.Substring(0, 8);
        }

        // One line per tick. `due` gates the cadence; the caller passes whether this tick is
        // interesting (ignition, low altitude) or not.
        public void Sample(double time, bool interesting, string line)
        {
            if (path == null) return;
            bool take = interesting || time - lastSample >= FastInterval || lastSample < -1e8;
            if (!take) return;
            lastSample = time;
            if (rows.Count < 200000) rows.Add(line);
            else dropped++;
            if (time - lastFlush >= 2 || interesting) Flush();
        }

        public void Flush()
        {
            if (path == null || rows.Count == 0) return;
            try
            {
                StringBuilder text = new StringBuilder();
                if (!headerWritten)
                {
                    text.Append(Header).Append('\n');
                    headerWritten = true;
                }
                foreach (string row in rows) text.Append(row).Append('\n');
                rows.Clear();
                File.AppendAllText(path, text.ToString());
                lastFlush = lastSample;
            }
            catch (Exception)
            {
                // A recorder must never be the reason a landing fails.
            }
        }

        public string Summary()
        {
            if (path == null) return "Flugschreiber: nicht verfuegbar";
            Flush();
            return "Flugschreiber: " + path + (dropped > 0 ? " (" + dropped + " Zeilen verworfen)" : "");
        }

        // The columns, in order. Kept as one constant so the header and the rows cannot drift apart.
        public const string Header =
            "t,dt,phase,coast,latch,abort,"
            + "altAsl,terrain,clearance,voraus,slope,cut,"
            + "vUp,vEast,vNorth,sink,lat,speed,mach,"
            + "rho,rhoTab,rhoScale,press,temp,cdA,cd,"
            + "drag,thrustVac,thrustMax,mass,fuelDv,"
            + "zielSink,aFrei,reqDv,predTd,reserve,predUnst,predIgn,predBurn,"
            + "aUpCmd,aOstCmd,aNordCmd,drossel,gateCut,"
            + "aimUp,aimOst,aimNord,lage,err,istAcc,"
            + "engines,engOn,flameout,engThr,engFlame,landed,zuendgrund,notes";

        // 51 values. Numbers are written with a fixed culture so a German Windows does not turn the
        // decimal point into a comma and break every parser, including mine.
        public static string Row(params object[] values)
        {
            StringBuilder line = new StringBuilder();
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0) line.Append(',');
                object v = values[i];
                if (v == null) continue;
                if (v is string) { line.Append((string)v); continue; }
                double d = Convert.ToDouble(v, CultureInfo.InvariantCulture);
                if (double.IsNaN(d) || double.IsInfinity(d)) continue;
                line.Append(d.ToString("0.######", CultureInfo.InvariantCulture));
            }
            return line.ToString();
        }
    }
}
