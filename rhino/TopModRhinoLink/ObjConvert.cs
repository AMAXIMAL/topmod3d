using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Rhino.Geometry;

namespace TopModRhinoLink
{
    /// <summary>A polygon soup: positions plus n-gon faces (vertex indices, CCW).</summary>
    public class PolyMesh
    {
        public List<Point3d> Verts = new List<Point3d>();
        public List<int[]> Faces = new List<int[]>();
        public bool IsEmpty => Faces.Count == 0;
    }

    public static class ObjConvert
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>Parse OBJ text (v / f lines, n-gons kept). Indices may be v, v/t, v/t/n, or negative.</summary>
        public static PolyMesh Parse(string text)
        {
            var pm = new PolyMesh();
            if (string.IsNullOrEmpty(text)) return pm;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length < 2) continue;
                if (line[0] == 'v' && line[1] == ' ')
                {
                    var t = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (t.Length < 4) continue;
                    pm.Verts.Add(new Point3d(
                        double.Parse(t[1], Inv), double.Parse(t[2], Inv), double.Parse(t[3], Inv)));
                }
                else if (line[0] == 'f' && line[1] == ' ')
                {
                    var t = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    var f = new List<int>(t.Length - 1);
                    for (int i = 1; i < t.Length; i++)
                    {
                        var s = t[i];
                        int slash = s.IndexOf('/');
                        if (slash >= 0) s = s.Substring(0, slash);
                        if (!int.TryParse(s, NumberStyles.Integer, Inv, out int idx)) continue;
                        idx = idx < 0 ? pm.Verts.Count + idx : idx - 1;
                        if (idx >= 0 && idx < pm.Verts.Count) f.Add(idx);
                    }
                    if (f.Count >= 3) pm.Faces.Add(f.ToArray());
                }
            }
            return pm;
        }

        public static string Write(PolyMesh pm, string name = "rhino")
        {
            var sb = new StringBuilder();
            sb.Append("# TopMod Rhino Link\no ").Append(name).Append('\n');
            foreach (var v in pm.Verts)
                sb.Append("v ").Append(v.X.ToString("0.######", Inv)).Append(' ')
                  .Append(v.Y.ToString("0.######", Inv)).Append(' ')
                  .Append(v.Z.ToString("0.######", Inv)).Append('\n');
            foreach (var f in pm.Faces)
            {
                sb.Append('f');
                foreach (var i in f) sb.Append(' ').Append(i + 1);
                sb.Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>TopMod (three.js) is Y-up, Rhino is Z-up: Rhino (X,Y,Z) = TopMod (x, -z, y). A proper rotation, so winding is unchanged.</summary>
        public static PolyMesh YupToZup(PolyMesh pm)
        {
            var o = new PolyMesh { Faces = pm.Faces };
            foreach (var v in pm.Verts) o.Verts.Add(new Point3d(v.X, -v.Z, v.Y));
            return o;
        }

        /// <summary>Inverse of <see cref="YupToZup"/>: TopMod (x,y,z) = Rhino (X, Z, -Y).</summary>
        public static PolyMesh ZupToYup(PolyMesh pm)
        {
            var o = new PolyMesh { Faces = pm.Faces };
            foreach (var v in pm.Verts) o.Verts.Add(new Point3d(v.X, v.Z, -v.Y));
            return o;
        }

        /// <summary>Scale every vertex (TopMod units are inches; Rhino docs can be anything).</summary>
        public static PolyMesh Scaled(PolyMesh pm, double s)
        {
            if (Math.Abs(s - 1.0) < 1e-12) return pm;
            var o = new PolyMesh { Faces = pm.Faces };
            foreach (var v in pm.Verts) o.Verts.Add(v * s);
            return o;
        }

        /// <summary>Polygon soup → Rhino mesh with n-gons preserved (used for Bake-as-mesh and as a Brep source).</summary>
        public static Mesh ToRhinoMesh(PolyMesh pm)
        {
            var m = new Mesh();
            foreach (var v in pm.Verts) m.Vertices.Add(v);
            foreach (var f in pm.Faces)
            {
                if (f.Length == 3) m.Faces.AddFace(f[0], f[1], f[2]);
                else if (f.Length == 4) m.Faces.AddFace(f[0], f[1], f[2], f[3]);
                else
                {
                    // fan-triangulate, then register the n-gon so the mesh remembers the polygon
                    var tris = new List<int>();
                    for (int i = 1; i < f.Length - 1; i++)
                        tris.Add(m.Faces.AddFace(f[0], f[i], f[i + 1]));
                    m.Ngons.AddNgon(MeshNgon.Create(f, tris));
                }
            }
            m.Normals.ComputeNormals();
            m.Compact();
            return m;
        }

        /// <summary>Polygon soup → Brep: one planar (trimmed NURBS) face per polygon, joined.</summary>
        public static Brep ToBrep(PolyMesh pm, double tol)
        {
            var faces = new List<Brep>();
            foreach (var f in pm.Faces)
            {
                var pts = new List<Point3d>(f.Length + 1);
                foreach (var i in f) pts.Add(pm.Verts[i]);
                pts.Add(pm.Verts[f[0]]);
                var pl = new Polyline(pts);
                if (!pl.IsValid) continue;
                Brep b = null;
                if (f.Length == 3 || f.Length == 4)
                {
                    // exact bilinear/planar patch, no trimming needed
                    var srf = f.Length == 3
                        ? NurbsSurface.CreateFromCorners(pts[0], pts[1], pts[2])
                        : NurbsSurface.CreateFromCorners(pts[0], pts[1], pts[2], pts[3]);
                    if (srf != null) b = srf.ToBrep();
                }
                if (b == null)
                {
                    var crv = pl.ToNurbsCurve();
                    var arr = Brep.CreatePlanarBreps(crv, tol);
                    if (arr != null && arr.Length > 0) b = arr[0];
                    else
                    {
                        // non-planar n-gon: patch it
                        var patch = Brep.CreatePatch(new GeometryBase[] { crv }, 3, 3, tol);
                        if (patch != null) b = patch;
                    }
                }
                if (b == null) continue;
                // orient the face normal with the polygon winding
                var n = PolygonNormal(pts, f.Length);
                var mid = Point3d.Origin;
                for (int i = 0; i < f.Length; i++) mid += pts[i];
                mid /= f.Length;
                var face = b.Faces[0];
                face.ClosestPoint(mid, out double u, out double v);
                if (face.NormalAt(u, v) * n < 0) b.Flip();
                faces.Add(b);
            }
            if (faces.Count == 0) return null;
            var joined = Brep.JoinBreps(faces, tol);
            if (joined == null || joined.Length == 0) return null;
            Brep result = joined[0];
            if (joined.Length > 1)
            {
                result = new Brep();
                foreach (var j in joined) result.Append(j);
            }
            if (result.IsSolid && result.SolidOrientation == BrepSolidOrientation.Inward) result.Flip();
            result.Compact();
            return result;
        }

        static Vector3d PolygonNormal(IList<Point3d> p, int n)
        {
            // Newell's method
            var nv = Vector3d.Zero;
            for (int i = 0; i < n; i++)
            {
                var a = p[i]; var b = p[(i + 1) % n];
                nv.X += (a.Y - b.Y) * (a.Z + b.Z);
                nv.Y += (a.Z - b.Z) * (a.X + b.X);
                nv.Z += (a.X - b.X) * (a.Y + b.Y);
            }
            nv.Unitize();
            return nv;
        }

        /// <summary>Rhino mesh → polygon soup (n-gons preserved, vertices welded).</summary>
        public static PolyMesh FromRhinoMesh(Mesh m, double tol)
        {
            var pm = new PolyMesh();
            var w = new VertexWelder(pm, tol);
            var covered = new HashSet<int>();
            for (int k = 0; k < m.Ngons.Count; k++)
            {
                var ng = m.Ngons[k];
                var bv = ng.BoundaryVertexIndexList();
                var f = new int[bv.Length];
                for (int i = 0; i < bv.Length; i++) f[i] = w.Add(m.Vertices[(int)bv[i]]);
                pm.Faces.Add(f);
                foreach (var fi in ng.FaceIndexList()) covered.Add((int)fi);
            }
            for (int fi = 0; fi < m.Faces.Count; fi++)
            {
                if (covered.Contains(fi)) continue;
                var mf = m.Faces[fi];
                int[] f = mf.IsQuad
                    ? new[] { w.Add(m.Vertices[mf.A]), w.Add(m.Vertices[mf.B]), w.Add(m.Vertices[mf.C]), w.Add(m.Vertices[mf.D]) }
                    : new[] { w.Add(m.Vertices[mf.A]), w.Add(m.Vertices[mf.B]), w.Add(m.Vertices[mf.C]) };
                if (Degenerate(f)) continue;
                pm.Faces.Add(f);
            }
            return pm;
        }

        /// <summary>
        /// Brep → polygon soup. Every face whose outer loop is a closed polyline becomes one n-gon
        /// (this is what TopMod-made breps look like, even after moving/scaling/point-editing in Rhino).
        /// Any other face (curved edges, inner loops) is meshed and its quads/tris are appended.
        /// </summary>
        public static PolyMesh FromBrep(Brep brep, double tol)
        {
            var pm = new PolyMesh();
            var w = new VertexWelder(pm, tol);
            var leftovers = new List<BrepFace>();
            foreach (var face in brep.Faces)
            {
                Polyline pl = null;
                if (face.Loops.Count == 1)
                {
                    var crv = face.OuterLoop.To3dCurve();
                    if (crv != null && crv.IsClosed && crv.TryGetPolyline(out var p) && p.Count >= 4) pl = p;
                }
                if (pl == null) { leftovers.Add(face); continue; }
                int n = pl.Count - 1;
                var pts = new List<Point3d>(n);
                for (int i = 0; i < n; i++) pts.Add(pl[i]);
                // make winding agree with the (oriented) face normal
                var fn = PolygonNormal(pts, n);
                face.ClosestPoint(pl.CenterPoint(), out double u, out double v);
                var sn = face.NormalAt(u, v);
                if (face.OrientationIsReversed) sn.Reverse();
                if (fn * sn < 0) pts.Reverse();
                var f = new int[n];
                for (int i = 0; i < n; i++) f[i] = w.Add(pts[i]);
                f = Dedupe(f);
                if (f.Length >= 3) pm.Faces.Add(f);
            }
            if (leftovers.Count > 0)
            {
                var mp = new MeshingParameters(0.5) { SimplePlanes = true, JaggedSeams = false, RefineGrid = false, GridMinCount = 1 };
                foreach (var face in leftovers)
                {
                    var ms = Mesh.CreateFromBrep(face.DuplicateFace(false), mp);
                    if (ms == null) continue;
                    foreach (var m in ms)
                    {
                        var sub = FromRhinoMesh(m, tol);
                        foreach (var f in sub.Faces)
                        {
                            var g = new int[f.Length];
                            for (int i = 0; i < f.Length; i++) g[i] = w.Add(sub.Verts[f[i]]);
                            g = Dedupe(g);
                            if (g.Length >= 3) pm.Faces.Add(g);
                        }
                    }
                }
            }
            return pm;
        }

        static int[] Dedupe(int[] f)
        {
            var o = new List<int>(f.Length);
            for (int i = 0; i < f.Length; i++)
                if (o.Count == 0 || o[o.Count - 1] != f[i]) o.Add(f[i]);
            if (o.Count > 1 && o[0] == o[o.Count - 1]) o.RemoveAt(o.Count - 1);
            return o.ToArray();
        }

        static bool Degenerate(int[] f)
        {
            for (int i = 0; i < f.Length; i++)
                for (int j = i + 1; j < f.Length; j++)
                    if (f[i] == f[j]) return true;
            return false;
        }

        /// <summary>Welds coincident points into a shared vertex list (grid hash, O(1) per point).</summary>
        class VertexWelder
        {
            readonly PolyMesh _pm; readonly double _tol; readonly double _cell;
            readonly Dictionary<(long, long, long), List<int>> _grid = new Dictionary<(long, long, long), List<int>>();
            public VertexWelder(PolyMesh pm, double tol) { _pm = pm; _tol = Math.Max(tol, 1e-9); _cell = _tol * 4; }
            public int Add(Point3d p)
            {
                long cx = (long)Math.Floor(p.X / _cell), cy = (long)Math.Floor(p.Y / _cell), cz = (long)Math.Floor(p.Z / _cell);
                for (long dx = -1; dx <= 1; dx++) for (long dy = -1; dy <= 1; dy++) for (long dz = -1; dz <= 1; dz++)
                    if (_grid.TryGetValue((cx + dx, cy + dy, cz + dz), out var lst))
                        foreach (var i in lst) if (_pm.Verts[i].DistanceTo(p) <= _tol) return i;
                int idx = _pm.Verts.Count; _pm.Verts.Add(p);
                if (!_grid.TryGetValue((cx, cy, cz), out var mine)) _grid[(cx, cy, cz)] = mine = new List<int>();
                mine.Add(idx);
                return idx;
            }
        }
    }
}
