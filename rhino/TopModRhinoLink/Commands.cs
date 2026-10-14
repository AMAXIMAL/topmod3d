using System;
using System.Collections.Generic;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.Input;
using Rhino.Input.Custom;

namespace TopModRhinoLink
{
    /// <summary>TopMod — open TopMod as a dockable Rhino panel (toggle).</summary>
    public class TopModCommand : Command
    {
        public override string EnglishName => "TopMod";
        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            var id = TopModPanel.PanelId;
            var p = TopModLinkPlugIn.Instance;
            if (Rhino.UI.Panels.IsPanelVisible(id)) { Rhino.UI.Panels.ClosePanel(id); return Result.Success; }
            Rhino.UI.Panels.OpenPanel(id);
            if (!p.WorkspaceApplied)
            {
                // first run: set up the TopMod workspace (panel docked right, half width, single Perspective view)
                p.WorkspaceApplied = true;
                Eto.Forms.Application.Instance.AsyncInvoke(() => RhinoApp.RunScript("_TopModWorkspace", false));
                RhinoApp.WriteLine("TopMod workspace applied. Run TopModWorkspace any time to restore it.");
            }
            return Result.Success;
        }
    }

    /// <summary>TopModDefaultLayout — back to Rhino's default window layout and four viewports.</summary>
    [CommandStyle(Style.ScriptRunner)]
    public class TopModDefaultLayoutCommand : Command
    {
        public override string EnglishName => "TopModDefaultLayout";
        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            RhinoApp.RunScript("_-WindowLayout \"Default Window Layout\"", false);
            if (doc != null)
            {
                foreach (var v in doc.Views) if (v.Maximized) v.Maximized = false;
                doc.Views.Redraw();
            }
            return Result.Success;
        }
    }

    /// <summary>TopModWorkspace — restore the TopMod window layout (panel docked right, half width) and maximize Perspective.</summary>
    [CommandStyle(Style.ScriptRunner)]
    public class TopModWorkspaceCommand : Command
    {
        public override string EnglishName => "TopModWorkspace";
        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            var p = TopModLinkPlugIn.Instance;
            if (!Rhino.UI.Panels.IsPanelVisible(TopModPanel.PanelId)) Rhino.UI.Panels.OpenPanel(TopModPanel.PanelId);
            bool ok = p.ApplyWorkspace(doc);
            p.WorkspaceApplied = true;
            if (!ok) RhinoApp.WriteLine("No embedded window layout; maximized Perspective only. Save one with SaveWindowLayout as \"TopMod\".");
            return Result.Success;
        }
    }

    /// <summary>TopModWindow — open TopMod in a separate floating window instead of the panel.</summary>
    public class TopModWindowCommand : Command
    {
        public override string EnglishName => "TopModWindow";
        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            var p = TopModLinkPlugIn.Instance;
            if (p.Form == null) p.Form = new TopModForm();
            p.Form.Show();
            p.Form.BringToFront();
            return Result.Success;
        }
    }

    /// <summary>TopModBake — copy the live model to the 'TopMod Baked' layer.</summary>
    public class TopModBakeCommand : Command
    {
        public override string EnglishName => "TopModBake";
        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            var go = new GetOption();
            go.SetCommandPrompt("Bake the live TopMod model");
            var asMesh = new OptionToggle(false, "NURBS", "Mesh");
            go.AddOptionToggle("As", ref asMesh);
            go.AcceptNothing(true);
            while (true)
            {
                var r = go.Get();
                if (r == GetResult.Option) continue;
                if (r == GetResult.Cancel) return Result.Cancel;
                break;
            }
            var id = TopModLinkPlugIn.Instance.Sync.Bake(doc, asMesh.CurrentValue);
            if (id == Guid.Empty) { RhinoApp.WriteLine("Nothing to bake — push a model from TopMod first."); return Result.Nothing; }
            doc.Objects.UnselectAll();
            doc.Objects.Select(id);
            RhinoApp.WriteLine("Baked to layer '{0}'.", LiveSync.BakeLayer);
            return Result.Success;
        }
    }

    /// <summary>TopModImport — pick Rhino objects and send them to TopMod as a new layer.</summary>
    public class TopModImportCommand : Command
    {
        public override string EnglishName => "TopModImport";
        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            var go = new GetObject();
            go.SetCommandPrompt("Select meshes, polysurfaces, surfaces or SubDs to send to TopMod");
            go.GeometryFilter = ObjectType.Mesh | ObjectType.Brep | ObjectType.Surface | ObjectType.PolysrfFilter | ObjectType.SubD | ObjectType.Extrusion;
            go.GroupSelect = true; go.SubObjectSelect = false;
            go.GetMultiple(1, 0);
            if (go.CommandResult() != Result.Success) return go.CommandResult();

            var tol = doc.ModelAbsoluteTolerance;
            var merged = new PolyMesh();
            foreach (var objref in go.Objects())
            {
                var geo = objref.Geometry();
                if (objref.Object() != null && objref.Object().Attributes.GetUserString(LiveSync.TagKey) == "1")
                {
                    RhinoApp.WriteLine("Skipping the live TopMod object (it is already in TopMod).");
                    continue;
                }
                PolyMesh pm = null;
                switch (geo)
                {
                    case Mesh m: pm = ObjConvert.FromRhinoMesh(m, tol); break;
                    case Brep b: pm = ObjConvert.FromBrep(b, tol); break;
                    case Extrusion ex: pm = ObjConvert.FromBrep(ex.ToBrep(true), tol); break;
                    case Surface s: pm = ObjConvert.FromBrep(s.ToBrep(), tol); break;
                    case SubD sd: { var sm = Mesh.CreateFromSubD(sd, 0); if (sm != null) pm = ObjConvert.FromRhinoMesh(sm, tol); break; }
                }
                if (pm == null || pm.IsEmpty) continue;
                int off = merged.Verts.Count;
                merged.Verts.AddRange(pm.Verts);
                foreach (var f in pm.Faces) { var g = new int[f.Length]; for (int i = 0; i < f.Length; i++) g[i] = f[i] + off; merged.Faces.Add(g); }
            }
            if (merged.IsEmpty) { RhinoApp.WriteLine("Nothing convertible was selected."); return Result.Nothing; }

            var sync = TopModLinkPlugIn.Instance.Sync;
            sync.QueueObj(LiveSync.ToTopModObj(merged, doc, "rhino_import"), "import");
            RhinoApp.WriteLine("Sent {0} faces / {1} vertices to TopMod as a new layer.", merged.Faces.Count, merged.Verts.Count);
            return Result.Success;
        }
    }

    /// <summary>TopModSetHtml — development override: run an external TOPMOD.html instead of the embedded copy (Enter = embedded).</summary>
    public class TopModSetHtmlCommand : Command
    {
        public override string EnglishName => "TopModSetHtml";
        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            var p = TopModLinkPlugIn.Instance;
            var gs = new GetString();
            gs.SetCommandPrompt("Path of a TOPMOD.html to run instead of the embedded one (Enter = embedded; current: " + (string.IsNullOrEmpty(p.DevHtmlPath) ? "embedded" : p.DevHtmlPath) + ")");
            gs.AcceptNothing(true);
            var r = gs.Get();
            if (r == GetResult.Cancel) return Result.Cancel;
            string v = r == GetResult.Nothing ? "" : gs.StringResult().Trim().Trim('"');
            p.SetDevHtmlPath(v);
            RhinoApp.WriteLine("TopMod app source is now: {0}", string.IsNullOrEmpty(v) ? "embedded" : v);
            if (p.Form != null) RhinoApp.WriteLine("Press Reload in the TopMod window.");
            return Result.Success;
        }
    }

}
