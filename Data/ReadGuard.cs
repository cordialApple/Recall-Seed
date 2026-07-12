using Microsoft.Data.Sqlite;

namespace PersonalServer.Data;

/// <summary>
/// Opens a read-only connection, converting the expected "database not found" case into an error
/// string the caller returns as structured output instead of throwing over stdio.
/// </summary>
public static class ReadGuard
{
    public static (SqliteConnection? Conn, string? Error) OpenReadOnly()
    {
        try { return (Db.OpenReadOnly(), null); }
        catch (DbNotFoundException ex) { return (null, ex.Message); }
    }
}
