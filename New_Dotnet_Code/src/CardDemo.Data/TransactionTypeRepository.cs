using CardDemo.Runtime;
using CardDemo.Domain;
using Microsoft.Data.Sqlite;

namespace CardDemo.Data;

/// <summary>
/// Relational repository for the optional-module TRANSACTION_TYPE table (DB2 DDL TRNTYPE.ddl; already
/// relational). Primary key <c>TR_TYPE</c> CHAR(2). Distinct from the base-app VSAM TRAN_TYPE
/// (CVTRA03Y) — same shape, different module/source. Exposes the VSAM-semantics operations over
/// <see cref="TransactionType"/>, each returning the exact two-character <see cref="FileStatus"/>.
/// </summary>
public sealed class TransactionTypeRepository : RepositoryBase
{
    private const string Table = "TRANSACTION_TYPE";
    private const string Cols = "TR_TYPE, TR_DESCRIPTION";
    private static readonly string[] KeyCols = { "TR_TYPE" };

    public TransactionTypeRepository(RelationalDb db) : base(db) { }
    public TransactionTypeRepository(SqliteConnection connection) : base(connection) { }

    /// <summary>Random read by primary key. Returns '00' or '23'.</summary>
    public string ReadByKey(string trType, out TransactionType? type)
    {
        using SqliteCommand c = Cmd($"SELECT {Cols} FROM {Table} WHERE TR_TYPE = @k");
        c.Parameters.AddWithValue("@k", trType);
        using SqliteDataReader rd = c.ExecuteReader();
        if (rd.Read()) { type = Map(rd); return FileStatus.Ok; }
        type = null;
        return FileStatus.RecordNotFound;
    }

    /// <summary>Inserts a new row. Returns '00' or '22'.</summary>
    public string Insert(TransactionType x)
    {
        using SqliteCommand c = Cmd($"INSERT INTO {Table} ({Cols}) VALUES (@TR_TYPE, @TR_DESCRIPTION)");
        Bind(c, x);
        try { c.ExecuteNonQuery(); return FileStatus.Ok; }
        catch (SqliteException e) when (e.SqliteErrorCode == SqliteConstraint) { return FileStatus.DuplicateKeyError; }
    }

    /// <summary>Updates an existing row by primary key. Returns '00' or '23'.</summary>
    public string Update(TransactionType x)
    {
        using SqliteCommand c = Cmd($"UPDATE {Table} SET TR_DESCRIPTION=@TR_DESCRIPTION WHERE TR_TYPE=@TR_TYPE");
        Bind(c, x);
        return c.ExecuteNonQuery() > 0 ? FileStatus.Ok : FileStatus.RecordNotFound;
    }

    /// <summary>Deletes by primary key. Returns '00' or '23'.</summary>
    public string Delete(string trType)
    {
        using SqliteCommand c = Cmd($"DELETE FROM {Table} WHERE TR_TYPE = @k");
        c.Parameters.AddWithValue("@k", trType);
        return c.ExecuteNonQuery() > 0 ? FileStatus.Ok : FileStatus.RecordNotFound;
    }

    /// <summary>Positions a browse at-or-after the given primary key (null/none = first row).</summary>
    public void StartBrowse(string? trType = null)
    {
        if (trType is null) StartBrowseAt();
        else StartBrowseAt(trType);
    }

    /// <summary>Positions a browse strictly after the given primary key.</summary>
    public void StartBrowseAfterKey(string trType) => StartBrowseAfter(trType);

    /// <summary>Reads the next row in ascending primary-key order. Returns '00' or '10'.</summary>
    public string ReadNext(out TransactionType? type)
        => Advance(true, Cols, Table, KeyCols, Map, out type);

    /// <summary>Reads the previous row in descending primary-key order. Returns '00' or '10'.</summary>
    public string ReadPrevious(out TransactionType? type)
        => Advance(false, Cols, Table, KeyCols, Map, out type);

    /// <summary>Ends the current browse.</summary>
    public void EndBrowse() => EndBrowseCore();

    /// <summary>All rows in ascending primary-key order.</summary>
    public IEnumerable<TransactionType> ReadAll()
    {
        using SqliteCommand c = Cmd($"SELECT {Cols} FROM {Table} ORDER BY TR_TYPE ASC");
        using SqliteDataReader rd = c.ExecuteReader();
        while (rd.Read()) yield return Map(rd);
    }

    /// <summary>Maps the current reader row to a <see cref="TransactionType"/>.</summary>
    private static TransactionType Map(SqliteDataReader rd) => new()
    {
        TrType = rd.GetText("TR_TYPE"),
        TrDescription = rd.GetText("TR_DESCRIPTION"),
    };

    /// <summary>Binds a <see cref="TransactionType"/> onto the named parameters of an insert/update command.</summary>
    private static void Bind(SqliteCommand c, TransactionType x)
    {
        c.Parameters.AddWithValue("@TR_TYPE", x.TrType);
        c.Parameters.AddWithValue("@TR_DESCRIPTION", x.TrDescription);
    }
}
