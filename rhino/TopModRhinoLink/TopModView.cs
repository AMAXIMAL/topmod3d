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
            string EdgeLabel(EdgeMode m) => m == EdgeMode.Visible ? "Edges: Visible" : m == EdgeMode.All ? "Edges: All" : "Edges: Off";
            var edges = new Button { Text = EdgeLabel(EdgeConduit.Instance?.Mode ?? EdgeMode.Visible), ToolTip = "Outline the live model in Rhino like TopMod: Visible (hidden edges removed) → All (see-through) → Off" };
            edges.Click += (s, e) =>
            {
                var next = EdgeConduit.Instance?.Next() ?? EdgeMode.Visible;
                TopModLinkPlugIn.Instance.SetEdgeMode(next);
                edges.Text = EdgeLabel(next);
            };
            var close = new Button { Text = "✕ Close", ToolTip = "Close TopMod and go back to Rhino's default layout (the live object stays; run TopMod to reopen)" };
            close.Click += (s, e) => Application.Instance.AsyncInvoke(() =>
            {
                var p = TopModLinkPlugIn.Instance;
                if (p.Form != null && p.Form.Visible) p.Form.Close();
                if (Rhino.UI.Panels.IsPanelVisible(TopModPanel.PanelId)) Rhino.UI.Panels.ClosePanel(TopModPanel.PanelId);
                RhinoApp.RunScript("_TopModDefaultLayout", false);
            });

            _live = new Button { Text = "● Live", ToolTip = "Live: TopMod and Rhino mirror each other 1:1. Click to Pause — Rhino keeps what it has while you keep modeling; click again to send the current model across." };
            _live.Click += (s, e) => SetPaused(!_paused);

            _separate = new Button { Text = "⧉ Separate", ToolTip = "Pop TopMod out of Rhino into its own window (drag it to another monitor). Press again to dock it back." };
            _separate.Click += (s, e) => Application.Instance.AsyncInvoke(ToggleSeparate);

            _status = new Label { Text = "loading…", VerticalAlignment = VerticalAlignment.Center, TextColor = Colors.Gray };

            var bar = new StackLayout
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4, Padding = new Padding(6, 4),
                VerticalContentAlignment = VerticalAlignment.Center,
                Items = { _live, bake, bakeMesh, import, reload, new Panel { Width = 8 }, edges, new Panel { Width = 8 }, layoutOn, layoutOff, _separate, close, new StackLayoutItem(null, true), _status }
            };

            Content = new TableLayout { Rows = { new TableRow(bar), new TableRow(_web) { ScaleHeight = true } } };

            _timer = new UITimer { Interval = 0.08 };   // fast enough to follow a streaming simulation
            _timer.Elapsed += (s, e) => Tick();

            Load += (s, e) => { UpdateSeparateLabel(); if (!_loaded) LoadPage(); else _timer.Start(); };
            UnLoad += (s, e) => _timer.Stop();
        }

        Button _separate;
        Button _live;
        bool _paused;

        /// <summary>Pause = stop the 1:1 copy both ways (model, fab curves, field poles, camera). Bake / Import still work.
        /// Resume sends TopMod's current state across; Rhino-side edits to the live object made while paused are dropped.</summary>
        void SetPaused(bool p)
        {
            _paused = p;
            _live.Text = p ? "❚❚ Paused" : "● Live";
            _live.TextColor = p ? Color.FromArgb(255, 140, 0) : SystemColors.ControlText;
            if (!p)
            {
                _lastRhinoVersion = TopModLinkPlugIn.Instance.Sync.RhinoVersion;   // drop live-object edits made while paused
                try { _web.ExecuteScript("window.__tmResync && window.__tmResync(); return 'ok';"); } catch { }
            }
            _status.Text = p ? "❚❚ paused — Rhino isn't following TopMod" : "resuming…";
        }

        static bool IsFloating { get { var f = TopModLinkPlugIn.Instance?.Form; return f != null && f.Visible; } }

        void UpdateSeparateLabel()
        {
            if (_separate == null) return;
            _separate.Text = IsFloating ? "⇲ Dock" : "⧉ Separate";
        }

        /// <summary>Docked panel ⇄ separate floating window. The same view (and the loaded model) moves between them.</summary>
        void ToggleSeparate()
        {
            var p = TopModLinkPlugIn.Instance;
            p.OpenedThisSession = true;
            if (IsFloating)
            {
                p.Form.Close();   // Closed handler releases the view
                Rhino.UI.Panels.OpenPanel(TopModPanel.PanelId);
                try
                {
                    var doc = RhinoDoc.ActiveDoc;
                    if (doc != null && Rhino.UI.Panels.GetPanel(TopModPanel.PanelId, doc) is TopModPanel panel && panel.Content == null)
                        panel.Content = Shared(compact: true);
                }
                catch { }
                RhinoApp.RunScript("_TopModWorkspace", false);
            }
            else
            {
                if (Rhino.UI.Panels.IsPanelVisible(TopModPanel.PanelId)) Rhino.UI.Panels.ClosePanel(TopModPanel.PanelId);
                Application.Instance.AsyncInvoke(() =>
                {
                    if (p.Form == null) p.Form = new TopModForm();
                    p.Form.Show();
                    p.Form.BringToFront();
                    UpdateSeparateLabel();
                    RhinoApp.RunScript("_TopModDefaultLayout", false);
                });
            }
            UpdateSeparateLabel();
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
                        if (v > _lastWebVersion && !_paused)
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
                    if (!_paused && root.TryGetProperty("fab", out var fab) && fab.ValueKind == JsonValueKind.Object && doc != null)
                    {
                        try { ApplyFab(doc, sync, fab); } catch (Exception ex) { RhinoApp.WriteLine("TopMod: fab curves — " + ex.Message); }
                    }
                    if (!_paused && root.TryGetProperty("poles", out var pl) && pl.ValueKind == JsonValueKind.Object && doc != null)
                    {
                        try { ApplyPoles(doc, pl); } catch (Exception ex) { RhinoApp.WriteLine("TopMod: field poles — " + ex.Message); }
                    }
                    if (!_paused && root.TryGetProperty("cam", out var cam) && cam.ValueKind == JsonValueKind.Object && doc != null)
                    {
                        try { SyncCamera(doc, cam); } catch { }
                    }
                    _status.Text = _paused ? "❚❚ paused — Rhino isn't following TopMod" : Status(ready, doc, sync, why);
                }

                // ---- Rhino → page ----
                var polesJson = TopModLinkPlugIn.Instance.Poles?.Pull();
                if (polesJson != null && !_paused)
                {
                    try { _web.ExecuteScript("return (window.__tmApplyPoles ? window.__tmApplyPoles(" + JsonSerializer.Serialize(polesJson) + ") : 'not ready');"); } catch { }
                }

                var pull = sync.Pull(_lastRhinoVersion);
                if (pull != null)
                {
                    var (ver, kind, obj) = pull.Value;
                    if (_paused && kind != "import") { _lastRhinoVersion = ver; return; }   // paused: ignore live-object edits (Import still works)
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

        static void ApplyPoles(RhinoDoc doc, JsonElement pl)
        {
            var poles = TopModLinkPlugIn.Instance.Poles; if (poles == null) return;
            if (!pl.TryGetProperty("v", out var v) || v.ValueKind != JsonValueKind.Object) { poles.ApplyFromWeb(doc, null, null); return; }
            string id = v.TryGetProperty("id", out var i) && i.ValueKind == JsonValueKind.String ? i.GetString() : null;
            var pts = new System.Collections.Generic.List<double[]>();
            if (v.TryGetProperty("pts", out var arr) && arr.ValueKind == JsonValueKind.Array)
                foreach (var p in arr.EnumerateArray())
                    if (p.ValueKind == JsonValueKind.Array && p.GetArrayLength() >= 3)
                        pts.Add(new[] { p[0].GetDouble(), p[1].GetDouble(), p[2].GetDouble(), p.GetArrayLength() > 3 ? p[3].GetDouble() : 1 });
            poles.ApplyFromWeb(doc, id, pts);
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
                // three.js: vertical FOV (degrees) over a view of aspect a = w/h.
                // Rhino: the 35 mm lens fits the 24 mm frame side to the viewport's SHORTER side.
                // Fit everything TopMod sees into Rhino's viewport as tightly as possible (same zoom, nothing cropped).
                double tv = Math.Tan(f.GetDouble() * Math.PI / 360.0);                      // TopMod half-height (tan)
                double aT = cam.TryGetProperty("a", out var ta) && ta.GetDouble() > 0 ? ta.GetDouble() : 1.0;
                double th = tv * aT;                                                         // TopMod half-width (tan)
                var sz = vp.Size;
                double aR = sz.Height > 0 ? (double)sz.Width / sz.Height : 1.0;
                double y = Math.Max(tv, th / aR);                                            // Rhino half-height that holds both
                double shortSide = aR < 1 ? y * aR : y;                                      // half-tan along Rhino's shorter side
                double lens = 12.0 / shortSide;
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
