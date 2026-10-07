using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Survos.Folio
{
    /// One result row: column names shared per query, values as SQLite storage values
    /// (long, double, string, byte[] or null).
    public sealed class FolioRow
    {
        readonly string[] columns;
        readonly object[] values;

        public FolioRow(string[] columns, object[] values)
        {
            this.columns = columns;
            this.values = values;
        }

        public object this[string column] => values[Index(column)];

        public bool IsNull(string column) => this[column] == null;

        public string GetString(string column) => this[column] switch
        {
            null => null,
            string s => s,
            byte[] b => Convert.ToBase64String(b),
            IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
            var v => v.ToString(),
        };

        public long? GetInt64(string column) => this[column] switch
        {
            null => null,
            long l => l,
            double d => (long)d,
            string s when long.TryParse(s, out var l) => l,
            _ => null,
        };

        public int? GetInt32(string column) => GetInt64(column) is { } l ? (int)l : null;

        int Index(string column)
        {
            int i = Array.IndexOf(columns, column);
            if (i < 0) throw new KeyNotFoundException($"No column '{column}' in this result.");
            return i;
        }
    }

    /// A folio database (read-only unless opened writable). Desktop opens it with native SQLite
    /// (Native/NativeFolioDatabase.cs registers itself in Opener); there is no Web backend yet.
    public interface IFolioDatabase : IDisposable
    {
        Task<List<FolioRow>> QueryAsync(string sql, params object[] args);
    }

    public static class FolioDatabase
    {
        /// Opens a SQLite file (path, writable). Set by the platform backend at startup.
        public static Func<string, bool, CancellationToken, Task<IFolioDatabase>> Opener;

        public static bool IsSupported => Opener != null;

        public static Task<IFolioDatabase> OpenAsync(string path, bool writable = false, CancellationToken cancellationToken = default)
        {
            if (Opener == null)
                throw new PlatformNotSupportedException("No SQLite backend for this platform (desktop only; Web is planned via sqlite-wasm).");
            return Opener(path, writable, cancellationToken);
        }
    }
}
