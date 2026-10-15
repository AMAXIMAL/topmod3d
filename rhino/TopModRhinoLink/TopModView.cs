using System;
using System.Text.Json;
using Eto.Drawing;
using Eto.Forms;
using Rhino;

namespace TopModRhinoLink
{
    /// <summary>
    /// The TopMod UI: a thin toolbar over a WebView running the embedded TopMod app. Hosted either in the
    /// dockable Rhino panel (<see cref="TopModPanel"/>) or in a floating window (<see cref="TopModForm"/>).
    /// All traffic is in-process through WebView.ExecuteScript — no server, no network.
    ///   every 250 ms:  __tmPoll()  → { out:{v,obj}|null, cmds:[...], ready, why }   (page → Rhino)
    ///   when Rhino changed the live object or an Import ran: __tmApply(kind, obj)   (Rhino → page)
    /// </summary>
    public class TopModView : Panel
    {
        readonly WebView _web;
        readonly Label _status;
        readonly UITimer _timer;
        int _lastRhinoVersion = -1;
        int _lastWebVersion = 0;
        bool _busy;
        bool _loaded;
        string _source = "";

        /// <summary>One shared instance: Rhino re-creates panels on dock/undock/resize, and reloading TopMod each time would lose the model.</summary>
        static TopModView _shared;
        public static TopModView Shared(bool compact)
        {
            if (_shared == null) _shared = new TopModView(compact);
            // detach from whatever hosted it before (a closed form, a re-created panel)
            if (_shared.Parent is Panel host && host != null) host.Content = null;
            return _shared;
        }

        public TopModView(bool compact)
        {
            _web = new WebView();

            var bake = new Button { Text = compact ? "Bake ▸" : "Bake ▸ Rhino", ToolTip = "Copy the live TopMod model to the 'TopMod Baked' layer as NURBS (stops following TopMod edits)" };
            bake.Click += (s, e) => Bake(false);
            var bakeMesh = new Button { Text = compact ? "Mesh ▸" : "Bake as Mesh", ToolTip = "Same, as a Rhino mesh (n-gons kept)" };
            bakeMesh.Click += (s, e) => Bake(true);
            var import = new Button { Text = compact ? "◂ Import" : "Import ◂ Rhino", ToolTip = "Pick meshes / polysurfaces / SubDs in Rhino and add them to TopMod as a new layer" };
            import.Click += (s, e) => Import();
            var reload = new Button { Text = compact ? "↻" : "Reload", ToolTip = "Reload the TopMod page" };
            reload.Click += (s, e) => LoadPage();
            var layoutOn = new Button { Text = "Layout", ToolTip = "TopMod workspace: panel docked right at half width, single shaded Perspective view" };
            layoutOn.Click += (s, e) => Application.Instance.AsyncInvoke(() => RhinoApp.RunScript("_TopModWorkspace", false));
            var layoutOff = new Button { Text = "Default", ToolTip = "Rhino's default window layout with four viewports" };
            layoutOff.Click += (s, e) => Application.Instance.AsyncInvoke(() => RhinoApp.RunScript("_TopModDefaultLayout", false));
            var close = new Button { Text = "✕ Close", ToolTip = "Close TopMod and go back to Rhino's default layout (the live object stays; run TopMod to reopen)" };
            close.Click += (s, e) => Application.Instance.AsyncInvoke(() =>
            {
                var p = TopModLinkPlugIn.Instance;
                if (p.Form != null && p.Form.Visible) p.Form.Close();
                if (Rhino.UI.Panels.IsPanelVisible(TopModPanel.PanelId)) Rhino.UI.Panels.ClosePanel(TopModPanel.PanelId);
                RhinoApp.RunScript("_TopModDefaultLayout", false);
            });

            _status = new Label { Text = "loading…", VerticalAlignment = VerticalAlignment.Center, TextColor = Colors.Gray };

            var bar = new StackLayout
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4, Padding = new Padding(6, 4),
                VerticalContentAlignment = VerticalAlignment.Center,
                Items = { bake, bakeMesh, import, reload, new Panel { Width = 8 }, layoutOn, layoutOff, close, new StackLayoutItem(null, true), _status }
            };

            Content = new TableLayout { Rows = { new TableRow(bar), new TableRow(_web) { ScaleHeight = true } } };

            _timer = new UITimer { Interval = 0.08 };   // fast enough to follow a streaming simulation
            _timer.Elapsed += (s, e) => Tick();

            Load += (s, e) => { if (!_loaded) LoadPage(); else _timer.Start(); };
            UnLoad += (s, e) => _timer.Stop();
        }

