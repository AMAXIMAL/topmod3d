using System;
using System.Collections.Generic;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace TopModRhinoLink
{
    /// <summary>
    /// Owns the single "live" object that mirrors the TopMod model, and watches it for Rhino-side edits.
    /// Web → Rhino: <see cref="ApplyFromWeb"/> replaces the live object's geometry (as a NURBS Brep).
    /// Rhino → Web: any replace of the live object (gumball, point edit, Scale, Rotate, SubD-ish tweaks…)
    /// bumps <see cref="RhinoVersion"/> and queues an OBJ the page picks up on its next poll.
    /// </summary>
    public class LiveSync
    {
        public const string TagKey = "TopModLive";
        public const string FabTagKey = "TopModFab";
        // layer tree:  TopMod / Live, Baked / Bake NN, Fab / Assembled, Flat, Net Cut, Net Fold
        public const string RootLayer = "TopMod";
        public const string LiveLayer = "TopMod::Live";
        public const string BakeLayer = "TopMod::Baked";
        public const string FabLayer = "TopMod::Fab";
        static readonly System.Drawing.Color CyanC = System.Drawing.Color.FromArgb(0, 229, 255);
        static readonly System.Drawing.Color OrangeC = System.Drawing.Color.FromArgb(255, 140, 0);
        static readonly System.Drawing.Color YellowC = System.Drawing.Color.FromArgb(255, 214, 0);
        static readonly System.Drawing.Color GreyC = System.Drawing.Color.FromArgb(120, 130, 140);
        public bool LiveIsMesh { get; private set; }

        readonly object _lock = new object();
        Guid _liveId = Guid.Empty;
        bool _suppress;                 // true while we are the ones modifying the live object
        int _rhinoVersion;              // bumps on every Rhino-side edit or Import request
        string _pendingObj;             // OBJ waiting for the page (Rhino units already converted to inches)
        string _pendingKind = "sync";   // "sync" (replace active layer) or "import" (add a layer)
        public int WebPushes { get; private set; }
        public int RhinoEdits { get; private set; }
        public DateTime LastActivity { get; private set; } = DateTime.MinValue;

        public int RhinoVersion { get { lock (_lock) return _rhinoVersion; } }
        public Guid LiveId => _liveId;

        public LiveSync()
        {
            RhinoDoc.ReplaceRhinoObject += OnReplace;
            RhinoDoc.AddRhinoObject += OnAdd;
            RhinoDoc.DeleteRhinoObject += OnDelete;
            RhinoDoc.UndeleteRhinoObject += OnUndelete;
            RhinoDoc.EndOpenDocument += (s, e) => _liveId = Guid.Empty;
            RhinoDoc.NewDocument += (s, e) => _liveId = Guid.Empty;
        }

        public void Dispose()
        {
            RhinoDoc.ReplaceRhinoObject -= OnReplace;
            RhinoDoc.AddRhinoObject -= OnAdd;
            RhinoDoc.DeleteRhinoObject -= OnDelete;
            RhinoDoc.UndeleteRhinoObject -= OnUndelete;
        }

        // ---------------- units ----------------
        /// <summary>TopMod works in inches (1 unit = 1 in). Factor that takes inches → doc units.</summary>
        public static double InToDoc(RhinoDoc doc) => RhinoMath.UnitScale(UnitSystem.Inches, doc.ModelUnitSystem);
        public static double DocToIn(RhinoDoc doc) => RhinoMath.UnitScale(doc.ModelUnitSystem, UnitSystem.Inches);

        // ---------------- web → rhino ----------------
        /// <summary>Replace (or create) the live object from OBJ text sent by the page. Must run on the UI thread.</summary>
        public void ApplyFromWeb(RhinoDoc doc, string objText, string mode = "nurbs")
        {
            var pm = ObjConvert.Scaled(ObjConvert.YupToZup(ObjConvert.Parse(objText)), InToDoc(doc));
            if (pm.IsEmpty) return;
            var tol = doc.ModelAbsoluteTolerance;
            // "mesh" while a simulation streams or a Fabrication tool is on (fast); NURBS once it settles / on Bake
            GeometryBase geo = mode == "mesh" ? (GeometryBase)ObjConvert.ToRhinoMesh(pm) : ObjConvert.ToBrep(pm, tol);
            if (geo == null) geo = ObjConvert.ToRhinoMesh(pm);   // never drop a push
            LiveIsMesh = geo is Mesh;

            _suppress = true;
            try
            {
                var live = FindLive(doc);
                if (live != null)
                {
                    // keep id, attributes, layer; swap geometry
                    bool ok = geo is Brep b ? doc.Objects.Replace(live.Id, b) : doc.Objects.Replace(live.Id, (Mesh)geo);
                    if (!ok) { doc.Objects.Delete(live, true); live = null; }
                }
                if (live == null)
                {
                    var attr = doc.CreateDefaultAttributes();
                    attr.LayerIndex = EnsureLayer(doc, LiveLayer, CyanC);
                    attr.Name = "TopMod (live)";
                    attr.SetUserString(TagKey, "1");
                    _liveId = geo is Brep b2 ? doc.Objects.AddBrep(b2, attr) : doc.Objects.AddMesh((Mesh)geo, attr);
                }
                WebPushes++; LastActivity = DateTime.Now;
                doc.Views.Redraw();
            }
            finally { _suppress = false; }
        }

        // ---------------- rhino → web ----------------
        bool _replacing;   // set by ReplaceRhinoObject on the live object; the following AddRhinoObject carries the new geometry

        void OnReplace(object sender, RhinoReplaceObjectEventArgs e)
        {
            if (_suppress || !IsLive(e.OldRhinoObject)) return;
            _replacing = true;
        }

        // Rhino fires Replace → Delete(old) → Add(new, same id). Gumball, Move/Scale/Rotate, PointsOn drags,
        // and Undo/Redo all land here with the finished geometry, which is the reliable place to read it.
        void OnAdd(object sender, RhinoObjectEventArgs e)
        {
            if (_suppress) return;
            bool mine = _replacing && (e.ObjectId == _liveId || IsLive(e.TheObject));
            _replacing = false;
            if (!mine && !IsLive(e.TheObject)) return;
            var geo = e.TheObject?.Geometry;
            if (geo == null) return;
            _liveId = e.ObjectId;
            QueueFromRhino(e.TheObject.Document, geo, "sync");
            RhinoEdits++; LastActivity = DateTime.Now;
        }

        void OnDelete(object sender, RhinoObjectEventArgs e)
        {
            if (_suppress) return;
            if (e.ObjectId == _liveId) _liveId = Guid.Empty;   // user deleted it: the next web push re-creates it
        }

        void OnUndelete(object sender, RhinoObjectEventArgs e)
        {
            if (_suppress || !IsLive(e.TheObject)) return;
            _liveId = e.ObjectId;
            var geo = e.TheObject?.Geometry; if (geo == null) return;
            QueueFromRhino(e.TheObject.Document, geo, "sync");
            RhinoEdits++; LastActivity = DateTime.Now;
        }

        /// <summary>Convert Rhino geometry to an OBJ in inches and hand it to the page on its next poll.</summary>
        public void QueueFromRhino(RhinoDoc doc, GeometryBase geo, string kind)
        {
            var tol = doc.ModelAbsoluteTolerance;
            PolyMesh pm = null;
            switch (geo)
            {
                case Brep b: pm = ObjConvert.FromBrep(b, tol); break;
                case Mesh m: pm = ObjConvert.FromRhinoMesh(m, tol); break;
                case Extrusion ex: pm = ObjConvert.FromBrep(ex.ToBrep(true), tol); break;
                case Surface s: pm = ObjConvert.FromBrep(s.ToBrep(), tol); break;
                case SubD sd: { var sm = Mesh.CreateFromSubD(sd, 0); if (sm != null) pm = ObjConvert.FromRhinoMesh(sm, tol); break; }
            }
            if (pm == null || pm.IsEmpty) return;
            QueueObj(ToTopModObj(pm, doc, kind == "import" ? "rhino_import" : "rhino"), kind);
        }

        /// <summary>Rhino polygon soup (doc units, Z-up) → OBJ text for TopMod (inches, Y-up).</summary>
        public static string ToTopModObj(PolyMesh pm, RhinoDoc doc, string name)
            => ObjConvert.Write(ObjConvert.ZupToYup(ObjConvert.Scaled(pm, DocToIn(doc))), name);

        public void QueueObj(string obj, string kind)
        {
            lock (_lock) { _pendingObj = obj; _pendingKind = kind; _rhinoVersion++; }
        }

        /// <summary>Page polls with the last version it saw; returns null when nothing changed.</summary>
        public (int version, string kind, string obj)? Pull(int sinceVersion)
        {
            lock (_lock)
            {
                if (_pendingObj == null || sinceVersion >= _rhinoVersion) return null;
                return (_rhinoVersion, _pendingKind, _pendingObj);
            }
        }

        // ---------------- bake ----------------
        /// <summary>Copy the live object to the bake layer as an ordinary, untracked object. Returns the new id.</summary>
        public Guid Bake(RhinoDoc doc, bool asMesh)
        {
            var live = FindLive(doc);
            if (live == null) return Guid.Empty;
            // each bake gets its own sub-layer: TopMod / Baked / Bake 01, Bake 02 …
            int n = 1;
            while (doc.Layers.FindByFullPath(BakeLayer + "::Bake " + n.ToString("00"), -1) >= 0) n++;
            string bakeName = "Bake " + n.ToString("00");
            int bakeIdx = EnsureLayer(doc, BakeLayer + "::" + bakeName, OrangeC);

            var attr = live.Attributes.Duplicate();
            attr.LayerIndex = bakeIdx;
            attr.DeleteUserString(TagKey);
            attr.Name = "TopMod " + bakeName + " " + DateTime.Now.ToString("HH:mm:ss");
            Guid id = Guid.Empty;
            var geo = live.Geometry.Duplicate();
            var tol = doc.ModelAbsoluteTolerance;
            if (asMesh)
            {
                Mesh mesh = geo as Mesh;
                if (mesh == null && geo is Brep b) mesh = ObjConvert.ToRhinoMesh(ObjConvert.FromBrep(b, tol));
                if (mesh != null) id = doc.Objects.AddMesh(mesh, attr);
            }
            else
            {
                Brep brep = geo as Brep;
                if (brep == null && geo is Mesh m) brep = ObjConvert.ToBrep(ObjConvert.FromRhinoMesh(m, tol), tol);   // streamed mesh → NURBS on bake
                if (brep != null) id = doc.Objects.AddBrep(brep, attr);
                else if (geo is Mesh m2) id = doc.Objects.AddMesh(m2, attr);
            }
            // freeze the live fabrication curves too, into matching sub-layers of this bake
            int copied = 0;
            foreach (var o in doc.Objects)
            {
                if (o.Attributes.GetUserString(FabTagKey) != "1") continue;
                var src = doc.Layers[o.Attributes.LayerIndex];
                var a = o.Attributes.Duplicate();
                a.DeleteUserString(FabTagKey);
                a.LayerIndex = EnsureLayer(doc, BakeLayer + "::" + bakeName + "::" + src.Name, src.Color);
                if (doc.Objects.Add(o.Geometry.Duplicate(), a) != Guid.Empty) copied++;
            }
            if (copied > 0) RhinoApp.WriteLine("TopMod: baked {0} fabrication curves.", copied);
            doc.Views.Redraw();
            return id;
        }

        // ---------------- fabrication → curves ----------------
        /// <summary>
        /// Mirror TopMod's fabrication output as curves: closed loops in their assembled 3D position and in the
        /// flat nested layout, plus papercraft cut/fold lines. Replaced wholesale on every change; Bake freezes them.
        /// </summary>
        public void ApplyFab(RhinoDoc doc, bool on, string mode, List<(List<Point3d> src, List<Point3d> dst)> loops, string netObj)
        {
            _suppress = true;
            try
            {
                // clear the previous live fab curves
                var old = new List<Guid>();
                foreach (var o in doc.Objects) if (o.Attributes.GetUserString(FabTagKey) == "1") old.Add(o.Id);
                foreach (var id in old) doc.Objects.Delete(id, true);
                if (!on) { doc.Views.Redraw(); return; }

                double s = InToDoc(doc);
                Point3d Conv(Point3d p) => new Point3d(p.X * s, -p.Z * s, p.Y * s);   // TopMod Y-up inches → Rhino Z-up doc units
                int iAsm = EnsureLayer(doc, FabLayer + "::Assembled", YellowC);
                int iFlat = EnsureLayer(doc, FabLayer + "::Flat", OrangeC);
                int k = 0;
                foreach (var (src, dst) in loops)
                {
                    k++;
                    AddLoop(doc, src, Conv, iAsm, (mode ?? "fab") + " " + k);
                    AddLoop(doc, dst, Conv, iFlat, (mode ?? "fab") + " flat " + k);
                }
                if (!string.IsNullOrEmpty(netObj))
                {
                    var pm = ObjConvert.Scaled(ObjConvert.YupToZup(ObjConvert.Parse(netObj)), s);
                    if (!pm.IsEmpty)
                    {
                        int iCut = EnsureLayer(doc, FabLayer + "::Net Cut", System.Drawing.Color.FromArgb(230, 60, 60));
                        int iFold = EnsureLayer(doc, FabLayer + "::Net Fold", GreyC);
                        // edge use-count: 1 = boundary (cut), 2 = shared (fold)
                        var count = new Dictionary<(int, int), int>();
                        foreach (var f in pm.Faces)
                            for (int i = 0; i < f.Length; i++)
                            {
                                int a = f[i], b = f[(i + 1) % f.Length];
                                var key = a < b ? (a, b) : (b, a);
                                count[key] = count.TryGetValue(key, out var c) ? c + 1 : 1;
                            }
                        foreach (var kv in count)
                        {
                            var ln = new Line(pm.Verts[kv.Key.Item1], pm.Verts[kv.Key.Item2]);
                            if (ln.Length < doc.ModelAbsoluteTolerance) continue;
                            var a = doc.CreateDefaultAttributes();
                            a.LayerIndex = kv.Value == 1 ? iCut : iFold;
                            a.SetUserString(FabTagKey, "1");
                            doc.Objects.AddLine(ln, a);
                        }
                    }
                }
                doc.Views.Redraw();
            }
            finally { _suppress = false; }
        }

        static void AddLoop(RhinoDoc doc, List<Point3d> pts, Func<Point3d, Point3d> conv, int layerIndex, string name)
        {
            if (pts == null || pts.Count < 2) return;
            var pl = new Polyline();
            foreach (var p in pts) pl.Add(conv(p));
            if (pl.Count > 2 && pl[0].DistanceTo(pl[pl.Count - 1]) > doc.ModelAbsoluteTolerance) pl.Add(pl[0]);   // close it
            var a = doc.CreateDefaultAttributes();
            a.LayerIndex = layerIndex;
            a.Name = name;
            a.SetUserString(FabTagKey, "1");
            doc.Objects.AddPolyline(pl, a);
        }

        // ---------------- helpers ----------------
        public RhinoObject FindLive(RhinoDoc doc)
        {
            if (_liveId != Guid.Empty)
            {
                var o = doc.Objects.FindId(_liveId);
                if (o != null && !o.IsDeleted) return o;
            }
            foreach (var o in doc.Objects)
            {
                if (IsLive(o)) { _liveId = o.Id; return o; }
            }
            _liveId = Guid.Empty;
            return null;
        }

        bool IsLive(RhinoObject o)
        {
            if (o == null) return false;
            if (o.Id == _liveId) return true;
            return o.Attributes.GetUserString(TagKey) == "1";
        }

        /// <summary>Find or create a layer by full path ("TopMod::Fab::Flat"), creating parents as needed.</summary>
        public static int EnsureLayer(RhinoDoc doc, string fullPath, System.Drawing.Color color)
        {
            int i = doc.Layers.FindByFullPath(fullPath, -1);
            if (i >= 0) return i;
            var parts = fullPath.Split(new[] { "::" }, StringSplitOptions.None);
            Guid parentId = Guid.Empty;
            string path = "";
            for (int k = 0; k < parts.Length; k++)
            {
                path = k == 0 ? parts[0] : path + "::" + parts[k];
                int idx = doc.Layers.FindByFullPath(path, -1);
                if (idx < 0)
                {
                    var layer = new Layer { Name = parts[k], Color = k == parts.Length - 1 ? color : GreyC };
                    if (parentId != Guid.Empty) layer.ParentLayerId = parentId;
                    idx = doc.Layers.Add(layer);
                }
                parentId = doc.Layers[idx].Id;
                i = idx;
            }
            return i;
        }
    }
}
