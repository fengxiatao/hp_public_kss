using FaceLocker.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FaceLocker.Services
{
    /// <summary>
    /// 数据库启动初始化器
    /// 负责数据库的创建、迁移和种子数据初始化
    /// </summary>
    public class DatabaseStartupInitializer : IDatabaseStartupInitializer
    {
        private readonly FaceLockerDbContext _db;
        private readonly ILogger<DatabaseStartupInitializer> _logger;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="db">数据库上下文</param>
        /// <param name="logger">日志记录器</param>
        public DatabaseStartupInitializer(FaceLockerDbContext db, ILogger<DatabaseStartupInitializer> logger)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _logger.LogInformation("DatabaseStartupInitializer 已创建");
        }

        /// <summary>
        /// 初始化数据库
        /// 1. 检测并准备数据库表结构
        /// 2. 插入必要的种子数据
        /// </summary>
        /// <returns>初始化任务</returns>
        public async Task InitializeAsync()
        {
            _logger.LogInformation("开始数据库初始化流程...");

            try
            {
                // 1) 先按"存在→迁移；不存在→创建"的策略确保表结构就绪
                _logger.LogInformation("开始检测并准备数据库与表结构...");
                await EnsureSchemaCreatedOrMigratedAsync();
                _logger.LogInformation("数据库与表结构准备完成");

                // 2) 条件种子（只有在表为空时才插入）
                _logger.LogInformation("开始检查并插入种子数据...");
                await SeedIfEmptyAsync();
                _logger.LogInformation("种子数据检查完成");

                _logger.LogInformation("数据库初始化流程完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "数据库初始化失败");
                throw;
            }
        }

        /// <summary>
        /// 种子数据插入（如果表为空）
        /// </summary>
        /// <returns>种子数据插入任务</returns>
        private async Task SeedIfEmptyAsync()
        {
            // 角色种子数据
            if (!await _db.Roles.AnyAsync())
            {
                _logger.LogInformation("未发现角色数据，正在插入默认角色...");
                var roles = RoleDefaults();
                await _db.Roles.AddRangeAsync(roles);
                var count = await _db.SaveChangesAsync();
                _logger.LogInformation("默认角色插入完成，共 {Count} 条，角色名称: {RoleNames}",
                    count, string.Join(", ", roles.Select(r => r.Name)));
            }
            else
            {
                var roleCount = await _db.Roles.CountAsync();
                _logger.LogInformation("角色数据已存在，共 {RoleCount} 条，跳过默认角色插入", roleCount);
            }

            // 用户种子数据
            if (!await _db.Users.AnyAsync())
            {
                _logger.LogInformation("未发现用户数据，正在插入默认用户...");
                var users = UserDefaults();
                await _db.Users.AddRangeAsync(users);
                var count = await _db.SaveChangesAsync();
                _logger.LogInformation("默认用户插入完成，共 {Count} 条，用户名称: {UserNames}",
                    count, string.Join(", ", users.Select(u => u.Name)));
            }
            else
            {
                var userCount = await _db.Users.CountAsync();
                _logger.LogInformation("用户数据已存在，共 {UserCount} 条，跳过默认用户插入", userCount);
            }

            // 储物格种子数据（注释掉的代码，根据需要启用）
            if (!await _db.Lockers.AnyAsync())
            {
                _logger.LogInformation("未发现储物格数据，正在插入默认储物格...");
                var lockers = LockerDefaults();
                await _db.Lockers.AddRangeAsync(lockers);
                var count = await _db.SaveChangesAsync();
                _logger.LogInformation("默认储物格插入完成，共 {Count} 条", count);
            }
            else
            {
                _logger.LogInformation("储物格数据已存在，跳过默认储物格插入");
            }
        }

        /// <summary>
        /// 获取默认角色列表
        /// </summary>
        /// <returns>默认角色列表</returns>
        private static List<Role> RoleDefaults() => Role.CreateDefaultRoles();

        /// <summary>
        /// 获取默认用户列表
        /// </summary>
        /// <returns>默认用户列表</returns>
        private static List<User> UserDefaults() => User.CreateDefaultUsers();

        /// <summary>
        /// 获取默认储物格列表
        /// </summary>
        /// <returns>默认储物格列表</returns>
        private static List<Locker> LockerDefaults() => Locker.CreateDefaultLockers();

        /// <summary>
        /// 先检测数据库与表结构：
        /// - 若存在待迁移 => 执行迁移
        /// - 若数据库不存在或没有任何表 => 创建数据库并按当前模型建表
        /// - 若检测到部分缺表但没有迁移 => 尝试仅创建缺失的表（基于当前模型）
        /// </summary>
        /// <param name="ct">取消令牌</param>
        /// <returns>架构创建或迁移任务</returns>
        private async Task EnsureSchemaCreatedOrMigratedAsync(CancellationToken ct = default)
        {
            var db = _db;
            var logger = _logger;

            logger.LogInformation("开始数据库架构检测...");

            // SQLite/关系型数据库创建器
            var dbCreator = (RelationalDatabaseCreator)db.Database.GetService<IDatabaseCreator>();

            // 1) 若数据库文件都不存在，先创建空库
            if (!await dbCreator.ExistsAsync(ct))
            {
                logger.LogInformation("数据库不存在，正在创建数据库文件...");
                await dbCreator.CreateAsync(ct);
                logger.LogInformation("数据库文件创建完成");
            }
            else
            {
                logger.LogInformation("数据库文件已存在");
            }

            // 2) 优先处理"有待迁移"的情况
            var pendingMigrations = await db.Database.GetPendingMigrationsAsync(ct);
            if (pendingMigrations.Any())
            {
                var migrationList = string.Join(", ", pendingMigrations);
                logger.LogInformation("检测到 {Count} 个待迁移: {Migrations}，开始执行迁移...",
                    pendingMigrations.Count(), migrationList);

                await db.Database.MigrateAsync(ct);
                logger.LogInformation("迁移完成");
                return;
            }
            else
            {
                logger.LogInformation("没有待迁移");
            }

            // 3) 没有待迁移 => 检查是否至少有一张表
            var hasAnyTables = await dbCreator.HasTablesAsync(ct);
            if (!hasAnyTables)
            {
                // 数据库为空库：按当前模型一次性创建全部表
                logger.LogInformation("数据库为空（无任何表），按模型创建全部表结构...");
                await dbCreator.CreateTablesAsync(ct);
                logger.LogInformation("表结构创建完成");
                return;
            }
            else
            {
                logger.LogInformation("数据库已有表结构");
            }

            // 4) 数据库已有表，但可能"部分缺表"
            //    用 EF 模型中的所有表名与 sqlite_master 对比
            var missingTables = await GetMissingTablesAsync(db, ct);
            if (missingTables.Count == 0)
            {
                logger.LogInformation("数据库表结构已满足当前模型要求，无需创建或迁移");
                return;
            }

            // 5) 存在部分缺表但无待迁移：尝试用 CreateTablesAsync() 一次性补齐
            logger.LogWarning("检测到缺失的表：{Tables}。尝试根据当前模型补齐缺表...",
                string.Join(", ", missingTables));

            try
            {
                await dbCreator.CreateTablesAsync(ct);
                logger.LogInformation("缺表已补齐");
            }
            catch (SqliteException ex)
            {
                // SQLite 若遇到已存在的表会报错；这里属于"部分已存在"的正常现象之一
                logger.LogError(ex, "补齐缺表时发生 SQLite 异常，可能是部分表已存在导致冲突。建议尽快改回迁移流程，或在开发期删除库文件后重建");
                throw; // 让上层感知失败
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "补齐缺表失败。建议生成迁移或清理数据库后重建");
                throw;
            }
        }

        /// <summary>
        /// 读取 EF 模型中的所有"表（非视图）"，返回当前数据库中缺失的表名列表
        /// </summary>
        /// <param name="db">数据库上下文</param>
        /// <param name="ct">取消令牌</param>
        /// <returns>缺失的表名列表</returns>
        private static async Task<List<string>> GetMissingTablesAsync(DbContext db, CancellationToken ct = default)
        {
            // 取出模型里的表名
            var modelTableNames = db.Model
                .GetEntityTypes()
                .Select(et => et.GetSchema() is string schema && !string.IsNullOrEmpty(schema)
                                ? $"{schema}.{et.GetTableName()}"
                                : et.GetTableName())
                .Where(n => !string.IsNullOrEmpty(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var missing = new List<string>();
            using var conn = db.Database.GetDbConnection();

            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync(ct);

            foreach (var table in modelTableNames)
            {
                // 支持 schema.table 或 table，两种写法分别判断
                var (schema, name) = SplitSchemaAndName(table);
                var exists = await TableExistsSqliteAsync(conn, schema, name, ct);
                if (!exists)
                    missing.Add(table!);
            }

            return missing;
        }

        /// <summary>
        /// 拆分架构名和表名
        /// </summary>
        /// <param name="full">完整的表名（可能包含架构）</param>
        /// <returns>架构名和表名的元组</returns>
        private static (string? schema, string name) SplitSchemaAndName(string? full)
        {
            if (string.IsNullOrWhiteSpace(full))
                return (null, "");

            var parts = full.Split('.', 2);
            return parts.Length == 2 ? (parts[0], parts[1]) : (null, parts[0]);
        }

        /// <summary>
        /// 针对 SQLite 的表存在性检测
        /// </summary>
        /// <param name="conn">数据库连接</param>
        /// <param name="schema">架构名（SQLite 中通常为空）</param>
        /// <param name="name">表名</param>
        /// <param name="ct">取消令牌</param>
        /// <returns>表是否存在</returns>
        private static async Task<bool> TableExistsSqliteAsync(System.Data.Common.DbConnection conn, string? schema, string name, CancellationToken ct)
        {
            // SQLite 的数据库/Schema 统一，直接查 sqlite_master
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=$name LIMIT 1;";

            var p = cmd.CreateParameter();
            p.ParameterName = "$name";
            p.Value = name;
            cmd.Parameters.Add(p);

            var result = await cmd.ExecuteScalarAsync(ct);
            return result is not null;
        }
    }
}
