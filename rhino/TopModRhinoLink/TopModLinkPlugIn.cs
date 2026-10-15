using System;
using System.IO;
using System.Reflection;
using Rhino;
using Rhino.PlugIns;

namespace TopModRhinoLink
{
    public class TopModLinkPlugIn : PlugIn
    {
        public static TopModLinkPlugIn Instance { get; private set; }
        public LiveSync Sync { get; private set; }
        public TopModForm Form { get; set; }

        public TopModLinkPlugIn() { Instance = this; }

        public override PlugInLoadTime LoadTime => PlugInLoadTime.AtStartup;

        protected override LoadReturnCode OnLoad(ref string errorMessage)
        {
            Sync = new LiveSync();
            LiveSync.DowelDiameterIn = Settings.GetDouble("DowelIn", 0.25);
            Rhino.UI.Panels.RegisterPanel(this, typeof(TopModPanel), "TopMod 3D", typeof(TopModLinkPlugIn).Assembly, "TopModRhinoLink.topmod.ico", Rhino.UI.PanelType.System);
            return LoadReturnCode.Success;
        }

        protected override void OnShutdown()
        {
            Sync?.Dispose();
        }

        /// <summary>Folder the app page is written to before loading (WKWebView wants a real file:// for a full-featured origin).</summary>
        public string AppFolder
        {
            get
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TopModRhinoLink");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        /// <summary>
        /// Writes TOPMOD.html (embedded in this .rhp, or a dev override set with TopModSetHtml) with the bridge
        /// injected, and returns the file path to load.
        /// </summary>
        public string PreparePage(out string source)
        {
            string html;
            var dev = Settings.GetString("HtmlPath", "");
            if (!string.IsNullOrWhiteSpace(dev) && File.Exists(dev)) { html = File.ReadAllText(dev); source = dev; }
            else { html = Resource("TOPMOD.html"); source = "embedded"; }
            var bridge = Resource("bridge.js");
            const string marker = "<!--topmod-rhino-bridge-->";
            string tag = marker + "<script>" + bridge + "</script>";
            int i = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
            html = i >= 0 ? html.Insert(i, tag) : html + tag;
            var path = Path.Combine(AppFolder, "TOPMOD.html");
            File.WriteAllText(path, html);
            return path;
        }

        static string Resource(string name)
        {
            using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
            if (s == null) throw new FileNotFoundException("embedded resource missing: " + name);
            using var r = new StreamReader(s);
            return r.ReadToEnd();
        }

        /// <summary>
        /// Apply the "TopMod" window layout (TopMod docked right at half width, saved as TopMod.rhw and embedded here)
        /// and maximize the Perspective viewport. Returns false when no layout is embedded.
        /// </summary>
        public bool ApplyWorkspace(RhinoDoc doc)
        {
            string rhw = null;
            try
            {
                using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("TopMod.rhw");
                if (s != null) { using var r = new StreamReader(s); rhw = r.ReadToEnd(); }
            }
            catch { }
            if (rhw != null)
            {
                // the TopMod dock band takes PanelFraction of the Rhino window (the .rhw stores an absolute width)
                int band = 1135;
                try
                {
                    var win = Rhino.UI.RhinoEtoApp.MainWindow;
                    if (win != null && win.Width > 400) band = (int)Math.Round(win.Width * PanelFraction);
                }
                catch { }
                rhw = rhw.Replace("dock_band_size=\"818,200\"", "dock_band_size=\"" + band + ",200\"")
                         .Replace("<band size=\"818\">", "<band size=\"" + band + "\">");
                var path = Path.Combine(AppFolder, "TopMod.rhw");
                File.WriteAllText(path, rhw);
                // import once (re-importing adds "TopMod 01", "TopMod 02"… instead of replacing), then restore by name
                // NOTE: only works from a [CommandStyle(Style.ScriptRunner)] command (TopModWorkspace)
                if (Settings.GetInteger("LayoutVersion", 0) < 2)
                {
                    RhinoApp.RunScript("_-WindowLayout _Import \"" + path + "\" _Enter", false);
                    Settings.SetInteger("LayoutVersion", 2);
                }
                RhinoApp.RunScript("_-WindowLayout \"TopMod\"", false);
            }
            if (doc != null)
            {
                foreach (var v in doc.Views)
                    if (v.ActiveViewport.Name == "Perspective")
                    {
                        doc.Views.ActiveView = v; v.Maximized = true;
                        var shaded = Rhino.Display.DisplayModeDescription.FindByName("Shaded");
                        if (shaded != null) v.ActiveViewport.DisplayMode = shaded;
                        break;
                    }
                doc.Views.Redraw();
            }
            return rhw != null;
        }

        /// <summary>Share of the Rhino window the TopMod panel takes in the TopMod workspace (0.63 ≈ the panel at 1135 of 1800 pt).</summary>
        public const double PanelFraction = 0.63;

        public bool WorkspaceApplied { get => Settings.GetBool("WorkspaceApplied", false); set => Settings.SetBool("WorkspaceApplied", value); }

        public string DevHtmlPath => Settings.GetString("HtmlPath", "");
        public void SetDevHtmlPath(string p) { Settings.SetString("HtmlPath", p ?? ""); }
    }
}
