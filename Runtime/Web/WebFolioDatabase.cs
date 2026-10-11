// Web backend: sqlite-wasm through FolioSqlite.jslib. Elsewhere this file compiles to nothing.
#if UNITY_WEBGL && !UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using AOT;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Survos.Folio
{
    /// Web backend: the folio is fetched by the browser and held in memory by sqlite-wasm.
    /// "path" is a URL (a .folio or .folio.gz); it is never written to disk.
    sealed class WebFolioDatabase : IFolioDatabase
    {
        [DllImport("__Internal")] static extern void FolioSqlite_Open(string url, int request, Action<int, int> callback);
        [DllImport("__Internal")] static extern string FolioSqlite_Query(int handle, string sql, string argsJson);
        [DllImport("__Internal")] static extern void FolioSqlite_Close(int handle);
        [DllImport("__Internal")] static extern string FolioSqlite_LastError();

        static readonly Dictionary<int, TaskCompletionSource<int>> pending = new();
        static int nextRequest = 1;

        readonly int handle;

        WebFolioDatabase(int handle) => this.handle = handle;

        [UnityEngine.Scripting.Preserve]
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Register() => FolioDatabase.Opener = OpenAsync;

        static async Task<IFolioDatabase> OpenAsync(string url, bool writable, CancellationToken cancellationToken)
        {
            int request = nextRequest++;
            var opened = new TaskCompletionSource<int>();
            pending[request] = opened;
            FolioSqlite_Open(url, request, OnOpened);
            int handle = await opened.Task;
            if (handle < 0) throw new System.IO.IOException($"Cannot open folio {url}: {FolioSqlite_LastError()}");
            return new WebFolioDatabase(handle);
        }

        [MonoPInvokeCallback(typeof(Action<int, int>))]
        static void OnOpened(int request, int handle)
        {
            if (pending.Remove(request, out var tcs)) tcs.SetResult(handle);
        }

        public Task<List<FolioRow>> QueryAsync(string sql, params object[] args)
        {
            var json = JObject.Parse(FolioSqlite_Query(handle, sql, JsonConvert.SerializeObject(args ?? Array.Empty<object>())));
            if (json["error"] is JValue error) throw new InvalidOperationException($"{error}\n{sql}");
            var columns = json["columns"].ToObject<string[]>();
            var rows = new List<FolioRow>();
            foreach (JArray r in json["rows"])
            {
                var values = new object[columns.Length];
                for (int i = 0; i < values.Length; i++)
                    values[i] = r[i] is JValue { Value: var v } ? v switch
                    {
                        int n => (long)n,
                        _ => v,
                    } : null;
                rows.Add(new FolioRow(columns, values));
            }
            return Task.FromResult(rows);
        }

        public void Dispose() => FolioSqlite_Close(handle);
    }
}
#endif
