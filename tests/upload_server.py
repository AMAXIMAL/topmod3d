import http.server, socketserver, base64, os, urllib.parse
SHOTS="/Users/ions/topmod-test/shots"
os.makedirs(SHOTS, exist_ok=True)
class H(http.server.BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"
    def _cors(self):
        self.send_header("Access-Control-Allow-Origin","*")
        self.send_header("Access-Control-Allow-Headers","*")
        self.send_header("Connection","close")
    def do_OPTIONS(self):
        self.send_response(204); self._cors(); self.send_header("Content-Length","0"); self.end_headers()
    def do_POST(self):
        q=urllib.parse.urlparse(self.path).query
        name=urllib.parse.parse_qs(q).get("name",["shot"])[0]
        name="".join(c for c in name if c.isalnum() or c in "-_.")[:120] or "shot"
        n=int(self.headers.get("Content-Length",0))
        body=self.rfile.read(n).decode("utf-8","ignore")
        if body.startswith("data:"): body=body.split(",",1)[1]
        try:
            raw=base64.b64decode(body); msg=b"ok"; code=200
            with open(os.path.join(SHOTS,name+".jpg"),"wb") as f: f.write(raw)
        except Exception as e:
            msg=str(e).encode(); code=400
        self.send_response(code); self._cors()
        self.send_header("Content-Length",str(len(msg))); self.end_headers(); self.wfile.write(msg)
    def log_message(self,*a): pass
class TS(socketserver.ThreadingMixIn, http.server.HTTPServer):
    daemon_threads=True
with TS(("127.0.0.1",8747),H) as s:
    s.serve_forever()
