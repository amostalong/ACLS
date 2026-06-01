namespace ACLS.Logging
{
    /// <summary>
    /// 日志级别，从低到高。设置某级别时，该级别及更高级别均会输出。
    /// </summary>
    public enum LogLevel
    {
        /// <summary>最详细的跟踪信息，只在深度调试时开启</summary>
        Trace = 0,

        /// <summary>调试信息，开发阶段排查问题时使用</summary>
        Debug = 1,

        /// <summary>常规信息，记录正常运行状态（默认级别）</summary>
        Info = 2,

        /// <summary>警告，不影响运行但值得关注的情况</summary>
        Warn = 3,

        /// <summary>错误，功能受损但系统能继续运行</summary>
        Error = 4,

        /// <summary>致命错误，系统无法继续运行</summary>
        Fatal = 5,
    }
}
