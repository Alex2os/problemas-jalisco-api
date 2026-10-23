using JaliscoProblemsAPI.Models;
using Npgsql;

namespace JaliscoProblemsAPI.Data.Repositories;

public sealed partial class ReportRepository(NpgsqlDataSource dataSource)
{
    private const string ReportSelect = """
        SELECT r.id, r.longitude, r.latitude, r.ubication, r.description, r.type, r.created_at, r.user_id, u.username,
            COALESCE(l.like_count, 0), COALESCE(c.comment_count, 0), mine.user_id IS NOT NULL,
            r.status, r.status_updated_at, r.status_version, r.user_id = $1,
            COALESCE((SELECT jsonb_agg(jsonb_build_object('id',i.id,'url','/reports/'||r.id||'/photos/'||i.id||'/content',
                'display_order',i.display_order,'created_at',i.created_at) ORDER BY i.display_order)
                FROM report_images i WHERE i.report_id = r.id),'[]'::jsonb)::text
        FROM reports r JOIN users u ON u.id = r.user_id
        LEFT JOIN (SELECT report_id, count(*) AS like_count FROM likes GROUP BY report_id) l ON l.report_id = r.id
        LEFT JOIN (SELECT report_id, count(*) AS comment_count FROM comments GROUP BY report_id) c ON c.report_id = r.id
        LEFT JOIN likes mine ON mine.report_id = r.id AND mine.user_id = $1
        """;

