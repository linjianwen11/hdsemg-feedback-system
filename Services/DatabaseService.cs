using Microsoft.Data.Sqlite;
using EMGFeedbackSystem.Models;
using System;
using System.Collections.Generic;
using System.IO;

namespace EMGFeedbackSystem.Services
{
    public class DatabaseService
    {
        private readonly string _connectionString;
        private readonly string _dbPath;

        public DatabaseService()
        {
            string appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "EMGFeedbackSystem");
            
            if (!Directory.Exists(appDataPath))
            {
                Directory.CreateDirectory(appDataPath);
            }

            _dbPath = Path.Combine(appDataPath, "subjects.db");
            _connectionString = $"Data Source={_dbPath}";
            
            InitializeDatabase();
        }

        private void InitializeDatabase()
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            string createTableSql = @"
                CREATE TABLE IF NOT EXISTS Subjects (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    SubjectId TEXT NOT NULL UNIQUE,
                    Name TEXT NOT NULL,
                    Gender TEXT,
                    Age INTEGER,
                    Notes TEXT,
                    UpperLimit REAL DEFAULT 1.0,
                    LeftLegMaxA REAL DEFAULT 0,
                    LeftLegMaxB REAL DEFAULT 0,
                    LeftLegMaxC REAL DEFAULT 0,
                    RightLegMaxA REAL DEFAULT 0,
                    RightLegMaxB REAL DEFAULT 0,
                    RightLegMaxC REAL DEFAULT 0,
                    LeftLegSide TEXT DEFAULT '健侧',
                    RightLegSide TEXT DEFAULT '健侧',
                    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
                    UpdatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
                );

                CREATE INDEX IF NOT EXISTS idx_name ON Subjects(Name);
                CREATE INDEX IF NOT EXISTS idx_subjectid ON Subjects(SubjectId);
            ";

            using var command = new SqliteCommand(createTableSql, connection);
            command.ExecuteNonQuery();

