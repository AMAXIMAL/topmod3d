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
            EdgeConduit.Init((EdgeMode)Settings.GetInteger("EdgeMode", (int)EdgeMode.Visible));
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
            // the package ships TOPMOD.html beside the .rhp: that copy is the one TopMod runs; the embedded copy is the fallback
            string packaged = null;
            try { packaged = Path.Combine(Path.GetDirectoryName(typeof(TopModLinkPlugIn).Assembly.Location) ?? "", "TOPMOD.html"); } catch { }
            if (!string.IsNullOrWhiteSpace(dev) && File.Exists(dev)) { html = File.ReadAllText(dev); source = dev; }
            else if (packaged != null && File.Exists(packaged) && (html = File.ReadAllText(packaged)).Contains("__tmBridge")) { source = "package"; }
            else { html = Resource("TOPMOD.html"); source = "embedded"; }   // a packaged copy without the Rhino hooks would leave the link dead
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
                         .Replace("<band size=\"818\">", "<band size=\"" + band + "\">")
                         // its own name: an import that collides with an existing layout gets renamed "… 01", "… 02"
                         .Replace("name=\"TopMod\">", "name=\"" + LayoutName + "\">")
                         .Replace("<locale_1033>TopMod</locale_1033>", "<locale_1033>" + LayoutName + "</locale_1033>");
                var path = Path.Combine(AppFolder, "TopMod.rhw");
                File.WriteAllText(path, rhw);
                // import once (re-importing adds "TopMod 01", "TopMod 02"… instead of replacing), then restore by name
                // NOTE: only works from a [CommandStyle(Style.ScriptRunner)] command (TopModWorkspace)
                if (Settings.GetInteger("LayoutVersion", 0) < 3)
                {
                    RhinoApp.RunScript("_-WindowLayout _Import \"" + path + "\" _Enter", false);
                    Settings.SetInteger("LayoutVersion", 3);
                }
                RhinoApp.RunScript("_-WindowLayout \"" + LayoutName + "\"", false);
            }
            MaximizeShadedPerspective(doc);
            // a restored layout rebuilds its views a moment later; set Shaded again once it has settled
            Eto.Forms.Application.Instance.AsyncInvoke(() => MaximizeShadedPerspective(RhinoDoc.ActiveDoc));
            return rhw != null;
        }

        public const string LayoutName = "TopMod Workspace";

        /// <summary>Single maximized Perspective view in Shaded mode.</summary>
        public static void MaximizeShadedPerspective(RhinoDoc doc)
        {
            if (doc == null) return;
            var shaded = Rhino.Display.DisplayModeDescription.FindByName("Shaded")
                      ?? Rhino.Display.DisplayModeDescription.GetDisplayMode(Rhino.Display.DisplayModeDescription.ShadedId);
            Rhino.Display.RhinoView persp = null;
            foreach (var v in doc.Views)
                if (v.ActiveViewport.Name == "Perspective") { persp = v; break; }
            if (persp == null) foreach (var v in doc.Views) if (v.ActiveViewport.IsPerspectiveProjection) { persp = v; break; }
            if (persp == null) return;
            doc.Views.ActiveView = persp;
            if (!persp.Maximized) persp.Maximized = true;
            if (shaded != null && persp.ActiveViewport.DisplayMode?.Id != shaded.Id) persp.ActiveViewport.DisplayMode = shaded;
            doc.Views.Redraw();
        }

        /// <summary>Share of the Rhino window the TopMod panel takes in the TopMod workspace (two thirds).</summary>
        public const double PanelFraction = 2.0 / 3.0;

        public bool WorkspaceApplied { get => Settings.GetBool("WorkspaceApplied", false); set => Settings.SetBool("WorkspaceApplied", value); }

        /// <summary>Edge overlay on the live object (Visible / All / Off), remembered between sessions.</summary>
        public void SetEdgeMode(EdgeMode m)
        {
            EdgeConduit.Instance?.Set(m);
            Settings.SetInteger("EdgeMode", (int)m);
        }

        public string DevHtmlPath => Settings.GetString("HtmlPath", "");
        public void SetDevHtmlPath(string p) { Settings.SetString("HtmlPath", p ?? ""); }
    }
}
