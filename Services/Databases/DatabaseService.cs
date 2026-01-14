using FaceLocker.Models;
using FaceLocker.Models.Settings;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace FaceLocker.Services
{
    /// <summary>
    /// 数据库服务实现类
    /// 负责数据库初始化、迁移、种子数据加载等操作
    /// </summary>
    public class DatabaseService : IDatabaseService
    {
        private readonly FaceLockerDbContext _context;
        private readonly ILogger<DatabaseService> _logger;
        private readonly IOptions<AppSettings> _appSettings;
        private readonly string _connectionString;
        private readonly string _dataDirectory;
        private bool _disposed = false;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="context">数据库上下文</param>
        /// <param name="logger">日志服务</param>
        /// <param name="appSettings">应用设置</param>
        public DatabaseService(
            FaceLockerDbContext context,
            ILogger<DatabaseService> logger,
            IOptions<AppSettings> appSettings)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _appSettings = appSettings ?? throw new ArgumentNullException(nameof(appSettings));

            // 从配置中获取数据库设置
            var _connectionString = _appSettings.Value.Database?.ConnectionString ?? "Data Source=./Data/database.db";

            _dataDirectory = _appSettings.Value.Database?.DataDirectory ?? "./Data/";

            _logger.LogInformation("DatabaseService initialized with connection string: {ConnectionString}", _connectionString);
        }

        #region 初始化数据库

        /// <summary>
        /// 初始化数据库（创建数据库、应用迁移、加载种子数据）
        /// </summary>
        /// <returns>初始化是否成功</returns>
        public async Task<bool> InitializeDatabaseAsync()
        {
            _logger.LogInformation("Starting database initialization process");

            // 首先记录详细的数据库连接信息
            LogDatabaseConnectionInfo();

            // 验证数据库路径和写入权限
            var (isValid, errorMessage) = ValidateDatabasePath();
            if (!isValid)
            {
                _logger.LogError("Database path validation failed: {ErrorMessage}", errorMessage);
                return false;
            }

            try
            {
                _logger.LogInformation("Beginning database initialization...");

                // 1. 确保数据库目录存在
                EnsureDatabaseDirectory();

                // 2. 执行数据库迁移或创建
                var initializationResult = await ExecuteDatabaseInitializationAsync(_context);
                if (!initializationResult)
                {
                    _logger.LogError("Database initialization failed during migration/creation phase");
                    return false;
                }

                // 3. 执行SQLite优化配置
                await ExecuteSqliteOptimizations(_context);

                // 4. 加载种子数据（如果数据库为空）
                await SeedDefaultDataIfEmptyAsync(_context);

                // 5. 验证数据库完整性
                await VerifyDatabaseIntegrity(_context);

                // 6. 记录初始化完成后的数据库状态
                LogDatabaseFinalState();

                _logger.LogInformation("Database initialization completed successfully");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Database initialization failed with exception: {ErrorMessage}", ex.Message);
                return false;
            }
        }
        #endregion

        #region 记录数据库初始化完成后的最终状态
        /// <summary>
        /// 记录数据库初始化完成后的最终状态
        /// </summary>
        private void LogDatabaseFinalState()
        {
            try
            {
                _logger.LogInformation("=== Database initialization final state ===");

                string fullPath = ExtractDatabasePathFromConnectionString();

                if (File.Exists(fullPath))
                {
                    var fileInfo = new FileInfo(fullPath);
                    _logger.LogInformation("Final file size: {FileSize} bytes", fileInfo.Length);
                    _logger.LogInformation("Final modification time: {LastWriteTime}", fileInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"));

                    // 格式化文件大小
                    string formattedSize = FormatFileSize(fileInfo.Length);
                    _logger.LogInformation("Formatted file size: {FormattedSize}", formattedSize);
                }

                _logger.LogInformation("=== Database state logging completed ===");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Exception occurred while logging database final state: {ErrorMessage}", ex.Message);
            }
        }
        #endregion

        #region 格式化文件大小显示
        /// <summary>
        /// 格式化文件大小显示
        /// </summary>
        private string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            int order = 0;
            double len = bytes;

            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }

            return $"{len:0.##} {sizes[order]}";
        }
        #endregion

        #region 记录数据库连接详细信息
        /// <summary>
        /// 记录数据库连接详细信息
        /// </summary>
        public void LogDatabaseConnectionInfo()
        {
            try
            {
                _logger.LogInformation("=== Database connection information ===");

                // 1. 记录连接字符串
                _logger.LogInformation("Connection string: {ConnectionString}", _connectionString);

                // 2. 提取并记录完整路径
                string fullPath = ExtractDatabasePathFromConnectionString();
                _logger.LogInformation("Full database path: {FullPath}", fullPath);

                // 3. 记录文件状态信息
                if (File.Exists(fullPath))
                {
                    var fileInfo = new FileInfo(fullPath);
                    _logger.LogInformation("Database file exists: Yes");
                    _logger.LogInformation("File size: {FileSize} bytes", fileInfo.Length);
                    _logger.LogInformation("Creation time: {CreationTime}", fileInfo.CreationTime.ToString("yyyy-MM-dd HH:mm:ss"));
                    _logger.LogInformation("Last modification time: {LastWriteTime}", fileInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"));
                    _logger.LogInformation("Last access time: {LastAccessTime}", fileInfo.LastAccessTime.ToString("yyyy-MM-dd HH:mm:ss"));

                    // 计算文件年龄
                    var fileAge = DateTime.Now - fileInfo.LastWriteTime;
                    _logger.LogInformation("File age: {Days} days {Hours} hours {Minutes} minutes", fileAge.Days, fileAge.Hours, fileAge.Minutes);
                }
                else
                {
                    _logger.LogInformation("Database file exists: No");
                    _logger.LogInformation("New database file will be created");
                }

                // 4. 记录目录权限信息
                string directory = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    _logger.LogInformation("Database directory: {Directory}", directory);
                    _logger.LogInformation("Directory exists: {DirectoryExists}", Directory.Exists(directory));

                    if (Directory.Exists(directory))
                    {
                        try
                        {
                            // 测试目录写入权限
                            string testFile = Path.Combine(directory, $"write_test_{Guid.NewGuid():N}.tmp");
                            File.WriteAllText(testFile, "test");
                            File.Delete(testFile);
                            _logger.LogInformation("Directory write permission: Normal");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Directory write permission: Exception - {ErrorMessage}", ex.Message);
                        }
                    }
                }

                // 5. 记录当前工作目录信息
                string currentDirectory = Directory.GetCurrentDirectory();
                _logger.LogInformation("Current working directory: {CurrentDirectory}", currentDirectory);

                // 6. 记录应用程序基目录
                string appBaseDirectory = AppDomain.CurrentDomain.BaseDirectory;
                _logger.LogInformation("Application base directory: {AppBaseDirectory}", appBaseDirectory);

                _logger.LogInformation("=== Database connection information completed ===");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception occurred while logging database connection information: {ErrorMessage}", ex.Message);
            }
        }
        #endregion

        #region 执行数据库初始化（迁移或创建）
        /// <summary>
        /// 执行数据库初始化（迁移或创建）
        /// </summary>
        private async Task<bool> ExecuteDatabaseInitializationAsync(FaceLockerDbContext context)
        {
            try
            {
                // 检查是否有待应用的迁移
                var pendingMigrations = await context.Database.GetPendingMigrationsAsync();
                var appliedMigrations = await context.Database.GetAppliedMigrationsAsync();

                _logger.LogInformation("Database migration status: {AppliedCount} migrations applied, {PendingCount} migrations pending",
                    appliedMigrations.Count(), pendingMigrations.Count());

                if (pendingMigrations.Any())
                {
                    // 有迁移待应用，执行迁移
                    await ApplyMigrationsWithFallbackAsync(context, pendingMigrations);
                }
                else
                {
                    // 没有待迁移，确保数据库创建
                    await EnsureDatabaseCreatedAsync(context);
                }

                // 应用SQLite优化配置
                await ExecuteSqliteOptimizations(context);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Database initialization execution failed: {ErrorMessage}", ex.Message);
                return false;
            }
        }
        #endregion

        #region 应用数据库迁移（带回退机制）
        /// <summary>
        /// 应用数据库迁移（带回退机制）
        /// </summary>
        private async Task<bool> ApplyMigrationsWithFallbackAsync(FaceLockerDbContext context, IEnumerable<string> pendingMigrations)
        {
            try
            {
                _logger.LogInformation("Detected {PendingMigrationCount} pending migrations, applying migrations...", pendingMigrations.Count());

                await context.Database.MigrateAsync();

                _logger.LogInformation("Successfully applied {PendingMigrationCount} database migrations", pendingMigrations.Count());
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Database migration failed: {ErrorMessage}, attempting fallback solution...", ex.Message);

                // 迁移失败时回退到重新创建数据库
                return await ExecuteDatabaseFallbackAsync(context);
            }
        }
        #endregion

        #region 确保数据库创建（无迁移情况）
        /// <summary>
        /// 确保数据库创建（无迁移情况）
        /// </summary>
        private async Task<bool> EnsureDatabaseCreatedAsync(FaceLockerDbContext context)
        {
            try
            {
                var created = await context.Database.EnsureCreatedAsync();
                if (created)
                {
                    _logger.LogInformation("Database table structure created successfully");
                }
                else
                {
                    _logger.LogInformation("Database table structure already exists");
                }
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Database creation failed: {ErrorMessage}", ex.Message);
                return false;
            }
        }
        #endregion

        #region 数据库迁移失败时的回退方案
        /// <summary>
        /// 数据库迁移失败时的回退方案
        /// </summary>
        private async Task<bool> ExecuteDatabaseFallbackAsync(FaceLockerDbContext context)
        {
            try
            {
                var dbPath = ExtractDatabasePathFromConnectionString();

                // 备份原有数据库文件
                if (File.Exists(dbPath))
                {
                    var backupPath = $"{dbPath}.backup.{DateTime.Now:yyyyMMddHHmmss}";
                    File.Copy(dbPath, backupPath);
                    _logger.LogWarning("Created database backup: {BackupPath}", backupPath);
                }

                // 删除有问题的数据库文件
                if (File.Exists(dbPath))
                {
                    File.Delete(dbPath);
                }

                // 重新创建数据库
                await context.Database.EnsureCreatedAsync();

                _logger.LogInformation("Database fallback solution executed successfully");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Database fallback solution execution failed: {ErrorMessage}", ex.Message);
                return false;
            }
        }

        #endregion

        #region 种子数据管理

        /// <summary>
        /// 如果数据库为空，加载种子数据
        /// </summary>
        private async Task SeedDefaultDataIfEmptyAsync(FaceLockerDbContext context)
        {
            try
            {
                _logger.LogInformation("Checking seed data...");

                bool hasData = false;

                // 检查是否已有角色数据
                if (!await context.Roles.AnyAsync())
                {
                    _logger.LogInformation("No role data found, creating default roles...");
                    await SeedDefaultRolesAsync(context);
                    hasData = true;
                }
                else
                {
                    _logger.LogInformation("Role data already exists, skipping creation");
                }

                // 检查是否已有用户数据
                if (!await context.Users.AnyAsync())
                {
                    _logger.LogInformation("No user data found, creating default users...");
                    await SeedDefaultUsersAsync(context);
                    hasData = true;
                }
                else
                {
                    _logger.LogInformation("User data already exists, skipping creation");
                }

                // 检查是否已有柜子数据
                if (!await context.Lockers.AnyAsync())
                {
                    _logger.LogInformation("No locker data found, creating default lockers...");
                    await SeedDefaultLockersAsync(context);
                    hasData = true;
                }
                else
                {
                    _logger.LogInformation("Locker data already exists, skipping creation");
                }

                if (hasData)
                {
                    _logger.LogInformation("Seed data was added to the database");
                }
                else
                {
                    _logger.LogInformation("No seed data was needed, all data already exists");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Warning occurred during seed data loading: {ErrorMessage}", ex.Message);
            }
        }

        /// <summary>
        /// 加载默认角色数据（使用静态方法）
        /// </summary>
        private async Task SeedDefaultRolesAsync(FaceLockerDbContext context)
        {
            try
            {
                _logger.LogInformation("Adding default role data...");

                var defaultRoles = Role.CreateDefaultRoles();
                await context.Roles.AddRangeAsync(defaultRoles);
                await context.SaveChangesAsync();

                _logger.LogInformation("Default role data added successfully, total {RoleCount} roles added", defaultRoles.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to add default role data: {ErrorMessage}", ex.Message);
                throw;
            }
        }

        /// <summary>
        /// 加载默认用户数据（使用静态方法）
        /// </summary>
        private async Task SeedDefaultUsersAsync(FaceLockerDbContext context)
        {
            try
            {
                _logger.LogInformation("Adding default user data...");

                var defaultUsers = User.CreateDefaultUsers();
                await context.Users.AddRangeAsync(defaultUsers);
                await context.SaveChangesAsync();

                _logger.LogInformation("Default user data added successfully, total {UserCount} users added", defaultUsers.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to add default user data: {ErrorMessage}", ex.Message);
                throw;
            }
        }

        /// <summary>
        /// 加载默认储物格数据（使用静态方法）
        /// </summary>
        private async Task SeedDefaultLockersAsync(FaceLockerDbContext context)
        {
            try
            {
                _logger.LogInformation("Adding default locker data...");

                var defaultLockers = Locker.CreateDefaultLockers();
                await context.Lockers.AddRangeAsync(defaultLockers);
                await context.SaveChangesAsync();

                _logger.LogInformation("Default locker data added successfully, total {LockerCount} lockers added", defaultLockers.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to add default locker data: {ErrorMessage}", ex.Message);
                throw;
            }
        }

        #endregion

        #region 数据库工具方法
        /// <summary>
        /// 检查数据库连接状态
        /// </summary>
        /// <returns>连接是否成功</returns>
        public async Task<bool> CheckDatabaseConnectionAsync()
        {
            SqliteConnection connection = null;
            try
            {
                connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                _logger.LogInformation("Database connection check successful");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Database connection check failed: {ErrorMessage}", ex.Message);
                return false;
            }
            finally
            {
                connection?.Close();
                connection?.Dispose();
            }
        }

        #endregion

        #region 私有辅助方法

        /// <summary>
        /// 从连接字符串提取数据库文件路径
        /// </summary>
        private string ExtractDatabasePathFromConnectionString()
        {
            if (string.IsNullOrEmpty(_connectionString))
                return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "database.db");

            var dataSourceIndex = _connectionString.IndexOf("Data Source=", StringComparison.OrdinalIgnoreCase);
            if (dataSourceIndex < 0)
                return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "database.db");

            var path = _connectionString.Substring(dataSourceIndex + 12).Split(';')[0].Trim();

            // 处理相对路径
            if (!Path.IsPathRooted(path))
            {
                // 优先使用应用程序基目录
                path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, path);

                // 如果路径中包含 "./"，需要规范化路径
                path = Path.GetFullPath(path);
            }

            return path;
        }

        /// <summary>
        /// 确保数据库目录存在
        /// </summary>
        private void EnsureDatabaseDirectory()
        {
            try
            {
                if (!string.IsNullOrEmpty(_dataDirectory) && !Directory.Exists(_dataDirectory))
                {
                    Directory.CreateDirectory(_dataDirectory);
                    _logger.LogInformation("Created database directory: {DataDirectory}", _dataDirectory);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error occurred while creating database directory: {ErrorMessage}", ex.Message);
            }
        }

        /// <summary>
        /// 验证数据库路径和写入权限
        /// </summary>
        private (bool IsValid, string ErrorMessage) ValidateDatabasePath()
        {
            try
            {
                // 首先验证和显示数据库路径
                var dbPath = ExtractDatabasePathFromConnectionString();
                _logger.LogInformation("Database file path: {DbPath}", dbPath);

                // 检查目录权限
                var directory = Path.GetDirectoryName(dbPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    try
                    {
                        Directory.CreateDirectory(directory);
                        _logger.LogInformation("Created database directory: {Directory}", directory);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to create directory: {ErrorMessage}", ex.Message);
                        return (false, "创建目录失败");
                    }
                }

                // 检查文件权限
                try
                {
                    if (File.Exists(dbPath))
                    {
                        var fileInfo = new FileInfo(dbPath);
                        _logger.LogInformation("Database file exists - Size: {FileSize} bytes, Path: {DbPath}", fileInfo.Length, dbPath);
                    }
                    else
                    {
                        _logger.LogInformation("Database file does not exist, new file will be created: {DbPath}", dbPath);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to check database file permissions: {ErrorMessage}", ex.Message);
                    return (false, "检查数据库文件权限失败");
                }

                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // 测试写入权限
                var testFile = Path.Combine(directory, "test_write.tmp");
                File.WriteAllText(testFile, "test");
                File.Delete(testFile);

                _logger.LogInformation("Database path validation completed successfully");
                return (true, "");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Database path validation failed: {ErrorMessage}", ex.Message);
                return (false, ex.Message);
            }
        }

        /// <summary>
        /// 执行SQLite优化配置
        /// </summary>
        private async Task ExecuteSqliteOptimizations(FaceLockerDbContext context)
        {
            try
            {
                // 设置journal_mode为DELETE（禁用WAL模式）
                await context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode = DELETE;");

                var optimizations = new[]
                {
                    "PRAGMA journal_mode = DELETE;",    // 强制使用DELETE模式，禁用WAL
                    "PRAGMA synchronous = FULL;",       // 完全同步，确保数据安全
                    "PRAGMA locking_mode = EXCLUSIVE;", // 独占锁定
                    "PRAGMA cache_size = 10000;",
                    "PRAGMA foreign_keys = ON;",
                    "PRAGMA temp_store = MEMORY;",
                    "PRAGMA mmap_size = 268435456;",    // 256MB内存映射
                    "PRAGMA busy_timeout = 5000;"       // 增加超时时间
                };

                foreach (var optimization in optimizations)
                {
                    await context.Database.ExecuteSqlRawAsync(optimization);
                }

                _logger.LogInformation("SQLite optimization configuration completed");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SQLite optimization configuration warning: {ErrorMessage}", ex.Message);
            }
        }

        /// <summary>
        /// 验证数据库完整性
        /// </summary>
        private async Task VerifyDatabaseIntegrity(FaceLockerDbContext context)
        {
            try
            {
                await context.Database.ExecuteSqlRawAsync("PRAGMA integrity_check;");
                await context.Database.ExecuteSqlRawAsync("PRAGMA foreign_key_check;");

                _logger.LogInformation("Database integrity verification passed");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Database integrity verification warning: {ErrorMessage}", ex.Message);
            }
        }

        #endregion

        #region IDisposable实现

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // 清理托管资源
                    _context?.Dispose();
                }
                _disposed = true;
            }
        }

        #endregion
    }
}