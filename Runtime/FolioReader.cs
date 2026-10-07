using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace Survos.Folio
{
    /// The first page (image) of a row.
    public sealed class FolioPage
    {
        /// Original image location (often s3://...; not directly fetchable). Clients get
        /// signed thumbnails from the site API (MuseadoImages).
        public string Url;
        public int? Width;
        public int? Height;
        /// ThumbHash, base64: a ~25-byte blurred preview to paint before the image loads.
        public string ThumbHash;
        /// Average colour "#rrggbb": the placeholder when there is no ThumbHash.
        public string Color;
    }

    /// One row of a folio core, with its canonical DTO and extras as JSON.
    public sealed class FolioItem
    {
        public string LocalId;
        public string Label;
        public string DtoType;
        public string DtoJson;
        public string ExtrasJson;
        public int? Year;
        public FolioPage Page;
    }

    /// A row id and its year: enough to choose what to show before loading full rows.
    public readonly struct FolioIndexEntry
    {
        public readonly string LocalId;
        public readonly int? Year;
        public FolioIndexEntry(string localId, int? year) { LocalId = localId; Year = year; }
    }

    /// An open folio: a SQLite file with item (rows), page (images) and core tables.
    /// See folio-bundle docs/pages-and-media.md for the schema.
    public sealed class FolioReader : IDisposable
    {
        public string Code { get; private set; }
        public string Title { get; private set; }
        public IFolioDatabase Database { get; }
        bool hasThumbHash, hasColor;

        FolioReader(IFolioDatabase db) => Database = db;

        /// Download (or reuse the cached copy of) a published folio and open it.
        public static async Awaitable<FolioReader> OpenAsync(FolioCatalogEntry entry, IProgress<float> progress = null)
        {
            var path = await FolioArchive.FetchAsync(entry, progress);
            return await OpenFileAsync(path, prepare: true);
        }

        /// prepare: add the indexes this class relies on (published builds are compact and
        /// drop secondary indexes). Needs a writable copy, such as the one FolioArchive keeps.
        public static async Task<FolioReader> OpenFileAsync(string path, bool prepare = false)
        {
            var folio = new FolioReader(await FolioDatabase.OpenAsync(path, writable: prepare));
            try
            {
                if (prepare)
                {
                    await folio.Database.QueryAsync("CREATE INDEX IF NOT EXISTS idx_page_row_seq ON page(row_id, seq)");
                    await folio.Database.QueryAsync("CREATE UNIQUE INDEX IF NOT EXISTS uniq_item_core_local ON item(core_id, local_id)");
                }
                await folio.ReadHeaderAsync();
            }
            catch { folio.Dispose(); throw; }
            return folio;
        }

        async Task ReadHeaderAsync()
        {
            // The folio table can also list a bootstrap entry; the real one owns the cores.
            var rows = await Database.QueryAsync(
                "SELECT f.code, f.label FROM folio f WHERE EXISTS (SELECT 1 FROM core c WHERE c.folio_code = f.code) LIMIT 1");
            if (rows.Count == 0) throw new InvalidOperationException("This SQLite file is not a folio (no folio with cores).");
            Code = rows[0].GetString("code");
            Title = rows[0].GetString("label") ?? Code;
            // Older builds have no thumb_hash/color columns.
            var columns = (await Database.QueryAsync("SELECT name FROM pragma_table_info('page')")).Select(r => r.GetString("name")).ToList();
            hasThumbHash = columns.Contains("thumb_hash");
            hasColor = columns.Contains("color");
        }

        const string Year = "CAST(COALESCE(json_extract(i.dto_data, '$.year'), json_extract(i.extras, '$.year')) AS INTEGER)";
        const string FirstPage = "LEFT JOIN page p ON p.id = (SELECT fp.id FROM page fp WHERE fp.row_id = i.id ORDER BY fp.seq, fp.id LIMIT 1)";

        public async Task<int> CountAsync(string core = "obj") =>
            (int)((await Database.QueryAsync("SELECT COUNT(*) AS n FROM item WHERE core_id = ?", CoreId(core)))[0].GetInt64("n") ?? 0);

        /// Every row of a core (optionally one DTO type, optionally only rows with an image):
        /// id and year only.
        public async Task<List<FolioIndexEntry>> IndexAsync(string core = "obj", string dtoType = null, bool withImage = true)
        {
            var sql = $"SELECT i.local_id, {Year} AS year FROM item i {(withImage ? FirstPage : "")} WHERE i.core_id = ?"
                      + (dtoType != null ? " AND i.dto_type = ?" : "")
                      + (withImage ? " AND p.url IS NOT NULL" : "");
            var args = dtoType != null ? new object[] { CoreId(core), dtoType } : new object[] { CoreId(core) };
            return (await Database.QueryAsync(sql, args)).Select(r => new FolioIndexEntry(r.GetString("local_id"), r.GetInt32("year"))).ToList();
        }

        /// Full rows for these ids, in the order given.
        public async Task<List<FolioItem>> ItemsAsync(IReadOnlyList<string> localIds, string core = "obj")
        {
            var found = new Dictionary<string, FolioItem>();
            var thumb = hasThumbHash ? "p.thumb_hash" : "NULL";
            var color = hasColor ? "p.color" : "NULL";
            // SQLite's default limit on bound parameters is far above this; chunks keep statements small.
            for (int start = 0; start < localIds.Count; start += 400)
            {
                var chunk = localIds.Skip(start).Take(400).ToList();
                var sql = $"SELECT i.local_id, i.label, i.dto_type, i.dto_data, i.extras, {Year} AS year, "
                          + $"p.url AS page_url, p.width AS page_width, p.height AS page_height, {thumb} AS page_thumb_hash, {color} AS page_color "
                          + $"FROM item i {FirstPage} WHERE i.core_id = ? AND i.local_id IN ({string.Join(",", chunk.Select(_ => "?"))})";
                var args = new object[] { CoreId(core) }.Concat(chunk).ToArray();
                foreach (var r in await Database.QueryAsync(sql, args))
                    found[r.GetString("local_id")] = Map(r);
            }
            return localIds.Where(found.ContainsKey).Select(id => found[id]).ToList();
        }

        static FolioItem Map(FolioRow r) => new()
        {
            LocalId = r.GetString("local_id"),
            Label = r.GetString("label"),
            DtoType = r.GetString("dto_type"),
            DtoJson = r.GetString("dto_data"),
            ExtrasJson = r.GetString("extras"),
            Year = r.GetInt32("year"),
            Page = r.IsNull("page_url") ? null : new FolioPage
            {
                Url = r.GetString("page_url"),
                Width = r.GetInt32("page_width"),
                Height = r.GetInt32("page_height"),
                ThumbHash = r.GetString("page_thumb_hash"),
                Color = r.GetString("page_color"),
            },
        };

        string CoreId(string core) => Code + ":" + core;

        public void Dispose() => Database.Dispose();
    }
}
