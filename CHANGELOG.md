# Changelog

## 0.2.0 — Unreleased

- Web backend: sqlite-wasm through `FolioSqlite.jslib`; on WebGL `FolioReader.OpenAsync` opens the
  folio's download URL in the browser (gunzipped there), and `OpenFileAsync` accepts a URL.

## 0.1.0 — Unreleased

- Folio catalog (`/folio/list.json`), download-once archive cache, `FolioReader` (index, rows with first page).
- ThumbHash decoder (byte-identical to the reference implementation) and placeholder textures.
- Signed thumbnail URLs from the site's WallCard API (`MuseadoImages`).
- Desktop backend on com.survos.sqlite; verified in the Editor and an IL2CPP macOS ARM64 player.
