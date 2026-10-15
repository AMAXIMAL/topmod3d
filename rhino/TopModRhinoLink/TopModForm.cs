using System;
using Eto.Drawing;
using Eto.Forms;
using Rhino.UI;

namespace TopModRhinoLink
{
    /// <summary>Floating TopMod window (TopModWindow command). The docked version is <see cref="TopModPanel"/>.</summary>
    public class TopModForm : Form
    {
        public TopModForm()
        {
            Title = "TopMod 3D";
            ClientSize = new Size(1180, 800);
            MinimumSize = new Size(640, 420);
            Resizable = true; Maximizable = true; Minimizable = true;
            Owner = RhinoEtoApp.MainWindow;
            Content = TopModView.Shared(compact: false);
            Closed += (s, e) => { Content = null; TopModLinkPlugIn.Instance.Form = null; };
        }
    }

    /// <summary>Dockable Rhino panel hosting TopMod (TopMod command). Drag its tab next to Layers/Properties, or float it.</summary>
    [System.Runtime.InteropServices.Guid("9c2e4b1a-5d3f-4e8a-b7c6-1f0a2d3e4f55")]
    public class TopModPanel : Panel, IPanel
    {
        public static Guid PanelId => typeof(TopModPanel).GUID;

        public TopModPanel(uint documentSerialNumber)
        {
            Content = TopModView.Shared(compact: true);
        }

        public void PanelShown(uint documentSerialNumber, ShowPanelReason reason)
        {
            // Rhino reopens panels that were open at quit. TopMod should only come up when asked for (TopMod command,
            // Layout button) — so a panel restored in the first moments after launch is closed again.
            var p = TopModLinkPlugIn.Instance;
            if (p != null && !p.OpenedThisSession && (DateTime.Now - p.LoadedAt).TotalSeconds < 30)
                Eto.Forms.Application.Instance.AsyncInvoke(() =>
                {
                    if (!p.OpenedThisSession && Rhino.UI.Panels.IsPanelVisible(PanelId)) Rhino.UI.Panels.ClosePanel(PanelId);
                });
        }
        public void PanelHidden(uint documentSerialNumber, ShowPanelReason reason) { }
        public void PanelClosing(uint documentSerialNumber, bool onCloseDocument) { Content = null; }
    }
}
