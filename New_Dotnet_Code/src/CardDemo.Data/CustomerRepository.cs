using CardDemo.Cobol.Runtime;
using CardDemo.Domain;
using Microsoft.Data.Sqlite;

namespace CardDemo.Data;

/// <summary>
/// Relational repository for the CUSTOMER table (copybook CVCUS01Y). Primary key <c>cust_id</c>.
/// Exposes the VSAM-semantics operations over <see cref="Customer"/>.
/// </summary>
public sealed class CustomerRepository : RepositoryBase
{
    private const string Table = "CUSTOMER";
    private const string Cols =
        "cust_id, first_name, middle_name, last_name, addr_line_1, addr_line_2, addr_line_3, " +
        "addr_state_cd, addr_country_cd, addr_zip, phone_num_1, phone_num_2, ssn, govt_issued_id, " +
        "dob_yyyy_mm_dd, eft_account_id, pri_card_holder_ind, fico_credit_score";
    private static readonly string[] KeyCols = { "cust_id" };

    public CustomerRepository(RelationalDb db) : base(db) { }
    public CustomerRepository(SqliteConnection connection) : base(connection) { }

    /// <summary>Random read by primary key. Returns '00' or '23'.</summary>
    public string ReadByKey(long custId, out Customer? customer)
    {
        using SqliteCommand c = Cmd($"SELECT {Cols} FROM {Table} WHERE cust_id = @id");
        c.Parameters.AddWithValue("@id", custId);
        using SqliteDataReader rd = c.ExecuteReader();
        if (rd.Read()) { customer = Map(rd); return FileStatus.Ok; }
        customer = null;
        return FileStatus.RecordNotFound;
    }

    /// <summary>Inserts a new row. Returns '00' or '22'.</summary>
    public string Insert(Customer x)
    {
        using SqliteCommand c = Cmd(
            $"INSERT INTO {Table} ({Cols}) VALUES " +
            "(@cust_id, @first_name, @middle_name, @last_name, @addr_line_1, @addr_line_2, @addr_line_3, " +
            "@addr_state_cd, @addr_country_cd, @addr_zip, @phone_num_1, @phone_num_2, @ssn, @govt_issued_id, " +
            "@dob_yyyy_mm_dd, @eft_account_id, @pri_card_holder_ind, @fico_credit_score)");
        Bind(c, x);
        try { c.ExecuteNonQuery(); return FileStatus.Ok; }
        catch (SqliteException e) when (e.SqliteErrorCode == SqliteConstraint) { return FileStatus.DuplicateKeyError; }
    }

    /// <summary>Updates an existing row by primary key. Returns '00' or '23'.</summary>
    public string Update(Customer x)
    {
        using SqliteCommand c = Cmd(
            $"UPDATE {Table} SET first_name=@first_name, middle_name=@middle_name, last_name=@last_name, " +
            "addr_line_1=@addr_line_1, addr_line_2=@addr_line_2, addr_line_3=@addr_line_3, " +
            "addr_state_cd=@addr_state_cd, addr_country_cd=@addr_country_cd, addr_zip=@addr_zip, " +
            "phone_num_1=@phone_num_1, phone_num_2=@phone_num_2, ssn=@ssn, govt_issued_id=@govt_issued_id, " +
            "dob_yyyy_mm_dd=@dob_yyyy_mm_dd, eft_account_id=@eft_account_id, " +
            "pri_card_holder_ind=@pri_card_holder_ind, fico_credit_score=@fico_credit_score " +
            "WHERE cust_id=@cust_id");
        Bind(c, x);
        return c.ExecuteNonQuery() > 0 ? FileStatus.Ok : FileStatus.RecordNotFound;
    }

    /// <summary>Deletes by primary key. Returns '00' or '23'.</summary>
    public string Delete(long custId)
    {
        using SqliteCommand c = Cmd($"DELETE FROM {Table} WHERE cust_id = @id");
        c.Parameters.AddWithValue("@id", custId);
        return c.ExecuteNonQuery() > 0 ? FileStatus.Ok : FileStatus.RecordNotFound;
    }

