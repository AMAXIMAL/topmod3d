import os
d = "/Users/ions/AMAXiMAL/projects/topmod/rhino/TopModRhinoLink/"
p = d + "TopModView.cs"; s = open(p, encoding="utf-8").read()
if "_shared" not in s:
    s = s.replace('''        bool _busy;
        string _source = "";
''', '''        bool _busy;
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
''')
    s = s.replace('''            Load += (s, e) => LoadPage();
            UnLoad += (s, e) => _timer.Stop();''', '''            Load += (s, e) => { if (!_loaded) LoadPage(); else _timer.Start(); };
            UnLoad += (s, e) => _timer.Stop();''')
    s = s.replace('''                _lastWebVersion = 0;
                _web.Url = new Uri(path);
                _status.Text = "loading " + _source + "…";''', '''                _lastWebVersion = 0;
                _web.Url = new Uri(path);
                _loaded = true;
                _status.Text = "loading " + _source + "…";''')
    open(p, "w", encoding="utf-8").write(s)
p = d + "TopModForm.cs"; s = open(p, encoding="utf-8").read()
s = s.replace('Content = new TopModView(compact: false);', 'Content = TopModView.Shared(compact: false);')
s = s.replace('Content = new TopModView(compact: true);', 'Content = TopModView.Shared(compact: true);')
s = s.replace('''            Closed += (s, e) => TopModLinkPlugIn.Instance.Form = null;''', '''            Closed += (s, e) => { Content = null; TopModLinkPlugIn.Instance.Form = null; };''')
s = s.replace('''        public void PanelClosing(uint documentSerialNumber, bool onCloseDocument) { }''', '''        public void PanelClosing(uint documentSerialNumber, bool onCloseDocument) { Content = null; }''')
open(p, "w", encoding="utf-8").write(s)
print("patched v4")
