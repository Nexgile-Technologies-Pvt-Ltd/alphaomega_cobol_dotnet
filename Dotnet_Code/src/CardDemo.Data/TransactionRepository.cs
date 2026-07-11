using CardDemo.Runtime;
using CardDemo.Domain;
using Microsoft.Data.Sqlite;

namespace CardDemo.Data;

/// <summary>
/// Relational repository for the TRANSACTION table (copybook CVTRA05Y). Primary key <c>tran_id</c>.
/// Exposes the VSAM-semantics operations over <see cref="Transaction"/>. The table name and the
/// <c>desc</c> column are SQL keywords, so both are quoted in every statement.
/// </summary>
public sealed class TransactionRepository : RepositoryBase
{
    private const string Table = "\"TRANSACTION\"";
    private const string Cols =
        "tran_id, type_cd, cat_cd, source, \"desc\", amt, merchant_id, merchant_name, merchant_city, " +
        "merchant_zip, card_num, orig_ts, proc_ts";
    private static readonly string[] KeyCols = { "tran_id" };

    public TransactionRepository(RelationalDb db) : base(db) { }
    public TransactionRepository(SqliteConnection connection) : base(connection) { }

    /// <summary>Random read by primary key. Returns '00' or '23'.</summary>
    public string ReadByKey(string tranId, out Transaction? transaction)
    {
        using SqliteCommand c = Cmd($"SELECT {Cols} FROM {Table} WHERE tran_id = @k");
        c.Parameters.AddWithValue("@k", tranId);
        using SqliteDataReader rd = c.ExecuteReader();
        if (rd.Read()) { transaction = Map(rd); return FileStatus.Ok; }
        transaction = null;
        return FileStatus.RecordNotFound;
    }

    /// <summary>Inserts a new row. Returns '00' or '22'.</summary>
    public string Insert(Transaction x)
    {
        using SqliteCommand c = Cmd(
            $"INSERT INTO {Table} ({Cols}) VALUES " +
            "(@tran_id, @type_cd, @cat_cd, @source, @desc, @amt, @merchant_id, @merchant_name, " +
            "@merchant_city, @merchant_zip, @card_num, @orig_ts, @proc_ts)");
        Bind(c, x);
        try { c.ExecuteNonQuery(); return FileStatus.Ok; }
        catch (SqliteException e) when (e.SqliteErrorCode == SqliteConstraint) { return FileStatus.DuplicateKeyError; }
    }

    /// <summary>Updates an existing row by primary key. Returns '00' or '23'.</summary>
    public string Update(Transaction x)
    {
        using SqliteCommand c = Cmd(
            $"UPDATE {Table} SET type_cd=@type_cd, cat_cd=@cat_cd, source=@source, \"desc\"=@desc, amt=@amt, " +
            "merchant_id=@merchant_id, merchant_name=@merchant_name, merchant_city=@merchant_city, " +
            "merchant_zip=@merchant_zip, card_num=@card_num, orig_ts=@orig_ts, proc_ts=@proc_ts " +
            "WHERE tran_id=@tran_id");
        Bind(c, x);
        return c.ExecuteNonQuery() > 0 ? FileStatus.Ok : FileStatus.RecordNotFound;
    }

    /// <summary>Deletes by primary key. Returns '00' or '23'.</summary>
    public string Delete(string tranId)
    {
        using SqliteCommand c = Cmd($"DELETE FROM {Table} WHERE tran_id = @k");
        c.Parameters.AddWithValue("@k", tranId);
        return c.ExecuteNonQuery() > 0 ? FileStatus.Ok : FileStatus.RecordNotFound;
    }

    /// <summary>Positions a browse at-or-after the given primary key (null/none = first row).</summary>
    public void StartBrowse(string? tranId = null)
    {
        if (tranId is null) StartBrowseAt();
        else StartBrowseAt(tranId);
    }

    /// <summary>Positions a browse strictly after the given primary key.</summary>
    public void StartBrowseAfterKey(string tranId) => StartBrowseAfter(tranId);

    /// <summary>Reads the next row in ascending primary-key order. Returns '00' or '10'.</summary>
    public string ReadNext(out Transaction? transaction)
        => Advance(true, Cols, Table, KeyCols, Map, out transaction);

    /// <summary>Reads the previous row in descending primary-key order. Returns '00' or '10'.</summary>
    public string ReadPrevious(out Transaction? transaction)
        => Advance(false, Cols, Table, KeyCols, Map, out transaction);

    /// <summary>Ends the current browse.</summary>
    public void EndBrowse() => EndBrowseCore();

    /// <summary>All rows in ascending primary-key order.</summary>
    public IEnumerable<Transaction> ReadAll()
    {
        using SqliteCommand c = Cmd($"SELECT {Cols} FROM {Table} ORDER BY tran_id ASC");
        using SqliteDataReader rd = c.ExecuteReader();
        while (rd.Read()) yield return Map(rd);
    }

    /// <summary>Maps the current reader row to a <see cref="Transaction"/>.</summary>
    private static Transaction Map(SqliteDataReader rd) => new()
    {
        TranId = rd.GetText("tran_id"),
        TypeCd = rd.GetText("type_cd"),
        CatCd = rd.GetInt32("cat_cd"),
        Source = rd.GetText("source"),
        Desc = rd.GetText("desc"),
        Amt = rd.GetMoney("amt"),
        MerchantId = rd.GetInt64("merchant_id"),
        MerchantName = rd.GetText("merchant_name"),
        MerchantCity = rd.GetText("merchant_city"),
        MerchantZip = rd.GetText("merchant_zip"),
        CardNum = rd.GetText("card_num"),
        OrigTs = rd.GetText("orig_ts"),
        ProcTs = rd.GetText("proc_ts"),
    };

    /// <summary>Binds a <see cref="Transaction"/> onto the named parameters of an insert/update command.</summary>
    private static void Bind(SqliteCommand c, Transaction x)
    {
        c.Parameters.AddWithValue("@tran_id", x.TranId);
        c.Parameters.AddWithValue("@type_cd", x.TypeCd);
        c.Parameters.AddWithValue("@cat_cd", x.CatCd);
        c.Parameters.AddWithValue("@source", x.Source);
        c.Parameters.AddWithValue("@desc", x.Desc);
        c.Parameters.AddWithValue("@amt", x.Amt);
        c.Parameters.AddWithValue("@merchant_id", x.MerchantId);
        c.Parameters.AddWithValue("@merchant_name", x.MerchantName);
        c.Parameters.AddWithValue("@merchant_city", x.MerchantCity);
        c.Parameters.AddWithValue("@merchant_zip", x.MerchantZip);
        c.Parameters.AddWithValue("@card_num", x.CardNum);
        c.Parameters.AddWithValue("@orig_ts", x.OrigTs);
        c.Parameters.AddWithValue("@proc_ts", x.ProcTs);
    }
}
