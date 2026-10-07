using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace Survos.Folio
{
    /// Signed thumbnail URLs for folio rows. A folio stores only the original image location;
    /// imgproxy URLs must be signed by the site, so they come from its WallCard API
    /// (/api/{folio}/rows?ids=...), one request per batch of up to 200 rows.
    public static class MuseadoImages
    {
        public sealed class Urls
        {
            /// imgproxy "tiny" preset.
            public string Thumb;
            /// imgproxy "thumb" preset (larger).
            public string Medium;
        }

        public static async Awaitable<Dictionary<string, Urls>> ResolveAsync(string apiBase, string folioCode, IReadOnlyList<string> localIds)
        {
            var result = new Dictionary<string, Urls>();
            for (int start = 0; start < localIds.Count; start += 200)
            {
                var chunk = localIds.Skip(start).Take(200).ToList();
                var url = $"{apiBase.TrimEnd('/')}/api/{folioCode}/rows?itemsPerPage={chunk.Count}&ids="
                          + string.Join(",", chunk.Select(UnityWebRequest.EscapeURL));
                JObject page;
                // One retry: a gateway hiccup (502) or a dropped request is usually fine the second time.
                try { page = await GetAsync(url); }
                catch (IOException) { page = await GetAsync(url); }
                var members = (page["hydra:member"] ?? page["member"]) as JArray ?? new JArray();
                foreach (var m in members)
                {
                    var id = (string)m["id"];
                    var image = m["image"];
                    if (id == null || image == null || image.Type == JTokenType.Null) continue;
                    result[id] = new Urls { Thumb = (string)image["thumb"], Medium = (string)image["medium"] };
                }
            }
            return result;
        }

        static async Awaitable<JObject> GetAsync(string url)
        {
            using var request = UnityWebRequest.Get(url);
            request.SetRequestHeader("Accept", "application/ld+json, application/json");
            request.timeout = 20;
            await request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
                throw new IOException($"Cannot resolve images ({request.responseCode} {request.error}): {url}");
            return JObject.Parse(request.downloadHandler.text);
        }
    }
}
