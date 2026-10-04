using Microsoft.Data.Sqlite;

namespace DeltaStudio.Infrastructure.Persistence;

/// <summary>A recent-project entry.</summary>
public sealed record ProjectRegistryEntry(string Directory, string Name, string ModelId, DateTimeOffset LastOpenedUtc, string LastAgent);

/// <summary>Result of an advisory lock acquisition.</summary>
public sealed record ProjectLockInfo(string ProjectDirectory, string AgentId, DateTimeOffset AcquiredUtc, int LeaseSeconds, bool Acquired);

/// <summary>
/// SQLite-backed registry: recent projects, save-time checkpoints (rollback support), and
/// advisory per-project locks so several AI agents (programmer, reviewer, safety reviewer…) or
/// the editor and a headless session cannot clobber each other.
/// Database path resolution: %DELTASTUDIO_DATA_DIR% or ~/.deltastudio (never a hard-coded path).
/// </summary>
public sealed class SqliteProjectRegistry : IAsyncDisposable
{
    private readonly string _dbPath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Creates/opens the registry (schema auto-migrated).</summary>
    public SqliteProjectRegistry(string? dataDir = null)
    {
        dataDir ??= Environment.GetEnvironmentVariable("DELTASTUDIO_DATA_DIR")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".deltastudio");
        Directory.CreateDirectory(dataDir);
        _dbPath = Path.Combine(dataDir, "registry.db");
    }

    private string ConnectionString => $"Data Source={_dbPath}";

    /// <summary>Ensures schema; returns an open connection.</summary>
    private async Task<SqliteConnection> OpenAsync(CancellationToken ct)
    {
        var c = new SqliteConnection(ConnectionString);
        await c.OpenAsync(ct).ConfigureAwait(false);
        SqliteCommand cmd = c.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS projects(
                dir TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                model_id TEXT NOT NULL,
                last_opened_utc TEXT NOT NULL,
                last_agent TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS checkpoints(
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                dir TEXT NOT NULL,
                created_utc TEXT NOT NULL,
                author TEXT NOT NULL,
                reason TEXT NOT NULL,
                json TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS ix_ck ON checkpoints(dir, id DESC);
            CREATE TABLE IF NOT EXISTS locks(
                dir TEXT PRIMARY KEY,
                agent_id TEXT NOT NULL,
                acquired_utc TEXT NOT NULL,
                lease_seconds INTEGER NOT NULL);
            """;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return c;
    }

    /// <summary>Registers/refreshes a project in the recents list.</summary>
    public async Task RegisterAsync(string directory, string name, string modelId, string agent, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using SqliteConnection c = await OpenAsync(ct).ConfigureAwait(false);
            SqliteCommand cmd = c.CreateCommand();
            cmd.CommandText = """
                INSERT INTO projects(dir, name, model_id, last_opened_utc, last_agent)
                VALUES($d,$n,$m,$t,$a)
                ON CONFLICT(dir) DO UPDATE SET name=$n, model_id=$m, last_opened_utc=$t, last_agent=$a;
                """;
            cmd.Parameters.AddWithValue("$d", Path.GetFullPath(directory));
            cmd.Parameters.AddWithValue("$n", name);
            cmd.Parameters.AddWithValue("$m", modelId);
            cmd.Parameters.AddWithValue("$t", DateTimeOffset.UtcNow.ToString("O"));
            cmd.Parameters.AddWithValue("$a", agent);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Most recently opened projects.</summary>
    public async Task<IReadOnlyList<ProjectRegistryEntry>> ListRecentAsync(int limit = 10, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using SqliteConnection c = await OpenAsync(ct).ConfigureAwait(false);
            SqliteCommand cmd = c.CreateCommand();
            cmd.CommandText = "SELECT dir,name,model_id,last_opened_utc,last_agent FROM projects ORDER BY last_opened_utc DESC LIMIT $l;";
            cmd.Parameters.AddWithValue("$l", limit);
            var list = new List<ProjectRegistryEntry>();
            await using SqliteDataReader r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await r.ReadAsync(ct).ConfigureAwait(false))
            {
                list.Add(new ProjectRegistryEntry(
                    r.GetString(0), r.GetString(1), r.GetString(2),
                    DateTimeOffset.Parse(r.GetString(3)), r.GetString(4)));
            }

            return list;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Stores a full project snapshot (checkpoint) before a risky change; keeps the latest 25.</summary>
    public async Task<long> AddCheckpointAsync(string directory, string author, string reason, string projectJson, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using SqliteConnection c = await OpenAsync(ct).ConfigureAwait(false);
            SqliteCommand cmd = c.CreateCommand();
            cmd.CommandText = "INSERT INTO checkpoints(dir, created_utc, author, reason, json) VALUES($d,$t,$a,$r,$j); SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("$d", Path.GetFullPath(directory));
            cmd.Parameters.AddWithValue("$t", DateTimeOffset.UtcNow.ToString("O"));
            cmd.Parameters.AddWithValue("$a", author);
            cmd.Parameters.AddWithValue("$r", reason);
            cmd.Parameters.AddWithValue("$j", projectJson);
            long id = (long)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;

            SqliteCommand prune = c.CreateCommand();
            prune.CommandText = "DELETE FROM checkpoints WHERE dir=$d AND id NOT IN (SELECT id FROM checkpoints WHERE dir=$d ORDER BY id DESC LIMIT 25);";
            prune.Parameters.AddWithValue("$d", Path.GetFullPath(directory));
            await prune.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return id;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Lists checkpoints (newest first).</summary>
    public async Task<IReadOnlyList<(long Id, DateTimeOffset CreatedUtc, string Author, string Reason)>> ListCheckpointsAsync(
        string directory, int limit = 10, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using SqliteConnection c = await OpenAsync(ct).ConfigureAwait(false);
            SqliteCommand cmd = c.CreateCommand();
            cmd.CommandText = "SELECT id, created_utc, author, reason FROM checkpoints WHERE dir=$d ORDER BY id DESC LIMIT $l;";
            cmd.Parameters.AddWithValue("$d", Path.GetFullPath(directory));
            cmd.Parameters.AddWithValue("$l", limit);
            var list = new List<(long, DateTimeOffset, string, string)>();
            await using SqliteDataReader r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await r.ReadAsync(ct).ConfigureAwait(false))
            {
                list.Add((r.GetInt64(0), DateTimeOffset.Parse(r.GetString(1)), r.GetString(2), r.GetString(3)));
            }

            return list;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Loads a checkpoint payload (for rollback via project_open/checkpoint restore).</summary>
    public async Task<string?> GetCheckpointJsonAsync(long id, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using SqliteConnection c = await OpenAsync(ct).ConfigureAwait(false);
            SqliteCommand cmd = c.CreateCommand();
            cmd.CommandText = "SELECT json FROM checkpoints WHERE id=$i;";
            cmd.Parameters.AddWithValue("$i", id);
            return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) as string;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Advisory lock acquisition for multi-agent safety. Same agent re-acquires to renew.</summary>
    public async Task<ProjectLockInfo> AcquireLockAsync(string directory, string agentId, int leaseSeconds = 300, CancellationToken ct = default)
    {
        string dir = Path.GetFullPath(directory);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using SqliteConnection c = await OpenAsync(ct).ConfigureAwait(false);
            SqliteCommand read = c.CreateCommand();
            read.CommandText = "SELECT agent_id, acquired_utc, lease_seconds FROM locks WHERE dir=$d;";
            read.Parameters.AddWithValue("$d", dir);
            string? holder = null;
            DateTimeOffset acquired = default;
            int lease = 0;
            await using (SqliteDataReader r = await read.ExecuteReaderAsync(ct).ConfigureAwait(false))
            {
                if (await r.ReadAsync(ct).ConfigureAwait(false))
                {
                    holder = r.GetString(0);
                    acquired = DateTimeOffset.Parse(r.GetString(1));
                    lease = r.GetInt32(2);
                }
            }

            bool held = holder is not null && holder != agentId && acquired.AddSeconds(lease) > DateTimeOffset.UtcNow;
            if (held)
            {
                return new ProjectLockInfo(dir, holder!, acquired, lease, false);
            }

            SqliteCommand up = c.CreateCommand();
            up.CommandText = """
                INSERT INTO locks(dir, agent_id, acquired_utc, lease_seconds) VALUES($d,$a,$t,$l)
                ON CONFLICT(dir) DO UPDATE SET agent_id=$a, acquired_utc=$t, lease_seconds=$l;
                """;
            up.Parameters.AddWithValue("$d", dir);
            up.Parameters.AddWithValue("$a", agentId);
            up.Parameters.AddWithValue("$t", DateTimeOffset.UtcNow.ToString("O"));
            up.Parameters.AddWithValue("$l", leaseSeconds);
            await up.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return new ProjectLockInfo(dir, agentId, DateTimeOffset.UtcNow, leaseSeconds, true);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Releases a lock (only the holder can release).</summary>
    public async Task<bool> ReleaseLockAsync(string directory, string agentId, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using SqliteConnection c = await OpenAsync(ct).ConfigureAwait(false);
            SqliteCommand cmd = c.CreateCommand();
            cmd.CommandText = "DELETE FROM locks WHERE dir=$d AND agent_id=$a;";
            cmd.Parameters.AddWithValue("$d", Path.GetFullPath(directory));
            cmd.Parameters.AddWithValue("$a", agentId);
            return await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false) > 0;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Current lock state (expired leases report as free).</summary>
    public async Task<ProjectLockInfo?> GetLockAsync(string directory, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using SqliteConnection c = await OpenAsync(ct).ConfigureAwait(false);
            SqliteCommand cmd = c.CreateCommand();
            cmd.CommandText = "SELECT agent_id, acquired_utc, lease_seconds FROM locks WHERE dir=$d;";
            cmd.Parameters.AddWithValue("$d", Path.GetFullPath(directory));
            await using SqliteDataReader r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (await r.ReadAsync(ct).ConfigureAwait(false))
            {
                var info = new ProjectLockInfo(Path.GetFullPath(directory), r.GetString(0), DateTimeOffset.Parse(r.GetString(1)), r.GetInt32(2), true);
                return info.AcquiredUtc.AddSeconds(info.LeaseSeconds) > DateTimeOffset.UtcNow ? info : null;
            }

            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync() => await Task.CompletedTask;
}
