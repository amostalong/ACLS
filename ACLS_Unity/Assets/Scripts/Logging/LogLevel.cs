namespace ACLS.Logging
{
    /// <summary>
    /// 日志级别，从低到高。设置某级别时，该级别及更高级别均会输出。
    /// </summary>
    public enum LogLevel
    {
        Debug = 0,
        Info  = 1,
        Warn  = 2,
        Error = 3,
    }
}
