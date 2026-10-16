/**
 * TopMod Gallery → Google Drive
 * Saves every gallery post (project .tmod + render .jpg) into a Drive folder.
 *
 * Setup (once):
 *  1. script.google.com → New project → paste this file.
 *  2. Change SECRET below to any long random word (you'll give the same word to Cloudflare).
 *  3. Deploy → New deployment → type "Web app" → Execute as: Me · Who has access: Anyone → Deploy → copy the URL.
 *  4. On the Mac:
 *       cd ~/AMAXiMAL/website/firebase
 *       wrangler pages secret put GDRIVE_HOOK   --project-name amaximal   (paste the web-app URL)
 *       wrangler pages secret put GDRIVE_SECRET --project-name amaximal   (paste the same SECRET word)
 */
const SECRET = "change-me-to-a-long-random-word";
const FOLDER_NAME = "TopMod Gallery";

function doPost(e) {
  const d = JSON.parse(e.postData.contents || "{}");
  if (!SECRET || d.secret !== SECRET) return out({ ok: false, error: "bad secret" });
  const folder = getFolder_();
  const day = (d.created || new Date().toISOString()).slice(0, 10);
  const base = (day + " " + (d.title || "Untitled") + " — " + (d.author || "Anonymous")).replace(/[\\/:*?"<>|]/g, "_").slice(0, 120);
  if (d.project) folder.createFile(base + ".tmod", d.project, "application/json")
    .setDescription("TopMod gallery post " + d.id + (d.note ? "\n\n" + d.note : "") + "\nhttps://amaximal.com/topmod3d/gallery/?p=" + d.id);
  if (d.image) folder.createFile(Utilities.newBlob(Utilities.base64Decode(d.image), "image/jpeg", base + ".jpg"));
  return out({ ok: true });
}

function getFolder_() {
  const it = DriveApp.getFoldersByName(FOLDER_NAME);
  return it.hasNext() ? it.next() : DriveApp.createFolder(FOLDER_NAME);
}
function out(o) { return ContentService.createTextOutput(JSON.stringify(o)).setMimeType(ContentService.MimeType.JSON); }
