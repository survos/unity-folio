// Desktop only: com.survos.sqlite ships macOS ARM64 and Windows x64 libraries. Elsewhere this
// file compiles to nothing, so the Survos.Sqlite assembly is unused and stripped (Web).
#if UNITY_EDITOR || UNITY_STANDALONE_OSX || UNITY_STANDALONE_WIN
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Survos.Sqlite;
using UnityEngine;

namespace Survos.Folio
{
    /// Desktop backend: com.survos.sqlite.
    sealed class NativeFolioDatabase : IFolioDatabase
    {
        readonly SqliteDatabase db;

        NativeFolioDatabase(SqliteDatabase db) => this.db = db;

        // Must live in an assembly the app references: a player only runs the init hooks of
        // assemblies it uses (an unreferenced Survos.Folio.Native was silently skipped).
        [UnityEngine.Scripting.Preserve]
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Register() => FolioDatabase.Opener = OpenAsync;

        static async Task<IFolioDatabase> OpenAsync(string path, bool writable, CancellationToken cancellationToken) =>
            new NativeFolioDatabase(await SqliteDatabase.OpenAsync(path,
                writable ? SqliteOpenMode.ReadWrite : SqliteOpenMode.ReadOnly, cancellationToken));

        public async Task<List<FolioRow>> QueryAsync(string sql, params object[] args)
        {
            var rows = new List<FolioRow>();
            string[] columns = null;
            await foreach (var row in db.QueryAsync(sql, args))
            {
                if (columns == null)
                {
                    columns = new string[row.Count];
                    for (int i = 0; i < columns.Length; i++) columns[i] = row.GetName(i);
                }
                var values = new object[columns.Length];
                for (int i = 0; i < values.Length; i++) values[i] = row[i];
                rows.Add(new FolioRow(columns, values));
            }
            return rows;
        }

        public void Dispose() => db.Dispose();
    }
}
#endif