        public void LoadPage()
        {
            _timer.Stop();
            try
            {
                var path = TopModLinkPlugIn.Instance.PreparePage(out _source);
                _lastWebVersion = 0;
                _web.Url = new Uri(new Uri(path).AbsoluteUri + "#nodemo");   // the bridge asks before running the demo
                _loaded = true;
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
                    string why = root.TryGetProperty("why", out var w) ? w.GetString() : "";
                    if (root.TryGetProperty("out", out var o) && o.ValueKind == JsonValueKind.Object && doc != null)
                    {
                        int v = o.GetProperty("v").GetInt32();
                        if (v > _lastWebVersion)
                        {
                            _lastWebVersion = v;
                            string mode = o.TryGetProperty("mode", out var md) ? md.GetString() : "nurbs";
                            try { sync.ApplyFromWeb(doc, o.GetProperty("obj").GetString(), mode); }
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
                    if (root.TryGetProperty("fab", out var fab) && fab.ValueKind == JsonValueKind.Object && doc != null)
                    {
                        try { ApplyFab(doc, sync, fab); } catch (Exception ex) { RhinoApp.WriteLine("TopMod: fab curves — " + ex.Message); }
                    }
                    if (root.TryGetProperty("cam", out var cam) && cam.ValueKind == JsonValueKind.Object && doc != null)
                    {
                        try { SyncCamera(doc, cam); } catch { }
                    }
                    _status.Text = Status(ready, doc, sync, why);
                }

                // ---- Rhino → page ----
                var pull = sync.Pull(_lastRhinoVersion);
                if (pull != null)
                {
                    var (ver, kind, obj) = pull.Value;
                    string js = "return (window.__tmApply ? window.__tmApply(" + JsonSerializer.Serialize(kind) + "," + JsonSerializer.Serialize(obj) + ") : 'not ready');";
                    string res = null;
                    try { res = _web.ExecuteScript(js); } catch (Exception ex) { res = ex.Message; }
                    if (res == "not ready" || string.IsNullOrEmpty(res)) return;   // page still starting: keep it queued and retry next tick
                    _lastRhinoVersion = ver;
                    if (res != "ok") RhinoApp.WriteLine("TopMod: {0} → page: {1}", kind, res);
                }
            }
            finally { _busy = false; }
        }

        static void ApplyFab(RhinoDoc doc, LiveSync sync, JsonElement fab)
        {
            bool on = fab.TryGetProperty("on", out var o) && o.GetBoolean();
            string mode = fab.TryGetProperty("mode", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
            var loops = new System.Collections.Generic.List<(System.Collections.Generic.List<Rhino.Geometry.Point3d>, System.Collections.Generic.List<Rhino.Geometry.Point3d>)>();
            if (fab.TryGetProperty("loops", out var ls) && ls.ValueKind == JsonValueKind.Array)
                foreach (var pair in ls.EnumerateArray())
                    loops.Add((Pts(pair, "src"), Pts(pair, "dst")));
            string net = fab.TryGetProperty("net", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
            sync.ApplyFab(doc, on, mode, loops, net);
        }

        static System.Collections.Generic.List<Rhino.Geometry.Point3d> Pts(JsonElement pair, string key)
        {
            var list = new System.Collections.Generic.List<Rhino.Geometry.Point3d>();
            if (!pair.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array) return list;
            foreach (var p in arr.EnumerateArray())
                if (p.ValueKind == JsonValueKind.Array && p.GetArrayLength() >= 3)
                    list.Add(new Rhino.Geometry.Point3d(p[0].GetDouble(), p[1].GetDouble(), p[2].GetDouble()));
            return list;
        }

        /// <summary>TopMod orbit → Rhino Perspective view. TopMod camera is Y-up in inches; Rhino is Z-up in doc units.</summary>
        void SyncCamera(RhinoDoc doc, JsonElement cam)
        {
            var view = doc.Views.ActiveView;
            if (view == null || !view.ActiveViewport.IsPerspectiveProjection)
            {
                foreach (var v in doc.Views) if (v.ActiveViewport.Name == "Perspective" || v.ActiveViewport.IsPerspectiveProjection) { view = v; break; }
            }
            if (view == null || !view.ActiveViewport.IsPerspectiveProjection) return;
            double s = LiveSync.InToDoc(doc);
            var p = cam.GetProperty("p"); var t = cam.GetProperty("t");
            Rhino.Geometry.Point3d Conv(JsonElement a) => new Rhino.Geometry.Point3d(a[0].GetDouble() * s, -a[2].GetDouble() * s, a[1].GetDouble() * s);
            var pos = Conv(p); var tgt = Conv(t);
            var vp = view.ActiveViewport;
            vp.SetCameraLocations(tgt, pos);
            vp.CameraUp = Rhino.Geometry.Vector3d.ZAxis;
            if (cam.TryGetProperty("fov", out var f))
            {
                double vfov = f.GetDouble() * Math.PI / 180.0;          // three.js: vertical FOV in degrees
                double lens = 12.0 / Math.Tan(vfov / 2.0);              // 24 mm frame height
                vp.Camera35mmLensLength = Math.Max(5, Math.Min(500, lens));
            }
            view.Redraw();
        }

        string Status(bool ready, RhinoDoc doc, LiveSync sync, string why)
        {
            if (!ready) return "starting… " + (string.IsNullOrEmpty(why) ? "" : "— " + why);
            bool live = doc != null && sync.FindLive(doc) != null;
            return (live ? (sync.LiveIsMesh ? "● live (mesh)" : "● live") : "○ idle") + "  ↑" + sync.WebPushes + " ↓" + sync.RhinoEdits + "  " + (doc?.ModelUnitSystem.ToString() ?? "");
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
