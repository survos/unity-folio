using System;
using System.IO;
using System.IO.Compression;
using UnityEngine;
using UnityEngine.Networking;

namespace Survos.Folio
{
    /// Downloads a published folio once and keeps it under persistentDataPath/folios, keyed by
    /// the catalog's version; a newer publication replaces it. Without a network the last
    /// downloaded copy is used.
    public static class FolioArchive
    {
        public static string Directory => Path.Combine(Application.persistentDataPath, "folios");

        public static string PathFor(string datasetKey, string locale = null) =>
            Path.Combine(Directory, datasetKey.Replace('/', '_') + (locale == null ? "" : "." + locale) + ".folio");

        /// The local SQLite file for this entry, downloading (and gunzipping) it when the cached
        /// copy is missing or older. progress: 0..1 over the download.
        public static async Awaitable<string> FetchAsync(FolioCatalogEntry entry, IProgress<float> progress = null)
        {
            System.IO.Directory.CreateDirectory(Directory);
            var path = PathFor(entry.DatasetKey, entry.Locale);
            var versionFile = path + ".version";
            if (File.Exists(path) && File.Exists(versionFile) && File.ReadAllText(versionFile) == entry.Version)
            {
                progress?.Report(1f);
                return path;
            }

            var download = path + ".download";
            using (var request = new UnityWebRequest(entry.DownloadUrl, UnityWebRequest.kHttpVerbGET))
            {
                request.downloadHandler = new DownloadHandlerFile(download) { removeFileOnAbort = true };
                var operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    progress?.Report(request.downloadProgress);
                    await Awaitable.NextFrameAsync();
                }
                if (request.result != UnityWebRequest.Result.Success)
                {
                    // Offline or the hub is down: an older copy is better than nothing.
                    if (File.Exists(path))
                    {
                        Debug.LogWarning($"Using the cached folio; cannot download {entry.DownloadUrl}: {request.error}");
                        return path;
                    }
                    throw new IOException($"Cannot download {entry.DownloadUrl}: {request.responseCode} {request.error}");
                }
            }
            progress?.Report(1f);

            // Gunzip off the main thread; a .gz is recognised by its magic bytes, not the URL.
            var unpacked = path + ".part";
            await Awaitable.BackgroundThreadAsync();
            try
            {
                using (var input = File.OpenRead(download))
                {
                    bool gzip = input.ReadByte() == 0x1f && input.ReadByte() == 0x8b;
                    input.Position = 0;
                    using var source = gzip ? new GZipStream(input, CompressionMode.Decompress) : (Stream)input;
                    using var output = File.Create(unpacked);
                    source.CopyTo(output, 1 << 20);
                }
                CheckHeader(unpacked);
                if (File.Exists(path)) File.Delete(path);
                File.Move(unpacked, path);
                File.WriteAllText(versionFile, entry.Version);
            }
            finally
            {
                if (File.Exists(download)) File.Delete(download);
                if (File.Exists(unpacked)) File.Delete(unpacked);
                await Awaitable.MainThreadAsync();
            }
            return path;
        }

        static void CheckHeader(string path)
        {
            var header = new byte[16];
            using var f = File.OpenRead(path);
            if (f.Read(header, 0, 16) != 16 || System.Text.Encoding.ASCII.GetString(header) != "SQLite format 3\0")
                throw new InvalidDataException("This download is not a SQLite folio.");
        }
    }
}
