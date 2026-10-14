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
        public const string LiveLayer = "TopMod Live";
        public const string BakeLayer = "TopMod Baked";

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
        public void ApplyFromWeb(RhinoDoc doc, string objText)
        {
            var pm = ObjConvert.Scaled(ObjConvert.YupToZup(ObjConvert.Parse(objText)), InToDoc(doc));
            if (pm.IsEmpty) return;
            var tol = doc.ModelAbsoluteTolerance;
            GeometryBase geo = ObjConvert.ToBrep(pm, tol);
            if (geo == null) geo = ObjConvert.ToRhinoMesh(pm);   // should not happen, but never drop a push

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
                    attr.LayerIndex = EnsureLayer(doc, LiveLayer, System.Drawing.Color.FromArgb(0, 229, 255));
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
            var attr = live.Attributes.Duplicate();
            attr.LayerIndex = EnsureLayer(doc, BakeLayer, System.Drawing.Color.FromArgb(255, 140, 0));
            attr.DeleteUserString(TagKey);
            attr.Name = "TopMod bake " + DateTime.Now.ToString("HH:mm:ss");
            Guid id = Guid.Empty;
            var geo = live.Geometry.Duplicate();
            if (asMesh)
            {
                Mesh mesh = geo as Mesh;
                if (mesh == null && geo is Brep b)
                {
                    var pm = ObjConvert.FromBrep(b, doc.ModelAbsoluteTolerance);
                    mesh = ObjConvert.ToRhinoMesh(pm);
                }
                if (mesh != null) id = doc.Objects.AddMesh(mesh, attr);
            }
            else
            {
                if (geo is Brep b) id = doc.Objects.AddBrep(b, attr);
                else if (geo is Mesh m) id = doc.Objects.AddMesh(m, attr);
            }
            doc.Views.Redraw();
            return id;
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

        public static int EnsureLayer(RhinoDoc doc, string name, System.Drawing.Color color)
        {
            int i = doc.Layers.FindByFullPath(name, -1);
            if (i >= 0) return i;
            var layer = new Layer { Name = name, Color = color };
            return doc.Layers.Add(layer);
        }
    }
}
