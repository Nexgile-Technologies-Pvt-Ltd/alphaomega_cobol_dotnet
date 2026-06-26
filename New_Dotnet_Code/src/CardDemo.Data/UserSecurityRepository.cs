using CardDemo.Cobol.Runtime;
using CardDemo.Domain;
using Microsoft.Data.Sqlite;

namespace CardDemo.Data;

/// <summary>
/// Relational repository for the USER_SECURITY table (copybook CSUSR01Y). Primary key <c>usr_id</c>.
/// Exposes the VSAM-semantics operations over <see cref="UserSecurity"/>.
/// </summary>
public sealed class UserSecurityRepository : RepositoryBase
{
    private const string Table = "USER_SECURITY";
    private const string Cols = "usr_id, first_name, last_name, pwd, usr_type";
    private static readonly string[] KeyCols = { "usr_id" };

    public UserSecurityRepository(RelationalDb db) : base(db) { }
    public UserSecurityRepository(SqliteConnection connection) : base(connection) { }

    /// <summary>Random read by primary key. Returns '00' or '23'.</summary>
    public string ReadByKey(string usrId, out UserSecurity? user)
    {
        using SqliteCommand c = Cmd($"SELECT {Cols} FROM {Table} WHERE usr_id = @k");
        c.Parameters.AddWithValue("@k", usrId);
        using SqliteDataReader rd = c.ExecuteReader();
        if (rd.Read()) { user = Map(rd); return FileStatus.Ok; }
        user = null;
        return FileStatus.RecordNotFound;
    }

    /// <summary>Inserts a new row. Returns '00' or '22'.</summary>
    public string Insert(UserSecurity x)
    {
        using SqliteCommand c = Cmd(
            $"INSERT INTO {Table} ({Cols}) VALUES (@usr_id, @first_name, @last_name, @pwd, @usr_type)");
        Bind(c, x);
        try { c.ExecuteNonQuery(); return FileStatus.Ok; }
        catch (SqliteException e) when (e.SqliteErrorCode == SqliteConstraint) { return FileStatus.DuplicateKeyError; }
    }

    /// <summary>Updates an existing row by primary key. Returns '00' or '23'.</summary>
    public string Update(UserSecurity x)
    {
        using SqliteCommand c = Cmd(
            $"UPDATE {Table} SET first_name=@first_name, last_name=@last_name, pwd=@pwd, usr_type=@usr_type " +
            "WHERE usr_id=@usr_id");
        Bind(c, x);
        return c.ExecuteNonQuery() > 0 ? FileStatus.Ok : FileStatus.RecordNotFound;
    }

    /// <summary>Deletes by primary key. Returns '00' or '23'.</summary>
    public string Delete(string usrId)
    {
        using SqliteCommand c = Cmd($"DELETE FROM {Table} WHERE usr_id = @k");
        c.Parameters.AddWithValue("@k", usrId);
        return c.ExecuteNonQuery() > 0 ? FileStatus.Ok : FileStatus.RecordNotFound;
    }

    /// <summary>Positions a browse at-or-after the given primary key (null/none = first row).</summary>
    public void StartBrowse(string? usrId = null)
    {
        if (usrId is null) StartBrowseAt();
        else StartBrowseAt(usrId);
    }

    /// <summary>Positions a browse strictly after the given primary key.</summary>
    public void StartBrowseAfterKey(string usrId) => StartBrowseAfter(usrId);

    /// <summary>Reads the next row in ascending primary-key order. Returns '00' or '10'.</summary>
    public string ReadNext(out UserSecurity? user)
        => Advance(true, Cols, Table, KeyCols, Map, out user);

    /// <summary>Reads the previous row in descending primary-key order. Returns '00' or '10'.</summary>
    public string ReadPrevious(out UserSecurity? user)
        => Advance(false, Cols, Table, KeyCols, Map, out user);

    /// <summary>Ends the current browse.</summary>
    public void EndBrowse() => EndBrowseCore();

    /// <summary>All rows in ascending primary-key order.</summary>
    public IEnumerable<UserSecurity> ReadAll()
    {
        using SqliteCommand c = Cmd($"SELECT {Cols} FROM {Table} ORDER BY usr_id ASC");
        using SqliteDataReader rd = c.ExecuteReader();
        while (rd.Read()) yield return Map(rd);
    }

    /// <summary>Maps the current reader row to a <see cref="UserSecurity"/>.</summary>
    private static UserSecurity Map(SqliteDataReader rd) => new()
    {
        UsrId = rd.GetText("usr_id"),
        FirstName = rd.GetText("first_name"),
        LastName = rd.GetText("last_name"),
        Pwd = rd.GetText("pwd"),
        UsrType = rd.GetText("usr_type"),
    };

    /// <summary>Binds a <see cref="UserSecurity"/> onto the named parameters of an insert/update command.</summary>
    private static void Bind(SqliteCommand c, UserSecurity x)
    {
        c.Parameters.AddWithValue("@usr_id", x.UsrId);
        c.Parameters.AddWithValue("@first_name", x.FirstName);
        c.Parameters.AddWithValue("@last_name", x.LastName);
        c.Parameters.AddWithValue("@pwd", x.Pwd);
        c.Parameters.AddWithValue("@usr_type", x.UsrType);
    }
}
