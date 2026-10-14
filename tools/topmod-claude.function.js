// Ask Claude for TopMod 3D (Cloudflare Pages Function) — POST /api/topmod-claude
// The app sends {prompt, scene, catalog, history, device, account}; this calls Claude with the
// build_model tool and returns {plan:{reply,steps}, remaining}.
//
// Secrets / bindings (set once, on the Mac):
//   wrangler pages secret put ANTHROPIC_API_KEY --project-name amaximal
//   KV binding COMMENTS (already exists; usage counters live under the "tmc:" prefix)
//
// Allowance (until credits / in-app purchase): guests FREE_GUEST builds per day per device+IP,
// Sign in with Apple users FREE_ACCOUNT builds per day.

const MODEL = "claude-sonnet-5-5";
const FREE_GUEST = 5, FREE_ACCOUNT = 25;
const APPLE_AUD = "com.amaximal.topmodjs";
const ORIGINS = ["https://amaximal.com", "https://www.amaximal.com", "null"]; // "null" = the iOS app (file/srcdoc origin)

const SYS = "You are the modeling assistant inside TopMod 3D, a topological mesh modeler by AMAXIMAL (based on TopMod from Texas A&M). " +
  "The user describes a 3D form; you build it by calling build_model once with a short friendly reply and an ordered list of steps. " +
  "Step objects (use exactly one key per step, plus params): " +
  "{new:true} start a fresh scene (only when the user asks for something new, not when editing). " +
  "{prim:NAME, params:{inputId:value}} create a primitive; NAME and input ids come from CATALOG.prims. " +
  "{mod:TYPE, params:{k:value}} add a live modifier from CATALOG.mods (parametric, preferred for whole-mesh changes: twist, bend, subdivide, smooth, tmHandles, tmRind, bejewel, thicken, mirror, array…). " +
  "{select:{all:true}|{every:N}|{dir:'up'|'down'|'left'|'right'|'front'|'back',count:N}|{random:N}|{faces:[i…]}} select faces before a selection tool. " +
  "{op:NAME, params:{inputId:value}} run a tool from CATALOG.ops on the selection (extrude, handle, pinch, dome, greeble…). " +
  "{render:MODE} viewport look from CATALOG.render (gold, diamond, sapphire, ruby, emerald, carbon, wire, solid…). " +
  "{act:'frameAll'|'topopt'|'physics'|'lineArt'} {name:'Layer name'} {clear:true}. " +
  "Rules: only use names, ids and option values from CATALOG; keep values inside min/max; prefer few, robust steps (usually 2–8); " +
  "handles in TopMod come from the tmHandles modifier (count, twist) on a closed polyhedron like a dodecahedron or cube; finish organic forms with subdivide; " +
  "Always answer by calling build_model (never plain text). " +
  "if the request is impossible with these tools, say so in reply and return the closest build. Never discuss anything unrelated to modeling in this app.";

const TOOL = {
  name: "build_model", description: "Build or edit the model in TopMod with ordered steps.",
  input_schema: { type: "object", properties: {
    reply: { type: "string", description: "One or two sentences to the user about what you built." },
    steps: { type: "array", items: { type: "object" } } }, required: ["reply", "steps"] }
};

const cors = (req) => {
  const o = req.headers.get("origin") || "null";
  return { "access-control-allow-origin": ORIGINS.includes(o) ? o : "https://amaximal.com",
           "access-control-allow-methods": "POST, OPTIONS", "access-control-allow-headers": "content-type", "vary": "origin" };
};
const json = (req, o, status = 200) => new Response(JSON.stringify(o), {
  status, headers: { "content-type": "application/json; charset=utf-8", "cache-control": "no-store", ...cors(req) } });

async function sha(s) {
  const d = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(s));
  return [...new Uint8Array(d)].slice(0, 12).map(b => b.toString(16).padStart(2, "0")).join("");
}
const b64u = s => Uint8Array.from(atob(s.replace(/-/g, "+").replace(/_/g, "/") + "===".slice((s.length + 3) % 4)), c => c.charCodeAt(0));

