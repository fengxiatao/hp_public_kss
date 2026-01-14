using FaceLocker.Models;
using FaceLocker.Models.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FaceLocker.Services
{
    /// <summary>
    /// FaceLocker 应用程序的主数据库上下文
    /// 简化版本：只包含基本表结构和默认值，无复杂配置
    /// </summary>
    public class FaceLockerDbContext : DbContext
    {
        private readonly AppSettings _appSettings;
        private readonly ILogger<FaceLockerDbContext>? _logger;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="options">数据库上下文选项</param>
        /// <param name="appSettings">应用程序设置</param>
        /// <param name="logger">日志记录器（可选）</param>
        public FaceLockerDbContext(DbContextOptions<FaceLockerDbContext> options,
            IOptions<AppSettings> appSettings,
            ILogger<FaceLockerDbContext>? logger = null) : base(options)
        {
            _appSettings = appSettings.Value;
            _logger = logger;

            // 记录数据库上下文创建信息
            _logger?.LogInformation("FaceLockerDbContext 已创建，使用连接字符串: {ConnectionString}",
                _appSettings.Database.ConnectionString ?? "默认连接字符串");
        }

        #region 数据库表集合

        /// <summary>
        /// 用户表
        /// </summary>
        public DbSet<User> Users { get; set; } = null!;

        /// <summary>
        /// 角色表
        /// </summary>
        public DbSet<Role> Roles { get; set; } = null!;

        /// <summary>
        /// 储物柜表
        /// </summary>
        public DbSet<Locker> Lockers { get; set; } = null!;

        /// <summary>
        /// 用户储物柜关联表
        /// </summary>
        public DbSet<UserLocker> UserLockers { get; set; } = null!;

        /// <summary>
        /// 访问日志表
        /// </summary>
        public DbSet<AccessLog> AccessLogs { get; set; } = null!;

        #endregion

        #region 配置数据库连接
        /// <summary>
        /// 配置数据库连接
        /// </summary>
        /// <param name="optionsBuilder">数据库选项构建器</param>
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            var connectionString = _appSettings.Database.ConnectionString ?? "Data Source=./Data/database.db";

            // 记录连接字符串信息（不记录敏感信息）
            _logger?.LogInformation("配置数据库连接，连接字符串模式: {ConnectionStringPattern}",
                connectionString.Length > 50 ? connectionString.Substring(0, 50) + "..." : connectionString);

            // 处理相对路径
            if (connectionString.StartsWith("Data Source=.") || connectionString.StartsWith("Data Source=./"))
            {
                var basePath = AppDomain.CurrentDomain.BaseDirectory;
                var relativePath = connectionString.Substring(12);

                if (relativePath.StartsWith("./"))
                    relativePath = relativePath.Substring(2);

                var fullPath = Path.GetFullPath(Path.Combine(basePath, relativePath));
                connectionString = $"Data Source={fullPath}";

                // 记录解析后的完整路径
                _logger?.LogInformation("解析相对路径为完整路径: {FullPath}", fullPath);

                // 确保目录存在
                var directory = Path.GetDirectoryName(fullPath);
                if (!Directory.Exists(directory) && !string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                    _logger?.LogInformation("创建数据库目录: {Directory}", directory);
                }
            }

            if (!optionsBuilder.IsConfigured)
            {
                optionsBuilder.UseSqlite(connectionString);
                _logger?.LogInformation("已配置 SQLite 数据库连接");
            }

            // 开发环境配置
#if DEBUG
            optionsBuilder.EnableSensitiveDataLogging();
            optionsBuilder.EnableDetailedErrors();
            if (_logger != null)
            {
                //optionsBuilder.LogTo(message => _logger.LogInformation("EF Core 日志: {Message}", message));
                _logger.LogInformation("已启用 EF Core 详细日志记录");
            }
#endif

            optionsBuilder.EnableServiceProviderCaching();
        }
        #endregion

        #region 配置实体模型

        /// <summary>
        /// 配置实体模型 - 简化版本，只配置基本属性和默认值
        /// </summary>
        /// <param name="modelBuilder">模型构建器</param>
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            _logger?.LogInformation("开始配置实体模型...");

            // 全局禁用外键约束
            modelBuilder.HasAnnotation("Relational:ForeignKeyConstraints", false);
            _logger?.LogInformation("已禁用全局外键约束");

            #region 用户表配置

            modelBuilder.Entity<User>(entity =>
            {
                entity.HasKey(e => e.Id);
                _logger?.LogDebug("配置 User 表主键: Id");

                // 字段长度约束
                entity.Property(e => e.UserNumber).HasMaxLength(50);
                entity.Property(e => e.IdNumber).HasMaxLength(50).IsRequired(false);
                entity.Property(e => e.PhoneNumber).HasMaxLength(50).IsRequired(false);
                entity.Property(e => e.Name).HasMaxLength(100).IsRequired(false);
                entity.Property(e => e.Password).HasMaxLength(64).IsRequired(false);
                entity.Property(e => e.Department).HasMaxLength(100).IsRequired(false);
                entity.Property(e => e.Remarks).HasMaxLength(500).IsRequired(false);

                // 默认值配置
                entity.Property(e => e.IsActive).HasDefaultValue(true);
                entity.Property(e => e.FaceConfidence).HasDefaultValue(0.0f);
                entity.Property(e => e.FaceFeatureVersion).HasDefaultValue(1);
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("datetime('now')");
                entity.Property(e => e.UpdatedAt).HasDefaultValueSql("datetime('now')");

                // 禁用外键约束
                entity.Ignore(e => e.UserLockers);
                entity.Ignore(e => e.AccessLogs);

                _logger?.LogDebug("User 表配置完成");
            });

            #endregion

            #region 角色表配置

            modelBuilder.Entity<Role>(entity =>
            {
                entity.HasKey(e => e.Id);
                _logger?.LogDebug("配置 Role 表主键: Id");

                // 字段长度约束
                entity.Property(e => e.Name).HasMaxLength(100);
                entity.Property(e => e.Description).HasMaxLength(500);

                // 默认值配置
                entity.Property(e => e.IsFromServer).HasDefaultValue(false);
                entity.Property(e => e.IsBuiltIn).HasDefaultValue(false);
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("datetime('now')");
                entity.Property(e => e.UpdatedAt).HasDefaultValueSql("datetime('now')");

                // 禁用外键约束
                entity.Ignore(e => e.Users);

                _logger?.LogDebug("Role 表配置完成");
            });

            #endregion

            #region 储物柜表配置

            modelBuilder.Entity<Locker>(entity =>
            {
                entity.HasKey(e => e.LockerId);
                _logger?.LogDebug("配置 Locker 表主键: LockerId");

                // 字段长度约束
                entity.Property(e => e.LockerName).HasMaxLength(50);
                entity.Property(e => e.LockerNumber).HasMaxLength(50);
                entity.Property(e => e.Location).HasMaxLength(100);

                // 默认值配置
                entity.Property(e => e.Status).HasDefaultValue(LockerStatus.Available);
                entity.Property(e => e.IsOpened).HasDefaultValue(false);
                entity.Property(e => e.IsAvailable).HasDefaultValue(true);
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("datetime('now')");
                entity.Property(e => e.UpdatedAt).HasDefaultValueSql("datetime('now')");

                // 禁用外键约束
                entity.Ignore(e => e.UserLockers);
                entity.Ignore(e => e.AccessLogs);

                _logger?.LogDebug("Locker 表配置完成");
            });

            #endregion

            #region 用户储物柜关联表配置

            modelBuilder.Entity<UserLocker>(entity =>
            {
                entity.HasKey(e => e.UserLockerId);
                _logger?.LogDebug("配置 UserLocker 表主键: UserLockerId");

                // 默认值配置
                entity.Property(e => e.AssignedAt).HasDefaultValueSql("datetime('now')");
                entity.Property(e => e.IsActive).HasDefaultValue(true);
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("datetime('now')");
                entity.Property(e => e.StoredTime).HasDefaultValueSql("datetime('now')");
                entity.Property(e => e.RetrievedTime).HasDefaultValueSql("datetime('now')");
                entity.Property(e => e.StorageStatus);

                // 禁用外键约束
                entity.Ignore(e => e.User);
                entity.Ignore(e => e.Locker);

                _logger?.LogDebug("UserLocker 表配置完成");
            });

            #endregion

            #region 访问日志表配置

            modelBuilder.Entity<AccessLog>(entity =>
            {
                entity.HasKey(e => e.Id);
                _logger?.LogDebug("配置 AccessLog 表主键: Id");

                // 字段长度约束
                entity.Property(e => e.UserId).HasMaxLength(50);
                entity.Property(e => e.UserName).HasMaxLength(100);
                entity.Property(e => e.LockerName).HasMaxLength(100);
                entity.Property(e => e.Details).HasMaxLength(500);

                // 默认值配置
                entity.Property(e => e.Timestamp).HasDefaultValueSql("datetime('now')");
                entity.Property(e => e.IsUploaded).HasDefaultValue(false);

                // 禁用外键约束
                entity.Ignore(e => e.User);
                entity.Ignore(e => e.Locker);

                _logger?.LogDebug("AccessLog 表配置完成");
            });

            #endregion

            _logger?.LogInformation("实体模型配置完成");
        }
        #endregion
        #region 保存更改到数据库（同步）

        /// <summary>
        /// 保存更改到数据库（同步）
        /// </summary>
        /// <returns>受影响的行数</returns>
        public override int SaveChanges()
        {
            // 记录保存前的实体状态
            var addedEntries = ChangeTracker.Entries().Where(e => e.State == EntityState.Added).ToList();
            var modifiedEntries = ChangeTracker.Entries().Where(e => e.State == EntityState.Modified).ToList();
            var deletedEntries = ChangeTracker.Entries().Where(e => e.State == EntityState.Deleted).ToList();

            _logger?.LogInformation("开始保存数据库更改: 新增 {AddedCount} 条, 修改 {ModifiedCount} 条, 删除 {DeletedCount} 条",
                addedEntries.Count, modifiedEntries.Count, deletedEntries.Count);

            // 更新时间戳
            UpdateTimestamps();

            try
            {
                var result = base.SaveChanges();
                _logger?.LogInformation("数据库保存成功，受影响行数: {Result}", result);
                return result;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "数据库保存失败");
                throw;
            }
        }
        #endregion

        #region 保存更改到数据库（异步）

        /// <summary>
        /// 保存更改到数据库（异步）
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>受影响的行数</returns>
        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            // 记录保存前的实体状态
            var addedEntries = ChangeTracker.Entries().Where(e => e.State == EntityState.Added).ToList();
            var modifiedEntries = ChangeTracker.Entries().Where(e => e.State == EntityState.Modified).ToList();
            var deletedEntries = ChangeTracker.Entries().Where(e => e.State == EntityState.Deleted).ToList();

            _logger?.LogInformation("开始异步保存数据库更改: 新增 {AddedCount} 条, 修改 {ModifiedCount} 条, 删除 {DeletedCount} 条",
                addedEntries.Count, modifiedEntries.Count, deletedEntries.Count);

            // 更新时间戳
            UpdateTimestamps();

            try
            {
                var result = await base.SaveChangesAsync(cancellationToken);
                _logger?.LogInformation("数据库异步保存成功，受影响行数: {Result}", result);
                return result;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "数据库异步保存失败");
                throw;
            }
        }
        #endregion

        #region 自动更新时间戳字段

        /// <summary>
        /// 自动更新时间戳字段
        /// 为新增或修改的实体更新 CreatedAt、UpdatedAt、Timestamp 等字段
        /// </summary>
        private void UpdateTimestamps()
        {
            var entries = ChangeTracker.Entries()
                .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified);

            var updatedCount = 0;
            foreach (var entry in entries)
            {
                if (entry.Entity is User user)
                {
                    if (entry.State == EntityState.Added)
                    {
                        user.CreatedAt = DateTime.Now;
                    }
                    user.UpdatedAt = DateTime.Now;
                    updatedCount++;
                }
                else if (entry.Entity is Role role)
                {
                    if (entry.State == EntityState.Added)
                    {
                        role.CreatedAt = DateTime.Now;
                    }
                    role.UpdatedAt = DateTime.Now;
                    updatedCount++;
                }
                else if (entry.Entity is Locker locker)
                {
                    if (entry.State == EntityState.Added)
                    {
                        locker.CreatedAt = DateTime.Now;
                    }
                    locker.UpdatedAt = DateTime.Now;
                    updatedCount++;
                }
                else if (entry.Entity is UserLocker userLocker)
                {
                    if (entry.State == EntityState.Added)
                    {
                        userLocker.CreatedAt = DateTime.Now;
                        userLocker.AssignedAt = DateTime.Now;
                    }
                    userLocker.StoredTime = DateTime.Now;
                    updatedCount++;
                }
                else if (entry.Entity is AccessLog log)
                {
                    log.Timestamp = DateTime.Now;
                    updatedCount++;
                }
            }

            if (updatedCount > 0)
            {
                _logger?.LogDebug("已更新 {UpdatedCount} 个实体的时间戳字段", updatedCount);
            }
        }
        #endregion

        #region 数据库健康检查

        /// <summary>
        /// 检查数据库连接是否可用
        /// </summary>
        /// <returns>连接是否成功</returns>
        public async Task<bool> CanConnectAsync()
        {
            _logger?.LogInformation("开始检查数据库连接状态...");

            try
            {
                var canConnect = await Database.CanConnectAsync();

                if (canConnect)
                {
                    _logger?.LogInformation("数据库连接检查成功");
                }
                else
                {
                    _logger?.LogWarning("数据库连接检查失败，无法连接到数据库");
                }

                return canConnect;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "数据库连接检查异常");
                return false;
            }
        }

        #endregion
    }
}
