# Survos Folio for Unity

Load a published Museado folio into a Unity app in a few lines: find it in the hub's
catalog, download it once (gzipped SQLite, cached), open it, and read rows with their
first image, ThumbHash placeholder and average colour.

```csharp
using Survos.Folio;

var catalog = await FolioCatalog.LoadAsync("https://recordia.org");      // /folio/list.json
var entry = FolioCatalog.Find(catalog, "mus/fpus");
using var folio = await FolioReader.OpenAsync(entry, progress);           // downloads once
var index = await folio.IndexAsync("obj");                                // id + year, every row with an image
var items = await folio.ItemsAsync(index.Take(50).Select(e => e.LocalId).ToList());
var placeholder = ThumbHash.ToTexture(items[0].Page.ThumbHash);           // ~32 px, blurred
```

Images: a folio stores only the original's location (often `s3://…`). Thumbnails are
signed imgproxy URLs, which only the site can sign, so `MuseadoImages.ResolveAsync(api,
folioCode, ids)` gets them from the site's WallCard API, up to 200 rows per request. Ask
for them when they are about to be seen (a room, a page of a grid), not up front.

## Install

Both packages install from GitHub; no Asset Store or registry is needed.

```json
"dependencies": {
  "com.survos.sqlite": "https://github.com/survos/unity-sqlite.git",
  "com.survos.folio": "https://github.com/survos/unity-folio.git"
}
```

Pin a tag (`…git#v0.1.0`) for anything you ship. For local development use
`"file:../../folio"` and `"file:../../sqlite"`.

## Pieces

| Type | Does |
| --- | --- |
| `FolioCatalog` | Reads the hub's `/folio/list.json`; finds a folio by dataset key or slug. |
| `FolioArchive` | Downloads `downloadUrl` to `persistentDataPath/folios`, gunzips it off the main thread, keeps it until the catalog publishes a newer version; falls back to the cached copy offline. |
| `FolioReader` | Opens the file; `IndexAsync` (id + year), `ItemsAsync` (rows with DTO/extras JSON and first page), `CountAsync`. Tolerates older builds without `thumb_hash`/`color`. |
| `ThumbHash` | Decoder (port of the reference `thumbHashToRGBA`, byte-identical output) and `ToTexture`. |
| `MuseadoImages` | Signed thumbnail URLs for a batch of rows, from the site API. |
| `FolioDatabase` | The SQLite seam: the desktop backend (com.survos.sqlite) registers itself; `IsSupported` is false where there is none. |

Published folios are compact builds without secondary indexes. `FolioReader.OpenAsync`
adds the two it needs to its cached copy (`page(row_id, seq)`, `item(core_id, local_id)`);
without them the first-page join on fpus takes 30 s instead of 70 ms.

## Platforms

- **macOS ARM64, Windows x64, Editor:** native SQLite via com.survos.sqlite.
- **Web (WebGL):** sqlite-wasm, through `Runtime/Web/FolioSqlite.jslib`. Verified in a Unity
  6000.6 Web build (headless Chrome) on `curatescape/baltimore` from recordia.org.

## Web backend: a jslib gluing sqlite-wasm to C#

The browser side is what rutado does (`assets/lib/folio-reader.js`): fetch the `.folio.gz`,
gunzip, `sqlite3_deserialize` into an in-memory sqlite-wasm database.

- `Runtime/Web/FolioSqlite.jslib` (`$FolioSqliteState` holds open databases by handle):
  - `FolioSqlite_Open(url, request, callback)`: loads sqlite-wasm on first use with a dynamic
    `import()` of `https://cdn.jsdelivr.net/npm/@sqlite.org/sqlite-wasm@3.53.4-build2/dist/index.mjs`
    (`locateFile` → `dist/sqlite3.wasm`); fetches the folio, gunzips it with
    `DecompressionStream('gzip')` when it starts with the gzip magic, rewrites a WAL-mode header to
    rollback journal (a WAL header makes `sqlite3_deserialize` fail on the first query), and calls
    back with a handle (-1 on failure; `FolioSqlite_LastError()` says why).
  - `FolioSqlite_Query(handle, sql, argsJson) -> string`: synchronous; returns
    `{"columns":[…],"rows":[[…]]}` (BLOBs base64) or `{"error":"…"}`.
  - `FolioSqlite_Close(handle)`.
- `Runtime/Web/WebFolioDatabase.cs` (`#if UNITY_WEBGL && !UNITY_EDITOR`) implements
  `IFolioDatabase` over those calls and registers in `FolioDatabase.Opener`. On the Web the
  "path" is a URL: `FolioReader.OpenAsync(entry)` opens `entry.DownloadUrl` directly (no
  `FolioArchive` copy; the browser's HTTP cache keeps it), and `OpenFileAsync(url)` works too.
- Indexes: the in-memory copy is writable, so `FolioReader`'s prepare step works as is.
- CORS: the folio URL must allow the page's origin (recordia.org's `/folio/` routes do for
  `*.survos.com` and localhost; zm's config also allows `*`, once deployed).
- Memory: fpus is 18 MB gzipped, 141 MB as SQLite, all of it in the wasm heap. Fine on
  desktop browsers; for big folios a server-side trimmed "app" build would be kinder.
- Not yet: the Cache API (offline), download progress.

## Notes for maintainers

- Keep this package app-neutral: folio rows, pages and placeholders, not museum layout.
  Mapping rows to an app's model (e.g. WallCards) belongs in the app.
- The native backend must stay in this assembly (under `#if`), not in a separate
  unreferenced asmdef: a player only runs `[RuntimeInitializeOnLoadMethod]` hooks of
  assemblies the app uses, and the registration was silently skipped.