// Verify a Sign in with Apple identity token; returns the Apple user id (sub) or null.
async function appleSub(token) {
  try {
    const [h, p, sig] = token.split(".");
    const head = JSON.parse(new TextDecoder().decode(b64u(h)));
    const body = JSON.parse(new TextDecoder().decode(b64u(p)));
    if (body.iss !== "https://appleid.apple.com" || body.aud !== APPLE_AUD || body.exp * 1000 < Date.now() - 30 * 86400e3) return null;
    const keys = await (await fetch("https://appleid.apple.com/auth/keys", { cf: { cacheTtl: 3600 } })).json();
    const jwk = keys.keys.find(k => k.kid === head.kid); if (!jwk) return null;
    const key = await crypto.subtle.importKey("jwk", jwk, { name: "RSASSA-PKCS1-v1_5", hash: "SHA-256" }, false, ["verify"]);
    const ok = await crypto.subtle.verify("RSASSA-PKCS1-v1_5", key, b64u(sig), new TextEncoder().encode(h + "." + p));
    return ok ? body.sub : null;
  } catch (e) { return null; }
}

export async function onRequestOptions({ request }) { return new Response(null, { status: 204, headers: cors(request) }); }

export async function onRequestPost({ request, env }) {
  if (!env.ANTHROPIC_API_KEY) return json(request, { error: "Claude isn't set up on the server yet" }, 503);
  let b; try { b = await request.json(); } catch (e) { return json(request, { error: "bad request" }, 400); }
  const prompt = String(b.prompt || "").trim().slice(0, 1200);
  if (!prompt) return json(request, { error: "empty prompt" }, 400);
  const catalog = JSON.stringify(b.catalog || {}); if (catalog.length > 60000) return json(request, { error: "catalog too large" }, 413);
  const scene = JSON.stringify(b.scene || {}).slice(0, 6000);

  // who is asking, and how many builds are left today
  const day = new Date().toISOString().slice(0, 10);
  const sub = b.account ? await appleSub(String(b.account)) : null;
  const ip = request.headers.get("cf-connecting-ip") || "0";
  const who = sub ? "a:" + await sha("apple:" + sub) : "g:" + await sha("guest:" + (b.device || "") + ":" + ip);
  const cap = sub ? FREE_ACCOUNT : FREE_GUEST;
  const k = "tmc:" + day + ":" + who;
  const used = +(await env.COMMENTS.get(k) || 0);
  if (used >= cap) return json(request, { error: sub ? "You've used today's Claude builds. More tomorrow." : "Free builds used for today. Sign in with Apple for more.", remaining: 0 }, 429);

  const history = (Array.isArray(b.history) ? b.history : []).slice(-8)
    .filter(m => m && (m.role === "user" || m.role === "assistant") && typeof m.content === "string")
    .map(m => ({ role: m.role, content: m.content.slice(0, 1500) }));
  if (history.length && history[0].role !== "user") history.shift();

  const r = await fetch("https://api.anthropic.com/v1/messages", {
    method: "POST",
    headers: { "content-type": "application/json", "x-api-key": env.ANTHROPIC_API_KEY, "anthropic-version": "2023-06-01" },
    body: JSON.stringify({
      model: MODEL, max_tokens: 2000,
      system: [{ type: "text", text: SYS }, { type: "text", text: "CATALOG " + catalog, cache_control: { type: "ephemeral" } }],
      tools: [TOOL], tool_choice: { type: "auto" },
      messages: history.concat([{ role: "user", content: "SCENE " + scene + "\n\nREQUEST " + prompt }])
    })
  });
  const j = await r.json().catch(() => ({}));
  if (!r.ok) return json(request, { error: "Claude is busy — try again in a moment", detail: (j.error && (j.error.type + ": " + j.error.message)) || ("upstream " + r.status) }, 503);
  const tu = (j.content || []).find(c => c.type === "tool_use");
  const tx = (j.content || []).filter(c => c.type === "text").map(c => c.text).join(" ").trim();
  const plan = tu ? tu.input : { reply: tx || "I couldn't plan that one.", steps: [] };
  if (!Array.isArray(plan.steps)) plan.steps = [];
  plan.steps = plan.steps.slice(0, 40);

  await env.COMMENTS.put(k, String(used + 1), { expirationTtl: 2 * 86400 });
  return json(request, { plan, remaining: cap - used - 1 });
}
