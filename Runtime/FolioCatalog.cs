using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace Survos.Folio
{
    /// One published folio, as listed by the hub's /folio/list.json.
    public sealed class FolioCatalogEntry
    {
        public string DatasetKey;
        public string Code;
        public string Slug;
        public string Locale;
        public string Title;
        public string Description;
        public long? SizeBytes;
        public int? RowCount;
        public List<string> Tags = new();
        public string Checksum;
        public string UpdatedAt;
        public bool? Compressed;
        public string BrowseUrl;
        public string DownloadUrl;

        /// What a cached copy is keyed by: a new publication gets a new version.
        public string Version => Checksum ?? UpdatedAt ?? SizeBytes?.ToString() ?? "1";
    }

    /// The hub's folio catalog (https://recordia.org/folio/list.json): which folios exist and
    /// where to download each one as a (gzipped) SQLite file.
    public static class FolioCatalog
    {
        sealed class Listing
        {
            [JsonProperty("folios")] public List<FolioCatalogEntry> Folios = new();
        }

        public static async Awaitable<List<FolioCatalogEntry>> LoadAsync(string hubBase)
        {
            var url = hubBase.TrimEnd('/') + "/folio/list.json";
            using var request = UnityWebRequest.Get(url);
            request.timeout = 20;
            await request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
                throw new IOException($"Cannot load the folio catalog {url}: {request.responseCode} {request.error}");
            return JsonConvert.DeserializeObject<Listing>(request.downloadHandler.text)?.Folios ?? new List<FolioCatalogEntry>();
        }

        /// A folio by dataset key ("mus/fpus") or slug ("fpus", "fortepan.en"). The dataset key
        /// alone means its default-locale build.
        public static FolioCatalogEntry Find(IEnumerable<FolioCatalogEntry> folios, string key) =>
            folios.Where(f => f.DownloadUrl != null)
                .OrderBy(f => f.Locale == null ? 0 : 1)
                .FirstOrDefault(f => f.DatasetKey == key || f.Slug == key);
    }
}
