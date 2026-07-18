// Application logger — writes to a log/ directory next to the executable.
// Both the Headless CLI and the WPF GUI use this so all runs leave an audit trail.
//
// Log file naming: lpc-YYYYMMDD-HHMMSS-<pid>.log under log/ (created lazily).
// Severity levels: Debug, Info, Warning, Error. Console mirror optional.
using System;
using System.IO;
using System.Threading;

namespace LpcSpriteGen.Core.Diagnostics;

public enum LogLevel { Debug, Info, Warning, Error }

public static class Logger
{
    private static readonly object _gate = new();
    private static string? _logDir;
    private static string? _logFile;
    private static LogLevel _minLevel = LogLevel.Info;
    private static bool _mirrorToConsole = true;

    /// <summary>Initialize the logger. Call once at process startup.</summary>
    /// <param name="baseDir">Where to create the log/ subdirectory. Defaults to next to the exe.</param>
    public static void Init(string? baseDir = null, LogLevel minLevel = LogLevel.Info, bool mirrorToConsole = true)
    {
        lock (_gate)
        {
            _minLevel = minLevel;
            _mirrorToConsole = mirrorToConsole;
            baseDir ??= ResolveDefaultBaseDir();
            _logDir = Path.Combine(baseDir, "log");
            Directory.CreateDirectory(_logDir);
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            _logFile = Path.Combine(_logDir, $"lpc-{stamp}-{Environment.ProcessId}.log");

            // Also keep a stable "latest.log" pointer (overwrite) for convenience.
            var latest = Path.Combine(_logDir, "latest.log");

            File.AppendAllText(_logFile,
                $"LPC Sprite Generator log — started {DateTime.Now:O}{Environment.NewLine}" +
                $"PID={Environment.ProcessId} Machine={Environment.MachineName} OS={Environment.OSVersion}{Environment.NewLine}" +
                new string('-', 80) + Environment.NewLine);
            try { File.Copy(_logFile, latest, overwrite: true); } catch { /* best-effort */ }
        }
    }

    /// <summary>Directory where log files live. Null until Init() is called.</summary>
    public static string? LogDirectory => _logDir;

    /// <summary>Path to the active log file. Null until Init() is called.</summary>
    public static string? LogFile => _logFile;

    public static void Debug(string msg, Exception? ex = null) => Write(LogLevel.Debug, msg, ex);
    public static void Info(string msg, Exception? ex = null) => Write(LogLevel.Info, msg, ex);
    public static void Warning(string msg, Exception? ex = null) => Write(LogLevel.Warning, msg, ex);
    public static void Error(string msg, Exception? ex = null) => Write(LogLevel.Error, msg, ex);

    private static void Write(LogLevel level, string msg, Exception? ex)
    {
        if (level < _minLevel) return;
        var line = $"{DateTime.Now:HH:mm:ss.fff} [{level}] {msg}";
        if (ex != null) line += Environment.NewLine + ex;
        line += Environment.NewLine;

        lock (_gate)
        {
            // If Init() was never called, fall back to console-only.
            if (_logFile != null)
            {
                try { File.AppendAllText(_logFile, line); }
                catch { /* logging must never throw */ }
            }
            if (_mirrorToConsole)
            {
                // ALL levels go to stderr so machine-readable JSON on stdout stays clean.
                // (Important for `--json` / `--list-items --json` / `--dump-catalog` callers.)
                try { Console.Error.Write(line); } catch { /* ignore */ }
            }
        }
    }

    private static string ResolveDefaultBaseDir()
    {
        // Place log/ next to the executable when available; otherwise next to CWD.
        var exePath = Environment.ProcessPath;
        var exeDir = !string.IsNullOrEmpty(exePath) ? Path.GetDirectoryName(exePath) : null;
        return !string.IsNullOrEmpty(exeDir) ? exeDir! : Directory.GetCurrentDirectory();
    }
}
