using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace ACLS.Logging
{
    /// <summary>
    /// 统一的静态日志入口。支持按频道（子系统）独立控制日志级别。
    /// 输出到 Unity Console（Debug.Log/Warning/Error），RuntimeLogger 自动捕获写入文件。
    ///
    /// 用法：
    /// <code>
    /// Log.Info(Log.Channels.Llm, "收到流式响应，长度={0}", length);
    /// Log.Warn(Log.Channels.Sim, "状态异常");
    /// if (Log.IsEnabled(Log.Channels.UI, LogLevel.Debug))
    ///     Log.Debug(Log.Channels.UI, $"复杂拼串 {expensive()}");
    /// </code>
    /// </summary>
    public static class Log
    {
        // ──── 预定义频道常量（方便 IDE 自动补全） ────
        public static class Channels
        {
            public const string Llm       = "Llm";
            public const string LlmReply  = "LlmReply";
            public const string WorldBuild= "WorldBuild";
            public const string Stage     = "StageCreate";
            public const string Sim       = "Sim";
            public const string UI        = "UI";
            public const string Save      = "Save";
            public const string System    = "System";
            public const string Content   = "Content";
            public const string Network   = "Network";
        }

        // ──── 主线程上下文（供后台线程投递 Debug.Log，确保 Console 即时显示） ────
        private static SynchronizationContext _mainContext;

        /// <summary>
        /// 设置主线程 SynchronizationContext。RuntimeLogger 初始化时调用。
        /// 在此之后，Log.Write 在后台线程被调用时会投递 Debug.Log 到主线程，
        /// 避免 Unity Console 因跨线程排队而延迟显示。
        /// </summary>
        public static void SetMainContext(SynchronizationContext ctx)
        {
            _mainContext = ctx;
        }

        // ──── 级别配置 ────
        private static readonly Dictionary<string, int> _levels =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private const int DefaultLevel = (int)LogLevel.Info;

        private static readonly object _lock = new object();

        // ──── 全局级别覆盖（null = 使用各频道独立级别） ────
        private static int? _globalLevel = null;

        /// <summary>设置全局日志级别覆盖。null 恢复各频道独立级别。</summary>
        public static void SetGlobalLevel(LogLevel? level)
        {
            lock (_lock)
            {
                _globalLevel = level.HasValue ? (int)level.Value : (int?)null;
            }
        }

        /// <summary>获取当前全局日志级别覆盖，null 表示未设置。</summary>
        public static LogLevel? GetGlobalLevel()
        {
            lock (_lock)
            {
                if (_globalLevel.HasValue)
                    return (LogLevel)_globalLevel.Value;
                return null;
            }
        }

        /// <summary>设置某个频道的日志级别。低于该级别的日志将被抑制。</summary>
        public static void SetLevel(string channel, LogLevel level)
        {
            if (string.IsNullOrWhiteSpace(channel)) return;
            lock (_lock)
            {
                _levels[channel.Trim()] = (int)level;
            }
        }

        /// <summary>重置所有频道到默认级别（Info）。</summary>
        public static void ResetAllLevels()
        {
            lock (_lock) _levels.Clear();
        }

        /// <summary>查询指定频道+级别是否启用了日志。</summary>
        public static bool IsEnabled(string channel, LogLevel level)
        {
            if (string.IsNullOrWhiteSpace(channel)) return false;
            int min;
            if (_globalLevel.HasValue)
                min = _globalLevel.Value;
            else if (!_levels.TryGetValue(channel.Trim(), out min))
                min = DefaultLevel;
            return (int)level >= min;
        }

        // ──── 写入方法 ────
        // 颜色参数为可选 hex（如 "#5fd95f"）或 null，传入时会把 message 包裹在
        // <color=...>…</color> 里，使 Unity Console 以指定颜色渲染。
        // 颜色不会写入 RuntimeLogger 文本文件（避免干扰 grep），仅在 Console 中生效。

        public static void Debug(string channel, string message) =>
            Write(LogLevel.Debug, channel, message);

        public static void Debug(string channel, string format, params object[] args) =>
            Write(LogLevel.Debug, channel, string.Format(format, args));

        public static void Info(string channel, string message) =>
            Write(LogLevel.Info, channel, message);

        public static void Info(string channel, string format, params object[] args) =>
            Write(LogLevel.Info, channel, string.Format(format, args));

        public static void Warn(string channel, string message) =>
            Write(LogLevel.Warn, channel, message);

        public static void Warn(string channel, string format, params object[] args) =>
            Write(LogLevel.Warn, channel, string.Format(format, args));

        public static void Error(string channel, string message) =>
            Write(LogLevel.Error, channel, message);

        public static void Error(string channel, string format, params object[] args) =>
            Write(LogLevel.Error, channel, string.Format(format, args));

        // ──── 彩色重载（仅 Console 着色，不影响 RuntimeLogger 文本文件） ────
        // 须显式传 hex 颜色，如 Log.InfoColored(Channels.Network, Colors.LlmIO, "...")。
        // 为了避免与原 Debug/Info/Warn/Error 重载歧义，color 参数必须是 string 显式变量。

        public static void DebugColored(string channel, string color, string message) =>
            WriteColored(LogLevel.Debug, channel, message, color);

        public static void DebugColored(string channel, string color, string format, params object[] args) =>
            WriteColored(LogLevel.Debug, channel, string.Format(format, args), color);

        public static void InfoColored(string channel, string color, string message) =>
            WriteColored(LogLevel.Info, channel, message, color);

        public static void InfoColored(string channel, string color, string format, params object[] args) =>
            WriteColored(LogLevel.Info, channel, string.Format(format, args), color);

        public static void WarnColored(string channel, string color, string message) =>
            WriteColored(LogLevel.Warn, channel, message, color);

        public static void WarnColored(string channel, string color, string format, params object[] args) =>
            WriteColored(LogLevel.Warn, channel, string.Format(format, args), color);

        public static void ErrorColored(string channel, string color, string message) =>
            WriteColored(LogLevel.Error, channel, message, color);

        public static void ErrorColored(string channel, string color, string format, params object[] args) =>
            WriteColored(LogLevel.Error, channel, string.Format(format, args), color);

        // 预设颜色常量——避免调用方零散地写 hex。
        public static class Colors
        {
            public const string LlmIO      = "#5fd95f";  // 亮绿：LLM 输入/输出
            public const string LlmIODim   = "#3a8a3a";  // 暗绿：响应摘要等
        }

        // ──── 内部实现 ────

        private static void Write(LogLevel level, string channel, string message)
        {
            // 快速通道：级别不够直接跳过
            if (!IsEnabled(channel, level)) return;

            // 只要 channel 不含 [] 就加括号，避免嵌套
            string prefix = channel.IndexOf('[') < 0
                ? $"[{channel}]"
                : channel;
            string formatted = $"{prefix} {message}";

            // 后台线程 → 投递到主线程执行 Debug.Log，确保 Console 即时显示
            // （RuntimeLogger 通过 logMessageReceivedThreaded 捕获写文件，不受影响）
            if (_mainContext != null && SynchronizationContext.Current != _mainContext)
            {
                _mainContext.Post(_ => LogToUnityConsole(level, formatted, null), null);
                return;
            }

            LogToUnityConsole(level, formatted, null);
        }

        private static void WriteColored(LogLevel level, string channel, string message, string color)
        {
            if (!IsEnabled(channel, level)) return;
            if (string.IsNullOrEmpty(color)) { Write(level, channel, message); return; }

            string prefix = channel.IndexOf('[') < 0
                ? $"[{channel}]"
                : channel;
            string formatted = $"{prefix} {message}";

            if (_mainContext != null && SynchronizationContext.Current != _mainContext)
            {
                _mainContext.Post(_ => LogToUnityConsole(level, formatted, color), null);
                return;
            }

            LogToUnityConsole(level, formatted, color);
        }

        /// <summary>实际调用 Unity Debug 系列 API。主线程/分发目标执行。</summary>
        private static void LogToUnityConsole(LogLevel level, string formatted, string color)
        {
            // 颜色仅在 Console 中生效；传入的颜色包裹在 message 内部。
            // 颜色包含 <…> 括号——RuntimeLogger 写文件时会按原文写入（含颜色标签），
            // 写文件前的清洗/过滤在 RuntimeLogger 内部处理，此处不处理。
            string outMessage = string.IsNullOrEmpty(color) ? formatted : $"<color={color}>{formatted}</color>";

            switch (level)
            {
                case LogLevel.Debug:
                case LogLevel.Info:
                    UnityEngine.Debug.Log(outMessage);
                    break;
                case LogLevel.Warn:
                    UnityEngine.Debug.LogWarning(outMessage);
                    break;
                case LogLevel.Error:
                    UnityEngine.Debug.LogError(outMessage);
                    break;
            }
        }
    }
}
