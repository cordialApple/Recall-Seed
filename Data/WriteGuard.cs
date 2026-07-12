using Microsoft.Data.Sqlite;

namespace PersonalServer.Data;

/// <summary>
/// Opens a read-write connection, converting the expected "database not found" case into an
/// error string the caller returns as structured output instead of throwing over stdio. The
/// connection already has busy_timeout and foreign_keys = ON set by <see cref="Db"/>.
/// </summary>
public static class WriteGuard
{
    public static (SqliteConnection? Conn, string? Error) OpenReadWrite()
    {
        try { return (Db.OpenReadWrite(), null); }
        catch (DbNotFoundException ex) { return (null, ex.Message); }
    }
}
