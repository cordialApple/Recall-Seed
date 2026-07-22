using Microsoft.Data.Sqlite;

namespace PersonalServer.Data;

/// <summary>
/// Opens a connection, converting expected failures — a missing db, or a db that exists but is
/// locked/corrupt (SqliteException from open or a startup pragma) — into an error string the caller
/// returns as structured output instead of throwing over stdio. Connections already have
/// busy_timeout and foreign_keys = ON set by <see cref="Db"/>.
/// </summary>
internal static class DbGuard
{
    public static (SqliteConnection? Conn, string? Error) OpenReadOnly(string? path = null)
        => Guard(() => Db.OpenReadOnly(path));

    public static (SqliteConnection? Conn, string? Error) OpenReadWrite(string? path = null)
        => Guard(() => Db.OpenReadWrite(path));

    static (SqliteConnection? Conn, string? Error) Guard(Func<SqliteConnection> open)
    {
        try { return (open(), null); }
        catch (DbNotFoundException ex) { return (null, ex.Message); }
        catch (SqliteException ex) { return (null, ex.Message); }
    }
}
