using CardDemo.Cobol.Runtime;
using Microsoft.Data.Sqlite;

namespace CardDemo.Data;

/// <summary>
/// A QSAM sequential file (e.g. DALYTRAN input, DALYREJS / SYSTRAN output) backed by a SQLite table of
/// fixed-length record images in insertion order. Records are stored byte-exact; the whole dataset can
/// be materialized back to a contiguous byte image for golden-master comparison.
/// </summary>
public sealed class SequentialFile
{
    private readonly SqliteConnection _conn;
    private readonly string _t;
    private readonly int _recordLength;
    private long _readCursor;

    internal SequentialFile(SqliteConnection conn, string name, int recordLength)
    {
        _conn = conn;
        _t = name;
        _recordLength = recordLength;
    }

    /// <summary>Fixed record length in bytes.</summary>
    public int RecordLength => _recordLength;

    /// <summary>Opens the file for input and rewinds the read cursor to the start.</summary>
    public void OpenInput() => _readCursor = 0;

    /// <summary>Opens the file for output (DISP=NEW): clears any existing records.</summary>
    public void OpenOutput()
    {
        using SqliteCommand c = Command($"DELETE FROM \"{_t}\"");
        c.ExecuteNonQuery();
        _readCursor = 0;
    }

    /// <summary>Reads the next record in sequence. Returns "00" or "10" (end of file).</summary>
    public string Read(out byte[]? image)
    {
        using SqliteCommand c = Command($"SELECT seq, image FROM \"{_t}\" WHERE seq > @s ORDER BY seq LIMIT 1");
        c.Parameters.AddWithValue("@s", _readCursor);
        using SqliteDataReader rd = c.ExecuteReader();
        if (rd.Read())
        {
            _readCursor = (long)rd.GetValue(0);
            image = (byte[])rd.GetValue(1);
            return FileStatus.Ok;
        }
        image = null;
        return FileStatus.EndOfFile;
    }

    /// <summary>Appends a record. Returns "00".</summary>
    public string Write(byte[] image)
    {
        if (image.Length != _recordLength)
            throw new ArgumentException($"{_t}: record length {image.Length} != defined {_recordLength}.", nameof(image));
        using SqliteCommand c = Command($"INSERT INTO \"{_t}\"(image) VALUES(@i)");
        c.Parameters.AddWithValue("@i", image);
        c.ExecuteNonQuery();
        return FileStatus.Ok;
    }

    /// <summary>Number of records currently in the file.</summary>
    public long Count()
    {
        using SqliteCommand c = Command($"SELECT COUNT(*) FROM \"{_t}\"");
        return (long)c.ExecuteScalar()!;
    }

    /// <summary>Loads a fixed-length record stream (e.g. an EBCDIC dataset image) by appending each record.</summary>
    public void LoadImage(byte[] datasetImage)
    {
        if (datasetImage.Length % _recordLength != 0)
            throw new ArgumentException(
                $"{_t}: dataset length {datasetImage.Length} is not a multiple of record length {_recordLength}.");
        for (int i = 0; i < datasetImage.Length; i += _recordLength)
            Write(datasetImage[i..(i + _recordLength)]);
    }

    /// <summary>Materializes the whole file as a contiguous byte image (records concatenated in order).</summary>
    public byte[] ToImage()
    {
        var result = new byte[checked((int)(Count() * _recordLength))];
        int offset = 0;
        using SqliteCommand c = Command($"SELECT image FROM \"{_t}\" ORDER BY seq");
        using SqliteDataReader rd = c.ExecuteReader();
        while (rd.Read())
        {
            var image = (byte[])rd.GetValue(0);
            image.CopyTo(result, offset);
            offset += _recordLength;
        }
        return result;
    }

    private SqliteCommand Command(string sql)
    {
        SqliteCommand c = _conn.CreateCommand();
        c.CommandText = sql;
        return c;
    }
}
