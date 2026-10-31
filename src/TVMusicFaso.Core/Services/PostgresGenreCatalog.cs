using Npgsql;
using TVMusicFaso.Core.Domain;

namespace TVMusicFaso.Core.Services;

/// <summary>Catalogue des genres dans la table <c>genres</c> ; une suppression archive le genre.</summary>
public sealed class PostgresGenreCatalog : IGenreCatalog
{
    private readonly string _connectionString;

    public PostgresGenreCatalog(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connectionString = connectionString;
    }

    public IReadOnlyList<GenreDefinition> GetAll()
    {
        using var connection = PostgresDatabase.Open(_connectionString);
        return ReadAll(connection, null);
    }

    public GenreDefinition Add(string label)
    {
        var cleaned = GenreCatalog.ValidateLabel(label);
        using var connection = PostgresDatabase.Open(_connectionString);
        using var transaction = connection.BeginTransaction();
        LockTable(connection, transaction);
        var all = ReadAll(connection, transaction);
        if (GenreCatalog.FindActiveByLabel(all, cleaned) is { } existing)
        {
            throw new InvalidOperationException($"Le genre « {existing.Label} » existe déjà.");
        }

        var genre = new GenreDefinition { Id = GenreCatalog.NextId(all), Label = cleaned };
        using (var insert = new NpgsqlCommand(
                   "INSERT INTO genres (id, label, is_seeded, is_archived) VALUES (@id, @label, FALSE, FALSE);",
                   connection,
                   transaction))
        {
            insert.Parameters.AddWithValue("id", genre.Id);
            insert.Parameters.AddWithValue("label", genre.Label);
            insert.ExecuteNonQuery();
        }

        transaction.Commit();
        return genre;
    }

    public GenreDefinition Rename(int id, string label)
    {
        var cleaned = GenreCatalog.ValidateLabel(label);
        using var connection = PostgresDatabase.Open(_connectionString);
        using var transaction = connection.BeginTransaction();
        LockTable(connection, transaction);
        var all = ReadAll(connection, transaction);
        var target = all.FirstOrDefault(item => item.Id == id && !item.IsArchived)
            ?? throw new InvalidOperationException("Genre introuvable ou supprimé.");
        if (GenreCatalog.FindActiveByLabel(all, cleaned, exceptId: id) is { } existing)
        {
            throw new InvalidOperationException($"Le genre « {existing.Label} » existe déjà.");
        }

        using (var update = new NpgsqlCommand("UPDATE genres SET label = @label WHERE id = @id;", connection, transaction))
        {
            update.Parameters.AddWithValue("id", id);
            update.Parameters.AddWithValue("label", cleaned);
            update.ExecuteNonQuery();
        }

        transaction.Commit();
        target.Label = cleaned;
        return target;
    }

    public int Delete(int id, int? replacementId)
    {
        using var connection = PostgresDatabase.Open(_connectionString);
        using var transaction = connection.BeginTransaction();
        LockTable(connection, transaction);
        var all = ReadAll(connection, transaction);
        var used = all.FirstOrDefault(item => item.Id == id)?.ClipCount ?? 0;
        GenreCatalog.CheckDeletion(all, id, replacementId, used);
        var moved = 0;
        if (replacementId is { } replacement && used > 0)
        {
            using var move = new NpgsqlCommand("UPDATE clips SET genre = @replacement WHERE genre = @id;", connection, transaction);
            move.Parameters.AddWithValue("id", id);
            move.Parameters.AddWithValue("replacement", replacement);
            moved = move.ExecuteNonQuery();
        }

        using (var archive = new NpgsqlCommand("UPDATE genres SET is_archived = TRUE WHERE id = @id;", connection, transaction))
        {
            archive.Parameters.AddWithValue("id", id);
            archive.ExecuteNonQuery();
        }

        transaction.Commit();
        return moved;
    }

    public void EnsureSeed()
    {
        var seeds = GenreCatalog.Seeds();
        using var connection = PostgresDatabase.Open(_connectionString);
        using var command = new NpgsqlCommand(
            """
            INSERT INTO genres (id, label, is_seeded, is_archived)
            SELECT seed.id, seed.label, TRUE, FALSE
            FROM unnest(@ids, @labels) AS seed(id, label)
            ON CONFLICT (id) DO NOTHING;
            """,
            connection);
        command.Parameters.AddWithValue("ids", seeds.Select(item => item.Id).ToArray());
        command.Parameters.AddWithValue("labels", seeds.Select(item => item.Label).ToArray());
        command.ExecuteNonQuery();
    }

    /// <summary>Deux postes qui créent un genre en même temps n'obtiennent pas le même numéro.</summary>
    private static void LockTable(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        using var command = new NpgsqlCommand("LOCK TABLE genres IN SHARE ROW EXCLUSIVE MODE;", connection, transaction);
        command.ExecuteNonQuery();
    }

    /// <summary>Genres et nombre de clips de chacun, en une requête (index sur <c>clips.genre</c>).</summary>
    private static List<GenreDefinition> ReadAll(NpgsqlConnection connection, NpgsqlTransaction? transaction)
    {
        using var command = new NpgsqlCommand(
            """
            SELECT g.id, g.label, g.is_seeded, g.is_archived, COALESCE(c.total, 0)
            FROM genres g
            LEFT JOIN (SELECT genre, COUNT(*)::INTEGER AS total FROM clips GROUP BY genre) c ON c.genre = g.id
            ORDER BY g.label;
            """,
            connection,
            transaction);
        using var reader = command.ExecuteReader();
        var genres = new List<GenreDefinition>();
        while (reader.Read())
        {
            genres.Add(new GenreDefinition
            {
                Id = reader.GetInt32(0),
                Label = reader.GetString(1),
                IsSeeded = reader.GetBoolean(2),
                IsArchived = reader.GetBoolean(3),
                ClipCount = reader.GetInt32(4)
            });
        }

        return genres;
    }
}
