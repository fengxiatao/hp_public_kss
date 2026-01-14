namespace FaceLocker.Models.Settings
{
    public class DatabaseSettings
    {
        /// <summary>
        /// SQLite连接字符串
        /// </summary>
        public string ConnectionString { get; set; } = "Data Source=Data/database.db;Cache=Shared;";

        /// <summary>
        /// 数据目录（相对路径或绝对路径）
        /// </summary>
        public string DataDirectory { get; set; } = "./Data/";
    }
}
