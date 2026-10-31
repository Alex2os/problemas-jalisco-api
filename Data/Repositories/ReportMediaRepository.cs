using JaliscoProblemsAPI.Models;
using JaliscoProblemsAPI.Services;
using JaliscoProblemsAPI.Infrastructure;
using Npgsql;
namespace JaliscoProblemsAPI.Data.Repositories;

public sealed partial class ReportRepository
{

    private static async Task<(string Status, int Version)> LockOwnerAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, int id, int user, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("SELECT user_id,status,status_version FROM reports WHERE id=$1 FOR UPDATE", connection, transaction);
        cmd.Parameters.AddWithValue(id);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new ReportOperationException(404, "El reporte no existe.");
        if (reader.GetInt32(0) != user) throw new ReportOperationException(403, "Solo el autor puede modificar este reporte.");
        return (reader.GetString(1), reader.GetInt32(2));
    }

    public static bool ValidTransition(string current, string next) => (current, next) switch
    {
        ("reported", "still_present" or "resolved") => true,
        ("still_present", "still_present" or "resolved") => true,
        ("resolved", "still_present") => true,
        _ => false
    };

    public async Task ChangeStatusAsync(int id, int user, string status, int expectedVersion, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var current = await LockOwnerAsync(connection, transaction, id, user, ct);
        if (current.Version != expectedVersion) throw new ReportOperationException(409, "El estado cambió. Actualiza el reporte antes de continuar.");
        if (current.Status == status && status != "still_present") return; // Idempotent retry; no fabricated history.
        if (!ValidTransition(current.Status, status)) throw new ReportOperationException(400, "El cambio de estado no es válido.");
        await using var cmd = new NpgsqlCommand("UPDATE reports SET status=$1,status_version=status_version+1,status_updated_at=clock_timestamp() WHERE id=$2", connection, transaction);
        cmd.Parameters.AddWithValue(status);
        cmd.Parameters.AddWithValue(id);
        await cmd.ExecuteNonQueryAsync(ct); // The database trigger writes history in the same transaction.
        await transaction.CommitAsync(ct);
    }

    public async Task<List<ReportStatusEntry>> GetStatusHistoryAsync(int id, int? beforeVersion, int limit, CancellationToken ct)
    {
        await using var cmd = dataSource.CreateCommand("""
            SELECT h.id,h.previous_status,h.status,h.actor_id,u.username,h.changed_at,h.version,h.source
            FROM report_status_history h LEFT JOIN users u ON u.id=h.actor_id
            WHERE h.report_id=$1 AND h.version < $2 ORDER BY h.version DESC LIMIT $3
            """);
        cmd.Parameters.AddWithValue(id);
        cmd.Parameters.AddWithValue(beforeVersion ?? int.MaxValue);
        cmd.Parameters.AddWithValue(limit);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var items = new List<ReportStatusEntry>();
        while (await reader.ReadAsync(ct))
        {
            items.Add(new ReportStatusEntry(
                reader.GetInt64(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetInt32(3), reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetDateTime(5), reader.GetInt32(6), reader.GetString(7)));
        }
        return items;
    }

    public async Task<int> CreateWithPhotosAsync(Report report, IReadOnlyList<StoredPhoto> photos, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using var cmd = new NpgsqlCommand("INSERT INTO reports(longitude,latitude,ubication,description,type,user_id,title) VALUES($1,$2,$3,$4,$5,$6,$7) RETURNING id", connection, transaction);
        cmd.Parameters.AddWithValue(report.Longitude);
        cmd.Parameters.AddWithValue(report.Latitude);
        cmd.Parameters.AddWithValue(report.Ubication);
        cmd.Parameters.AddWithValue(report.Description);
        cmd.Parameters.AddWithValue(report.Type);
        cmd.Parameters.AddWithValue(report.UserId);
        cmd.Parameters.AddWithValue(NpgsqlTypes.NpgsqlDbType.Varchar, (object?)report.Title ?? DBNull.Value);
        var id = (int)(await cmd.ExecuteScalarAsync(ct))!;
        await InsertPhotosAsync(connection, transaction, id, report.UserId, photos, ct);
        await transaction.CommitAsync(ct);
        return id;
    }

    public async Task AddPhotosAsync(int id, int user, IReadOnlyList<StoredPhoto> photos, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await LockOwnerAsync(connection, transaction, id, user, ct);
        await InsertPhotosAsync(connection, transaction, id, user, photos, ct);
        await transaction.CommitAsync(ct);
    }

    private static async Task InsertPhotosAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, int id, int user, IReadOnlyList<StoredPhoto> photos, CancellationToken ct)
    {
        await using var slotsCmd = new NpgsqlCommand("SELECT display_order FROM report_images WHERE report_id=$1", connection, transaction);
        slotsCmd.Parameters.AddWithValue(id);
        var occupied = new HashSet<int>();
        await using (var reader = await slotsCmd.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct)) occupied.Add(reader.GetInt16(0));
        var slots = Enumerable.Range(1, 3).Where(slot => !occupied.Contains(slot)).ToArray();
        if (photos.Count > slots.Length) throw new ReportOperationException(409, "El reporte admite un máximo de 3 fotos. Actualiza para ver las fotos actuales.");
        for (var i = 0; i < photos.Count; i++)
        {
            await using var cmd = new NpgsqlCommand("INSERT INTO report_images(report_id,user_id,storage_key,content_type,byte_size,display_order) VALUES($1,$2,$3,'image/jpeg',$4,$5)", connection, transaction);
            cmd.Parameters.AddWithValue(id);
            cmd.Parameters.AddWithValue(user);
            cmd.Parameters.AddWithValue(photos[i].Key);
            cmd.Parameters.AddWithValue(photos[i].Size);
            cmd.Parameters.AddWithValue((short)slots[i]);
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    public async Task<string> DeletePhotoAsync(int id, int photo, int user, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await LockOwnerAsync(connection, transaction, id, user, ct);
        await using var cmd = new NpgsqlCommand("DELETE FROM report_images WHERE id=$1 AND report_id=$2 RETURNING storage_key", connection, transaction);
        cmd.Parameters.AddWithValue(photo);
        cmd.Parameters.AddWithValue(id);
        var key = await cmd.ExecuteScalarAsync(ct) as string;
        if (key is null) throw new ReportOperationException(404, "La foto no existe.");
        await transaction.CommitAsync(ct);
        return key;
    }

    public async Task<string?> GetPhotoKeyAsync(int id, int photo, CancellationToken ct)
    {
        await using var cmd = dataSource.CreateCommand("SELECT storage_key FROM report_images WHERE report_id=$1 AND id=$2");
        cmd.Parameters.AddWithValue(id);
        cmd.Parameters.AddWithValue(photo);
        return await cmd.ExecuteScalarAsync(ct) as string;
    }

    public async Task<bool> IsPhotoReferencedAsync(string key, CancellationToken cancellationToken)
    {
        await using var cmd = dataSource.CreateCommand("SELECT EXISTS(SELECT 1 FROM report_images WHERE storage_key=$1)");
        cmd.Parameters.AddWithValue(key);
        return (bool)(await cmd.ExecuteScalarAsync(cancellationToken))!;
    }
}
