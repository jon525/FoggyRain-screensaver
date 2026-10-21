// Foggy Rain screen saver for Windows
// Freezes whatever is on screen, slowly fogs it like a cold window pane,
// and lets raindrops bead up and run down the glass, wiping trails through the fog.
//
// Build:     run build.bat (uses the C# compiler that ships with Windows / .NET Framework 4)
// Install:   right-click FoggyRain.scr -> Install
// Settings:  right-click FoggyRain.scr -> Configure  (or "Settings..." in Windows' screen saver panel)
//            Settings are stored in the registry under HKEY_CURRENT_USER\Software\FoggyRain

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: System.Reflection.AssemblyTitle("Foggy Rain")]
[assembly: System.Reflection.AssemblyProduct("Foggy Rain")]

namespace FoggyRain
{
    // =====================================================================================
    //  Program entry
    // =====================================================================================
    static class Program
    {
        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")]
        static extern bool SetProcessDpiAwarenessContext(IntPtr value);

        public static string LogPath;

        // Writes a line to FoggyRain.log next to the .scr (or in %TEMP% if that folder isn't writable).
        public static void Log(string msg)
        {
            if (LogPath == null) return;
            try
            {
                File.AppendAllText(LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + msg + Environment.NewLine);
            }
            catch { }
        }

        static void SetUpLog()
        {
            string dir = null;
            try { dir = Path.GetDirectoryName(Application.ExecutablePath); } catch { }
            string[] candidates = { dir, Path.GetTempPath() };
            foreach (string d in candidates)
            {
                if (string.IsNullOrEmpty(d)) continue;
                try
                {
                    string p = Path.Combine(d, "FoggyRain.log");
                    if (File.Exists(p) && new FileInfo(p).Length > 1000000) File.Delete(p);
                    File.AppendAllText(p, Environment.NewLine);
                    LogPath = p;
                    return;
                }
                catch { }
            }
        }

        [STAThread]
        static void Main(string[] args)
        {
            Settings.Load();

            string mode = args.Length > 0 ? args[0].Trim().ToLowerInvariant() : "/s";
            if (mode.Length > 2) mode = mode.Substring(0, 2);

            if (mode == "/c")
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new SettingsForm());
                return;
            }
            if (mode == "/p") return; // no live preview in the little monitor picture

