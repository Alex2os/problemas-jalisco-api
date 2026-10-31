using JaliscoProblemsAPI.Models;
using NpgsqlTypes;

namespace JaliscoProblemsAPI.Data.Repositories;

public sealed partial class ReportRepository
{
    public async Task<List<(ReportWithUsername Report, ReportRecommendation Recommendation)>> GetRecommendedReportsAsync(
        int? viewerId, double? latitude, double? longitude, int limit, int offset, CancellationToken cancellationToken)
    {
        // Select the page before loading photos/counts. No user location or viewing history is stored.
        var sql = """
            WITH preferred_types AS (
                SELECT type FROM reports WHERE user_id = $1 AND $1 > 0
                UNION
                SELECT r.type FROM reports r JOIN likes l ON l.report_id = r.id WHERE l.user_id = $1 AND $1 > 0
            ), candidates AS (
                SELECT r.id, r.status, r.created_at,
                    EXISTS (SELECT 1 FROM preferred_types p WHERE p.type = r.type) AS preferred,
                    CASE WHEN $2::double precision IS NULL THEN NULL ELSE
                        6371.0 * 2 * asin(sqrt(least(1.0, greatest(0.0,
                            power(sin(radians(r.latitude - $2) / 2), 2)
                            + cos(radians($2)) * cos(radians(r.latitude))
                            * power(sin(radians(r.longitude - $3) / 2), 2)))))
                    END AS distance_km
                FROM reports r
            ), ranked AS (
                SELECT *, (status <> 'resolved') AS unresolved,
                    COALESCE(distance_km <= 5.0, FALSE) AS nearby
                FROM candidates
                ORDER BY (status <> 'resolved') DESC, COALESCE(distance_km <= 5.0, FALSE) DESC,
                    preferred DESC, created_at DESC NULLS LAST, id DESC
                LIMIT $4 OFFSET $5
            )
            SELECT report.*, ranked.distance_km, ranked.preferred, ranked.nearby
            FROM (
            """ + ReportSelect + """
            ) report JOIN ranked ON ranked.id = report.id
            ORDER BY ranked.unresolved DESC, ranked.nearby DESC, ranked.preferred DESC,
                ranked.created_at DESC NULLS LAST, ranked.id DESC
            """;
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(viewerId ?? 0);
        command.Parameters.AddWithValue(NpgsqlDbType.Double, (object?)latitude ?? DBNull.Value);
        command.Parameters.AddWithValue(NpgsqlDbType.Double, (object?)longitude ?? DBNull.Value);
        command.Parameters.AddWithValue(limit);
        command.Parameters.AddWithValue(offset);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var items = new List<(ReportWithUsername, ReportRecommendation)>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var report = ReadReport(reader);
            double? distance = reader.IsDBNull(18) ? null : reader.GetDouble(18);
            var preferred = reader.GetBoolean(19);
            var nearby = reader.GetBoolean(20);
            var reason = nearby && preferred ? "nearby_and_related" : nearby ? "nearby" : preferred ? "related_category" : "recent";
            items.Add((report, new ReportRecommendation(report.Id, reason, distance.HasValue ? Math.Round(distance.Value, 2) : null)));
        }
        return items;
    }
}
