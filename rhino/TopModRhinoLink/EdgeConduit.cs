using System;
using System.Collections.Generic;
using System.Drawing;
using Rhino;
using Rhino.Display;
using Rhino.Geometry;

namespace TopModRhinoLink
{
    public enum EdgeMode { Off = 0, Visible = 1, All = 2 }

    /// <summary>
    /// Draws the live TopMod object's polygon edges in the Rhino viewports, the way TopMod outlines its faces.
    ///   Visible — edges hidden behind the model are left out (hidden-line look)
    ///   All     — back edges show through in a light tone, front edges dark on top (X-ray / overlapping)
    /// Works for both the NURBS (Brep) and the streamed mesh form of the live object.
    /// </summary>
    public class EdgeConduit : DisplayConduit
    {
        public static EdgeConduit Instance { get; private set; }
        public EdgeMode Mode { get; private set; }

        static readonly Color Front = Color.FromArgb(22, 22, 26);
        static readonly Color Back = Color.FromArgb(170, 140, 110);

        uint _serial;
        Line[] _lines = Array.Empty<Line>();

        public static void Init(EdgeMode mode)
        {
            Instance = new EdgeConduit();
            Instance.Set(mode, redraw: false);
        }

        public void Set(EdgeMode mode, bool redraw = true)
        {
            Mode = mode;
            Enabled = mode != EdgeMode.Off;
            if (redraw) RhinoDoc.ActiveDoc?.Views.Redraw();
        }

        public EdgeMode Next() => Mode == EdgeMode.Visible ? EdgeMode.All : Mode == EdgeMode.All ? EdgeMode.Off : EdgeMode.Visible;

        protected override void PostDrawObjects(DrawEventArgs e)
        {
            var doc = e.RhinoDoc;
            var sync = TopModLinkPlugIn.Instance?.Sync;
            if (doc == null || sync == null) return;
            var live = sync.FindLive(doc);
            if (live == null || !live.Visible) return;
            if (live.RuntimeSerialNumber != _serial)
            {
                _serial = live.RuntimeSerialNumber;
                _lines = Build(live.Geometry);
            }
            if (_lines.Length == 0) return;

            if (Mode == EdgeMode.All)
            {
                e.Display.EnableDepthTesting(false);
                e.Display.DrawLines(_lines, Back, 1);
                e.Display.EnableDepthTesting(true);
            }
            // front edges, nudged a hair toward the eye so they win the depth test against their own faces
            var cam = e.Viewport.CameraLocation;
            var shown = new Line[_lines.Length];
            for (int i = 0; i < _lines.Length; i++)
                shown[i] = new Line(Nudge(_lines[i].From, cam), Nudge(_lines[i].To, cam));
            e.Display.DrawLines(shown, Front, 1);
        }

        static Point3d Nudge(Point3d p, Point3d cam) => p + (cam - p) * 0.0015;

        static Line[] Build(GeometryBase g)
        {
            var list = new List<Line>();
            switch (g)
            {
                case Brep b:
                    // TopMod's faces are planar polygons: every Brep edge is a straight segment
                    foreach (var ed in b.Edges) list.Add(new Line(ed.PointAtStart, ed.PointAtEnd));
                    break;
                case Mesh m:
                {
                    var seen = new HashSet<long>();
                    var v = m.Vertices;
                    void Add(int a, int c)
                    {
                        if (a == c) return;
                        long k = a < c ? ((long)a << 32) | (uint)c : ((long)c << 32) | (uint)a;
                        if (seen.Add(k)) list.Add(new Line(v[a], v[c]));
                    }
                    // n-gons: only their outer boundary (the triangulation inside a polygon is not an edge)
                    var inNgon = new bool[m.Faces.Count];
                    for (int n = 0; n < m.Ngons.Count; n++)
                    {
                        var ng = m.Ngons[n];
                        foreach (var fi in ng.FaceIndexList()) if (fi < inNgon.Length) inNgon[fi] = true;
                        var bl = ng.BoundaryVertexIndexList();
                        for (int k = 0; k < bl.Length; k++) Add((int)bl[k], (int)bl[(k + 1) % bl.Length]);
                    }
                    for (int i = 0; i < m.Faces.Count; i++)
                    {
                        if (inNgon[i]) continue;
                        var f = m.Faces[i];
                        Add(f.A, f.B); Add(f.B, f.C);
                        if (f.IsQuad) { Add(f.C, f.D); Add(f.D, f.A); } else Add(f.C, f.A);
                    }
                    break;
                }
            }
            return list.ToArray();
        }
    }
}