            if (Settings.On("log")) SetUpLog();
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object s, System.Threading.ThreadExceptionEventArgs e)
            {
                Log("CRASH (UI thread): " + e.Exception);
                Environment.Exit(1);
            };
            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
            {
                Log("CRASH (background): " + e.ExceptionObject);
            };

            // Keep the rest of the PC responsive while we run.
            try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }

            // Per-monitor DPI awareness so each screen's bounds are its real pixels.
            bool dpiSet = false;
            try { dpiSet = SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch { }
            if (!dpiSet) try { SetProcessDPIAware(); } catch { }

            Log("Start  args='" + string.Join(" ", args) + "'  OS=" + Environment.OSVersion +
                "  64-bit=" + Environment.Is64BitProcess + "  cores=" + Environment.ProcessorCount);
            foreach (Screen sc in Screen.AllScreens)
                Log("  screen " + sc.DeviceName + "  " + sc.Bounds + (sc.Primary ? "  (primary)" : ""));

            try
            {
                // One window per monitor, each with its own slice of the screenshot.
                // (A single giant window spanning monitors was too heavy for the graphics driver.)
                bool primaryOnly = (int)Settings.Get("monitors") == 1;
                List<Form> forms = new List<Form>();
                int seed = Environment.TickCount;
                foreach (Screen sc in Screen.AllScreens)
                {
                    Rectangle b = sc.Bounds;
                    if (primaryOnly && !sc.Primary)
                    {
                        forms.Add(new BlankForm(b));
                        continue;
                    }
                    forms.Add(new SaverForm(b, Background.For(b), seed++));
                }

                ApplicationContext ctx = new ApplicationContext();
                bool closing = false;
                foreach (Form f in forms)
                {
                    f.FormClosed += delegate
                    {
                        if (closing) return;
                        closing = true;
                        foreach (Form o in forms) if (!o.IsDisposed) o.Close();
                        ctx.ExitThread();
                    };
                    f.Show();
                }
                Application.Run(ctx);
                Log("Exit normally");
            }
            catch (Exception ex)
            {
                Log("CRASH (startup): " + ex);
            }
        }
    }

    // =====================================================================================
    //  Settings: definitions, presets, registry storage
    // =====================================================================================
    enum Kind { Slider, Check, Color, Choice, File }

    class Def
    {
        public string Key, Tab, Label, Hint, Unit, ZeroText;
        public Kind Kind;
        public float Min, Max, Default;
        public int Decimals;
        public string[] Choices;
        public string DefaultText = "";
    }

    static class Settings
    {
        public static List<Def> Defs = new List<Def>();
        static Dictionary<string, Def> byKey = new Dictionary<string, Def>();
        static Dictionary<string, float> values = new Dictionary<string, float>();
        static Dictionary<string, string> texts = new Dictionary<string, string>();
        public static List<string> PresetNames = new List<string>();
        static Dictionary<string, object[]> presets = new Dictionary<string, object[]>();
        const string RegPath = @"Software\FoggyRain";

        static Def Add(Kind kind, string tab, string key, string label, float min, float max, float def, int dec, string unit, string hint)
        {
            Def d = new Def();
            d.Kind = kind; d.Tab = tab; d.Key = key; d.Label = label;
            d.Min = min; d.Max = max; d.Default = def; d.Decimals = dec;
            d.Unit = unit ?? ""; d.Hint = hint;
            Defs.Add(d);
            byKey[key] = d;
            return d;
        }
        static Def Slider(string tab, string key, string label, float min, float max, float def, int dec, string unit, string hint)
        {
            return Add(Kind.Slider, tab, key, label, min, max, def, dec, unit, hint);
        }
        static Def Check(string tab, string key, string label, bool def, string hint)
        {
            return Add(Kind.Check, tab, key, label, 0, 1, def ? 1 : 0, 0, "", hint);
        }
        static Def ColorSetting(string tab, string key, string label, int rgb, string hint)
        {
            return Add(Kind.Color, tab, key, label, 0, 16777215, rgb, 0, "", hint);
        }
        static Def Choice(string tab, string key, string label, int def, string[] choices, string hint)
        {
            Def d = Add(Kind.Choice, tab, key, label, 0, choices.Length - 1, def, 0, "", hint);
            d.Choices = choices;
            return d;
        }
        static Def FileSetting(string tab, string key, string label, string hint)
        {
            return Add(Kind.File, tab, key, label, 0, 0, 0, 0, "", hint);
        }
        static void Preset(string name, params object[] kv)
        {
            PresetNames.Add(name);
            presets[name] = kv;
        }

        static Settings()
        {
            int cores = Math.Max(1, Environment.ProcessorCount);

            // ---------- Fog ----------
            Slider("Fog", "fogTime", "Time to fog over", 3, 600, 45, 0, "sec",
                "How long the glass takes to fog up completely.");
            Slider("Fog", "fogMax", "Fog thickness", 0, 100, 93, 0, "%",
                "How opaque the fog gets. 100% hides the screen completely.");
            Slider("Fog", "fogUneven", "Unevenness", 0, 100, 50, 0, "%",
                "How patchy the fog is while it creeps in. 0% fogs evenly everywhere.");
            Slider("Fog", "fogBlur", "Blur", 2, 60, 28, 0, "",
                "How blurred the screen looks through the fog.");
            Slider("Fog", "fogTint", "Tint strength", 0, 100, 52, 0, "%",
                "How much the fog is coloured by the tint colour below.");
            ColorSetting("Fog", "fogTintColor", "Tint colour", 0xD6DDE4,
                "The colour of the condensation. Pale grey-blue looks like a cold window.");
            Slider("Fog", "fogGrain", "Grain", 0, 30, 4, 0, "",
                "Fine speckle in the fog, like real condensation.");
            Slider("Fog", "refog", "Trail re-fog time", 1, 300, 25, 0, "sec",
                "How long the clear streaks left by drops take to fog over again.");
            Slider("Fog", "darken", "Darken the screen", 0, 90, 0, 0, "%",
                "Dims the frozen screen behind the glass, for a dusk or night mood.");
            Slider("Fog", "loopMin", "Wipe the glass every", 0, 60, 0, 0, "min",
                "Clears the fog and starts over after this long.").ZeroText = "Never";

            // ---------- Rain ----------
            Slider("Rain", "rainDelay", "Rain starts after", 0, 120, 1.5f, 1, "sec",
                "Pause before the first drop hits the glass.");
            Slider("Rain", "rainStart", "Starting intensity", 0, 50, 1.2f, 1, "drops/sec",
                "How many drops land per second at the start (scaled for screen size).");
            Slider("Rain", "rainMax", "Full intensity", 0, 100, 5.2f, 1, "drops/sec",
                "How many drops land per second once the rain has built up.");
            Slider("Rain", "rainRamp", "Build-up time", 0, 300, 25, 0, "sec",
                "How long the rain takes to go from starting to full intensity.");
            Slider("Rain", "dropSize", "Raindrop size", 25, 300, 100, 0, "%",
                "Size of the drops that land on the glass.");
            Slider("Rain", "maxDrop", "Biggest drop size", 5, 40, 13, 0, "",
                "Drops merge as they run into each other, up to this size.");
            Slider("Rain", "stick", "Size before a drop runs", 1, 15, 4.2f, 1, "",
                "Smaller drops stay stuck to the glass. Lower values make more drops run.");
            Slider("Rain", "speed", "Running speed", 10, 500, 100, 0, "%",
                "How fast drops slide down the glass.");
            Slider("Rain", "stops", "Stop-and-go", 0, 300, 100, 0, "%",
                "How often running drops catch and pause before sliding on.");
            Slider("Rain", "wiggle", "Wiggle", 0, 300, 100, 0, "%",
                "How much drops meander and zig-zag on their way down. 0% runs dead straight.");
            Slider("Rain", "wiggleLen", "Distance between turns", 25, 400, 100, 0, "%",
                "Short means tight, nervous zig-zags. Long means lazy, sweeping curves.");
            Slider("Rain", "wiggleVar", "Wiggle variety", 0, 100, 60, 0, "%",
                "How different each drop's wiggliness is from the others.");
            Slider("Rain", "wobble", "Jitter", 0, 300, 100, 0, "%",
                "Fine, shaky sideways jitter on top of the wiggle.");
            Slider("Rain", "wind", "Wind", -100, 100, 0, 0, "%",
                "Blows running drops sideways at a slant.");
            Slider("Rain", "trailBeads", "Beads left behind", 0, 300, 100, 0, "%",
                "Running drops leave little droplets in their trail.");

            // ---------- Condensation ----------
            Slider("Condensation", "beadRate", "Bead formation rate", 0, 400, 100, 0, "%",
                "How quickly tiny condensation beads appear as the glass fogs.");
            Slider("Condensation", "beadMax", "Maximum beads", 0, 10000, 2200, 0, "",
                "Most beads on the glass at once (per 1080p screen area).");
            Slider("Condensation", "beadSize", "Bead size", 25, 300, 100, 0, "%",
                "Size of the condensation beads.");

            // ---------- Drops ----------
            Slider("Drops", "lens", "Lens magnification", 0, 10, 2.5f, 1, "",
                "Each drop shows an upside-down view of the screen like a tiny lens. Higher shows a wider view.");
            Slider("Drops", "glint", "Highlight brightness", 0, 255, 235, 0, "",
                "The bright reflection at the top of each drop.");
            Slider("Drops", "rim", "Edge darkness", 0, 100, 50, 0, "%",
                "The dark outline around each drop.");
            Slider("Drops", "glow", "Bottom glow", 0, 100, 25, 0, "%",
                "Light gathered at the bottom of each drop.");
            Slider("Drops", "stretch", "Running drop stretch", 100, 200, 115, 0, "%",
                "How elongated drops look while they slide.");

            // ---------- Lightning ----------
            Check("Lightning", "lightning", "Lightning flashes", false,
                "Occasional flashes light up the window, sometimes with a flicker.");
            Slider("Lightning", "lightEvery", "Average time between flashes", 3, 300, 40, 0, "sec",
                "Flashes come at random, about this far apart on average.");
            Slider("Lightning", "lightStrength", "Flash brightness", 5, 100, 60, 0, "%",
                "How bright the flashes are.");

            // ---------- Background ----------
            Choice("Background", "bgMode", "Show behind the glass", 0,
                new string[] {
                    "Frozen screen (wallpaper if it can't be captured)",
                    "Frozen screen (picture below if it can't be captured)",
                    "Always the desktop wallpaper",
                    "Always the picture below" },
                "When Windows starts the screen saver itself it can hide your desktop, so a stand-in is used.");
            FileSetting("Background", "bgPicture", "Picture",
                "Any JPG, PNG or BMP. It is scaled to fill each monitor.");

            // ---------- General ----------
            Slider("General", "fps", "Frame rate", 10, 60, 30, 0, "fps",
                "Smoother at higher values but uses more of your PC. 30 is plenty.");
            Slider("General", "quality", "Render resolution", 25, 100, 100, 0, "%",
                "Lower this if your PC struggles. The image is scaled up to fill the screen.");
            Slider("General", "threads", "CPU cores to use", 1, cores, Math.Max(1, cores - 1), 0, "",
                "How many processor cores to draw with. Leaving one free keeps the PC responsive.");
            Choice("General", "monitors", "Show on", 0,
                new string[] { "All monitors", "Main monitor only (others go black)" },
                "Which screens get the fog and rain.");
            Check("General", "exitOnMove", "Exit when the mouse moves", true,
                "Turn off to exit only by clicking or pressing a key.");
            Slider("General", "mouseSens", "Mouse movement needed to exit", 1, 200, 8, 0, "px",
                "How far the mouse must move before the screen saver closes.");
            Check("General", "hideCursor", "Hide the mouse pointer", true,
                "Hides the pointer while the screen saver runs.");
            Check("General", "log", "Write a diagnostic log", true,
                "Keeps FoggyRain.log next to the screen saver for troubleshooting.");

            // ---------- Presets (only change the look; General settings are kept) ----------
            Preset("Default");
            Preset("Light drizzle", "rainStart", 0.4, "rainMax", 1.5, "rainRamp", 40, "dropSize", 70, "fogTime", 90,
                   "beadRate", 70, "speed", 70);
            Preset("Heavy downpour", "rainStart", 4, "rainMax", 22, "rainRamp", 15, "dropSize", 125, "speed", 140,
                   "fogTime", 30, "stops", 60, "trailBeads", 140);
            Preset("Storm", "rainStart", 5, "rainMax", 28, "rainRamp", 12, "dropSize", 120, "speed", 160, "wind", 40,
                   "wobble", 170, "wiggle", 140, "lightning", 1, "lightEvery", 18, "lightStrength", 75, "darken", 40, "fogTime", 25,
                   "fogTintColor", 0xA9B3BE);
            Preset("Thick fog", "fogMax", 99, "fogTime", 20, "fogBlur", 50, "fogTint", 75, "fogUneven", 30,
                   "rainStart", 0.5, "rainMax", 2, "refog", 12, "beadRate", 200);
            Preset("Night window", "darken", 65, "fogTintColor", 0x3C4652, "fogTint", 45, "glint", 170, "fogMax", 85,
                   "rainMax", 4);
            Preset("Calm and slow", "speed", 55, "stops", 160, "wobble", 50, "wiggle", 70, "wiggleLen", 180, "rainStart", 0.8, "rainMax", 2.5,
                   "fogTime", 120, "refog", 60);
            Preset("Windy", "wind", -65, "wobble", 220, "wiggle", 150, "wiggleLen", 70, "speed", 150, "rainMax", 10, "stops", 50);
            Preset("Quick demo", "fogTime", 8, "rainDelay", 0, "rainStart", 6, "rainMax", 14, "rainRamp", 5,
                   "loopMin", 1, "lightning", 1, "lightEvery", 12);
        }

        public static Def Find(string key) { return byKey[key]; }

        public static float Get(string key)
        {
            float v;
            if (values.TryGetValue(key, out v)) return v;
            return byKey[key].Default;
        }

        public static bool On(string key) { return Get(key) >= 0.5f; }

        public static string GetText(string key)
        {
            string v;
            if (texts.TryGetValue(key, out v)) return v;
            return byKey[key].DefaultText;
        }

        public static void SetText(string key, string v) { texts[key] = v ?? ""; }
        public static int Rgb(string key) { return ((int)Get(key)) & 0xFFFFFF; }

        public static void Set(string key, float v)
        {
            Def d = byKey[key];
            if (float.IsNaN(v)) v = d.Default;
            if (v < d.Min) v = d.Min;
            if (v > d.Max) v = d.Max;
            values[key] = v;
        }

        public static string Format(Def d, float v)
        {
            if (d.ZeroText != null && v == 0) return d.ZeroText;
            if (d.Key == "wind")
            {
                if (v == 0) return "None";
                return (v < 0 ? "To the left " : "To the right ") + Math.Abs(v).ToString("0") + "%";
            }
            string s = v.ToString("F" + d.Decimals, CultureInfo.CurrentCulture);
            return d.Unit.Length > 0 ? s + " " + d.Unit : s;
        }

        public static void ResetDefaults()
        {
            values.Clear();
            texts.Clear();
        }

        public static void ApplyPreset(string name)
        {
            object[] kv;
            if (!presets.TryGetValue(name, out kv)) return;
            foreach (Def d in Defs)
                if (d.Tab != "General" && d.Tab != "Background" && d.Kind != Kind.File) values[d.Key] = d.Default;
            for (int i = 0; i + 1 < kv.Length; i += 2)
                Set((string)kv[i], Convert.ToSingle(kv[i + 1], CultureInfo.InvariantCulture));
        }

        public static void Load()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RegPath))
                {
                    if (k == null) return;
                    foreach (Def d in Defs)
                    {
                        object o = k.GetValue(d.Key);
                        if (o == null) continue;
                        if (d.Kind == Kind.File) { SetText(d.Key, o.ToString()); continue; }
                        float v;
                        if (float.TryParse(o.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                            Set(d.Key, v);
                    }
                }
            }
            catch { }
        }

        public static void Save()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RegPath))
                {
                    foreach (Def d in Defs)
                    {
                        if (d.Kind == Kind.File) k.SetValue(d.Key, GetText(d.Key));
                        else k.SetValue(d.Key, Get(d.Key).ToString("R", CultureInfo.InvariantCulture));
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not save settings:\n" + ex.Message, "Foggy Rain", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }

    // =====================================================================================
    //  Settings window
    // =====================================================================================
    class SettingsForm : Form
    {
        List<Action> refreshers = new List<Action>();
        ComboBox presetBox;

        public SettingsForm()
        {
            Text = "Foggy Rain settings";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9f);
            ClientSize = new Size(660, 650);

            Label pl = new Label();
            pl.Text = "Preset:";
            pl.Location = new Point(14, 17);
            pl.AutoSize = true;
            Controls.Add(pl);

            presetBox = new ComboBox();
            presetBox.DropDownStyle = ComboBoxStyle.DropDownList;
            presetBox.Location = new Point(70, 13);
            presetBox.Width = 220;
            foreach (string n in Settings.PresetNames) presetBox.Items.Add(n);
            presetBox.SelectedIndex = 0;
            Controls.Add(presetBox);

            Button apply = MakeButton("Apply preset", 300, 12, 120);
            apply.Click += delegate
            {
                Settings.ApplyPreset((string)presetBox.SelectedItem);
                RefreshAll();
            };

            Label note = new Label();
            note.Text = "Presets change the look only.";
            note.ForeColor = Color.DimGray;
            note.Location = new Point(430, 17);
            note.AutoSize = true;
            Controls.Add(note);

            TabControl tabs = new TabControl();
            tabs.Location = new Point(12, 48);
            tabs.Size = new Size(636, 548);
            Controls.Add(tabs);

            Dictionary<string, TabPage> pages = new Dictionary<string, TabPage>();
            Dictionary<string, int> ys = new Dictionary<string, int>();
            foreach (Def d in Settings.Defs)
            {
                TabPage p;
                if (!pages.TryGetValue(d.Tab, out p))
                {
                    p = new TabPage(d.Tab);
                    p.AutoScroll = true;
                    p.UseVisualStyleBackColor = true;
                    tabs.TabPages.Add(p);
                    pages[d.Tab] = p;
                    ys[d.Tab] = 10;
                }
                ys[d.Tab] = AddRow(p, d, ys[d.Tab]);
            }

            Button reset = MakeButton("Reset all", 12, 610, 100);
            reset.Click += delegate
            {
                Settings.ResetDefaults();
                RefreshAll();
            };

            Button test = MakeButton("Test", 318, 610, 100);
            test.Click += delegate { RunTest(); };

            Button ok = MakeButton("OK", 428, 610, 100);
            ok.Click += delegate { Settings.Save(); Close(); };

            Button cancel = MakeButton("Cancel", 538, 610, 110);
            cancel.Click += delegate { Close(); };

            AcceptButton = ok;
            CancelButton = cancel;

            RefreshAll();
        }

        Button MakeButton(string text, int x, int y, int w)
        {
            Button b = new Button();
            b.Text = text;
            b.Location = new Point(x, y);
            b.Size = new Size(w, 28);
            Controls.Add(b);
            return b;
        }

        void RefreshAll()
        {
            foreach (Action a in refreshers) a();
        }

        void RunTest()
        {
            Settings.Save();
            Hide();
            Application.DoEvents();
            System.Threading.Thread.Sleep(350); // let the window vanish before the screen is captured
            try
            {
                Process p = Process.Start(Application.ExecutablePath, "/s");
                if (p != null)
                {
                    p.EnableRaisingEvents = true;
                    p.Exited += delegate
                    {
                        try { BeginInvoke(new MethodInvoker(delegate { Show(); Activate(); })); } catch { }
                    };
                    return;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not start the test:\n" + ex.Message, "Foggy Rain");
            }
            Show();
        }

        int AddRow(TabPage p, Def d, int y)
        {
            Def def = d;
            bool updating = false;

            if (d.Kind == Kind.Check)
            {
                CheckBox cb = new CheckBox();
                cb.Text = d.Label;
                cb.Location = new Point(12, y + 4);
                cb.AutoSize = true;
                p.Controls.Add(cb);
                refreshers.Add(delegate
                {
                    updating = true;
                    cb.Checked = Settings.On(def.Key);
                    updating = false;
                });
                cb.CheckedChanged += delegate
                {
                    if (!updating) Settings.Set(def.Key, cb.Checked ? 1 : 0);
                };
            }
            else
            {
                Label name = new Label();
                name.Text = d.Label;
                name.Location = new Point(12, y + 6);
                name.Size = new Size(205, 20);
                p.Controls.Add(name);

                if (d.Kind == Kind.Slider)
                {
                    float mult = (float)Math.Pow(10, d.Decimals);
                    int steps = Math.Max(1, (int)Math.Round((d.Max - d.Min) * mult));
                    TrackBar tb = new TrackBar();
                    tb.AutoSize = false;
                    tb.TickStyle = TickStyle.None;
                    tb.Location = new Point(220, y);
                    tb.Size = new Size(270, 30);
                    tb.Minimum = 0;
                    tb.Maximum = steps;
                    tb.SmallChange = 1;
                    tb.LargeChange = Math.Max(1, steps / 10);
                    p.Controls.Add(tb);

                    Label val = new Label();
                    val.Location = new Point(495, y + 6);
                    val.Size = new Size(110, 20);
                    p.Controls.Add(val);

                    refreshers.Add(delegate
                    {
                        float v = Settings.Get(def.Key);
                        updating = true;
                        int pos = (int)Math.Round((v - def.Min) * mult);
                        tb.Value = Math.Max(0, Math.Min(steps, pos));
                        updating = false;
                        val.Text = Settings.Format(def, v);
                    });
                    tb.ValueChanged += delegate
                    {
                        if (updating) return;
                        float v = def.Min + tb.Value / mult;
                        Settings.Set(def.Key, v);
                        val.Text = Settings.Format(def, Settings.Get(def.Key));
                    };
                }
                else if (d.Kind == Kind.Color)
                {
                    Button btn = new Button();
                    btn.Location = new Point(220, y + 1);
                    btn.Size = new Size(120, 26);
                    btn.FlatStyle = FlatStyle.Flat;
                    p.Controls.Add(btn);
                    refreshers.Add(delegate
                    {
                        int c = Settings.Rgb(def.Key);
                        btn.BackColor = Color.FromArgb((c >> 16) & 255, (c >> 8) & 255, c & 255);
                    });
                    btn.Click += delegate
                    {
                        using (ColorDialog cd = new ColorDialog())
                        {
                            int c = Settings.Rgb(def.Key);
                            cd.Color = Color.FromArgb((c >> 16) & 255, (c >> 8) & 255, c & 255);
                            cd.FullOpen = true;
                            if (cd.ShowDialog(this) == DialogResult.OK)
                            {
                                Settings.Set(def.Key, (cd.Color.R << 16) | (cd.Color.G << 8) | cd.Color.B);
                                btn.BackColor = Color.FromArgb(cd.Color.R, cd.Color.G, cd.Color.B);
                            }
                        }
                    };
                }
                else if (d.Kind == Kind.File)
                {
                    TextBox tx = new TextBox();
                    tx.Location = new Point(220, y + 3);
                    tx.Width = 260;
                    tx.ReadOnly = true;
                    p.Controls.Add(tx);
                    Button browse = new Button();
                    browse.Text = "Browse...";
                    browse.Location = new Point(486, y + 1);
                    browse.Size = new Size(70, 26);
                    p.Controls.Add(browse);
                    Button clr = new Button();
                    clr.Text = "Clear";
                    clr.Location = new Point(560, y + 1);
                    clr.Size = new Size(48, 26);
                    p.Controls.Add(clr);
                    refreshers.Add(delegate { tx.Text = Settings.GetText(def.Key); });
                    browse.Click += delegate
                    {
                        using (OpenFileDialog ofd = new OpenFileDialog())
                        {
                            ofd.Title = "Choose a picture";
                            ofd.Filter = "Pictures|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff|All files|*.*";
                            string cur = Settings.GetText(def.Key);
                            try { if (cur.Length > 0) ofd.InitialDirectory = Path.GetDirectoryName(cur); } catch { }
                            if (ofd.ShowDialog(this) == DialogResult.OK)
                            {
                                Settings.SetText(def.Key, ofd.FileName);
                                tx.Text = ofd.FileName;
                            }
                        }
                    };
                    clr.Click += delegate { Settings.SetText(def.Key, ""); tx.Text = ""; };
                }
                else if (d.Kind == Kind.Choice)
                {
                    ComboBox box = new ComboBox();
                    box.DropDownStyle = ComboBoxStyle.DropDownList;
                    box.Location = new Point(220, y + 2);
                    box.Width = 380;
                    foreach (string c in d.Choices) box.Items.Add(c);
                    p.Controls.Add(box);
                    refreshers.Add(delegate
                    {
                        updating = true;
                        box.SelectedIndex = Math.Max(0, Math.Min(def.Choices.Length - 1, (int)Settings.Get(def.Key)));
                        updating = false;
                    });
                    box.SelectedIndexChanged += delegate
                    {
                        if (!updating) Settings.Set(def.Key, box.SelectedIndex);
                    };
                }
            }

            Label hint = new Label();
            hint.Text = d.Hint;
            hint.ForeColor = Color.DimGray;
            hint.Font = new Font("Segoe UI", 8f);
            hint.Location = new Point(14, y + 31);
            hint.Size = new Size(590, 17);
            p.Controls.Add(hint);

            return y + 58;
        }
    }

    // =====================================================================================
    //  Background: the frozen screen, or a stand-in picture when Windows hides the desktop
    // =====================================================================================
    static class Background
    {
        // Returns a bitmap exactly the size of this monitor.
        public static Bitmap For(Rectangle b)
        {
            int mode = (int)Settings.Get("bgMode");
            string picture = Settings.GetText("bgPicture");

            if (mode == 2) return Stand(b, WallpaperPath(), "wallpaper");
            if (mode == 3) return Stand(b, picture, "picture");

            Bitmap shot = new Bitmap(b.Width, b.Height, PixelFormat.Format32bppRgb);
            try
            {
                using (Graphics g = Graphics.FromImage(shot))
                    g.CopyFromScreen(b.Left, b.Top, 0, 0, b.Size, CopyPixelOperation.SourceCopy);
            }
            catch (Exception ex)
            {
                Program.Log("  screen capture failed: " + ex.Message);
            }
            if (!LooksBlack(shot))
            {
                Program.Log("  using the frozen screen for " + b);
                return shot;
            }

            // Windows ran us on its separate screen-saver desktop, where the real desktop can't be seen.
            Program.Log("  screen capture came back black for " + b + " - using a stand-in");
            string first = mode == 1 ? picture : WallpaperPath();
            string second = mode == 1 ? WallpaperPath() : picture;
            Bitmap alt = Load(b, first);
            if (alt == null) alt = Load(b, second);
            if (alt == null) { Program.Log("  no stand-in picture could be loaded"); return shot; }
            shot.Dispose();
            return alt;
        }

        static Bitmap Stand(Rectangle b, string path, string what)
        {
            Bitmap bmp = Load(b, path);
            if (bmp != null) return bmp;
            Program.Log("  could not load the " + what + " (" + path + "), using black");
            return new Bitmap(b.Width, b.Height, PixelFormat.Format32bppRgb);
        }

        // True when almost every sampled pixel is black.
        static bool LooksBlack(Bitmap bmp)
        {
            int lit = 0, total = 0;
            int stepX = Math.Max(1, bmp.Width / 96), stepY = Math.Max(1, bmp.Height / 54);
            BitmapData d = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
            try
            {
                int[] row = new int[bmp.Width];
                for (int y = stepY / 2; y < bmp.Height; y += stepY)
                {
                    Marshal.Copy(d.Scan0 + y * d.Stride, row, 0, bmp.Width);
                    for (int x = stepX / 2; x < bmp.Width; x += stepX)
                    {
                        int c = row[x];
                        if (((c >> 16) & 255) + ((c >> 8) & 255) + (c & 255) > 30) lit++;
                        total++;
                    }
                }
            }
            finally { bmp.UnlockBits(d); }
            return lit < Math.Max(1, total / 200);
        }

        public static string WallpaperPath()
        {
            string p = null;
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop"))
                    if (k != null) p = k.GetValue("WallPaper") as string;
            }
            catch { }
            if (string.IsNullOrEmpty(p) || !File.Exists(p))
                p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                 @"Microsoft\Windows\Themes\TranscodedWallpaper");
            return p;
        }

        // Loads a picture scaled to cover the monitor (cropping the overflow), or null.
        static Bitmap Load(Rectangle b, string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            try
            {
                using (Image img = Image.FromFile(path))
                {
                    Bitmap dst = new Bitmap(b.Width, b.Height, PixelFormat.Format32bppRgb);
                    float scale = Math.Max((float)b.Width / img.Width, (float)b.Height / img.Height);
                    float w = img.Width * scale, h = img.Height * scale;
                    using (Graphics g = Graphics.FromImage(dst))
                    {
                        g.Clear(Color.Black);
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.DrawImage(img, (b.Width - w) / 2f, (b.Height - h) / 2f, w, h);
                    }
                    Program.Log("  using picture " + path + " for " + b);
                    return dst;
                }
            }
            catch (Exception ex)
            {
                Program.Log("  could not load " + path + ": " + ex.Message);
                return null;
            }
        }
    }

    // =====================================================================================
    //  Lightning: shared by every monitor so flashes stay in sync
    // =====================================================================================
    static class Lightning
    {
        static readonly Stopwatch clock = Stopwatch.StartNew();
        static readonly Random rnd = new Random();
        static double last, pending = -1;
        static float flash;

        public static float Level(bool on, float every, float strength)
        {
            double now = clock.Elapsed.TotalSeconds;
            float dt = (float)Math.Min(0.1, now - last);
            if (dt <= 0) return flash;
            last = now;
            if (on)
            {
                if (pending > 0)
                {
                    pending -= dt;
                    if (pending <= 0) flash = Math.Max(flash, strength * (0.5f + 0.5f * (float)rnd.NextDouble()));
                }
                else if (rnd.NextDouble() < dt / Math.Max(1f, every))
                {
                    flash = strength;
                    if (rnd.NextDouble() < 0.65) pending = 0.08 + rnd.NextDouble() * 0.25;
                }
            }
            flash *= (float)Math.Exp(-dt * 7.0);
            if (flash < 0.004f) flash = 0;
            return flash;
        }
    }

    // =====================================================================================
    //  Full-screen windows
    // =====================================================================================
    class SaverBase : Form
    {
        Point firstMouse;
        bool haveMouse;
        bool exitOnMove;
        float mouseSens;

        protected SaverBase(Rectangle b)
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = b;
            TopMost = true;
            ShowInTaskbar = false;
            KeyPreview = true;
            BackColor = Color.Black;
            exitOnMove = Settings.On("exitOnMove");
            mouseSens = Settings.Get("mouseSens");
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            if (Settings.On("hideCursor")) Cursor.Hide();
        }

        protected override void OnKeyDown(KeyEventArgs e) { Close(); }
        protected override void OnMouseDown(MouseEventArgs e) { Close(); }
        protected override void OnMouseWheel(MouseEventArgs e) { Close(); }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (!exitOnMove) return;
            if (!haveMouse) { firstMouse = e.Location; haveMouse = true; return; }
            if (Math.Abs(e.X - firstMouse.X) > mouseSens || Math.Abs(e.Y - firstMouse.Y) > mouseSens) Close();
        }
    }

    class BlankForm : SaverBase
    {
        public BlankForm(Rectangle b) : base(b) { }
    }

    class Drop
    {
        public float x, y, r, vx, vy, stall, trail;
        public float slope, slopeTarget, jink, wig;   // meandering path
        public bool dead;
    }

    unsafe class SaverForm : SaverBase
    {
        // ---------- GDI interop ----------
        [StructLayout(LayoutKind.Sequential)]
        struct BITMAPINFOHEADER
        {
            public int biSize, biWidth, biHeight;
            public short biPlanes, biBitCount;
            public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
        }
        [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER bmi, uint usage, out IntPtr bits, IntPtr hSection, uint offset);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, int rop);
        [DllImport("gdi32.dll")] static extern bool StretchBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, int sw, int sh, int rop);
        [DllImport("gdi32.dll")] static extern int SetStretchBltMode(IntPtr hdc, int mode);
        const int SRCCOPY = 0x00CC0020;

        const int CELL = 4;                       // fog mask resolution (pixels per cell)
        const double MAX_RENDER_PIXELS = 8400000; // per monitor; above this render scaled

        // ---------- Settings (read once at start) ----------
        float fogTime, fogMax, uneven, refog, loopSec;
        float rainDelay, rainStart, rainMax, rainRamp, dropSize, speed, stops, wobble, windSlant, trailBeads;
        float wiggle, wiggleLen, wiggleVar;
        float beadRate, beadMax, beadSize;
        float lensMag, glint, rimK, glow, stretch;
        bool lightning;
        float lightEvery, lightStrength;

        int W, H, SW, SH, MW, MH;
        float unit, RUN_R, MAX_R;
        int[] sharp, fog, A, mx0, wx;
        float[] clear, noise;

        IntPtr hdcMem, hDib, oldBmp, bits;

        List<Drop> beads = new List<Drop>();
        List<Drop> runners = new List<Drop>();
        List<Drop> pending = new List<Drop>();
        List<Drop>[] grid;
        int gridCell, gridW, gridH;

        Random rnd;
        string name;
        Stopwatch clock = new Stopwatch();
        double lastTime, lastReportWall;
        float elapsed, fogT, cycleStart, fogLevel, beadAcc, rainAcc;
        bool wiping;
        Timer timer;
        ParallelOptions popt = new ParallelOptions();

        // Health reporting
        int frames;
        double frameMsSum, frameMsMax, nextReport = 5.0;
        Stopwatch frameWatch = new Stopwatch();

        public SaverForm(Rectangle vs, Bitmap shot, int seed) : base(vs)
        {
            rnd = new Random(seed);
            name = "screen@" + vs.X + "," + vs.Y;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.Opaque, true);

            ReadSettings();

            SW = vs.Width; SH = vs.Height;
            double s = Math.Min(1.0, Math.Sqrt(MAX_RENDER_PIXELS / ((double)SW * SH))) * (Settings.Get("quality") / 100.0);
            W = Math.Max(16, (int)(SW * s));
            H = Math.Max(16, (int)(SH * s));
            unit = Math.Max(0.3f, H / 1080f);
            RUN_R = Settings.Get("stick") * unit;
            MAX_R = Math.Max(RUN_R * 1.2f, Settings.Get("maxDrop") * unit);
            popt.MaxDegreeOfParallelism = Math.Max(1, Math.Min(Environment.ProcessorCount, (int)Settings.Get("threads")));
            Program.Log("  " + name + " render size " + W + "x" + H + "  threads=" + popt.MaxDegreeOfParallelism);

            PrepareImages(shot);
            shot.Dispose();

            MW = W / CELL + 2;
            MH = H / CELL + 2;
            clear = new float[MW * MH];
            A = new int[MW * MH];
            noise = MakeNoise(MW, MH);
            mx0 = new int[W];
            wx = new int[W];
            for (int x = 0; x < W; x++)
            {
                float fx = (x + 0.5f) / CELL - 0.5f;
                if (fx < 0) fx = 0;
                int i = (int)fx;
                mx0[x] = i;
                wx[x] = (int)((fx - i) * 256f);
            }

            gridCell = Math.Max(8, (int)(24 * unit));
            gridW = W / gridCell + 1;
            gridH = H / gridCell + 1;
            grid = new List<Drop>[gridW * gridH];
            for (int i = 0; i < grid.Length; i++) grid[i] = new List<Drop>();

            BITMAPINFOHEADER bi = new BITMAPINFOHEADER();
            bi.biSize = Marshal.SizeOf(typeof(BITMAPINFOHEADER));
            bi.biWidth = W;
            bi.biHeight = -H; // top-down
            bi.biPlanes = 1;
            bi.biBitCount = 32;
            hdcMem = CreateCompatibleDC(IntPtr.Zero);
            hDib = CreateDIBSection(hdcMem, ref bi, 0, out bits, IntPtr.Zero, 0);
            if (hdcMem == IntPtr.Zero || hDib == IntPtr.Zero || bits == IntPtr.Zero)
                throw new Exception("Could not create the drawing surface (CreateDIBSection failed, error " + Marshal.GetLastWin32Error() + ")");
            oldBmp = SelectObject(hdcMem, hDib);
            Program.Log("  " + name + " ready");

            timer = new Timer();
            timer.Interval = Math.Max(1, (int)(1000f / Math.Max(1f, Settings.Get("fps"))));
            timer.Tick += delegate { Invalidate(); };
        }

        void ReadSettings()
        {
            fogTime = Math.Max(0.5f, Settings.Get("fogTime"));
            fogMax = Settings.Get("fogMax") / 100f;
            uneven = Settings.Get("fogUneven") / 100f;
            refog = Math.Max(0.5f, Settings.Get("refog"));
            loopSec = Settings.Get("loopMin") * 60f;

            rainDelay = Settings.Get("rainDelay");
            rainStart = Settings.Get("rainStart");
            rainMax = Settings.Get("rainMax");
            rainRamp = Settings.Get("rainRamp");
            dropSize = Settings.Get("dropSize") / 100f;
            speed = Settings.Get("speed") / 100f;
            stops = Settings.Get("stops") / 100f;
            wobble = Settings.Get("wobble") / 100f;
            wiggle = Settings.Get("wiggle") / 100f;
            wiggleLen = Settings.Get("wiggleLen") / 100f;
            wiggleVar = Settings.Get("wiggleVar") / 100f;
            windSlant = Settings.Get("wind") / 100f * 0.8f;
            trailBeads = Settings.Get("trailBeads") / 100f;

            beadRate = Settings.Get("beadRate") / 100f;
            beadMax = Settings.Get("beadMax");
            beadSize = Settings.Get("beadSize") / 100f;

            lensMag = Settings.Get("lens");
            glint = Settings.Get("glint");
            rimK = Settings.Get("rim") / 100f * 3.8f;
            glow = Settings.Get("glow") / 100f;
            stretch = Settings.Get("stretch") / 100f;

            lightning = Settings.On("lightning");
            lightEvery = Settings.Get("lightEvery");
            lightStrength = Settings.Get("lightStrength") / 100f;
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            Activate();
            clock.Start();
            timer.Start();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            timer.Stop();
            Program.Log("  " + name + " closed after " + elapsed.ToString("0.0") + "s");
            if (hdcMem != IntPtr.Zero)
            {
                SelectObject(hdcMem, oldBmp);
                DeleteObject(hDib);
                DeleteDC(hdcMem);
                hdcMem = IntPtr.Zero;
            }
            base.OnFormClosed(e);
        }

        // ---------- Image preparation ----------
        void PrepareImages(Bitmap shot)
        {
            Bitmap b = (W == SW && H == SH) ? shot : Resize(shot, W, H);
            sharp = ToArray(b);

            float blur = Math.Max(2f, Settings.Get("fogBlur"));
            float d1 = Math.Min(6f, blur);
            int w1 = Math.Max(2, (int)(W / d1)), h1 = Math.Max(2, (int)(H / d1));
            int w2 = Math.Max(2, (int)(W / blur)), h2 = Math.Max(2, (int)(H / blur));
            using (Bitmap m1 = Resize(b, w1, h1))
            using (Bitmap m2 = Resize(m1, w2, h2))
            using (Bitmap m3 = Resize(m2, w1, h1))
            using (Bitmap m4 = Resize(m3, W, H))
                fog = ToArray(m4);
            if (b != shot) b.Dispose();

            // Darken the frozen screen, tint the blurred copy and add faint grain.
            int keep = (int)(256f * (1f - Settings.Get("darken") / 100f));
            int tint = (int)Settings.Get("fogTint");
            int tc = Settings.Rgb("fogTintColor");
            int tr = (tc >> 16) & 255, tg = (tc >> 8) & 255, tbl = tc & 255;
            int grain = (int)Settings.Get("fogGrain");
            int[] f = fog, sh = sharp;
            int w = W;
            Parallel.For(0, H, popt, y =>
            {
                Random r = new Random(y * 7919 + 17);
                int row = y * w;
                for (int x = 0; x < w; x++)
                {
                    int c = f[row + x];
                    int n = grain > 0 ? r.Next(-grain, grain + 1) : 0;
                    int R = (((((c >> 16) & 255) * keep) >> 8) * (100 - tint) + tr * tint) / 100 + n;
                    int G = (((((c >> 8) & 255) * keep) >> 8) * (100 - tint) + tg * tint) / 100 + n;
                    int B = ((((c & 255) * keep) >> 8) * (100 - tint) + tbl * tint) / 100 + n;
                    if (R < 0) R = 0; else if (R > 255) R = 255;
                    if (G < 0) G = 0; else if (G > 255) G = 255;
                    if (B < 0) B = 0; else if (B > 255) B = 255;
                    f[row + x] = (R << 16) | (G << 8) | B;

                    if (keep < 256)
                    {
                        int sc = sh[row + x];
                        int sr = (((sc >> 16) & 255) * keep) >> 8;
                        int sg = (((sc >> 8) & 255) * keep) >> 8;
                        int sb = ((sc & 255) * keep) >> 8;
                        sh[row + x] = (sr << 16) | (sg << 8) | sb;
                    }
                }
            });
        }

        static Bitmap Resize(Bitmap src, int w, int h)
        {
            Bitmap dst = new Bitmap(w, h, PixelFormat.Format32bppRgb);
            using (Graphics g = Graphics.FromImage(dst))
            using (ImageAttributes ia = new ImageAttributes())
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingMode = CompositingMode.SourceCopy;
                ia.SetWrapMode(WrapMode.TileFlipXY);
                g.DrawImage(src, new Rectangle(0, 0, w, h), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, ia);
            }
            return dst;
        }

        static int[] ToArray(Bitmap b)
        {
            BitmapData d = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
            int[] a = new int[b.Width * b.Height];
            if (d.Stride == b.Width * 4)
                Marshal.Copy(d.Scan0, a, 0, a.Length);
            else
                for (int y = 0; y < b.Height; y++)
                    Marshal.Copy(d.Scan0 + y * d.Stride, a, y * b.Width, b.Width);
            b.UnlockBits(d);
            return a;
        }

        // Smooth value noise in 0..1, used so the fog creeps in unevenly.
        float[] MakeNoise(int w, int h)
        {
            float[] res = new float[w * h];
            int[] cells = { 4, 9, 20 };
            float[] amps = { 1f, 0.5f, 0.25f };
            for (int k = 0; k < cells.Length; k++)
            {
                int gx = cells[k];
                int gy = Math.Max(2, (int)Math.Round(cells[k] * (double)h / w));
                float[] lat = new float[(gx + 2) * (gy + 2)];
                for (int i = 0; i < lat.Length; i++) lat[i] = (float)rnd.NextDouble();
                for (int y = 0; y < h; y++)
                {
                    float fy = (float)y / h * gy;
                    int iy = (int)fy; float ty = fy - iy; ty = ty * ty * (3 - 2 * ty);
                    for (int x = 0; x < w; x++)
                    {
                        float fx = (float)x / w * gx;
                        int ix = (int)fx; float tx = fx - ix; tx = tx * tx * (3 - 2 * tx);
                        float a = lat[iy * (gx + 2) + ix], b = lat[iy * (gx + 2) + ix + 1];
                        float c = lat[(iy + 1) * (gx + 2) + ix], d = lat[(iy + 1) * (gx + 2) + ix + 1];
                        float top = a + (b - a) * tx, bot = c + (d - c) * tx;
                        res[y * w + x] += amps[k] * (top + (bot - top) * ty);
                    }
                }
            }
            float mn = float.MaxValue, mx = float.MinValue;
            for (int i = 0; i < res.Length; i++) { if (res[i] < mn) mn = res[i]; if (res[i] > mx) mx = res[i]; }
            float span = Math.Max(1e-5f, mx - mn);
            for (int i = 0; i < res.Length; i++) res[i] = (res[i] - mn) / span;
            return res;
        }

        // ---------- Frame ----------
        protected override void OnPaint(PaintEventArgs e)
        {
            if (hdcMem == IntPtr.Zero) return;
            double now = clock.Elapsed.TotalSeconds;
            float dt = (float)Math.Min(0.05, Math.Max(0.0, now - lastTime));
            lastTime = now;

            frameWatch.Restart();
            try
            {
                Step(dt);
                float flash = Lightning.Level(lightning, lightEvery, lightStrength);
                Render(flash);
            }
            catch (Exception ex)
            {
                Program.Log("CRASH (frame at t=" + elapsed.ToString("0.0") + "s, beads=" + beads.Count +
                            ", runners=" + runners.Count + "): " + ex);
                Close();
                return;
            }
            double ms = frameWatch.Elapsed.TotalMilliseconds;
            frames++;
            frameMsSum += ms;
            if (ms > frameMsMax) frameMsMax = ms;
            if (elapsed >= nextReport)
            {
                double wall = clock.Elapsed.TotalSeconds;
                Program.Log("  " + name + " t=" + elapsed.ToString("0") + "s  fps=" + (frames / Math.Max(0.001, wall - lastReportWall)).ToString("0") +
                            "  frame avg=" + (frameMsSum / Math.Max(1, frames)).ToString("0.0") + "ms max=" + frameMsMax.ToString("0.0") +
                            "ms  beads=" + beads.Count + " runners=" + runners.Count +
                            "  mem=" + (GC.GetTotalMemory(false) / 1048576) + "MB");
                frames = 0; frameMsSum = 0; frameMsMax = 0;
                nextReport += 5.0;
                lastReportWall = wall;
            }

            IntPtr hdc = e.Graphics.GetHdc();
            try
            {
                if (W == SW && H == SH)
                    BitBlt(hdc, 0, 0, W, H, hdcMem, 0, 0, SRCCOPY);
                else
                {
                    SetStretchBltMode(hdc, 3); // COLORONCOLOR: fast; HALFTONE was very slow
                    StretchBlt(hdc, 0, 0, SW, SH, hdcMem, 0, 0, W, H, SRCCOPY);
                }
            }
            finally { e.Graphics.ReleaseHdc(hdc); }
        }

        float Rand() { return (float)rnd.NextDouble(); }

        float Gauss()
        {
            float u1 = Math.Max(1e-6f, Rand()), u2 = Rand();
            return (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2));
        }

        float NextTrail()
        {
            if (trailBeads <= 0.001f) return float.MaxValue;
            return (6 + 30 * Rand()) * unit / trailBeads;
        }

        void Step(float dt)
        {
            elapsed += dt;

            // Fog clock: runs forwards, or backwards quickly while the glass is being wiped.
            if (wiping)
            {
                fogT -= dt * fogTime / 4f;
                if (fogT <= 0) { fogT = 0; wiping = false; cycleStart = elapsed; beads.Clear(); }
            }
            else
            {
                fogT += dt;
                if (loopSec > 0 && elapsed - cycleStart >= loopSec) wiping = true;
            }
            float g = fogT / fogTime;
            fogLevel = Math.Min(1f, g);

            // Fog grows back over wiped areas; combine with uneven build-up into per-cell opacity.
            float regrow = dt / refog;
            float u = uneven;
            for (int i = 0; i < clear.Length; i++)
            {
                float c = clear[i];
                if (c > 0) { c -= regrow; if (c < 0) c = 0; clear[i] = c; }
                float local = g * (1f + u) - noise[i] * u;
                if (local <= 0) { A[i] = 0; continue; }
                if (local > 1) local = 1;
                local = local * local * (3 - 2 * local);
                A[i] = (int)(256f * fogMax * local * (1f - c));
            }

            float area = (W * (float)H) / (1920f * 1080f);

            // Condensation beads appear as the glass fogs.
            int maxBeads = (int)(beadMax * area);
            beadAcc += dt * 80f * beadRate * area * fogLevel;
            while (beadAcc >= 1)
            {
                beadAcc -= 1;
                if (beads.Count < maxBeads)
                {
                    Drop b = new Drop();
                    b.x = Rand() * W; b.y = Rand() * H;
                    b.r = (0.7f + 2.0f * (float)Math.Pow(Rand(), 2.2)) * unit * beadSize;
                    beads.Add(b);
                }
            }

            // Rebuild the bead lookup grid.
            for (int i = 0; i < grid.Length; i++) grid[i].Clear();
            for (int i = 0; i < beads.Count; i++)
            {
                Drop b = beads[i];
                if (!b.dead) grid[GridIndex(b.x, b.y)].Add(b);
            }

            // Rain hitting the glass.
            float rainRate = 0f;
            if (elapsed >= rainDelay)
            {
                float ramp = rainRamp <= 0 ? 1f : Math.Min(1f, (elapsed - rainDelay) / rainRamp);
                rainRate = (rainStart + (rainMax - rainStart) * ramp) * area;
            }
            rainAcc += dt * rainRate;
            while (rainAcc >= 1)
            {
                rainAcc -= 1;
                Drop d = new Drop();
                d.x = Rand() * W; d.y = Rand() * H * 0.9f;
                d.r = Math.Min(MAX_R, (2.0f + 6.5f * (float)Math.Pow(Rand(), 2.5)) * unit * dropSize);
                Absorb(d);
                StampClear(d.x, d.y, d.r * 1.3f);
                if (d.r >= RUN_R)
                {
                    d.stall = 0.2f + Rand() * 1.3f;
                    d.trail = NextTrail();
                    d.wig = Math.Max(0f, 1f + (Rand() * 2f - 1f) * wiggleVar);
                    runners.Add(d);
                }
                else beads.Add(d);
            }

            // Running drops.
            for (int i = 0; i < runners.Count; i++)
            {
                Drop d = runners[i];
                if (float.IsNaN(d.x) || float.IsNaN(d.y) || float.IsNaN(d.r) || float.IsInfinity(d.vy)) { d.dead = true; continue; }
                if (d.stall > 0) { d.stall -= dt; d.vy *= 0.5f; continue; }

                float excess = (d.r - RUN_R * 0.65f) / unit;
                if (excess <= 0) { d.dead = true; Drop b = new Drop(); b.x = d.x; b.y = d.y; b.r = d.r; pending.Add(b); continue; }

                d.vy += (excess * 160f * unit * speed - d.vy * 2.2f) * dt;
                if (Rand() < dt * 0.9f * stops)
                {
                    d.stall = (0.08f + Rand() * 0.7f) / (1f + excess * 0.3f);
                    d.vy *= 0.25f;
                    if (Rand() < 0.6f) d.jink = 0; // drops often set off at a new angle after catching
                }
                // Meander: hold a sideways angle for a stretch, then turn to a new one.
                float travel = Math.Abs(d.vy * dt);
                d.jink -= travel;
                if (d.jink <= 0)
                {
                    float amt = wiggle * d.wig;
                    d.slopeTarget = Gauss() * 0.42f * amt;
                    if (Rand() < 0.12f) d.slopeTarget = (Rand() < 0.5f ? -1f : 1f) * (0.6f + 0.6f * Rand()) * amt; // sharp sideways jog
                    if (d.slopeTarget > 1.6f) d.slopeTarget = 1.6f;
                    if (d.slopeTarget < -1.6f) d.slopeTarget = -1.6f;
                    d.jink = (4f + 26f * Rand()) * unit * wiggleLen;
                }
                // Turn smoothly into the new direction over a few pixels; bigger, faster drops turn more lazily.
                float turnDist = (2.5f + d.r / unit * 0.35f) * unit;
                d.slope += (d.slopeTarget - d.slope) * Math.Min(1f, travel / turnDist);

                d.vx += (Rand() - 0.5f) * 70f * unit * wobble * dt;
                d.vx += ((d.slope + windSlant) * d.vy - d.vx) * Math.Min(1f, 9f * dt);

                float ox = d.x, oy = d.y;
                d.x += d.vx * dt;
                d.y += d.vy * dt;
                StampLine(ox, oy, d.x, d.y, d.r * 0.85f);

                d.trail -= (float)Math.Sqrt((d.x - ox) * (d.x - ox) + (d.y - oy) * (d.y - oy));
                if (d.trail <= 0)
                {
                    float br = d.r * (0.22f + 0.2f * Rand());
                    if (br > 0.6f * unit && beads.Count < maxBeads * 1.3f + 50)
                    {
                        Drop b = new Drop();
                        b.x = d.x + (Rand() - 0.5f) * d.r * 0.4f;
                        b.y = d.y - d.r * 0.9f;
                        b.r = br;
                        pending.Add(b);
                        d.r = (float)Math.Sqrt(Math.Max(d.r * d.r - br * br, 0.25f));
                    }
                    d.trail = NextTrail();
                }

                Absorb(d);
                if (d.y - d.r * 2 > H || d.x < -d.r * 2 || d.x > W + d.r * 2) d.dead = true;
            }

            runners.RemoveAll(IsDead);
            beads.RemoveAll(IsDead);
            if (pending.Count > 0) { beads.AddRange(pending); pending.Clear(); }
        }

        static bool IsDead(Drop d) { return d.dead; }

        int GridIndex(float x, float y)
        {
            int gx = (int)(x / gridCell), gy = (int)(y / gridCell);
            if (gx < 0) gx = 0; else if (gx >= gridW) gx = gridW - 1;
            if (gy < 0) gy = 0; else if (gy >= gridH) gy = gridH - 1;
            return gy * gridW + gx;
        }

        // Merge any beads the drop touches into it.
        void Absorb(Drop d)
        {
            int cx = (int)(d.x / gridCell), cy = (int)(d.y / gridCell);
            for (int gy = cy - 1; gy <= cy + 1; gy++)
            {
                if (gy < 0 || gy >= gridH) continue;
                for (int gx = cx - 1; gx <= cx + 1; gx++)
                {
                    if (gx < 0 || gx >= gridW) continue;
                    List<Drop> cell = grid[gy * gridW + gx];
                    for (int k = 0; k < cell.Count; k++)
                    {
                        Drop b = cell[k];
                        if (b.dead || b == d) continue;
                        float dx = b.x - d.x, dy = b.y - d.y;
                        float rr = d.r + b.r * 0.6f;
                        if (dx * dx + dy * dy < rr * rr)
                        {
                            b.dead = true;
                            d.r = Math.Min(MAX_R, (float)Math.Sqrt(d.r * d.r + b.r * b.r));
                            d.x += dx * 0.08f;
                        }
                    }
                }
            }
        }

        void StampLine(float x0, float y0, float x1, float y1, float rad)
        {
            float dx = x1 - x0, dy = y1 - y0;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            int steps = (int)(len / Math.Max(1f, rad * 0.5f)) + 1;
            if (steps > 200) steps = 200;
            for (int i = 1; i <= steps; i++)
            {
                float t = (float)i / steps;
                StampClear(x0 + dx * t, y0 + dy * t, rad);
            }
        }

        void StampClear(float x, float y, float rad)
        {
            float cx = (x - 1.5f) / CELL, cy = (y - 1.5f) / CELL;
            float rc = Math.Max(0.75f, rad / CELL);
            int ix0 = Math.Max(0, (int)(cx - rc)), ix1 = Math.Min(MW - 1, (int)(cx + rc) + 1);
            int iy0 = Math.Max(0, (int)(cy - rc)), iy1 = Math.Min(MH - 1, (int)(cy + rc) + 1);
            float inv = 1f / (rc * rc);
            for (int iy = iy0; iy <= iy1; iy++)
            {
                float ddy = iy - cy;
                for (int ix = ix0; ix <= ix1; ix++)
                {
                    float ddx = ix - cx;
                    float d2 = (ddx * ddx + ddy * ddy) * inv;
                    if (d2 >= 1) continue;
                    float v = 1f - d2 * d2;
                    int k = iy * MW + ix;
                    if (clear[k] < v) clear[k] = v;
                }
            }
        }

        // ---------- Rendering ----------
        void Render(float flash)
        {
            int w = W, mw = MW;
            int[] s = sharp, f = fog, a = A, m0 = mx0, wxx = wx;
            IntPtr dib = bits;
            int fl = (int)(flash * 210f);
            int flB = fl + fl / 6;

            Parallel.For(0, H, popt, y =>
            {
                float fy = (y + 0.5f) / CELL - 0.5f;
                if (fy < 0) fy = 0;
                int my0 = (int)fy;
                int wy = (int)((fy - my0) * 256f);
                int r0 = my0 * mw;
                int row = y * w;
                uint* d = (uint*)dib.ToPointer() + row;
                for (int x = 0; x < w; x++)
                {
                    int i0 = r0 + m0[x];
                    int fx = wxx[x];
                    int a0 = a[i0] + (((a[i0 + 1] - a[i0]) * fx) >> 8);
                    int a1 = a[i0 + mw] + (((a[i0 + mw + 1] - a[i0 + mw]) * fx) >> 8);
                    int al = a0 + (((a1 - a0) * wy) >> 8);
                    uint sp = (uint)s[row + x];
                    uint px;
                    if (al <= 0) px = sp;
                    else
                    {
                        uint fp = (uint)f[row + x];
                        uint ua = (uint)al, ia = (uint)(256 - al);
                        uint rb = (((sp & 0xFF00FFu) * ia + (fp & 0xFF00FFu) * ua) >> 8) & 0xFF00FFu;
                        uint gg = (((sp & 0x00FF00u) * ia + (fp & 0x00FF00u) * ua) >> 8) & 0x00FF00u;
                        px = rb | gg;
                    }
                    if (fl > 0)
                    {
                        int R = (int)((px >> 16) & 255) + fl;
                        int G = (int)((px >> 8) & 255) + fl;
                        int B = (int)(px & 255) + flB;
                        if (R > 255) R = 255;
                        if (G > 255) G = 255;
                        if (B > 255) B = 255;
                        px = (uint)((R << 16) | (G << 8) | B);
                    }
                    d[x] = px;
                }
            });

            uint* dst = (uint*)bits.ToPointer();
            for (int i = 0; i < beads.Count; i++) DrawDrop(dst, beads[i].x, beads[i].y, beads[i].r, 1f);
            for (int i = 0; i < runners.Count; i++) DrawDrop(dst, runners[i].x, runners[i].y, runners[i].r, stretch);
        }

        // A drop is a tiny lens: it shows an inverted, shrunken view of the frozen screen,
        // with a dark rim, a bright glint at the top-left and a little light gathered at the bottom.
        void DrawDrop(uint* dst, float cx, float cy, float r, float stretchY)
        {
            if (r <= 0.05f) return;
            float ry = r * stretchY;
            int x0 = Math.Max(0, (int)(cx - r - 1)), x1 = Math.Min(W - 1, (int)(cx + r + 1));
            int y0 = Math.Max(0, (int)(cy - ry - 1)), y1 = Math.Min(H - 1, (int)(cy + ry + 1));
            if (x0 > x1 || y0 > y1) return;
            float ir = 1f / r, iry = 1f / ry;
            float lens = r * lensMag + 4f * unit;
            float hlk = r < 3f * unit ? 6f : 12f;

            for (int y = y0; y <= y1; y++)
            {
                float ny = (y + 0.5f - cy) * iry;
                for (int x = x0; x <= x1; x++)
                {
                    float nx = (x + 0.5f - cx) * ir;
                    float d2 = nx * nx + ny * ny;
                    if (d2 >= 1f + 3f * ir) continue;
                    float dist = (float)Math.Sqrt(d2);
                    float cov = (1f - dist) * r + 0.5f;
                    if (cov <= 0) continue;
                    if (cov > 1) cov = 1;
                    if (dist > 1) dist = 1;

                    int sx = (int)(cx - nx * lens);
                    int sy = (int)(cy - ny * lens * stretchY - r * 0.5f);
                    if (sx < 0) sx = 0; else if (sx >= W) sx = W - 1;
                    if (sy < 0) sy = 0; else if (sy >= H) sy = H - 1;
                    uint c = (uint)sharp[sy * W + sx];

                    float shade = 1.06f - 0.22f * dist * dist;
                    if (dist > 0.78f) shade *= Math.Max(0f, 1f - (dist - 0.78f) * rimK);
                    if (ny > 0.3f) shade += glow * (ny - 0.3f) * (1f - dist);

                    float hx = nx + 0.32f, hy = ny + 0.42f;
                    float hl = 1f - (hx * hx + hy * hy) * hlk;
                    if (hl < 0) hl = 0;
                    hl = hl * hl * glint;

                    int R = (int)(((c >> 16) & 255) * shade + hl);
                    int G = (int)(((c >> 8) & 255) * shade + hl);
                    int B = (int)((c & 255) * shade + hl);
                    if (R > 255) R = 255; if (G > 255) G = 255; if (B > 255) B = 255;
                    if (R < 0) R = 0; if (G < 0) G = 0; if (B < 0) B = 0;

                    int idx = y * W + x;
                    if (cov < 1f)
                    {
                        uint o = dst[idx];
                        float ic = 1f - cov;
                        R = (int)(R * cov + ((o >> 16) & 255) * ic);
                        G = (int)(G * cov + ((o >> 8) & 255) * ic);
                        B = (int)(B * cov + (o & 255) * ic);
                    }
                    dst[idx] = (uint)((R << 16) | (G << 8) | B);
                }
            }
        }
    }
}
