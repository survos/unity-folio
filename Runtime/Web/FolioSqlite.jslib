// Web backend for com.survos.folio: the official sqlite-wasm build, loaded from jsdelivr on
// first use. A folio is fetched (gunzipped in the browser when it is a .folio.gz), copied
// into an in-memory database, and queried synchronously from C#.
var FolioSqlite = {
  $FolioSqliteState: { loading: null, dbs: {}, next: 1, error: "" },

  FolioSqlite_Open__deps: ["$FolioSqliteState"],
  FolioSqlite_Open: function (urlPtr, request, callback) {
    var url = UTF8ToString(urlPtr);
    var S = FolioSqliteState;
    var base = "https://cdn.jsdelivr.net/npm/@sqlite.org/sqlite-wasm@3.53.4-build2/dist/";
    var done = function (handle) { {{{ makeDynCall("vii", "callback") }}}(request, handle); };
    var open = function () {
      if (!S.loading)
        S.loading = import(base + "index.mjs").then(function (m) {
          return m.default({ locateFile: function () { return base + "sqlite3.wasm"; }, print: function () {}, printErr: console.warn });
        }).catch(function (e) { S.loading = null; throw e; });
      var sqlite;
      return S.loading.then(function (s) {
        sqlite = s;
        return fetch(url);
      }).then(function (response) {
        if (!response.ok) throw new Error("Folio download failed (" + response.status + "): " + url);
        return response.arrayBuffer();
      }).then(function (buffer) {
        var bytes = new Uint8Array(buffer);
        if (bytes[0] === 0x1f && bytes[1] === 0x8b) {
          var stream = new Blob([bytes]).stream().pipeThrough(new DecompressionStream("gzip"));
          return new Response(stream).arrayBuffer().then(function (b) { return new Uint8Array(b); });
        }
        return bytes;
      }).then(function (bytes) {
        if (new TextDecoder().decode(bytes.subarray(0, 15)) !== "SQLite format 3") throw new Error("Not a SQLite folio: " + url);
        // A WAL-mode header makes sqlite3_deserialize fail on the first query; read it as rollback-journal.
        if (bytes[18] === 2) { bytes[18] = 1; bytes[19] = 1; }
        var db = new sqlite.oo1.DB();
        var p = sqlite.wasm.allocFromTypedArray(bytes);
        var rc = sqlite.capi.sqlite3_deserialize(db.pointer, "main", p, bytes.length, bytes.length,
          sqlite.capi.SQLITE_DESERIALIZE_FREEONCLOSE | sqlite.capi.SQLITE_DESERIALIZE_RESIZEABLE);
        if (rc !== 0) { sqlite.wasm.dealloc(p); db.close(); throw new Error("Could not open folio (SQLite " + rc + ")"); }
        var handle = S.next++;
        S.dbs[handle] = db;
        return handle;
      });
    };
    open().then(done, function (e) {
      S.error = String((e && e.message) || e);
      console.error("FolioSqlite:", e);
      done(-1);
    });
  },

  // Runs one statement. Returns JSON {"columns":[...],"rows":[[...],...]} or {"error":"..."}.
  // args: a JSON array of bound values. BLOBs come back base64-encoded.
  FolioSqlite_Query__deps: ["$FolioSqliteState"],
  FolioSqlite_Query: function (handle, sqlPtr, argsPtr) {
    var S = FolioSqliteState;
    var result;
    try {
      var db = S.dbs[handle];
      if (!db) throw new Error("Folio database " + handle + " is not open.");
      var columns = [];
      var rows = db.exec({
        sql: UTF8ToString(sqlPtr),
        bind: JSON.parse(UTF8ToString(argsPtr)),
        rowMode: "array",
        columnNames: columns,
        returnValue: "resultRows",
      });
      for (var i = 0; i < rows.length; i++)
        for (var j = 0; j < rows[i].length; j++) {
          var v = rows[i][j];
          if (v instanceof Uint8Array) {
            var s = "";
            for (var k = 0; k < v.length; k++) s += String.fromCharCode(v[k]);
            rows[i][j] = btoa(s);
          } else if (typeof v === "bigint") rows[i][j] = Number(v);
        }
      result = JSON.stringify({ columns: columns, rows: rows });
    } catch (e) {
      result = JSON.stringify({ error: String((e && e.message) || e) });
    }
    var size = lengthBytesUTF8(result) + 1;
    var buffer = _malloc(size);
    stringToUTF8(result, buffer, size);
    return buffer;
  },

  FolioSqlite_Close__deps: ["$FolioSqliteState"],
  FolioSqlite_Close: function (handle) {
    var S = FolioSqliteState;
    if (S.dbs[handle]) { S.dbs[handle].close(); delete S.dbs[handle]; }
  },

  FolioSqlite_LastError__deps: ["$FolioSqliteState"],
  FolioSqlite_LastError: function () {
    var s = FolioSqliteState.error || "";
    var size = lengthBytesUTF8(s) + 1;
    var buffer = _malloc(size);
    stringToUTF8(s, buffer, size);
    return buffer;
  },
};

autoAddDeps(FolioSqlite, "$FolioSqliteState");
mergeInto(LibraryManager.library, FolioSqlite);
