using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using CNCMes.Server.Models;

namespace CNCMes.Server.Data;

public class SqliteDb
{
    private readonly string _connectionString;
    private readonly ILogger<SqliteDb> _logger;
    private readonly string _dataDir;

    public SqliteDb(IConfiguration configuration, ILogger<SqliteDb> logger)
    {
        _logger = logger;
        _dataDir = configuration.GetValue<string>("ServerConfig:DataDirectory") ?? "data";
        if (!Path.IsPathRooted(_dataDir))
        {
            _dataDir = Path.Combine(AppContext.BaseDirectory, _dataDir);
        }

        if (!Directory.Exists(_dataDir))
        {
            Directory.CreateDirectory(_dataDir);
        }

        var dbPath = Path.Combine(_dataDir, "cnc_mes.db");
        _connectionString = $"Data Source={dbPath};Cache=Shared;";
    }

    public string DataDirectory => _dataDir;

    public SqliteConnection CreateConnection()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return conn;
    }

    public void Initialize()
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();

        // Ensure work_orders table schema migration for folder auto-sync & backup
        try
        {
            using var alterCmd = conn.CreateCommand();
            alterCmd.CommandText = "ALTER TABLE work_orders ADD COLUMN folder_path TEXT;";
            alterCmd.ExecuteNonQuery();
        } catch { }
        try
        {
            using var alterCmd = conn.CreateCommand();
            alterCmd.CommandText = "ALTER TABLE work_orders ADD COLUMN backup_path TEXT;";
            alterCmd.ExecuteNonQuery();
        } catch { }
        try
        {
            using var alterCmd = conn.CreateCommand();
            alterCmd.CommandText = "ALTER TABLE work_orders ADD COLUMN completed_at TEXT;";
            alterCmd.ExecuteNonQuery();
        } catch { }
        try
        {
            using var alterCmd = conn.CreateCommand();
            alterCmd.CommandText = "ALTER TABLE work_orders ADD COLUMN is_hidden INTEGER NOT NULL DEFAULT 0;";
            alterCmd.ExecuteNonQuery();
        } catch { }
        try
        {
            using var alterCmd = conn.CreateCommand();
            alterCmd.CommandText = "ALTER TABLE wo_parts ADD COLUMN post_process TEXT;";
            alterCmd.ExecuteNonQuery();
        } catch { }
        try
        {
            using var alterCmd = conn.CreateCommand();
            alterCmd.CommandText = "ALTER TABLE wo_parts ADD COLUMN post_process_status TEXT;";
            alterCmd.ExecuteNonQuery();
        } catch { }
        try
        {
            using var alterCmd = conn.CreateCommand();
            alterCmd.CommandText = "ALTER TABLE wo_parts ADD COLUMN post_process_notes TEXT;";
            alterCmd.ExecuteNonQuery();
        } catch { }

        cmd.CommandText = @"
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;

            CREATE TABLE IF NOT EXISTS users (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                username TEXT NOT NULL UNIQUE,
                password_hash TEXT NOT NULL,
                password_salt TEXT NOT NULL,
                display_name TEXT NOT NULL,
                role TEXT NOT NULL DEFAULT 'WORKER',
                is_active INTEGER NOT NULL DEFAULT 1,
                created_at TEXT NOT NULL,
                last_login_at TEXT
            );

            CREATE TABLE IF NOT EXISTS machines (
                machine_id TEXT PRIMARY KEY,
                machine_name TEXT NOT NULL,
                model_type TEXT NOT NULL,
                controller_type TEXT NOT NULL,
                ip_address TEXT NOT NULL,
                port INTEGER NOT NULL DEFAULT 8193,
                status TEXT NOT NULL DEFAULT '대기',
                current_program TEXT,
                spindle_rpm INTEGER NOT NULL DEFAULT 0,
                feed_rate REAL NOT NULL DEFAULT 0,
                run_time_minutes REAL NOT NULL DEFAULT 0,
                active_job_number TEXT,
                active_part_name TEXT,
                active_worker TEXT,
                last_heartbeat TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS setting_groups (
                group_id INTEGER PRIMARY KEY AUTOINCREMENT,
                group_name TEXT NOT NULL UNIQUE,
                material TEXT NOT NULL,
                thickness REAL NOT NULL,
                fixture_type TEXT,
                origin_point TEXT,
                shared_tools_json TEXT,
                notes TEXT,
                created_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS work_orders (
                order_id INTEGER PRIMARY KEY AUTOINCREMENT,
                order_number TEXT NOT NULL UNIQUE,
                customer_name TEXT NOT NULL,
                target_delivery_date TEXT NOT NULL,
                status TEXT NOT NULL DEFAULT '대기',
                progress_rate INTEGER NOT NULL DEFAULT 0,
                notes TEXT,
                created_by TEXT NOT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                folder_path TEXT,
                backup_path TEXT,
                completed_at TEXT,
                is_hidden INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS wo_parts (
                part_id INTEGER PRIMARY KEY AUTOINCREMENT,
                order_id INTEGER NOT NULL,
                part_code TEXT NOT NULL,
                part_name TEXT NOT NULL,
                material TEXT NOT NULL,
                thickness REAL NOT NULL,
                target_qty INTEGER NOT NULL DEFAULT 1,
                completed_qty INTEGER NOT NULL DEFAULT 0,
                production_type TEXT NOT NULL DEFAULT '단품',
                setting_group_id INTEGER,
                current_step_sequence INTEGER NOT NULL DEFAULT 1,
                status TEXT NOT NULL DEFAULT '대기',
                created_at TEXT NOT NULL,
                post_process TEXT,
                post_process_status TEXT,
                post_process_notes TEXT,
                FOREIGN KEY(order_id) REFERENCES work_orders(order_id) ON DELETE CASCADE,
                FOREIGN KEY(setting_group_id) REFERENCES setting_groups(group_id)
            );

            CREATE TABLE IF NOT EXISTS routing_steps (
                step_id INTEGER PRIMARY KEY AUTOINCREMENT,
                part_id INTEGER NOT NULL,
                step_sequence INTEGER NOT NULL,
                step_name TEXT NOT NULL,
                target_machine TEXT NOT NULL,
                nc_program_name TEXT,
                nc_program_version TEXT,
                status TEXT NOT NULL DEFAULT '대기',
                machined_qty INTEGER NOT NULL DEFAULT 0,
                inspected_qty INTEGER NOT NULL DEFAULT 0,
                passed_qty INTEGER NOT NULL DEFAULT 0,
                failed_qty INTEGER NOT NULL DEFAULT 0,
                rework_qty INTEGER NOT NULL DEFAULT 0,
                notes TEXT,
                FOREIGN KEY(part_id) REFERENCES wo_parts(part_id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS work_assignments (
                assignment_id INTEGER PRIMARY KEY AUTOINCREMENT,
                step_id INTEGER NOT NULL,
                machine_id TEXT NOT NULL,
                worker_username TEXT NOT NULL,
                assigned_qty INTEGER NOT NULL,
                status TEXT NOT NULL DEFAULT '배정',
                assigned_at TEXT NOT NULL,
                FOREIGN KEY(step_id) REFERENCES routing_steps(step_id) ON DELETE CASCADE,
                FOREIGN KEY(machine_id) REFERENCES machines(machine_id)
            );

            CREATE TABLE IF NOT EXISTS work_executions (
                execution_id INTEGER PRIMARY KEY AUTOINCREMENT,
                step_id INTEGER NOT NULL,
                assignment_id INTEGER,
                machine_id TEXT NOT NULL,
                worker_username TEXT NOT NULL,
                start_time TEXT NOT NULL,
                end_time TEXT,
                machined_qty INTEGER NOT NULL DEFAULT 0,
                status TEXT NOT NULL DEFAULT '가공중',
                notes TEXT,
                FOREIGN KEY(step_id) REFERENCES routing_steps(step_id) ON DELETE CASCADE,
                FOREIGN KEY(machine_id) REFERENCES machines(machine_id)
            );

            CREATE TABLE IF NOT EXISTS inspection_records (
                inspection_id INTEGER PRIMARY KEY AUTOINCREMENT,
                step_id INTEGER NOT NULL,
                part_id INTEGER NOT NULL,
                lot_id TEXT,
                inspector_username TEXT NOT NULL,
                inspected_at TEXT NOT NULL,
                inspection_type TEXT NOT NULL DEFAULT '전수검사',
                inspected_qty INTEGER NOT NULL,
                passed_qty INTEGER NOT NULL,
                failed_qty INTEGER NOT NULL DEFAULT 0,
                rework_qty INTEGER NOT NULL DEFAULT 0,
                dimension_data_json TEXT,
                result_verdict TEXT NOT NULL DEFAULT '합격',
                attachment_path TEXT,
                notes TEXT,
                FOREIGN KEY(step_id) REFERENCES routing_steps(step_id) ON DELETE CASCADE,
                FOREIGN KEY(part_id) REFERENCES wo_parts(part_id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS post_process_reports (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                part_id INTEGER NOT NULL,
                order_id INTEGER NOT NULL,
                order_number TEXT NOT NULL,
                part_name TEXT NOT NULL,
                machine_id TEXT NOT NULL,
                worker TEXT NOT NULL,
                completed_qty INTEGER NOT NULL,
                selected_processes TEXT NOT NULL,
                notes TEXT,
                status TEXT NOT NULL DEFAULT '후공정대기',
                created_at TEXT NOT NULL,
                completed_at TEXT
            );

            CREATE TABLE IF NOT EXISTS permanent_events (
                event_id INTEGER PRIMARY KEY AUTOINCREMENT,
                part_code TEXT NOT NULL,
                part_id INTEGER,
                order_id INTEGER,
                step_id INTEGER,
                machine_id TEXT,
                event_category TEXT NOT NULL DEFAULT '가공중',
                event_type TEXT NOT NULL,
                description TEXT NOT NULL,
                cause TEXT,
                action_taken TEXT,
                before_state TEXT,
                after_state TEXT,
                related_drawing_rev TEXT,
                related_nc_version TEXT,
                status TEXT NOT NULL DEFAULT '접수',
                author_username TEXT NOT NULL,
                created_at TEXT NOT NULL,
                resolved_at TEXT
            );

            CREATE TABLE IF NOT EXISTS event_attachments (
                attachment_id INTEGER PRIMARY KEY AUTOINCREMENT,
                event_id INTEGER NOT NULL,
                file_type TEXT NOT NULL DEFAULT 'IMAGE',
                original_filename TEXT NOT NULL,
                stored_filename TEXT NOT NULL,
                file_size INTEGER NOT NULL,
                annotations_json TEXT,
                uploaded_at TEXT NOT NULL,
                FOREIGN KEY(event_id) REFERENCES permanent_events(event_id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS handover_logs (
                handover_id INTEGER PRIMARY KEY AUTOINCREMENT,
                sender_username TEXT NOT NULL,
                receiver_username TEXT,
                machine_id TEXT NOT NULL,
                order_id INTEGER,
                part_id INTEGER,
                summary TEXT NOT NULL,
                pending_issues TEXT,
                notes TEXT,
                created_at TEXT NOT NULL,
                confirmed_at TEXT
            );

            CREATE TABLE IF NOT EXISTS notices (
                notice_id INTEGER PRIMARY KEY AUTOINCREMENT,
                author TEXT NOT NULL,
                title TEXT NOT NULL,
                content TEXT NOT NULL,
                priority TEXT NOT NULL DEFAULT '일반',
                related_machine TEXT,
                created_at TEXT NOT NULL,
                official_acks_json TEXT
            );

            CREATE TABLE IF NOT EXISTS chat_messages (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                channel_type TEXT NOT NULL DEFAULT 'GENERAL',
                topic_key TEXT NOT NULL DEFAULT '공통',
                author TEXT NOT NULL,
                content TEXT NOT NULL,
                attachment_info TEXT,
                created_at TEXT NOT NULL,
                promoted_event_id INTEGER
            );

            CREATE TABLE IF NOT EXISTS machine_work_logs (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                machine_id TEXT NOT NULL,
                order_number TEXT NOT NULL DEFAULT '-',
                part_name TEXT NOT NULL DEFAULT '-',
                work_detail TEXT NOT NULL,
                status TEXT NOT NULL DEFAULT '가공중',
                worker_name TEXT NOT NULL,
                sub_worker_name TEXT,
                qty INTEGER NOT NULL DEFAULT 0,
                created_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS sub_worker_tasks (
                task_id INTEGER PRIMARY KEY AUTOINCREMENT,
                machine_id TEXT NOT NULL,
                order_number TEXT NOT NULL DEFAULT '-',
                part_name TEXT NOT NULL DEFAULT '-',
                leader_username TEXT NOT NULL,
                sub_worker_name TEXT NOT NULL,
                instruction TEXT NOT NULL,
                report_content TEXT,
                reported_qty INTEGER NOT NULL DEFAULT 0,
                status TEXT NOT NULL DEFAULT '지시중',
                created_at TEXT NOT NULL,
                reported_at TEXT
            );

            CREATE TABLE IF NOT EXISTS machine_used_tools (
                tool_log_id INTEGER PRIMARY KEY AUTOINCREMENT,
                machine_id TEXT NOT NULL,
                order_number TEXT NOT NULL DEFAULT '-',
                part_name TEXT NOT NULL DEFAULT '-',
                tool_name TEXT NOT NULL,
                maker TEXT NOT NULL DEFAULT '-',
                tool_diameter TEXT NOT NULL DEFAULT '-',
                flute_length TEXT NOT NULL DEFAULT '-',
                material TEXT NOT NULL DEFAULT '-',
                notes TEXT NOT NULL DEFAULT '-',
                worker_name TEXT NOT NULL,
                created_at TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_wo_orders_num ON work_orders(order_number);
            CREATE INDEX IF NOT EXISTS idx_wo_parts_code ON wo_parts(part_code);
            CREATE INDEX IF NOT EXISTS idx_perm_events_code ON permanent_events(part_code);
            CREATE INDEX IF NOT EXISTS idx_routing_steps_part ON routing_steps(part_id);
            CREATE INDEX IF NOT EXISTS idx_executions_step ON work_executions(step_id);
            CREATE INDEX IF NOT EXISTS idx_inspections_step ON inspection_records(step_id);
            CREATE INDEX IF NOT EXISTS idx_chat_topic ON chat_messages(channel_type, topic_key);
            CREATE INDEX IF NOT EXISTS idx_mwork_machine ON machine_work_logs(machine_id);
            CREATE INDEX IF NOT EXISTS idx_used_tools_machine ON machine_used_tools(machine_id);
        ";

        cmd.ExecuteNonQuery();

        // Robust Schema Migrations for existing DB files (Guaranteed execution AFTER tables exist)
        EnsureColumn(conn, "work_orders", "folder_path", "TEXT");
        EnsureColumn(conn, "work_orders", "backup_path", "TEXT");
        EnsureColumn(conn, "work_orders", "completed_at", "TEXT");
        EnsureColumn(conn, "work_orders", "is_hidden", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(conn, "wo_parts", "post_process", "TEXT");
        EnsureColumn(conn, "wo_parts", "post_process_status", "TEXT");
        EnsureColumn(conn, "wo_parts", "post_process_notes", "TEXT");

        // 최초 DB 생성 시에만 7대 CNC 장비 기본 시딩 (이후 사용자가 추가/삭제한 장비 목록 유지)
        SeedMachines(conn);

        _logger.LogInformation("CNC MES SQLite Database initialized successfully.");
    }

    private void SeedMachines(SqliteConnection conn)
    {
        using (var countCmd = conn.CreateCommand())
        {
        }

        // 스티치 UI(code.html / screen.png)와 1:1 완벽 일치하는 7대 기본 장비 (K01, K02, R01, R02, R03, R04, R05)
        var machines = new[]
        {
            new { Id = "K01", Name = "Roku-Roku 1호기", Model = "Roku-Roku", Controller = "FANUC 31i-A5", Ip = "192.168.1.201" },
            new { Id = "K02", Name = "Roku-Roku 2호기", Model = "Roku-Roku", Controller = "FANUC 31i-A5", Ip = "192.168.1.202" },
            new { Id = "R01", Name = "RoboDrill 1호기", Model = "RoboDrill", Controller = "FANUC 31i-B5", Ip = "192.168.1.101" },
            new { Id = "R02", Name = "RoboDrill 2호기", Model = "RoboDrill", Controller = "FANUC 31i-B5", Ip = "192.168.1.102" },
            new { Id = "R03", Name = "RoboDrill 3호기", Model = "RoboDrill", Controller = "FANUC 31i-B", Ip = "192.168.1.103" },
            new { Id = "R04", Name = "RoboDrill 4호기", Model = "RoboDrill", Controller = "FANUC 31i-B", Ip = "192.168.1.104" },
            new { Id = "R05", Name = "RoboDrill 5호기", Model = "RoboDrill", Controller = "FANUC 0i-MF", Ip = "192.168.1.105" }
        };

        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        foreach (var m in machines)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT OR IGNORE INTO machines 
                (machine_id, machine_name, model_type, controller_type, ip_address, port, status, last_heartbeat)
                VALUES (@id, @name, @model, @ctrl, @ip, 8193, '대기', @now);
            ";
            cmd.Parameters.AddWithValue("@id", m.Id);
            cmd.Parameters.AddWithValue("@name", m.Name);
            cmd.Parameters.AddWithValue("@model", m.Model);
            cmd.Parameters.AddWithValue("@ctrl", m.Controller);
            cmd.Parameters.AddWithValue("@ip", m.Ip);
            cmd.Parameters.AddWithValue("@now", now);
            cmd.ExecuteNonQuery();
        }
    }

    #region User & Authentication
    public bool HasAnyUser()
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM users;";
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    public User? GetUserByUsername(string username)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, username, password_hash, password_salt, display_name, role, is_active, created_at, last_login_at FROM users WHERE username = @u;";
        cmd.Parameters.AddWithValue("@u", username);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;

        return new User
        {
            Id = reader.GetInt32(0),
            Username = reader.GetString(1),
            PasswordHash = reader.GetString(2),
            PasswordSalt = reader.GetString(3),
            DisplayName = reader.GetString(4),
            Role = reader.GetString(5),
            IsActive = reader.GetInt32(6),
            CreatedAt = reader.GetString(7),
            LastLoginAt = reader.IsDBNull(8) ? null : reader.GetString(8)
        };
    }

    public List<User> GetAllUsers()
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, username, '', '', display_name, role, is_active, created_at, last_login_at FROM users ORDER BY id ASC;";
        using var reader = cmd.ExecuteReader();
        var list = new List<User>();
        while (reader.Read())
        {
            list.Add(new User
            {
                Id = reader.GetInt32(0),
                Username = reader.GetString(1),
                DisplayName = reader.GetString(4),
                Role = reader.GetString(5),
                IsActive = reader.GetInt32(6),
                CreatedAt = reader.GetString(7),
                LastLoginAt = reader.IsDBNull(8) ? null : reader.GetString(8)
            });
        }
        return list;
    }

    public bool CreateUser(string username, string password, string displayName, string role)
    {
        var salt = GenerateSalt();
        var hash = HashPassword(password, salt);
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO users (username, password_hash, password_salt, display_name, role, is_active, created_at)
            VALUES (@u, @h, @s, @d, @r, 1, @now);
        ";
        cmd.Parameters.AddWithValue("@u", username);
        cmd.Parameters.AddWithValue("@h", hash);
        cmd.Parameters.AddWithValue("@s", salt);
        cmd.Parameters.AddWithValue("@d", displayName);
        cmd.Parameters.AddWithValue("@r", role);
        cmd.Parameters.AddWithValue("@now", now);

        try
        {
            return cmd.ExecuteNonQuery() > 0;
        }
        catch
        {
            return false;
        }
    }

    public bool UpdateUserRole(int id, string role)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE users SET role = @r WHERE id = @id;";
        cmd.Parameters.AddWithValue("@r", role);
        cmd.Parameters.AddWithValue("@id", id);
        return cmd.ExecuteNonQuery() > 0;
    }

    public bool ResetUserPassword(int id, string newPassword)
    {
        var salt = GenerateSalt();
        var hash = HashPassword(newPassword, salt);
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE users SET password_hash = @h, password_salt = @s WHERE id = @id;";
        cmd.Parameters.AddWithValue("@h", hash);
        cmd.Parameters.AddWithValue("@s", salt);
        cmd.Parameters.AddWithValue("@id", id);
        return cmd.ExecuteNonQuery() > 0;
    }

    public bool ToggleUserActive(int id, int isActive)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE users SET is_active = @a WHERE id = @id;";
        cmd.Parameters.AddWithValue("@a", isActive);
        cmd.Parameters.AddWithValue("@id", id);
        return cmd.ExecuteNonQuery() > 0;
    }

    public bool DeleteUser(int id)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM users WHERE id = @id AND role != 'ADMIN';";
        cmd.Parameters.AddWithValue("@id", id);
        return cmd.ExecuteNonQuery() > 0;
    }

    public void UpdateLastLogin(string username)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE users SET last_login_at = @now WHERE username = @u;";
        cmd.Parameters.AddWithValue("@now", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.Parameters.AddWithValue("@u", username);
        cmd.ExecuteNonQuery();
    }

    public static string GenerateSalt()
    {
        var bytes = new byte[16];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes);
    }

    public static string HashPassword(string password, string salt)
    {
        using var pbkdf2 = new Rfc2898DeriveBytes(password, Convert.FromBase64String(salt), 100000, HashAlgorithmName.SHA256);
        return Convert.ToBase64String(pbkdf2.GetBytes(32));
    }

    public static bool VerifyPassword(string password, string salt, string hash)
    {
        return HashPassword(password, salt) == hash;
    }
    #endregion

    #region Machines
    public List<MachineStatus> GetAllMachines()
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT machine_id, machine_name, model_type, controller_type, ip_address, port, status, 
                   current_program, spindle_rpm, feed_rate, run_time_minutes, active_job_number, 
                   active_part_name, active_worker, last_heartbeat 
            FROM machines ORDER BY machine_id ASC;";
        using var reader = cmd.ExecuteReader();
        var list = new List<MachineStatus>();
        while (reader.Read())
        {
            list.Add(new MachineStatus
            {
                MachineId = reader.GetString(0),
                MachineName = reader.GetString(1),
                ModelType = reader.GetString(2),
                ControllerType = reader.GetString(3),
                IpAddress = reader.GetString(4),
                Port = reader.GetInt32(5),
                Status = reader.GetString(6),
                CurrentProgram = reader.IsDBNull(7) ? null : reader.GetString(7),
                SpindleRpm = reader.GetInt32(8),
                FeedRate = reader.GetDouble(9),
                RunTimeMinutes = reader.GetDouble(10),
                ActiveJobNumber = reader.IsDBNull(11) ? null : reader.GetString(11),
                ActivePartName = reader.IsDBNull(12) ? null : reader.GetString(12),
                ActiveWorker = reader.IsDBNull(13) ? null : reader.GetString(13),
                LastHeartbeat = reader.GetString(14)
            });
        }
        return list;
    }

    public void AddMachine(MachineStatus m)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT OR REPLACE INTO machines 
            (machine_id, machine_name, model_type, controller_type, ip_address, port, status, last_heartbeat)
            VALUES (@id, @name, @model, @ctrl, @ip, @port, @st, @now);
        ";
        cmd.Parameters.AddWithValue("@id", m.MachineId.Trim().ToUpperInvariant());
        cmd.Parameters.AddWithValue("@name", m.MachineName.Trim());
        cmd.Parameters.AddWithValue("@model", string.IsNullOrWhiteSpace(m.ModelType) ? "RoboDrill" : m.ModelType.Trim());
        cmd.Parameters.AddWithValue("@ctrl", string.IsNullOrWhiteSpace(m.ControllerType) ? "FANUC 31i-B5" : m.ControllerType.Trim());
        cmd.Parameters.AddWithValue("@ip", string.IsNullOrWhiteSpace(m.IpAddress) ? "192.168.1.100" : m.IpAddress.Trim());
        cmd.Parameters.AddWithValue("@port", m.Port > 0 ? m.Port : 8193);
        cmd.Parameters.AddWithValue("@st", string.IsNullOrWhiteSpace(m.Status) ? "대기" : m.Status);
        cmd.Parameters.AddWithValue("@now", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.ExecuteNonQuery();
    }

    public bool DeleteMachine(string machineId)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM machines WHERE machine_id = @id;";
        cmd.Parameters.AddWithValue("@id", machineId);
        return cmd.ExecuteNonQuery() > 0;
    }

    public void UpdateMachineStatus(string machineId, string status, string? program, int rpm, double feed, string? jobNum, string? partName, string? worker)
    {
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE machines SET 
                status = @st, current_program = @prog, spindle_rpm = @rpm, feed_rate = @feed,
                active_job_number = @jn, active_part_name = @pn, active_worker = @w,
                last_heartbeat = @now
            WHERE machine_id = @id;
        ";
        cmd.Parameters.AddWithValue("@id", machineId);
        cmd.Parameters.AddWithValue("@st", status);
        cmd.Parameters.AddWithValue("@prog", (object?)program ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@rpm", rpm);
        cmd.Parameters.AddWithValue("@feed", feed);
        cmd.Parameters.AddWithValue("@jn", (object?)jobNum ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@pn", (object?)partName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@w", (object?)worker ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@now", now);
        cmd.ExecuteNonQuery();

        // Also record into machine_work_logs so recent history shows OrderNumber + PartName + WorkDetail
        if (!string.IsNullOrWhiteSpace(worker) || !string.IsNullOrWhiteSpace(program))
        {
            using var logCmd = conn.CreateCommand();
            logCmd.CommandText = @"
                INSERT INTO machine_work_logs (machine_id, order_number, part_name, work_detail, status, worker_name, qty, created_at)
                VALUES (@mid, @ord, @prt, @det, @st, @wk, 0, @now);
            ";
            logCmd.Parameters.AddWithValue("@mid", machineId);
            logCmd.Parameters.AddWithValue("@ord", string.IsNullOrWhiteSpace(jobNum) ? "-" : jobNum);
            logCmd.Parameters.AddWithValue("@prt", string.IsNullOrWhiteSpace(partName) ? "-" : partName);
            logCmd.Parameters.AddWithValue("@det", string.IsNullOrWhiteSpace(program) ? status : program);
            logCmd.Parameters.AddWithValue("@st", status);
            logCmd.Parameters.AddWithValue("@wk", string.IsNullOrWhiteSpace(worker) ? "-" : worker);
            logCmd.Parameters.AddWithValue("@now", now);
            logCmd.ExecuteNonQuery();
        }
    }

    public int AddMachineWorkLog(MachineWorkLog log)
    {
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO machine_work_logs (machine_id, order_number, part_name, work_detail, status, worker_name, sub_worker_name, qty, created_at)
            VALUES (@mid, @ord, @prt, @det, @st, @wk, @sub, @qty, @now);
            SELECT last_insert_rowid();
        ";
        cmd.Parameters.AddWithValue("@mid", log.MachineId);
        cmd.Parameters.AddWithValue("@ord", string.IsNullOrWhiteSpace(log.OrderNumber) ? "-" : log.OrderNumber);
        cmd.Parameters.AddWithValue("@prt", string.IsNullOrWhiteSpace(log.PartName) ? "-" : log.PartName);
        cmd.Parameters.AddWithValue("@det", log.WorkDetail);
        cmd.Parameters.AddWithValue("@st", log.Status);
        cmd.Parameters.AddWithValue("@wk", log.WorkerName);
        cmd.Parameters.AddWithValue("@sub", (object?)log.SubWorkerName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@qty", log.Qty);
        cmd.Parameters.AddWithValue("@now", now);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public List<MachineWorkLog> GetMachineWorkLogs(string? machineId = null, int limit = 60)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        if (!string.IsNullOrWhiteSpace(machineId))
        {
            cmd.CommandText = @"
                SELECT id, machine_id, order_number, part_name, work_detail, status, worker_name, sub_worker_name, qty, created_at
                FROM machine_work_logs
                WHERE machine_id = @mid
                ORDER BY id DESC LIMIT @lim;
            ";
            cmd.Parameters.AddWithValue("@mid", machineId);
        }
        else
        {
            cmd.CommandText = @"
                SELECT id, machine_id, order_number, part_name, work_detail, status, worker_name, sub_worker_name, qty, created_at
                FROM machine_work_logs
                ORDER BY id DESC LIMIT @lim;
            ";
        }
        cmd.Parameters.AddWithValue("@lim", limit);

        using var reader = cmd.ExecuteReader();
        var list = new List<MachineWorkLog>();
        while (reader.Read())
        {
            list.Add(new MachineWorkLog
            {
                Id = reader.GetInt32(0),
                MachineId = reader.GetString(1),
                OrderNumber = reader.GetString(2),
                PartName = reader.GetString(3),
                WorkDetail = reader.GetString(4),
                Status = reader.GetString(5),
                WorkerName = reader.GetString(6),
                SubWorkerName = reader.IsDBNull(7) ? null : reader.GetString(7),
                Qty = reader.GetInt32(8),
                CreatedAt = reader.GetString(9)
            });
        }
        return list;
    }

    public MachineWorkLog? GetMachineWorkLogById(int id)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, machine_id, order_number, part_name, work_detail, status, worker_name, sub_worker_name, qty, created_at
            FROM machine_work_logs
            WHERE id = @id;
        ";
        cmd.Parameters.AddWithValue("@id", id);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return new MachineWorkLog
        {
            Id = reader.GetInt32(0),
            MachineId = reader.GetString(1),
            OrderNumber = reader.GetString(2),
            PartName = reader.GetString(3),
            WorkDetail = reader.GetString(4),
            Status = reader.GetString(5),
            WorkerName = reader.GetString(6),
            SubWorkerName = reader.IsDBNull(7) ? null : reader.GetString(7),
            Qty = reader.GetInt32(8),
            CreatedAt = reader.GetString(9)
        };
    }

    public (MachineWorkLog? before, MachineWorkLog? after) UpdateMachineWorkLog(int id, MachineWorkLog updated)
    {
        var before = GetMachineWorkLogById(id);
        if (before == null) return (null, null);

        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE machine_work_logs
            SET machine_id = @mid,
                order_number = @ord,
                part_name = @prt,
                work_detail = @det,
                status = @st,
                worker_name = @wk,
                qty = @qty
            WHERE id = @id;
        ";
        cmd.Parameters.AddWithValue("@mid", string.IsNullOrWhiteSpace(updated.MachineId) ? before.MachineId : updated.MachineId);
        cmd.Parameters.AddWithValue("@ord", string.IsNullOrWhiteSpace(updated.OrderNumber) ? "-" : updated.OrderNumber);
        cmd.Parameters.AddWithValue("@prt", string.IsNullOrWhiteSpace(updated.PartName) ? "-" : updated.PartName);
        cmd.Parameters.AddWithValue("@det", string.IsNullOrWhiteSpace(updated.WorkDetail) ? before.WorkDetail : updated.WorkDetail);
        cmd.Parameters.AddWithValue("@st", string.IsNullOrWhiteSpace(updated.Status) ? before.Status : updated.Status);
        cmd.Parameters.AddWithValue("@wk", string.IsNullOrWhiteSpace(updated.WorkerName) ? before.WorkerName : updated.WorkerName);
        cmd.Parameters.AddWithValue("@qty", updated.Qty);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();

        var after = GetMachineWorkLogById(id);
        return (before, after);
    }

    public MachineWorkLog? DeleteMachineWorkLog(int id)
    {
        var before = GetMachineWorkLogById(id);
        if (before == null) return null;

        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM machine_work_logs WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
        return before;
    }

    public int CreateSubWorkerTask(SubWorkerTask t)
    {
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO sub_worker_tasks (machine_id, order_number, part_name, leader_username, sub_worker_name, instruction, report_content, reported_qty, status, created_at)
            VALUES (@mid, @ord, @prt, @leader, @sub, @inst, @rep, @qty, '지시중', @now);
            SELECT last_insert_rowid();
        ";
        cmd.Parameters.AddWithValue("@mid", t.MachineId);
        cmd.Parameters.AddWithValue("@ord", string.IsNullOrWhiteSpace(t.OrderNumber) ? "-" : t.OrderNumber);
        cmd.Parameters.AddWithValue("@prt", string.IsNullOrWhiteSpace(t.PartName) ? "-" : t.PartName);
        cmd.Parameters.AddWithValue("@leader", t.LeaderUsername);
        cmd.Parameters.AddWithValue("@sub", t.SubWorkerName);
        cmd.Parameters.AddWithValue("@inst", t.Instruction);
        cmd.Parameters.AddWithValue("@rep", (object?)t.ReportContent ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@qty", t.ReportedQty);
        cmd.Parameters.AddWithValue("@now", now);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public SubWorkerTask? ReportSubWorkerTask(int taskId, string reportContent, int reportedQty, string status = "중앙보고완료")
    {
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        using var conn = CreateConnection();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                UPDATE sub_worker_tasks
                SET report_content = @rep, reported_qty = @qty, status = @st, reported_at = @now
                WHERE task_id = @tid;
            ";
            cmd.Parameters.AddWithValue("@rep", reportContent);
            cmd.Parameters.AddWithValue("@qty", reportedQty);
            cmd.Parameters.AddWithValue("@st", status);
            cmd.Parameters.AddWithValue("@now", now);
            cmd.Parameters.AddWithValue("@tid", taskId);
            cmd.ExecuteNonQuery();
        }

        var tasks = GetSubWorkerTasks(null);
        return tasks.FirstOrDefault(x => x.TaskId == taskId);
    }

    public List<SubWorkerTask> GetSubWorkerTasks(string? leaderUsername = null)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        if (!string.IsNullOrWhiteSpace(leaderUsername))
        {
            cmd.CommandText = @"
                SELECT task_id, machine_id, order_number, part_name, leader_username, sub_worker_name, instruction, report_content, reported_qty, status, created_at, reported_at
                FROM sub_worker_tasks
                WHERE leader_username = @leader
                ORDER BY task_id DESC LIMIT 50;
            ";
            cmd.Parameters.AddWithValue("@leader", leaderUsername);
        }
        else
        {
            cmd.CommandText = @"
                SELECT task_id, machine_id, order_number, part_name, leader_username, sub_worker_name, instruction, report_content, reported_qty, status, created_at, reported_at
                FROM sub_worker_tasks
                ORDER BY task_id DESC LIMIT 50;
            ";
        }

        using var reader = cmd.ExecuteReader();
        var list = new List<SubWorkerTask>();
        while (reader.Read())
        {
            list.Add(new SubWorkerTask
            {
                TaskId = reader.GetInt32(0),
                MachineId = reader.GetString(1),
                OrderNumber = reader.GetString(2),
                PartName = reader.GetString(3),
                LeaderUsername = reader.GetString(4),
                SubWorkerName = reader.GetString(5),
                Instruction = reader.GetString(6),
                ReportContent = reader.IsDBNull(7) ? null : reader.GetString(7),
                ReportedQty = reader.GetInt32(8),
                Status = reader.GetString(9),
                CreatedAt = reader.GetString(10),
                ReportedAt = reader.IsDBNull(11) ? null : reader.GetString(11)
            });
        }
        return list;
    }

    // =========================================================================
    // 사용 공구 이력 관리 (Machine Used Tools)
    // =========================================================================
    public int AddMachineUsedTool(MachineUsedTool tool)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        cmd.CommandText = @"
            INSERT INTO machine_used_tools (
                machine_id, order_number, part_name, tool_name, maker, tool_diameter, flute_length, material, notes, worker_name, created_at
            ) VALUES (
                @mid, @ord, @part, @tn, @maker, @dia, @fl, @mat, @notes, @w, @now
            );
            SELECT last_insert_rowid();
        ";
        cmd.Parameters.AddWithValue("@mid", tool.MachineId);
        cmd.Parameters.AddWithValue("@ord", string.IsNullOrWhiteSpace(tool.OrderNumber) ? "-" : tool.OrderNumber);
        cmd.Parameters.AddWithValue("@part", string.IsNullOrWhiteSpace(tool.PartName) ? "-" : tool.PartName);
        cmd.Parameters.AddWithValue("@tn", tool.ToolName);
        cmd.Parameters.AddWithValue("@maker", string.IsNullOrWhiteSpace(tool.Maker) ? "-" : tool.Maker);
        cmd.Parameters.AddWithValue("@dia", string.IsNullOrWhiteSpace(tool.ToolDiameter) ? "-" : tool.ToolDiameter);
        cmd.Parameters.AddWithValue("@fl", string.IsNullOrWhiteSpace(tool.FluteLength) ? "-" : tool.FluteLength);
        cmd.Parameters.AddWithValue("@mat", string.IsNullOrWhiteSpace(tool.Material) ? "-" : tool.Material);
        cmd.Parameters.AddWithValue("@notes", string.IsNullOrWhiteSpace(tool.Notes) ? "-" : tool.Notes);
        cmd.Parameters.AddWithValue("@w", tool.WorkerName);
        cmd.Parameters.AddWithValue("@now", now);

        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public List<MachineUsedTool> GetMachineUsedTools(string? machineId = null, int limit = 60)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        if (!string.IsNullOrWhiteSpace(machineId))
        {
            cmd.CommandText = @"
                SELECT tool_log_id, machine_id, order_number, part_name, tool_name, maker, tool_diameter, flute_length, material, notes, worker_name, created_at
                FROM machine_used_tools
                WHERE machine_id = @mid
                ORDER BY tool_log_id DESC LIMIT @lim;
            ";
            cmd.Parameters.AddWithValue("@mid", machineId);
        }
        else
        {
            cmd.CommandText = @"
                SELECT tool_log_id, machine_id, order_number, part_name, tool_name, maker, tool_diameter, flute_length, material, notes, worker_name, created_at
                FROM machine_used_tools
                ORDER BY tool_log_id DESC LIMIT @lim;
            ";
        }
        cmd.Parameters.AddWithValue("@lim", limit);

        using var reader = cmd.ExecuteReader();
        var list = new List<MachineUsedTool>();
        while (reader.Read())
        {
            list.Add(new MachineUsedTool
            {
                ToolLogId = reader.GetInt32(0),
                MachineId = reader.GetString(1),
                OrderNumber = reader.GetString(2),
                PartName = reader.GetString(3),
                ToolName = reader.GetString(4),
                Maker = reader.GetString(5),
                ToolDiameter = reader.GetString(6),
                FluteLength = reader.GetString(7),
                Material = reader.GetString(8),
                Notes = reader.GetString(9),
                WorkerName = reader.GetString(10),
                CreatedAt = reader.GetString(11)
            });
        }
        return list;
    }

    public bool DeleteMachineUsedTool(int toolLogId)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM machine_used_tools WHERE tool_log_id = @id;";
        cmd.Parameters.AddWithValue("@id", toolLogId);
        return cmd.ExecuteNonQuery() > 0;
    }

    #endregion

    #region Setting Groups
    public List<SettingGroup> GetAllSettingGroups()
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT group_id, group_name, material, thickness, fixture_type, origin_point, shared_tools_json, notes, created_at FROM setting_groups ORDER BY group_name ASC;";
        using var reader = cmd.ExecuteReader();
        var list = new List<SettingGroup>();
        while (reader.Read())
        {
            list.Add(new SettingGroup
            {
                GroupId = reader.GetInt32(0),
                GroupName = reader.GetString(1),
                Material = reader.GetString(2),
                Thickness = reader.GetDouble(3),
                FixtureType = reader.IsDBNull(4) ? null : reader.GetString(4),
                OriginPoint = reader.IsDBNull(5) ? null : reader.GetString(5),
                SharedToolsJson = reader.IsDBNull(6) ? null : reader.GetString(6),
                Notes = reader.IsDBNull(7) ? null : reader.GetString(7),
                CreatedAt = reader.GetString(8)
            });
        }
        return list;
    }

    public int CreateSettingGroup(SettingGroup group)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO setting_groups (group_name, material, thickness, fixture_type, origin_point, shared_tools_json, notes, created_at)
            VALUES (@gn, @mat, @th, @fx, @op, @tools, @notes, @now);
            SELECT last_insert_rowid();
        ";
        cmd.Parameters.AddWithValue("@gn", group.GroupName);
        cmd.Parameters.AddWithValue("@mat", group.Material);
        cmd.Parameters.AddWithValue("@th", group.Thickness);
        cmd.Parameters.AddWithValue("@fx", (object?)group.FixtureType ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@op", (object?)group.OriginPoint ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@tools", (object?)group.SharedToolsJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@notes", (object?)group.Notes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@now", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        return Convert.ToInt32(cmd.ExecuteScalar());
    }
    #endregion

    #region Work Orders & Parts & Routing Steps
    public List<WorkOrder> GetAllWorkOrders(string? statusFilter = null, bool includeHidden = false, bool onlyHidden = false)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        var sql = "SELECT order_id, order_number, customer_name, target_delivery_date, status, progress_rate, notes, created_by, created_at, updated_at, folder_path, backup_path, completed_at, COALESCE(is_hidden, 0) FROM work_orders WHERE 1=1";

        if (onlyHidden)
        {
            sql += " AND is_hidden = 1";
        }
        else if (!includeHidden)
        {
            sql += " AND (is_hidden = 0 OR is_hidden IS NULL)";
        }

        if (!string.IsNullOrEmpty(statusFilter))
        {
            sql += " AND status = @st";
            cmd.Parameters.AddWithValue("@st", statusFilter);
        }

        sql += " ORDER BY target_delivery_date ASC;";
        cmd.CommandText = sql;

        using var reader = cmd.ExecuteReader();
        var orders = new List<WorkOrder>();
        while (reader.Read())
        {
            orders.Add(new WorkOrder
            {
                OrderId = reader.GetInt32(0),
                OrderNumber = reader.GetString(1),
                CustomerName = reader.GetString(2),
                TargetDeliveryDate = reader.GetString(3),
                Status = reader.GetString(4),
                ProgressRate = reader.GetInt32(5),
                Notes = reader.IsDBNull(6) ? null : reader.GetString(6),
                CreatedBy = reader.GetString(7),
                CreatedAt = reader.GetString(8),
                UpdatedAt = reader.GetString(9),
                FolderPath = reader.IsDBNull(10) ? null : reader.GetString(10),
                BackupPath = reader.IsDBNull(11) ? null : reader.GetString(11),
                CompletedAt = reader.IsDBNull(12) ? null : reader.GetString(12),
                IsHidden = reader.GetInt32(13) == 1
            });
        }

        foreach (var order in orders)
        {
            order.Parts = GetPartsByOrderId(order.OrderId);
        }
        return orders;
    }

    public WorkOrder? GetWorkOrderById(int orderId)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT order_id, order_number, customer_name, target_delivery_date, status, progress_rate, notes, created_by, created_at, updated_at, folder_path, backup_path, completed_at, COALESCE(is_hidden, 0) FROM work_orders WHERE order_id = @id;";
        cmd.Parameters.AddWithValue("@id", orderId);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;

        var order = new WorkOrder
        {
            OrderId = reader.GetInt32(0),
            OrderNumber = reader.GetString(1),
            CustomerName = reader.GetString(2),
            TargetDeliveryDate = reader.GetString(3),
            Status = reader.GetString(4),
            ProgressRate = reader.GetInt32(5),
            Notes = reader.IsDBNull(6) ? null : reader.GetString(6),
            CreatedBy = reader.GetString(7),
            CreatedAt = reader.GetString(8),
            UpdatedAt = reader.GetString(9),
            FolderPath = reader.IsDBNull(10) ? null : reader.GetString(10),
            BackupPath = reader.IsDBNull(11) ? null : reader.GetString(11),
            CompletedAt = reader.IsDBNull(12) ? null : reader.GetString(12),
            IsHidden = reader.GetInt32(13) == 1
        };
        order.Parts = GetPartsByOrderId(order.OrderId);
        return order;
    }

    public bool SetWorkOrderHidden(int orderId, bool isHidden)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE work_orders SET is_hidden = @hid, updated_at = @now WHERE order_id = @oid;";
        cmd.Parameters.AddWithValue("@hid", isHidden ? 1 : 0);
        cmd.Parameters.AddWithValue("@now", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.Parameters.AddWithValue("@oid", orderId);
        return cmd.ExecuteNonQuery() > 0;
    }

    public List<WOPart> GetPartsByOrderId(int orderId)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT p.part_id, p.order_id, o.order_number, p.part_code, p.part_name, p.material, p.thickness, 
                   p.target_qty, p.completed_qty, p.production_type, p.setting_group_id, g.group_name, 
                   p.current_step_sequence, p.status, p.created_at,
                   COALESCE(p.post_process, '') AS post_process,
                   COALESCE(p.post_process_status, '') AS post_process_status,
                   COALESCE(p.post_process_notes, '') AS post_process_notes
            FROM wo_parts p
            JOIN work_orders o ON p.order_id = o.order_id
            LEFT JOIN setting_groups g ON p.setting_group_id = g.group_id
            WHERE p.order_id = @oid
            ORDER BY p.part_id ASC;
        ";
        cmd.Parameters.AddWithValue("@oid", orderId);

        using var reader = cmd.ExecuteReader();
        var parts = new List<WOPart>();
        while (reader.Read())
        {
            parts.Add(new WOPart
            {
                PartId = reader.GetInt32(0),
                OrderId = reader.GetInt32(1),
                OrderNumber = reader.GetString(2),
                PartCode = reader.GetString(3),
                PartName = reader.GetString(4),
                Material = reader.GetString(5),
                Thickness = reader.GetDouble(6),
                TargetQty = reader.GetInt32(7),
                CompletedQty = reader.GetInt32(8),
                ProductionType = reader.GetString(9),
                SettingGroupId = reader.IsDBNull(10) ? null : reader.GetInt32(10),
                SettingGroupName = reader.IsDBNull(11) ? null : reader.GetString(11),
                CurrentStepSequence = reader.GetInt32(12),
                Status = reader.GetString(13),
                CreatedAt = reader.GetString(14),
                PostProcess = reader.IsDBNull(15) ? null : reader.GetString(15),
                PostProcessStatus = reader.IsDBNull(16) ? null : reader.GetString(16),
                PostProcessNotes = reader.IsDBNull(17) ? null : reader.GetString(17)
            });
        }

        foreach (var p in parts)
        {
            p.RoutingSteps = GetRoutingStepsByPartId(p.PartId);
        }
        return parts;
    }

    public WOPart? GetPartById(int partId)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT p.part_id, p.order_id, o.order_number, p.part_code, p.part_name, p.material, p.thickness, 
                   p.target_qty, p.completed_qty, p.production_type, p.setting_group_id, g.group_name, 
                   p.current_step_sequence, p.status, p.created_at,
                   COALESCE(p.post_process, '') AS post_process,
                   COALESCE(p.post_process_status, '') AS post_process_status,
                   COALESCE(p.post_process_notes, '') AS post_process_notes
            FROM wo_parts p
            JOIN work_orders o ON p.order_id = o.order_id
            LEFT JOIN setting_groups g ON p.setting_group_id = g.group_id
            WHERE p.part_id = @pid;
        ";
        cmd.Parameters.AddWithValue("@pid", partId);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;

        var part = new WOPart
        {
            PartId = reader.GetInt32(0),
            OrderId = reader.GetInt32(1),
            OrderNumber = reader.GetString(2),
            PartCode = reader.GetString(3),
            PartName = reader.GetString(4),
            Material = reader.GetString(5),
            Thickness = reader.GetDouble(6),
            TargetQty = reader.GetInt32(7),
            CompletedQty = reader.GetInt32(8),
            ProductionType = reader.GetString(9),
            SettingGroupId = reader.IsDBNull(10) ? null : reader.GetInt32(10),
            SettingGroupName = reader.IsDBNull(11) ? null : reader.GetString(11),
            CurrentStepSequence = reader.GetInt32(12),
            Status = reader.GetString(13),
            CreatedAt = reader.GetString(14),
            PostProcess = reader.IsDBNull(15) ? null : reader.GetString(15),
            PostProcessStatus = reader.IsDBNull(16) ? null : reader.GetString(16),
            PostProcessNotes = reader.IsDBNull(17) ? null : reader.GetString(17)
        };
        part.RoutingSteps = GetRoutingStepsByPartId(part.PartId);
        return part;
    }

    public List<RoutingStep> GetRoutingStepsByPartId(int partId)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT step_id, part_id, step_sequence, step_name, target_machine, nc_program_name, nc_program_version,
                   status, machined_qty, inspected_qty, passed_qty, failed_qty, rework_qty, notes
            FROM routing_steps
            WHERE part_id = @pid
            ORDER BY step_sequence ASC;
        ";
        cmd.Parameters.AddWithValue("@pid", partId);

        using var reader = cmd.ExecuteReader();
        var steps = new List<RoutingStep>();
        while (reader.Read())
        {
            steps.Add(new RoutingStep
            {
                StepId = reader.GetInt32(0),
                PartId = reader.GetInt32(1),
                StepSequence = reader.GetInt32(2),
                StepName = reader.GetString(3),
                TargetMachine = reader.GetString(4),
                NcProgramName = reader.IsDBNull(5) ? null : reader.GetString(5),
                NcProgramVersion = reader.IsDBNull(6) ? null : reader.GetString(6),
                Status = reader.GetString(7),
                MachinedQty = reader.GetInt32(8),
                InspectedQty = reader.GetInt32(9),
                PassedQty = reader.GetInt32(10),
                FailedQty = reader.GetInt32(11),
                ReworkQty = reader.GetInt32(12),
                Notes = reader.IsDBNull(13) ? null : reader.GetString(13)
            });
        }
        return steps;
    }

    public int CreateWorkOrder(WorkOrder order)
    {
        using var conn = CreateConnection();
        using var tx = conn.BeginTransaction();

        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            INSERT INTO work_orders (order_number, customer_name, target_delivery_date, status, progress_rate, notes, created_by, created_at, updated_at, folder_path, backup_path, completed_at)
            VALUES (@on, @cn, @tdd, @st, 0, @notes, @cb, @now, @now, @fp, @bp, @ca);
            SELECT last_insert_rowid();
        ";
        cmd.Parameters.AddWithValue("@on", order.OrderNumber);
        cmd.Parameters.AddWithValue("@cn", order.CustomerName);
        cmd.Parameters.AddWithValue("@tdd", order.TargetDeliveryDate);
        cmd.Parameters.AddWithValue("@st", order.Status);
        cmd.Parameters.AddWithValue("@notes", (object?)order.Notes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@cb", order.CreatedBy);
        cmd.Parameters.AddWithValue("@now", now);
        cmd.Parameters.AddWithValue("@fp", (object?)order.FolderPath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@bp", (object?)order.BackupPath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ca", (object?)order.CompletedAt ?? DBNull.Value);

        var orderId = Convert.ToInt32(cmd.ExecuteScalar());

        // Parts & Steps insertion
        foreach (var p in order.Parts)
        {
            // Auto recommend Single vs Mass Production if not explicitly chosen
            var prodType = p.ProductionType;
            if (string.IsNullOrEmpty(prodType))
            {
                prodType = p.TargetQty >= 4 ? ProductionTypes.MassProduction : ProductionTypes.SinglePart;
            }

            using var partCmd = conn.CreateCommand();
            partCmd.Transaction = tx;
            partCmd.CommandText = @"
                INSERT INTO wo_parts (order_id, part_code, part_name, material, thickness, target_qty, completed_qty, production_type, setting_group_id, current_step_sequence, status, created_at)
                VALUES (@oid, @pc, @pn, @mat, @th, @tq, 0, @pt, @sg, 1, '대기', @now);
                SELECT last_insert_rowid();
            ";
            partCmd.Parameters.AddWithValue("@oid", orderId);
            partCmd.Parameters.AddWithValue("@pc", p.PartCode);
            partCmd.Parameters.AddWithValue("@pn", p.PartName);
            partCmd.Parameters.AddWithValue("@mat", p.Material);
            partCmd.Parameters.AddWithValue("@th", p.Thickness);
            partCmd.Parameters.AddWithValue("@tq", p.TargetQty);
            partCmd.Parameters.AddWithValue("@pt", prodType);
            partCmd.Parameters.AddWithValue("@sg", (object?)p.SettingGroupId ?? DBNull.Value);
            partCmd.Parameters.AddWithValue("@now", now);

            var partId = Convert.ToInt32(partCmd.ExecuteScalar());

            // Default routing steps if not provided: 1차가공 -> 1차측정 -> 2차가공 -> 2차측정 -> 최종완료
            var steps = p.RoutingSteps.Count > 0 ? p.RoutingSteps : new List<RoutingStep>
            {
                new RoutingStep { StepSequence = 1, StepName = "1차 가공", TargetMachine = "R01" },
                new RoutingStep { StepSequence = 2, StepName = "1차 측정", TargetMachine = "측정실(Micro-Vu)" },
                new RoutingStep { StepSequence = 3, StepName = "2차 가공", TargetMachine = "R02" },
                new RoutingStep { StepSequence = 4, StepName = "2차 측정", TargetMachine = "측정실(Micro-Vu)" }
            };

            foreach (var s in steps)
            {
                using var stepCmd = conn.CreateCommand();
                stepCmd.Transaction = tx;
                stepCmd.CommandText = @"
                    INSERT INTO routing_steps (part_id, step_sequence, step_name, target_machine, nc_program_name, nc_program_version, status, machined_qty, inspected_qty, passed_qty, failed_qty, rework_qty, notes)
                    VALUES (@pid, @seq, @sname, @tm, @nc, @ncver, '대기', 0, 0, 0, 0, 0, @notes);
                ";
                stepCmd.Parameters.AddWithValue("@pid", partId);
                stepCmd.Parameters.AddWithValue("@seq", s.StepSequence);
                stepCmd.Parameters.AddWithValue("@sname", s.StepName);
                stepCmd.Parameters.AddWithValue("@tm", s.TargetMachine);
                stepCmd.Parameters.AddWithValue("@nc", (object?)s.NcProgramName ?? DBNull.Value);
                stepCmd.Parameters.AddWithValue("@ncver", (object?)s.NcProgramVersion ?? DBNull.Value);
                stepCmd.Parameters.AddWithValue("@notes", (object?)s.Notes ?? DBNull.Value);
                stepCmd.ExecuteNonQuery();
            }
        }

        tx.Commit();
        return orderId;
    }
    #endregion

    #region Work Execution (실제 가공 기록 - 분할/교차/중단/재개)
    public int StartExecution(int stepId, int? assignmentId, string machineId, string workerUsername, int targetQty, string? notes)
    {
        using var conn = CreateConnection();
        using var tx = conn.BeginTransaction();

        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            INSERT INTO work_executions (step_id, assignment_id, machine_id, worker_username, start_time, machined_qty, status, notes)
            VALUES (@sid, @aid, @mid, @w, @now, 0, '가공중', @notes);
            SELECT last_insert_rowid();
        ";
        cmd.Parameters.AddWithValue("@sid", stepId);
        cmd.Parameters.AddWithValue("@aid", (object?)assignmentId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@mid", machineId);
        cmd.Parameters.AddWithValue("@w", workerUsername);
        cmd.Parameters.AddWithValue("@now", now);
        cmd.Parameters.AddWithValue("@notes", (object?)notes ?? DBNull.Value);

        var execId = Convert.ToInt32(cmd.ExecuteScalar());

        // Update step status to Machining
        using var updateStepCmd = conn.CreateCommand();
        updateStepCmd.Transaction = tx;
        updateStepCmd.CommandText = "UPDATE routing_steps SET status = '가공중' WHERE step_id = @sid;";
        updateStepCmd.Parameters.AddWithValue("@sid", stepId);
        updateStepCmd.ExecuteNonQuery();

        // Update part & order status to InProgress
        using var updatePartCmd = conn.CreateCommand();
        updatePartCmd.Transaction = tx;
        updatePartCmd.CommandText = @"
            UPDATE wo_parts SET status = '가공중' WHERE part_id = (SELECT part_id FROM routing_steps WHERE step_id = @sid);
            UPDATE work_orders SET status = '가공중' WHERE order_id = (SELECT order_id FROM wo_parts WHERE part_id = (SELECT part_id FROM routing_steps WHERE step_id = @sid));
        ";
        updatePartCmd.Parameters.AddWithValue("@sid", stepId);
        updatePartCmd.ExecuteNonQuery();

        tx.Commit();

        // Also record into machine_work_logs with resolved order_number and part_name
        try
        {
            using var infoCmd = conn.CreateCommand();
            infoCmd.CommandText = @"
                SELECT wo.order_number, wp.part_name, rs.step_name
                FROM routing_steps rs
                JOIN wo_parts wp ON rs.part_id = wp.part_id
                JOIN work_orders wo ON wp.order_id = wo.order_id
                WHERE rs.step_id = @sid;
            ";
            infoCmd.Parameters.AddWithValue("@sid", stepId);
            using var r = infoCmd.ExecuteReader();
            string ordNo = "-", prtName = "-", stepName = "가공시작";
            if (r.Read())
            {
                ordNo = r.GetString(0);
                prtName = r.GetString(1);
                stepName = r.GetString(2);
            }
            r.Close();

            AddMachineWorkLog(new MachineWorkLog
            {
                MachineId = machineId,
                OrderNumber = ordNo,
                PartName = prtName,
                WorkDetail = string.IsNullOrWhiteSpace(notes) ? $"{stepName} 시작" : notes,
                Status = "가공중",
                WorkerName = workerUsername,
                Qty = targetQty
            });
        }
        catch { }

        return execId;
    }

    public void FinishExecution(int executionId, int machinedQty, string? notes)
    {
        using var conn = CreateConnection();
        using var tx = conn.BeginTransaction();

        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        // 1. Update execution
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            UPDATE work_executions 
            SET end_time = @now, machined_qty = @qty, status = '가공완료', notes = COALESCE(@notes, notes)
            WHERE execution_id = @eid;
            SELECT step_id FROM work_executions WHERE execution_id = @eid;
        ";
        cmd.Parameters.AddWithValue("@now", now);
        cmd.Parameters.AddWithValue("@qty", machinedQty);
        cmd.Parameters.AddWithValue("@notes", (object?)notes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@eid", executionId);

        var stepId = Convert.ToInt32(cmd.ExecuteScalar());

        // 2. Accumulate machined_qty in routing_steps and set to WaitingInspection! (측정대기 필수 원칙)
        using var stepCmd = conn.CreateCommand();
        stepCmd.Transaction = tx;
        stepCmd.CommandText = @"
            UPDATE routing_steps 
            SET machined_qty = machined_qty + @qty, status = '측정대기'
            WHERE step_id = @sid;
        ";
        stepCmd.Parameters.AddWithValue("@qty", machinedQty);
        stepCmd.Parameters.AddWithValue("@sid", stepId);
        stepCmd.ExecuteNonQuery();

        // 3. Update Part status to WaitingInspection
        using var partCmd = conn.CreateCommand();
        partCmd.Transaction = tx;
        partCmd.CommandText = @"
            UPDATE wo_parts SET status = '측정대기' 
            WHERE part_id = (SELECT part_id FROM routing_steps WHERE step_id = @sid);
        ";
        partCmd.Parameters.AddWithValue("@sid", stepId);
        partCmd.ExecuteNonQuery();

        tx.Commit();

        // Also record into machine_work_logs with resolved order_number and part_name
        try
        {
            using var infoCmd = conn.CreateCommand();
            infoCmd.CommandText = @"
                SELECT we.machine_id, we.worker_username, wo.order_number, wp.part_name, rs.step_name
                FROM work_executions we
                JOIN routing_steps rs ON we.step_id = rs.step_id
                JOIN wo_parts wp ON rs.part_id = wp.part_id
                JOIN work_orders wo ON wp.order_id = wo.order_id
                WHERE we.execution_id = @eid;
            ";
            infoCmd.Parameters.AddWithValue("@eid", executionId);
            using var r = infoCmd.ExecuteReader();
            if (r.Read())
            {
                var mid = r.GetString(0);
                var wkr = r.GetString(1);
                var ordNo = r.GetString(2);
                var prtName = r.GetString(3);
                var stepName = r.GetString(4);
                r.Close();

                AddMachineWorkLog(new MachineWorkLog
                {
                    MachineId = mid,
                    OrderNumber = ordNo,
                    PartName = prtName,
                    WorkDetail = string.IsNullOrWhiteSpace(notes) ? $"{stepName} 완료 ({machinedQty}EA)" : notes,
                    Status = "가공완료",
                    WorkerName = wkr,
                    Qty = machinedQty
                });
            }
        }
        catch { }
    }

    public List<WorkExecution> GetExecutionsByStepId(int stepId)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT we.execution_id, we.step_id, we.assignment_id, we.machine_id, we.worker_username,
                   we.start_time, we.end_time, we.machined_qty, we.status, we.notes,
                   COALESCE(wo.order_number, '-'), COALESCE(wp.part_name, '-')
            FROM work_executions we
            LEFT JOIN routing_steps rs ON we.step_id = rs.step_id
            LEFT JOIN wo_parts wp ON rs.part_id = wp.part_id
            LEFT JOIN work_orders wo ON wp.order_id = wo.order_id
            WHERE we.step_id = @sid
            ORDER BY we.execution_id DESC;
        ";
        cmd.Parameters.AddWithValue("@sid", stepId);
        using var reader = cmd.ExecuteReader();
        var list = new List<WorkExecution>();
        while (reader.Read())
        {
            list.Add(new WorkExecution
            {
                ExecutionId = reader.GetInt32(0),
                StepId = reader.GetInt32(1),
                AssignmentId = reader.IsDBNull(2) ? null : reader.GetInt32(2),
                MachineId = reader.GetString(3),
                WorkerUsername = reader.GetString(4),
                StartTime = reader.GetString(5),
                EndTime = reader.IsDBNull(6) ? null : reader.GetString(6),
                MachinedQty = reader.GetInt32(7),
                Status = reader.GetString(8),
                Notes = reader.IsDBNull(9) ? null : reader.GetString(9),
                OrderNumber = reader.GetString(10),
                PartName = reader.GetString(11)
            });
        }
        return list;
    }

    #endregion

    #region Inspection (공정별 측정 게이트웨이 - 절대 필수)
    public int RecordInspection(InspectionRecord record)
    {
        using var conn = CreateConnection();
        using var tx = conn.BeginTransaction();

        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            INSERT INTO inspection_records 
            (step_id, part_id, lot_id, inspector_username, inspected_at, inspection_type, inspected_qty, passed_qty, failed_qty, rework_qty, dimension_data_json, result_verdict, attachment_path, notes)
            VALUES (@sid, @pid, @lid, @insp, @now, @itype, @iqty, @pqty, @fqty, @rqty, @dim, @verdict, @att, @notes);
            SELECT last_insert_rowid();
        ";
        cmd.Parameters.AddWithValue("@sid", record.StepId);
        cmd.Parameters.AddWithValue("@pid", record.PartId);
        cmd.Parameters.AddWithValue("@lid", (object?)record.LotId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@insp", record.InspectorUsername);
        cmd.Parameters.AddWithValue("@now", now);
        cmd.Parameters.AddWithValue("@itype", record.InspectionType);
        cmd.Parameters.AddWithValue("@iqty", record.InspectedQty);
        cmd.Parameters.AddWithValue("@pqty", record.PassedQty);
        cmd.Parameters.AddWithValue("@fqty", record.FailedQty);
        cmd.Parameters.AddWithValue("@rqty", record.ReworkQty);
        cmd.Parameters.AddWithValue("@dim", (object?)record.DimensionDataJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@verdict", record.ResultVerdict);
        cmd.Parameters.AddWithValue("@att", (object?)record.AttachmentPath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@notes", (object?)record.Notes ?? DBNull.Value);

        var inspId = Convert.ToInt32(cmd.ExecuteScalar());

        // Update routing_steps based on verdict
        using var stepCmd = conn.CreateCommand();
        stepCmd.Transaction = tx;
        if (record.ResultVerdict == "합격")
        {
            stepCmd.CommandText = @"
                UPDATE routing_steps SET 
                    inspected_qty = inspected_qty + @iqty,
                    passed_qty = passed_qty + @pqty,
                    failed_qty = failed_qty + @fqty,
                    rework_qty = rework_qty + @rqty,
                    status = '합격완료'
                WHERE step_id = @sid;
            ";
        }
        else if (record.ResultVerdict == "재가공")
        {
            stepCmd.CommandText = @"
                UPDATE routing_steps SET 
                    inspected_qty = inspected_qty + @iqty,
                    failed_qty = failed_qty + @fqty,
                    rework_qty = rework_qty + @rqty,
                    status = '재가공'
                WHERE step_id = @sid;
            ";
        }
        else
        {
            stepCmd.CommandText = @"
                UPDATE routing_steps SET 
                    inspected_qty = inspected_qty + @iqty,
                    failed_qty = failed_qty + @fqty,
                    status = '불합격'
                WHERE step_id = @sid;
            ";
        }
        stepCmd.Parameters.AddWithValue("@iqty", record.InspectedQty);
        stepCmd.Parameters.AddWithValue("@pqty", record.PassedQty);
        stepCmd.Parameters.AddWithValue("@fqty", record.FailedQty);
        stepCmd.Parameters.AddWithValue("@rqty", record.ReworkQty);
        stepCmd.Parameters.AddWithValue("@sid", record.StepId);
        stepCmd.ExecuteNonQuery();

        // Advance Part step if Passed and next step exists
        if (record.ResultVerdict == "합격")
        {
            using var partCheckCmd = conn.CreateCommand();
            partCheckCmd.Transaction = tx;
            partCheckCmd.CommandText = @"
                SELECT r.step_sequence, p.target_qty 
                FROM routing_steps r 
                JOIN wo_parts p ON r.part_id = p.part_id
                WHERE r.step_id = @sid;
            ";
            partCheckCmd.Parameters.AddWithValue("@sid", record.StepId);
            using var rdr = partCheckCmd.ExecuteReader();
            if (rdr.Read())
            {
                var curSeq = rdr.GetInt32(0);
                rdr.Close();

                // Check if next step exists
                using var nextStepCmd = conn.CreateCommand();
                nextStepCmd.Transaction = tx;
                nextStepCmd.CommandText = "SELECT COUNT(1) FROM routing_steps WHERE part_id = @pid AND step_sequence > @seq;";
                nextStepCmd.Parameters.AddWithValue("@pid", record.PartId);
                nextStepCmd.Parameters.AddWithValue("@seq", curSeq);
                var hasNext = Convert.ToInt32(nextStepCmd.ExecuteScalar()) > 0;

                using var partAdvCmd = conn.CreateCommand();
                partAdvCmd.Transaction = tx;
                if (hasNext)
                {
                    partAdvCmd.CommandText = @"
                        UPDATE wo_parts SET 
                            current_step_sequence = current_step_sequence + 1,
                            status = '대기'
                        WHERE part_id = @pid;
                    ";
                }
                else
                {
                    // All steps completed!
                    partAdvCmd.CommandText = @"
                        UPDATE wo_parts SET 
                            completed_qty = completed_qty + @pqty,
                            status = '완료'
                        WHERE part_id = @pid;
                    ";
                    partAdvCmd.Parameters.AddWithValue("@pqty", record.PassedQty);
                }
                partAdvCmd.Parameters.AddWithValue("@pid", record.PartId);
                partAdvCmd.ExecuteNonQuery();
            }
        }

        // Recalculate order overall progress rate
        RecalculateOrderProgress(conn, tx, record.PartId);

        tx.Commit();
        return inspId;
    }

    private void RecalculateOrderProgress(SqliteConnection conn, SqliteTransaction tx, int partId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            SELECT order_id FROM wo_parts WHERE part_id = @pid;
        ";
        cmd.Parameters.AddWithValue("@pid", partId);
        var orderIdObj = cmd.ExecuteScalar();
        if (orderIdObj == null) return;
        var orderId = Convert.ToInt32(orderIdObj);

        using var progCmd = conn.CreateCommand();
        progCmd.Transaction = tx;
        progCmd.CommandText = @"
            SELECT SUM(completed_qty), SUM(target_qty) FROM wo_parts WHERE order_id = @oid;
        ";
        progCmd.Parameters.AddWithValue("@oid", orderId);
        using var reader = progCmd.ExecuteReader();
        if (reader.Read())
        {
            var comp = reader.IsDBNull(0) ? 0 : reader.GetInt32(0);
            var tgt = reader.IsDBNull(1) ? 1 : reader.GetInt32(1);
            var rate = tgt > 0 ? (int)((double)comp / tgt * 100) : 0;
            reader.Close();

            using var updateOrder = conn.CreateCommand();
            updateOrder.Transaction = tx;
            updateOrder.CommandText = @"
                UPDATE work_orders SET 
                    progress_rate = @pr,
                    status = CASE WHEN @pr >= 100 THEN '완료' ELSE status END,
                    updated_at = @now
                WHERE order_id = @oid;
            ";
            updateOrder.Parameters.AddWithValue("@pr", rate);
            updateOrder.Parameters.AddWithValue("@now", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            updateOrder.Parameters.AddWithValue("@oid", orderId);
            updateOrder.ExecuteNonQuery();
        }
    }

    public List<InspectionRecord> GetInspectionsByPartId(int partId)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT inspection_id, step_id, part_id, lot_id, inspector_username, inspected_at, inspection_type,
                   inspected_qty, passed_qty, failed_qty, rework_qty, dimension_data_json, result_verdict, attachment_path, notes
            FROM inspection_records WHERE part_id = @pid ORDER BY inspection_id DESC;
        ";
        cmd.Parameters.AddWithValue("@pid", partId);
        using var reader = cmd.ExecuteReader();
        var list = new List<InspectionRecord>();
        while (reader.Read())
        {
            list.Add(new InspectionRecord
            {
                InspectionId = reader.GetInt32(0),
                StepId = reader.GetInt32(1),
                PartId = reader.GetInt32(2),
                LotId = reader.IsDBNull(3) ? null : reader.GetString(3),
                InspectorUsername = reader.GetString(4),
                InspectedAt = reader.GetString(5),
                InspectionType = reader.GetString(6),
                InspectedQty = reader.GetInt32(7),
                PassedQty = reader.GetInt32(8),
                FailedQty = reader.GetInt32(9),
                ReworkQty = reader.GetInt32(10),
                DimensionDataJson = reader.IsDBNull(11) ? null : reader.GetString(11),
                ResultVerdict = reader.GetString(12),
                AttachmentPath = reader.IsDBNull(13) ? null : reader.GetString(13),
                Notes = reader.IsDBNull(14) ? null : reader.GetString(14)
            });
        }
        return list;
    }

    #endregion

    #region Permanent Event History (핵심 기능: 전주기 추적 및 과거 이력 선제 경고)
    public int CreatePermanentEvent(PermanentEvent ev)
    {
        using var conn = CreateConnection();
        using var tx = conn.BeginTransaction();

        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            INSERT INTO permanent_events 
            (part_code, part_id, order_id, step_id, machine_id, event_category, event_type, description, cause, action_taken, before_state, after_state, related_drawing_rev, related_nc_version, status, author_username, created_at)
            VALUES (@pc, @pid, @oid, @sid, @mid, @cat, @type, @desc, @cause, @action, @bstate, @astate, @drev, @ncver, @st, @author, @now);
            SELECT last_insert_rowid();
        ";
        cmd.Parameters.AddWithValue("@pc", ev.PartCode);
        cmd.Parameters.AddWithValue("@pid", (object?)ev.PartId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@oid", (object?)ev.OrderId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@sid", (object?)ev.StepId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@mid", (object?)ev.MachineId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@cat", ev.EventCategory);
        cmd.Parameters.AddWithValue("@type", ev.EventType);
        cmd.Parameters.AddWithValue("@desc", ev.Description);
        cmd.Parameters.AddWithValue("@cause", (object?)ev.Cause ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@action", (object?)ev.ActionTaken ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@bstate", (object?)ev.BeforeState ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@astate", (object?)ev.AfterState ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@drev", (object?)ev.RelatedDrawingRev ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ncver", (object?)ev.RelatedNcVersion ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@st", ev.Status);
        cmd.Parameters.AddWithValue("@author", ev.AuthorUsername);
        cmd.Parameters.AddWithValue("@now", now);

        var eventId = Convert.ToInt32(cmd.ExecuteScalar());

        // Attachments
        foreach (var att in ev.Attachments)
        {
            using var attCmd = conn.CreateCommand();
            attCmd.Transaction = tx;
            attCmd.CommandText = @"
                INSERT INTO event_attachments (event_id, file_type, original_filename, stored_filename, file_size, annotations_json, uploaded_at)
                VALUES (@eid, @ft, @orig, @stored, @size, @ann, @now);
            ";
            attCmd.Parameters.AddWithValue("@eid", eventId);
            attCmd.Parameters.AddWithValue("@ft", att.FileType);
            attCmd.Parameters.AddWithValue("@orig", att.OriginalFilename);
            attCmd.Parameters.AddWithValue("@stored", att.StoredFilename);
            attCmd.Parameters.AddWithValue("@size", att.FileSize);
            attCmd.Parameters.AddWithValue("@ann", (object?)att.AnnotationsJson ?? DBNull.Value);
            attCmd.Parameters.AddWithValue("@now", now);
            attCmd.ExecuteNonQuery();
        }

        tx.Commit();
        return eventId;
    }

    public bool UpdatePermanentEvent(int eventId, PermanentEvent ev)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE permanent_events
            SET part_code = @pc,
                machine_id = @mid,
                event_category = @cat,
                event_type = @type,
                description = @desc,
                cause = @cause,
                action_taken = @action,
                status = @st
            WHERE event_id = @eid;
        ";
        cmd.Parameters.AddWithValue("@pc", ev.PartCode);
        cmd.Parameters.AddWithValue("@mid", (object?)ev.MachineId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@cat", ev.EventCategory);
        cmd.Parameters.AddWithValue("@type", ev.EventType);
        cmd.Parameters.AddWithValue("@desc", ev.Description);
        cmd.Parameters.AddWithValue("@cause", (object?)ev.Cause ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@action", (object?)ev.ActionTaken ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@st", string.IsNullOrWhiteSpace(ev.Status) ? "진행" : ev.Status);
        cmd.Parameters.AddWithValue("@eid", eventId);
        return cmd.ExecuteNonQuery() > 0;
    }

    public List<PermanentEvent> GetEventsByPartCode(string partCode)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT event_id, part_code, part_id, order_id, step_id, machine_id, event_category, event_type, 
                   description, cause, action_taken, before_state, after_state, related_drawing_rev, 
                   related_nc_version, status, author_username, created_at, resolved_at
            FROM permanent_events
            WHERE part_code = @pc
            ORDER BY event_id DESC;
        ";
        cmd.Parameters.AddWithValue("@pc", partCode);

        using var reader = cmd.ExecuteReader();
        var list = new List<PermanentEvent>();
        while (reader.Read())
        {
            list.Add(new PermanentEvent
            {
                EventId = reader.GetInt32(0),
                PartCode = reader.GetString(1),
                PartId = reader.IsDBNull(2) ? null : reader.GetInt32(2),
                OrderId = reader.IsDBNull(3) ? null : reader.GetInt32(3),
                StepId = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                MachineId = reader.IsDBNull(5) ? null : reader.GetString(5),
                EventCategory = reader.GetString(6),
                EventType = reader.GetString(7),
                Description = reader.GetString(8),
                Cause = reader.IsDBNull(9) ? null : reader.GetString(9),
                ActionTaken = reader.IsDBNull(10) ? null : reader.GetString(10),
                BeforeState = reader.IsDBNull(11) ? null : reader.GetString(11),
                AfterState = reader.IsDBNull(12) ? null : reader.GetString(12),
                RelatedDrawingRev = reader.IsDBNull(13) ? null : reader.GetString(13),
                RelatedNcVersion = reader.IsDBNull(14) ? null : reader.GetString(14),
                Status = reader.GetString(15),
                AuthorUsername = reader.GetString(16),
                CreatedAt = reader.GetString(17),
                ResolvedAt = reader.IsDBNull(18) ? null : reader.GetString(18)
            });
        }

        foreach (var ev in list)
        {
            ev.Attachments = GetAttachmentsByEventId(ev.EventId);
        }
        return list;
    }

    public List<PermanentEvent> GetAllEvents(string? categoryFilter = null)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        if (!string.IsNullOrEmpty(categoryFilter))
        {
            cmd.CommandText = "SELECT event_id, part_code, part_id, order_id, step_id, machine_id, event_category, event_type, description, cause, action_taken, before_state, after_state, related_drawing_rev, related_nc_version, status, author_username, created_at, resolved_at FROM permanent_events WHERE event_category = @cat ORDER BY event_id DESC;";
            cmd.Parameters.AddWithValue("@cat", categoryFilter);
        }
        else
        {
            cmd.CommandText = "SELECT event_id, part_code, part_id, order_id, step_id, machine_id, event_category, event_type, description, cause, action_taken, before_state, after_state, related_drawing_rev, related_nc_version, status, author_username, created_at, resolved_at FROM permanent_events ORDER BY event_id DESC;";
        }

        using var reader = cmd.ExecuteReader();
        var list = new List<PermanentEvent>();
        while (reader.Read())
        {
            list.Add(new PermanentEvent
            {
                EventId = reader.GetInt32(0),
                PartCode = reader.GetString(1),
                PartId = reader.IsDBNull(2) ? null : reader.GetInt32(2),
                OrderId = reader.IsDBNull(3) ? null : reader.GetInt32(3),
                StepId = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                MachineId = reader.IsDBNull(5) ? null : reader.GetString(5),
                EventCategory = reader.GetString(6),
                EventType = reader.GetString(7),
                Description = reader.GetString(8),
                Cause = reader.IsDBNull(9) ? null : reader.GetString(9),
                ActionTaken = reader.IsDBNull(10) ? null : reader.GetString(10),
                BeforeState = reader.IsDBNull(11) ? null : reader.GetString(11),
                AfterState = reader.IsDBNull(12) ? null : reader.GetString(12),
                RelatedDrawingRev = reader.IsDBNull(13) ? null : reader.GetString(13),
                RelatedNcVersion = reader.IsDBNull(14) ? null : reader.GetString(14),
                Status = reader.GetString(15),
                AuthorUsername = reader.GetString(16),
                CreatedAt = reader.GetString(17),
                ResolvedAt = reader.IsDBNull(18) ? null : reader.GetString(18)
            });
        }
        foreach (var ev in list)
        {
            ev.Attachments = GetAttachmentsByEventId(ev.EventId);
        }
        return list;
    }

    public List<EventAttachment> GetAttachmentsByEventId(int eventId)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT attachment_id, event_id, file_type, original_filename, stored_filename, file_size, annotations_json, uploaded_at FROM event_attachments WHERE event_id = @eid;";
        cmd.Parameters.AddWithValue("@eid", eventId);
        using var reader = cmd.ExecuteReader();
        var list = new List<EventAttachment>();
        while (reader.Read())
        {
            list.Add(new EventAttachment
            {
                AttachmentId = reader.GetInt32(0),
                EventId = reader.GetInt32(1),
                FileType = reader.GetString(2),
                OriginalFilename = reader.GetString(3),
                StoredFilename = reader.GetString(4),
                FileSize = reader.GetInt64(5),
                AnnotationsJson = reader.IsDBNull(6) ? null : reader.GetString(6),
                UploadedAt = reader.GetString(7)
            });
        }
        return list;
    }

    #endregion

    #region Handover Logs (인수인계)
    public int CreateHandover(HandoverLog log)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO handover_logs (sender_username, receiver_username, machine_id, order_id, part_id, summary, pending_issues, notes, created_at)
            VALUES (@su, @ru, @mid, @oid, @pid, @sum, @pi, @notes, @now);
            SELECT last_insert_rowid();
        ";
        cmd.Parameters.AddWithValue("@su", log.SenderUsername);
        cmd.Parameters.AddWithValue("@ru", (object?)log.ReceiverUsername ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@mid", log.MachineId);
        cmd.Parameters.AddWithValue("@oid", (object?)log.OrderId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@pid", (object?)log.PartId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@sum", log.Summary);
        cmd.Parameters.AddWithValue("@pi", (object?)log.PendingIssues ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@notes", (object?)log.Notes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@now", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public void ConfirmHandover(int handoverId, string receiverUsername)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE handover_logs SET receiver_username = @ru, confirmed_at = @now WHERE handover_id = @id;
        ";
        cmd.Parameters.AddWithValue("@id", handoverId);
        cmd.Parameters.AddWithValue("@ru", receiverUsername);
        cmd.Parameters.AddWithValue("@now", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.ExecuteNonQuery();
    }

    public List<HandoverLog> GetAllHandovers()
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT handover_id, sender_username, receiver_username, machine_id, order_id, part_id, summary, pending_issues, notes, created_at, confirmed_at FROM handover_logs ORDER BY handover_id DESC;";
        using var reader = cmd.ExecuteReader();
        var list = new List<HandoverLog>();
        while (reader.Read())
        {
            list.Add(new HandoverLog
            {
                HandoverId = reader.GetInt32(0),
                SenderUsername = reader.GetString(1),
                ReceiverUsername = reader.IsDBNull(2) ? null : reader.GetString(2),
                MachineId = reader.GetString(3),
                OrderId = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                PartId = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                Summary = reader.GetString(6),
                PendingIssues = reader.IsDBNull(7) ? null : reader.GetString(7),
                Notes = reader.IsDBNull(8) ? null : reader.GetString(8),
                CreatedAt = reader.GetString(9),
                ConfirmedAt = reader.IsDBNull(10) ? null : reader.GetString(10)
            });
        }
        return list;
    }

    #endregion

    #region Notices (공지 및 공식 서명 확인)
    public List<Notice> GetAllNotices()
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT notice_id, author, title, content, priority, related_machine, created_at, official_acks_json FROM notices ORDER BY notice_id DESC;";
        using var reader = cmd.ExecuteReader();
        var list = new List<Notice>();
        while (reader.Read())
        {
            list.Add(new Notice
            {
                NoticeId = reader.GetInt32(0),
                Author = reader.GetString(1),
                Title = reader.GetString(2),
                Content = reader.GetString(3),
                Priority = reader.GetString(4),
                RelatedMachine = reader.IsDBNull(5) ? null : reader.GetString(5),
                CreatedAt = reader.GetString(6),
                OfficialAcksJson = reader.IsDBNull(7) ? null : reader.GetString(7)
            });
        }
        return list;
    }

    public int CreateNotice(Notice notice)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO notices (author, title, content, priority, related_machine, created_at, official_acks_json)
            VALUES (@auth, @t, @c, @pri, @rm, @now, '[]');
            SELECT last_insert_rowid();
        ";
        cmd.Parameters.AddWithValue("@auth", notice.Author);
        cmd.Parameters.AddWithValue("@t", notice.Title);
        cmd.Parameters.AddWithValue("@c", notice.Content);
        cmd.Parameters.AddWithValue("@pri", notice.Priority);
        cmd.Parameters.AddWithValue("@rm", (object?)notice.RelatedMachine ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@now", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public void AcknowledgeNotice(int noticeId, string username)
    {
        using var conn = CreateConnection();
        using var tx = conn.BeginTransaction();

        using var getCmd = conn.CreateCommand();
        getCmd.Transaction = tx;
        getCmd.CommandText = "SELECT official_acks_json FROM notices WHERE notice_id = @id;";
        getCmd.Parameters.AddWithValue("@id", noticeId);
        var rawJson = getCmd.ExecuteScalar()?.ToString() ?? "[]";

        var list = new List<Dictionary<string, string>>();
        try
        {
            list = JsonSerializer.Deserialize<List<Dictionary<string, string>>>(rawJson) ?? new();
        }
        catch { }

        if (!list.Any(x => x.GetValueOrDefault("username") == username))
        {
            list.Add(new Dictionary<string, string>
            {
                ["username"] = username,
                ["confirmed_at"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            });
        }

        var jsonOpts = new JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        using var updateCmd = conn.CreateCommand();
        updateCmd.Transaction = tx;
        updateCmd.CommandText = "UPDATE notices SET official_acks_json = @j WHERE notice_id = @id;";
        updateCmd.Parameters.AddWithValue("@j", JsonSerializer.Serialize(list, jsonOpts));
        updateCmd.Parameters.AddWithValue("@id", noticeId);
        updateCmd.ExecuteNonQuery();

        tx.Commit();
    }
    #endregion

    #region Chat & Topics (Zulip 스타일 토픽 기반 소통)
    public List<ChatMessage> GetMessagesByTopic(string channelType, string topicKey)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, channel_type, topic_key, author, content, attachment_info, created_at, promoted_event_id
            FROM chat_messages 
            WHERE channel_type = @ct AND topic_key = @tk
            ORDER BY id ASC;
        ";
        cmd.Parameters.AddWithValue("@ct", channelType);
        cmd.Parameters.AddWithValue("@tk", topicKey);

        using var reader = cmd.ExecuteReader();
        var list = new List<ChatMessage>();
        while (reader.Read())
        {
            list.Add(new ChatMessage
            {
                Id = reader.GetInt32(0),
                ChannelType = reader.GetString(1),
                TopicKey = reader.GetString(2),
                Author = reader.GetString(3),
                Content = reader.GetString(4),
                AttachmentInfo = reader.IsDBNull(5) ? null : reader.GetString(5),
                CreatedAt = reader.GetString(6),
                PromotedEventId = reader.IsDBNull(7) ? null : reader.GetInt32(7)
            });
        }
        return list;
    }

    public int AddChatMessage(ChatMessage msg)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO chat_messages (channel_type, topic_key, author, content, attachment_info, created_at)
            VALUES (@ct, @tk, @auth, @cnt, @att, @now);
            SELECT last_insert_rowid();
        ";
        cmd.Parameters.AddWithValue("@ct", msg.ChannelType);
        cmd.Parameters.AddWithValue("@tk", msg.TopicKey);
        cmd.Parameters.AddWithValue("@auth", msg.Author);
        cmd.Parameters.AddWithValue("@cnt", msg.Content);
        cmd.Parameters.AddWithValue("@att", (object?)msg.AttachmentInfo ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@now", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public void LinkChatToPermanentEvent(int messageId, int eventId)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE chat_messages SET promoted_event_id = @eid WHERE id = @mid;";
        cmd.Parameters.AddWithValue("@eid", eventId);
        cmd.Parameters.AddWithValue("@mid", messageId);
        cmd.ExecuteNonQuery();
    }
    #endregion

    // =========================================================================
    // 작업지시서 완료/보관(Backup) 및 상태 업데이트
    // =========================================================================
    public bool UpdateWorkOrderStatus(int orderId, string status, string? backupPath = null)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        if (status == "가공완료" || status == "완료")
        {
            cmd.CommandText = @"
                UPDATE work_orders
                SET status = @st,
                    progress_rate = 100,
                    backup_path = COALESCE(@bp, backup_path),
                    completed_at = @now,
                    updated_at = @now
                WHERE order_id = @oid;

                UPDATE wo_parts
                SET status = '가공완료'
                WHERE order_id = @oid;
            ";
            cmd.Parameters.AddWithValue("@st", status);
            cmd.Parameters.AddWithValue("@bp", (object?)backupPath ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@now", now);
            cmd.Parameters.AddWithValue("@oid", orderId);
        }
        else
        {
            cmd.CommandText = @"
                UPDATE work_orders
                SET status = @st,
                    backup_path = COALESCE(@bp, backup_path),
                    updated_at = @now
                WHERE order_id = @oid;
            ";
            cmd.Parameters.AddWithValue("@st", status);
            cmd.Parameters.AddWithValue("@bp", (object?)backupPath ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@now", now);
            cmd.Parameters.AddWithValue("@oid", orderId);
        }
        return cmd.ExecuteNonQuery() > 0;
    }

    public WorkOrder? GetWorkOrderByNumber(string orderNumber)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT order_id FROM work_orders WHERE order_number = @on LIMIT 1;";
        cmd.Parameters.AddWithValue("@on", orderNumber);
        var res = cmd.ExecuteScalar();
        if (res == null || res == DBNull.Value) return null;
        return GetWorkOrderById(Convert.ToInt32(res));
    }

    // -------------------------------------------------------------------------
    // Post-Process Tracking & Reporting Methods
    // -------------------------------------------------------------------------
    public int AddPostProcessReport(PostProcessReport report)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO post_process_reports 
                (part_id, order_id, order_number, part_name, machine_id, worker, completed_qty, selected_processes, notes, status, created_at)
            VALUES 
                (@pid, @oid, @on, @pn, @mid, @wk, @qty, @proc, @notes, @st, @cat);
            SELECT last_insert_rowid();
        ";
        cmd.Parameters.AddWithValue("@pid", report.PartId);
        cmd.Parameters.AddWithValue("@oid", report.OrderId);
        cmd.Parameters.AddWithValue("@on", report.OrderNumber);
        cmd.Parameters.AddWithValue("@pn", report.PartName);
        cmd.Parameters.AddWithValue("@mid", report.MachineId);
        cmd.Parameters.AddWithValue("@wk", report.Worker);
        cmd.Parameters.AddWithValue("@qty", report.CompletedQty);
        cmd.Parameters.AddWithValue("@proc", report.SelectedProcesses);
        cmd.Parameters.AddWithValue("@notes", (object?)report.Notes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@st", string.IsNullOrEmpty(report.Status) ? "후공정대기" : report.Status);
        cmd.Parameters.AddWithValue("@cat", string.IsNullOrEmpty(report.CreatedAt) ? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") : report.CreatedAt);

        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public List<PostProcessReport> GetPostProcessReports(string? status = null)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        var sql = @"
            SELECT id, part_id, order_id, order_number, part_name, machine_id, worker, 
                   completed_qty, selected_processes, notes, status, created_at, completed_at
            FROM post_process_reports
        ";
        if (!string.IsNullOrEmpty(status))
        {
            sql += " WHERE status = @st";
            cmd.Parameters.AddWithValue("@st", status);
        }
        sql += " ORDER BY id DESC;";
        cmd.CommandText = sql;

        using var reader = cmd.ExecuteReader();
        var list = new List<PostProcessReport>();
        while (reader.Read())
        {
            list.Add(new PostProcessReport
            {
                Id = reader.GetInt32(0),
                PartId = reader.GetInt32(1),
                OrderId = reader.GetInt32(2),
                OrderNumber = reader.GetString(3),
                PartName = reader.GetString(4),
                MachineId = reader.GetString(5),
                Worker = reader.GetString(6),
                CompletedQty = reader.GetInt32(7),
                SelectedProcesses = reader.GetString(8),
                Notes = reader.IsDBNull(9) ? null : reader.GetString(9),
                Status = reader.GetString(10),
                CreatedAt = reader.GetString(11),
                CompletedAt = reader.IsDBNull(12) ? null : reader.GetString(12)
            });
        }
        return list;
    }

    public bool UpdatePostProcessReportStatus(int id, string status, string? completedAt = null)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE post_process_reports 
            SET status = @st, completed_at = @cat
            WHERE id = @id;
        ";
        cmd.Parameters.AddWithValue("@st", status);
        cmd.Parameters.AddWithValue("@cat", (object?)completedAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id", id);
        return cmd.ExecuteNonQuery() > 0;
    }

    public bool UpdatePartPostProcess(int partId, int completedQty, string processes, string notes)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE wo_parts 
            SET completed_qty = @cqty,
                post_process = @proc,
                post_process_status = '후공정대기',
                post_process_notes = @notes,
                status = '가공완료'
            WHERE part_id = @pid;
        ";
        cmd.Parameters.AddWithValue("@cqty", completedQty);
        cmd.Parameters.AddWithValue("@proc", processes);
        cmd.Parameters.AddWithValue("@notes", notes);
        cmd.Parameters.AddWithValue("@pid", partId);
        return cmd.ExecuteNonQuery() > 0;
    }

    private void EnsureColumn(SqliteConnection conn, string table, string column, string colType)
    {
        try
        {
            using var pragmaCmd = conn.CreateCommand();
            pragmaCmd.CommandText = $"PRAGMA table_info({table});";
            using var reader = pragmaCmd.ExecuteReader();
            var exists = false;
            while (reader.Read())
            {
                var colName = reader.GetString(1);
                if (colName.Equals(column, StringComparison.OrdinalIgnoreCase))
                {
                    exists = true;
                    break;
                }
            }
            if (!exists)
            {
                using var alterCmd = conn.CreateCommand();
                alterCmd.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {colType};";
                alterCmd.ExecuteNonQuery();
                _logger.LogInformation("Successfully migrated column {Table}.{Column}", table, column);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Column migration warning for {Table}.{Column}", table, column);
        }
    }

}