    public async Task<bool> UserExistsAsync(int userId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT EXISTS (SELECT 1 FROM users WHERE id = $1)");
        command.Parameters.AddWithValue(userId);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    public async Task<bool> ReportExistsAsync(int reportId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT EXISTS (SELECT 1 FROM reports WHERE id = $1)");
        command.Parameters.AddWithValue(reportId);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    public async Task<int> CreateReportAsync(Report report, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("""
            INSERT INTO reports (longitude, latitude, ubication, description, type, user_id)
            VALUES ($1, $2, $3, $4, $5, $6) RETURNING id
            """);
        command.Parameters.AddWithValue(report.Longitude);
        command.Parameters.AddWithValue(report.Latitude);
        command.Parameters.AddWithValue(report.Ubication);
        command.Parameters.AddWithValue(report.Description);
        command.Parameters.AddWithValue(report.Type);
        command.Parameters.AddWithValue(report.UserId);
        return (int)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    public async Task<ReportWithUsername?> GetByIdAsync(int reportId, int? viewerId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(ReportSelect + " WHERE r.id = $2");
        command.Parameters.AddWithValue(viewerId ?? 0);
        command.Parameters.AddWithValue(reportId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadReport(reader) : null;
    }

    public async Task<List<ReportWithUsername>> GetReportsAsync(string? query, int? limit, int? beforeId, int? viewerId, CancellationToken cancellationToken)
    {
        // Optional pagination is additive: omitted parameters preserve the full map/list response.
        var sql = ReportSelect + " WHERE TRUE";
        await using var command = dataSource.CreateCommand();
        command.Parameters.AddWithValue(viewerId ?? 0);
        if (query is not null)
        {
            command.Parameters.AddWithValue($"%{query}%");
            sql += $" AND r.ubication ILIKE ${command.Parameters.Count}";
        }
        if (beforeId.HasValue)
        {
            command.Parameters.AddWithValue(beforeId.Value);
            sql += $" AND r.id < ${command.Parameters.Count}";
        }
        sql += " ORDER BY r.id DESC";
        if (limit.HasValue)
        {
            command.Parameters.AddWithValue(limit.Value);
            sql += $" LIMIT ${command.Parameters.Count}";
        }
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var reports = new List<ReportWithUsername>();
        while (await reader.ReadAsync(cancellationToken)) reports.Add(ReadReport(reader));
        return reports;
    }

    public async Task<CommentWithUsername> CreateCommentAsync(Comment comment, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("""
            INSERT INTO comments (user_id, report_id, user_comment) VALUES ($1, $2, $3)
            RETURNING id, user_id, report_id, user_comment, created_at, (SELECT username FROM users WHERE id = $1)
            """);
        command.Parameters.AddWithValue(comment.UserId);
        command.Parameters.AddWithValue(comment.ReportId);
        command.Parameters.AddWithValue(comment.UserComment);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) throw new InvalidOperationException("Comment insert returned no row.");
        return ReadComment(reader, comment.UserId);
    }

    public async Task<List<CommentWithUsername>> GetCommentsAsync(int reportId, int? limit, int? afterId, int? viewerId, CancellationToken cancellationToken)
    {
        var sql = """
            SELECT c.id, c.user_id, c.report_id, c.user_comment, c.created_at, u.username
            FROM comments c JOIN users u ON u.id = c.user_id WHERE c.report_id = $1
            """;
        await using var command = dataSource.CreateCommand();
        command.Parameters.AddWithValue(reportId);
        if (afterId.HasValue)
        {
            command.Parameters.AddWithValue(afterId.Value);
            sql += " AND c.id > $2";
        }
        sql += " ORDER BY c.id ASC";
        if (limit.HasValue)
        {
            command.Parameters.AddWithValue(limit.Value);
            sql += $" LIMIT ${command.Parameters.Count}";
        }
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var comments = new List<CommentWithUsername>();
        while (await reader.ReadAsync(cancellationToken))
            comments.Add(ReadComment(reader, viewerId));
        return comments;
    }

    private static ReportWithUsername ReadReport(NpgsqlDataReader reader) => new()
    {
        Id = reader.GetInt32(0), Longitude = reader.GetDouble(1), Latitude = reader.GetDouble(2),
        Ubication = reader.GetString(3), Description = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
        Type = reader.GetString(5), CreatedAt = reader.IsDBNull(6) ? null : reader.GetDateTime(6),
        UserId = reader.GetInt32(7), Username = reader.GetString(8),
        LikeCount = reader.GetInt64(9), CommentCount = reader.GetInt64(10), LikedByMe = reader.GetBoolean(11),
        Status = reader.GetString(12), StatusUpdatedAt = reader.GetDateTime(13), StatusVersion = reader.GetInt32(14),
        CanManage = reader.GetBoolean(15),
        Photos = System.Text.Json.JsonSerializer.Deserialize<List<ReportPhoto>>(reader.GetString(16)) ?? []
    };

    private static CommentWithUsername ReadComment(NpgsqlDataReader reader, int? viewerId) => new()
    {
        Id = reader.GetInt32(0), UserId = reader.GetInt32(1), ReportId = reader.GetInt32(2),
        UserComment = reader.GetString(3), CreatedAt = reader.IsDBNull(4) ? null : reader.GetDateTime(4),
        Username = reader.GetString(5), CanDelete = viewerId == reader.GetInt32(1)
    };

    public async Task<ReportInteractions?> GetInteractionsAsync(int reportId, int? viewerId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT r.id,
                (SELECT count(*) FROM likes WHERE report_id = r.id),
                (SELECT count(*) FROM comments WHERE report_id = r.id),
                EXISTS (SELECT 1 FROM likes WHERE report_id = r.id AND user_id = $2)
            FROM reports r WHERE r.id = $1
            """);
        command.Parameters.AddWithValue(reportId);
        command.Parameters.AddWithValue(viewerId ?? 0);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new ReportInteractions { ReportId = reader.GetInt32(0), LikeCount = reader.GetInt64(1), CommentCount = reader.GetInt64(2), LikedByMe = reader.GetBoolean(3) };
    }

    public async Task SetLikeAsync(int reportId, int userId, bool liked, CancellationToken cancellationToken)
    {
        // The existing composite primary key is the concurrency guard. Repeated requests are safe.
        var sql = liked
            ? "INSERT INTO likes (user_id, report_id) VALUES ($1, $2) ON CONFLICT (user_id, report_id) DO NOTHING"
            : "DELETE FROM likes WHERE user_id = $1 AND report_id = $2";
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(reportId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<DeleteCommentResult> DeleteCommentAsync(int reportId, int commentId, int userId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("DELETE FROM comments WHERE id = $1 AND report_id = $2 AND user_id = $3");
        command.Parameters.AddWithValue(commentId);
        command.Parameters.AddWithValue(reportId);
        command.Parameters.AddWithValue(userId);
        if (await command.ExecuteNonQueryAsync(cancellationToken) > 0) return DeleteCommentResult.Deleted;
        // Distinguish a missing/already-deleted comment from another author's comment.
        await using var lookup = dataSource.CreateCommand("SELECT user_id FROM comments WHERE id = $1 AND report_id = $2");
        lookup.Parameters.AddWithValue(commentId);
        lookup.Parameters.AddWithValue(reportId);
        return await lookup.ExecuteScalarAsync(cancellationToken) is null ? DeleteCommentResult.NotFound : DeleteCommentResult.Forbidden;
    }
}

public enum DeleteCommentResult { Deleted, NotFound, Forbidden }