            EnsureColumnExists(connection, "LeftLegSide", "TEXT", "'健侧'");
            EnsureColumnExists(connection, "RightLegSide", "TEXT", "'健侧'");
        }

        private static void EnsureColumnExists(SqliteConnection connection, string columnName, string columnType, string defaultValue)
        {
            using var tableInfo = new SqliteCommand("PRAGMA table_info(Subjects);", connection);
            using var reader = tableInfo.ExecuteReader();
            while (reader.Read())
            {
                string name = reader.GetString(1);
                if (string.Equals(name, columnName, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            string alterSql = $"ALTER TABLE Subjects ADD COLUMN {columnName} {columnType} DEFAULT {defaultValue};";
            using var alterCommand = new SqliteCommand(alterSql, connection);
            alterCommand.ExecuteNonQuery();
        }

        public string GenerateNewSubjectId()
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            string sql = "SELECT IFNULL(MAX(Id), 0) FROM Subjects";
            using var command = new SqliteCommand(sql, connection);
            var maxId = Convert.ToInt32(command.ExecuteScalar());
            
            return $"S{(maxId + 1).ToString("D6")}";
        }

        public void SaveSubject(Subject subject)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            string sql = @"
                INSERT INTO Subjects 
                (SubjectId, Name, Gender, Age, Notes, UpperLimit, 
                 LeftLegMaxA, LeftLegMaxB, LeftLegMaxC,
                 RightLegMaxA, RightLegMaxB, RightLegMaxC,
                 LeftLegSide, RightLegSide)
                VALUES 
                (@SubjectId, @Name, @Gender, @Age, @Notes, @UpperLimit,
                 @LeftLegMaxA, @LeftLegMaxB, @LeftLegMaxC,
                 @RightLegMaxA, @RightLegMaxB, @RightLegMaxC,
                 @LeftLegSide, @RightLegSide)
                ON CONFLICT(SubjectId) DO UPDATE SET
                    Name = excluded.Name,
                    Gender = excluded.Gender,
                    Age = excluded.Age,
                    Notes = excluded.Notes,
                    UpperLimit = excluded.UpperLimit,
                    LeftLegMaxA = excluded.LeftLegMaxA,
                    LeftLegMaxB = excluded.LeftLegMaxB,
                    LeftLegMaxC = excluded.LeftLegMaxC,
                    RightLegMaxA = excluded.RightLegMaxA,
                    RightLegMaxB = excluded.RightLegMaxB,
                    RightLegMaxC = excluded.RightLegMaxC,
                    LeftLegSide = excluded.LeftLegSide,
                    RightLegSide = excluded.RightLegSide,
                    UpdatedAt = CURRENT_TIMESTAMP;
            ";

            using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@SubjectId", subject.SubjectId);
            command.Parameters.AddWithValue("@Name", subject.Name);
            command.Parameters.AddWithValue("@Gender", subject.Gender);
            command.Parameters.AddWithValue("@Age", subject.Age);
            command.Parameters.AddWithValue("@Notes", subject.Notes ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@UpperLimit", subject.UpperLimit);
            command.Parameters.AddWithValue("@LeftLegMaxA", subject.LeftLegMaxA);
            command.Parameters.AddWithValue("@LeftLegMaxB", subject.LeftLegMaxB);
            command.Parameters.AddWithValue("@LeftLegMaxC", subject.LeftLegMaxC);
            command.Parameters.AddWithValue("@RightLegMaxA", subject.RightLegMaxA);
            command.Parameters.AddWithValue("@RightLegMaxB", subject.RightLegMaxB);
            command.Parameters.AddWithValue("@RightLegMaxC", subject.RightLegMaxC);
            command.Parameters.AddWithValue("@LeftLegSide", subject.LeftLegSide);
            command.Parameters.AddWithValue("@RightLegSide", subject.RightLegSide);

            command.ExecuteNonQuery();
        }

        public Subject? GetSubjectByNameAndInfo(string name, string gender, int age)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            string sql = @"
                SELECT * FROM Subjects 
                WHERE Name = @Name AND Gender = @Gender AND Age = @Age
                LIMIT 1;
            ";

            using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@Name", name);
            command.Parameters.AddWithValue("@Gender", gender);
            command.Parameters.AddWithValue("@Age", age);

            using var reader = command.ExecuteReader();
            if (reader.Read())
            {
                return ReadSubjectFromReader(reader);
            }

            return null;
        }

        public Subject? GetSubjectBySubjectId(string subjectId)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            string sql = "SELECT * FROM Subjects WHERE SubjectId = @SubjectId LIMIT 1;";
            using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@SubjectId", subjectId);

            using var reader = command.ExecuteReader();
            if (reader.Read())
            {
                return ReadSubjectFromReader(reader);
            }

            return null;
        }

        public List<Subject> SearchSubjects(string keyword)
        {
            var subjects = new List<Subject>();

            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            string sql = @"
                SELECT * FROM Subjects 
                WHERE Name LIKE @Keyword OR SubjectId LIKE @Keyword
                ORDER BY UpdatedAt DESC;
            ";

            using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@Keyword", $"%{keyword}%");

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                subjects.Add(ReadSubjectFromReader(reader));
            }

            return subjects;
        }

        public List<Subject> GetAllSubjects()
        {
            var subjects = new List<Subject>();

            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            string sql = "SELECT * FROM Subjects ORDER BY UpdatedAt DESC;";
            using var command = new SqliteCommand(sql, connection);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                subjects.Add(ReadSubjectFromReader(reader));
            }

            return subjects;
        }

        private Subject ReadSubjectFromReader(SqliteDataReader reader)
        {
            return new Subject
            {
                Id = reader.GetInt32(reader.GetOrdinal("Id")),
                SubjectId = reader.GetString(reader.GetOrdinal("SubjectId")),
                Name = reader.GetString(reader.GetOrdinal("Name")),
                Gender = reader.IsDBNull(reader.GetOrdinal("Gender")) ? string.Empty : reader.GetString(reader.GetOrdinal("Gender")),
                Age = reader.IsDBNull(reader.GetOrdinal("Age")) ? 0 : reader.GetInt32(reader.GetOrdinal("Age")),
                Notes = reader.IsDBNull(reader.GetOrdinal("Notes")) ? null : reader.GetString(reader.GetOrdinal("Notes")),
                UpperLimit = reader.IsDBNull(reader.GetOrdinal("UpperLimit")) ? 1.0 : reader.GetDouble(reader.GetOrdinal("UpperLimit")),
                LeftLegMaxA = reader.IsDBNull(reader.GetOrdinal("LeftLegMaxA")) ? 0 : reader.GetDouble(reader.GetOrdinal("LeftLegMaxA")),
                LeftLegMaxB = reader.IsDBNull(reader.GetOrdinal("LeftLegMaxB")) ? 0 : reader.GetDouble(reader.GetOrdinal("LeftLegMaxB")),
                LeftLegMaxC = reader.IsDBNull(reader.GetOrdinal("LeftLegMaxC")) ? 0 : reader.GetDouble(reader.GetOrdinal("LeftLegMaxC")),
                RightLegMaxA = reader.IsDBNull(reader.GetOrdinal("RightLegMaxA")) ? 0 : reader.GetDouble(reader.GetOrdinal("RightLegMaxA")),
                RightLegMaxB = reader.IsDBNull(reader.GetOrdinal("RightLegMaxB")) ? 0 : reader.GetDouble(reader.GetOrdinal("RightLegMaxB")),
                RightLegMaxC = reader.IsDBNull(reader.GetOrdinal("RightLegMaxC")) ? 0 : reader.GetDouble(reader.GetOrdinal("RightLegMaxC")),
                LeftLegSide = reader.IsDBNull(reader.GetOrdinal("LeftLegSide")) ? string.Empty : reader.GetString(reader.GetOrdinal("LeftLegSide")),
                RightLegSide = reader.IsDBNull(reader.GetOrdinal("RightLegSide")) ? string.Empty : reader.GetString(reader.GetOrdinal("RightLegSide"))
            };
        }
    }
}
