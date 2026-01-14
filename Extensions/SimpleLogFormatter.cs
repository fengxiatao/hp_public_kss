using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FaceLocker.Extensions
{
    public class SimpleLogFormatter : ConsoleFormatter
    {
        public SimpleLogFormatter() : base("simpleLog") { }

        public override void Write<TState>(
            in LogEntry<TState> logEntry,
            IExternalScopeProvider scopeProvider,
            TextWriter writer)
        {
            // 时间戳
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var loggerName = logEntry.Category;
            var className = loggerName?.Split('.').Last() ?? loggerName;

            // 获取日志等级的小写字符串, 取缩写和颜色
            if (!_levelMap.TryGetValue(logEntry.LogLevel, out var levelInfo))
                levelInfo = ("uknw", "\u001b[0m"); // unknown/default

            var shortLevel = levelInfo.Short;
            var color = levelInfo.Color;
            var reset = "\u001b[0m";

            // 日志消息
            var message = logEntry.Formatter?.Invoke(logEntry.State, logEntry.Exception);

            // EventId
            var eventId = logEntry.EventId.Id;

            // 输出格式
            writer.WriteLine($"{timestamp} {color}{shortLevel}{reset}: {className}[{eventId}] {message}");

            // 异常输出（如有）
            if (logEntry.Exception != null)
            {
                writer.WriteLine(logEntry.Exception.ToString());
            }
        }

        private static readonly Dictionary<LogLevel, (string Short, string Color)> _levelMap = new()
        {
            [LogLevel.Trace] = ("trce", "\u001b[37m"),        // Gray
            [LogLevel.Debug] = ("dbug", "\u001b[36m"),        // Cyan
            [LogLevel.Information] = ("info", "\u001b[32m"),  // Green
            [LogLevel.Warning] = ("warn", "\u001b[33m"),      // Yellow
            [LogLevel.Error] = ("fail", "\u001b[31m"),        // Red
            [LogLevel.Critical] = ("crit", "\u001b[41m"),     // Red background
            [LogLevel.None] = ("none", "\u001b[0m"),
        };
    }
}
