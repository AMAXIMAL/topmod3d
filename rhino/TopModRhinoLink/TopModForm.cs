using System;
using System.Text.Json;
using Eto.Drawing;
using Eto.Forms;
using Rhino;
using Rhino.UI;

namespace TopModRhinoLink
{
    /// <summary>
    /// The TopMod window inside Rhino: a thin toolbar over a WebView running the embedded TopMod app.
    /// All traffic is in-process through WebView.ExecuteScript — no server, no network.
    ///   every 250 ms:  __tmPoll()  → { out:{v,obj}|null, cmds:[...] }   (page → Rhino)
    ///   when Rhino changed the live object or an Import ran: __tmApply(kind, obj)   (Rhino → page)
    /// </summary>
    public class TopModForm : Form
    {
        readonly WebView _web;
        readonly Label _status;
        readonly UITimer _timer;
        int _lastRhinoVersion = -1;
        int _lastWebVersion = 0;
        bool _busy;
        string _source = "";

        public TopModForm()
        {
            var plugin = TopModLinkPlugIn.Instance;
            Title = "TopMod 3D";
            ClientSize = new Size(1180, 800);
            MinimumSize = new Size(640, 420);
            Resizable = true; Maximizable = true; Minimizable = true;
            Owner = RhinoEtoApp.MainWindow;

            _web = new WebView();

            var bake = new Button { Text = "Bake ▸ Rhino", ToolTip = "Copy the live TopMod model to the 'TopMod Baked' layer as NURBS (stops following TopMod edits)" };
            bake.Click += (s, e) => Bake(false);
            var bakeMesh = new Button { Text = "Bake as Mesh", ToolTip = "Same, as a Rhino mesh (n-gons kept)" };
            bakeMesh.Click += (s, e) => Bake(true);
            var import = new Button { Text = "Import ◂ Rhino", ToolTip = "Pick meshes / polysurfaces / SubDs in Rhino and add them to TopMod as a new layer" };
            import.Click += (s, e) => Import();
            var reload = new Button { Text = "Reload" };
            reload.Click += (s, e) => LoadPage();

            _status = new Label { Text = "loading…", VerticalAlignment = VerticalAlignment.Center, TextColor = Colors.Gray };

            var bar = new StackLayout
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6, Padding = new Padding(8, 6),
                VerticalContentAlignment = VerticalAlignment.Center,
                Items = { bake, bakeMesh, import, new Panel { Width = 12 }, reload, new StackLayoutItem(null, true), _status }
            };

            Content = new TableLayout { Rows = { new TableRow(bar), new TableRow(_web) { ScaleHeight = true } } };

            Load += (s, e) => LoadPage();

            _timer = new UITimer { Interval = 0.25 };
            _timer.Elapsed += (s, e) => Tick();
            Closed += (s, e) => { _timer.Stop(); plugin.Form = null; };
        }

        void LoadPage()
        {
            _timer.Stop();
            try
            {
                var path = TopModLinkPlugIn.Instance.PreparePage(out _source);
                _lastWebVersion = 0;
                _web.Url = new Uri(path);
                _status.Text = "loading " + _source + "…";
            }
            catch (Exception ex)
            {
                _status.Text = "⚠ " + ex.Message;
                RhinoApp.WriteLine("TopMod: " + ex.Message);
                return;
            }
            _timer.Start();
        }

        void Tick()
        {
            if (_busy) return;
            _busy = true;
            try
            {
                var doc = RhinoDoc.ActiveDoc;
                var sync = TopModLinkPlugIn.Instance.Sync;

                // ---- page → Rhino ----
                string json = null;
                try { json = _web.ExecuteScript("return (window.__tmPoll ? window.__tmPoll() : '');"); } catch { }
                if (!string.IsNullOrEmpty(json))
                {
                    using var d = JsonDocument.Parse(json);
                    var root = d.RootElement;
                    bool ready = root.TryGetProperty("ready", out var r) && r.GetBoolean();
                    if (root.TryGetProperty("out", out var o) && o.ValueKind == JsonValueKind.Object && doc != null)
                    {
                        int v = o.GetProperty("v").GetInt32();
                        if (v > _lastWebVersion)
                        {
                            _lastWebVersion = v;
                            try { sync.ApplyFromWeb(doc, o.GetProperty("obj").GetString()); }
                            catch (Exception ex) { RhinoApp.WriteLine("TopMod: push failed — " + ex.Message); }
                        }
                    }
                    if (root.TryGetProperty("cmds", out var cmds) && cmds.ValueKind == JsonValueKind.Array)
                        foreach (var c in cmds.EnumerateArray())
                            switch (c.GetString())
                            {
                                case "bake": Bake(false); break;
                                case "bakeMesh": Bake(true); break;
                                case "import": Import(); break;
                            }
                    _status.Text = Status(ready, doc, sync);
                }

                // ---- Rhino → page ----
                var pull = sync.Pull(_lastRhinoVersion);
                if (pull != null)
                {
                    var (ver, kind, obj) = pull.Value;
                    _lastRhinoVersion = ver;
                    string js = "return (window.__tmApply ? window.__tmApply(" + JsonSerializer.Serialize(kind) + "," + JsonSerializer.Serialize(obj) + ") : 'not ready');";
                    string res = null;
                    try { res = _web.ExecuteScript(js); } catch (Exception ex) { res = ex.Message; }
                    if (res != "ok") RhinoApp.WriteLine("TopMod: {0} → page: {1}", kind, res);
                }
            }
            finally { _busy = false; }
        }

        string Status(bool ready, RhinoDoc doc, LiveSync sync)
        {
            if (!ready) return "starting TopMod… (" + _source + ")";
            bool live = doc != null && sync.FindLive(doc) != null;
            return (live ? "● live in Rhino" : "○ nothing pushed yet")
                 + "   ↑" + sync.WebPushes + "  ↓" + sync.RhinoEdits
                 + "   " + (doc?.ModelUnitSystem.ToString() ?? "");
        }

        void Bake(bool asMesh)
        {
            var doc = RhinoDoc.ActiveDoc; if (doc == null) return;
            var id = TopModLinkPlugIn.Instance.Sync.Bake(doc, asMesh);
            RhinoApp.WriteLine(id == Guid.Empty ? "TopMod: nothing to bake yet — make something in TopMod first." : "TopMod: baked to layer '" + LiveSync.BakeLayer + "'.");
        }

        void Import()
        {
            // the pick must run as a command; the timer keeps polling, so the result arrives through Pull()
            Application.Instance.AsyncInvoke(() => RhinoApp.RunScript("_-TopModImport", false));
        }
    }
}
