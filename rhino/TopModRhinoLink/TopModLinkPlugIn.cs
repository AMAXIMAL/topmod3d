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

        public string DevHtmlPath => Settings.GetString("HtmlPath", "");
        public void SetDevHtmlPath(string p) { Settings.SetString("HtmlPath", p ?? ""); }
    }
}
