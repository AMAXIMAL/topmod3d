using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace TopModRhinoLink
{
    /// <summary>
    /// Field lines poles as real Rhino point objects (layer TopMod::Field Poles; red = north, blue = south).
    /// TopMod → Rhino: the page reports pole positions whenever they change; the points are created / moved to match.
    /// Rhino → TopMod: moving any pole point (gumball, Move, drag, nudge, undo) sends all pole positions back and
    /// TopMod re-traces the field lines.
    /// </summary>
    public class FieldPoles : IDisposable
    {
        public const string TagKey = "TopModPole";        // value = pole index
        public const string Layer = "TopMod::Field Poles";
        static readonly System.Drawing.Color NorthC = System.Drawing.Color.FromArgb(255, 59, 48);
        static readonly System.Drawing.Color SouthC = System.Drawing.Color.FromArgb(59, 130, 255);

        readonly object _lock = new object();
        bool _suppress;
        string _srcId;                 // which pole set the points belong to ("tool" or "mod<id>")
        List<double[]> _last;          // last positions from the page (TopMod world, inches, Y-up) + q
        string _pending;               // JSON waiting for the page
        int _version, _sentVersion;
        bool _dirty;                   // a pole point was moved in Rhino; gather on idle

        public FieldPoles()
        {
            RhinoDoc.AddRhinoObject += OnAdd;
            RhinoDoc.UndeleteRhinoObject += OnAdd;
            RhinoApp.Idle += OnIdle;
        }

        public void Dispose()
        {
            RhinoDoc.AddRhinoObject -= OnAdd;
            RhinoDoc.UndeleteRhinoObject -= OnAdd;
            RhinoApp.Idle -= OnIdle;
        }

        static bool IsPole(RhinoObject o) => o != null && !string.IsNullOrEmpty(o.Attributes.GetUserString(TagKey));

        // TopMod Y-up inches ⇄ Rhino Z-up doc units (same mapping as the mesh and camera)
        static Point3d ToRhino(double[] p, double s) => new Point3d(p[0] * s, -p[2] * s, p[1] * s);
        static double[] ToTopMod(Point3d p, double s) => new[] { p.X / s, p.Z / s, -p.Y / s };

        List<RhinoObject> Points(RhinoDoc doc)
        {
            var list = new List<RhinoObject>();
            foreach (var o in doc.Objects.GetObjectList(ObjectType.Point)) if (IsPole(o) && !o.IsDeleted) list.Add(o);
            return list;
        }

        /// <summary>Page → Rhino. <paramref name="id"/> null (or no points) = remove the pole points.</summary>
        public void ApplyFromWeb(RhinoDoc doc, string id, List<double[]> pts)
        {
            if (doc == null) return;
            double s = LiveSync.InToDoc(doc), tol = Math.Max(doc.ModelAbsoluteTolerance, 1e-6);
            _suppress = true;
            try
            {
                var existing = Points(doc);
                if (id == null || pts == null || pts.Count == 0)
                {
                    foreach (var o in existing) doc.Objects.Delete(o, true);
                    _srcId = null; _last = null;
                    if (existing.Count > 0) doc.Views.Redraw();
                    return;
                }
                var byIdx = new Dictionary<int, RhinoObject>();
                foreach (var o in existing)
                    if (int.TryParse(o.Attributes.GetUserString(TagKey), out int k) && !byIdx.ContainsKey(k)) byIdx[k] = o;
                    else doc.Objects.Delete(o, true);
                int layer = LiveSync.EnsureLayer(doc, Layer, NorthC);
                for (int i = 0; i < pts.Count; i++)
                {
                    var p = ToRhino(pts[i], s);
                    bool north = pts[i].Length > 3 && pts[i][3] > 0;
                    var col = north ? NorthC : SouthC;
                    string name = (north ? "N" : "S") + " pole " + (i + 1);
                    if (byIdx.TryGetValue(i, out var o))
                    {
                        byIdx.Remove(i);
                        var cur = (o.Geometry as Rhino.Geometry.Point)?.Location ?? Point3d.Unset;
                        if (!cur.IsValid || cur.DistanceTo(p) > tol) doc.Objects.Replace(o.Id, p);
                        if (o.Attributes.ObjectColor != col || o.Attributes.Name != name)
                        {
                            var a = o.Attributes.Duplicate();
                            a.ObjectColor = col; a.ColorSource = ObjectColorSource.ColorFromObject; a.Name = name;
                            doc.Objects.ModifyAttributes(o.Id, a, true);
                        }
                    }
                    else
                    {
                        var a = doc.CreateDefaultAttributes();
                        a.LayerIndex = layer; a.Name = name;
                        a.ObjectColor = col; a.ColorSource = ObjectColorSource.ColorFromObject;
                        a.SetUserString(TagKey, i.ToString(CultureInfo.InvariantCulture));
                        doc.Objects.AddPoint(p, a);
                    }
                }
                foreach (var o in byIdx.Values) doc.Objects.Delete(o, true);   // fewer poles than before
                _srcId = id; _last = pts;
                doc.Views.Redraw();
            }
            finally { _suppress = false; }
        }

        void OnAdd(object sender, RhinoObjectEventArgs e)
        {
            if (_suppress || _srcId == null || !IsPole(e.TheObject)) return;
            _dirty = true;   // a gumball drag fires many adds; gather once on idle
        }

        void OnIdle(object sender, EventArgs e)
        {
            if (!_dirty) return;
            _dirty = false;
            var doc = RhinoDoc.ActiveDoc;
            if (doc == null || _srcId == null || _last == null) return;
            double s = LiveSync.InToDoc(doc);
            var pts = _last.Select(p => (double[])p.Clone()).ToList();
            bool changed = false;
            foreach (var o in Points(doc))
            {
                if (!int.TryParse(o.Attributes.GetUserString(TagKey), out int k) || k < 0 || k >= pts.Count) continue;
                if (!(o.Geometry is Rhino.Geometry.Point gp)) continue;
                var w = ToTopMod(gp.Location, s);
                if (Math.Abs(w[0] - pts[k][0]) + Math.Abs(w[1] - pts[k][1]) + Math.Abs(w[2] - pts[k][2]) > 1e-7) changed = true;
                pts[k] = new[] { w[0], w[1], w[2], pts[k].Length > 3 ? pts[k][3] : 1 };
            }
            if (!changed) return;
            _last = pts;
            var sb = new StringBuilder();
            sb.Append("{\"id\":").Append(System.Text.Json.JsonSerializer.Serialize(_srcId)).Append(",\"pts\":[");
            for (int i = 0; i < pts.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('[').Append(F(pts[i][0])).Append(',').Append(F(pts[i][1])).Append(',').Append(F(pts[i][2])).Append(']');
            }
            sb.Append("]}");
            lock (_lock) { _pending = sb.ToString(); _version++; }
        }

        static string F(double v) => v.ToString("R", CultureInfo.InvariantCulture);

        /// <summary>JSON for window.__tmApplyPoles, or null when Rhino has nothing new.</summary>
        public string Pull()
        {
            lock (_lock)
            {
                if (_pending == null || _sentVersion >= _version) return null;
                _sentVersion = _version;
                return _pending;
            }
        }
    }
}