    /// <summary>Positions a browse at-or-after the given primary key (null/none = first row).</summary>
    public void StartBrowse(long? custId = null)
    {
        if (custId is null) StartBrowseAt();
        else StartBrowseAt(custId.Value);
    }

    /// <summary>Positions a browse strictly after the given primary key.</summary>
    public void StartBrowseAfterKey(long custId) => StartBrowseAfter(custId);

    /// <summary>Reads the next row in ascending primary-key order. Returns '00' or '10'.</summary>
    public string ReadNext(out Customer? customer)
        => Advance(true, Cols, Table, KeyCols, Map, out customer);

    /// <summary>Reads the previous row in descending primary-key order. Returns '00' or '10'.</summary>
    public string ReadPrevious(out Customer? customer)
        => Advance(false, Cols, Table, KeyCols, Map, out customer);

    /// <summary>Ends the current browse.</summary>
    public void EndBrowse() => EndBrowseCore();

    /// <summary>All rows in ascending primary-key order.</summary>
    public IEnumerable<Customer> ReadAll()
    {
        using SqliteCommand c = Cmd($"SELECT {Cols} FROM {Table} ORDER BY cust_id ASC");
        using SqliteDataReader rd = c.ExecuteReader();
        while (rd.Read()) yield return Map(rd);
    }

    /// <summary>Maps the current reader row to a <see cref="Customer"/>.</summary>
    private static Customer Map(SqliteDataReader rd) => new()
    {
        CustId = rd.GetInt64("cust_id"),
        FirstName = rd.GetText("first_name"),
        MiddleName = rd.GetText("middle_name"),
        LastName = rd.GetText("last_name"),
        AddrLine1 = rd.GetText("addr_line_1"),
        AddrLine2 = rd.GetText("addr_line_2"),
        AddrLine3 = rd.GetText("addr_line_3"),
        AddrStateCd = rd.GetText("addr_state_cd"),
        AddrCountryCd = rd.GetText("addr_country_cd"),
        AddrZip = rd.GetText("addr_zip"),
        PhoneNum1 = rd.GetText("phone_num_1"),
        PhoneNum2 = rd.GetText("phone_num_2"),
        Ssn = rd.GetInt64("ssn"),
        GovtIssuedId = rd.GetText("govt_issued_id"),
        DobYyyyMmDd = rd.GetText("dob_yyyy_mm_dd"),
        EftAccountId = rd.GetText("eft_account_id"),
        PriCardHolderInd = rd.GetText("pri_card_holder_ind"),
        FicoCreditScore = rd.GetInt32("fico_credit_score"),
    };

    /// <summary>Binds a <see cref="Customer"/> onto the named parameters of an insert/update command.</summary>
    private static void Bind(SqliteCommand c, Customer x)
    {
        c.Parameters.AddWithValue("@cust_id", x.CustId);
        c.Parameters.AddWithValue("@first_name", x.FirstName);
        c.Parameters.AddWithValue("@middle_name", x.MiddleName);
        c.Parameters.AddWithValue("@last_name", x.LastName);
        c.Parameters.AddWithValue("@addr_line_1", x.AddrLine1);
        c.Parameters.AddWithValue("@addr_line_2", x.AddrLine2);
        c.Parameters.AddWithValue("@addr_line_3", x.AddrLine3);
        c.Parameters.AddWithValue("@addr_state_cd", x.AddrStateCd);
        c.Parameters.AddWithValue("@addr_country_cd", x.AddrCountryCd);
        c.Parameters.AddWithValue("@addr_zip", x.AddrZip);
        c.Parameters.AddWithValue("@phone_num_1", x.PhoneNum1);
        c.Parameters.AddWithValue("@phone_num_2", x.PhoneNum2);
        c.Parameters.AddWithValue("@ssn", x.Ssn);
        c.Parameters.AddWithValue("@govt_issued_id", x.GovtIssuedId);
        c.Parameters.AddWithValue("@dob_yyyy_mm_dd", x.DobYyyyMmDd);
        c.Parameters.AddWithValue("@eft_account_id", x.EftAccountId);
        c.Parameters.AddWithValue("@pri_card_holder_ind", x.PriCardHolderInd);
        c.Parameters.AddWithValue("@fico_credit_score", x.FicoCreditScore);
    }
}
