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
- **Web:** not yet. `FolioDatabase.IsSupported` is false; an app should fall back to the
  WallCard API (MuseumBrowser does). The plan is below.

## Web backend (planned): a jslib gluing sqlite-wasm to C#

The browser side is what rutado already does (`assets/lib/folio-reader.js`): fetch the
`.folio.gz`, gunzip, `sqlite3_deserialize` it into an in-memory sqlite-wasm database,
keep the archive in the Cache API. The Unity side needs a thin bridge:

- `Runtime/Plugins/WebGL/SurvosFolio.jslib`, with a `$SurvosFolio` state object
  (`autoAddDeps`) holding open databases by handle.
  - `SurvosFolio_Open(url, version) -> handle`: starts the download asynchronously;
    loads sqlite-wasm with a dynamic `import()` of
    `https://cdn.jsdelivr.net/npm/@sqlite.org/sqlite-wasm@3.53.4-build2/dist/index.mjs`
    (`locateFile` → `dist/sqlite3.wasm`), or from a URL the page provides; gunzips with
    `DecompressionStream('gzip')` (no fflate needed); caches by url + version.
  - `SurvosFolio_State(handle) -> int` (0 pending, 1 open, -1 failed),
    `SurvosFolio_Progress(handle) -> float`, `SurvosFolio_Error(handle) -> string`.
    C# polls these once a frame from an `Awaitable`, so no `SendMessage` callbacks.
  - `SurvosFolio_Query(handle, sql, argsJson) -> string`: synchronous (sqlite-wasm's oo1
    API is synchronous on the main thread); returns `{"columns":[…],"rows":[[…]]}`,
    BLOBs as base64. Allocate the result with `_malloc` + `stringToUTF8`; IL2CPP frees it.
  - `SurvosFolio_Close(handle)`.
- `Runtime/Web/WebFolioDatabase.cs` under `#if UNITY_WEBGL && !UNITY_EDITOR`, implementing
  `IFolioDatabase` over those calls and registering in `FolioDatabase.Opener`, the same
  way the native backend does. `FolioReader` and everything above it stay unchanged.
- Indexes: the in-memory copy is writable, so `FolioReader`'s prepare step works as is.
- Memory: fpus is 18 MB gzipped, 141 MB as SQLite, all of it in the wasm heap. Fine on
  desktop browsers; for big folios a server-side trimmed "app" build would be kinder.

## Notes for maintainers

- Keep this package app-neutral: folio rows, pages and placeholders, not museum layout.
  Mapping rows to an app's model (e.g. WallCards) belongs in the app.
- The native backend must stay in this assembly (under `#if`), not in a separate
  unreferenced asmdef: a player only runs `[RuntimeInitializeOnLoadMethod]` hooks of
  assemblies the app uses, and the registration was silently skipped.
