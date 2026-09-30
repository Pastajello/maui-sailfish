using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging;

namespace SailfishKitchen.Services;

/// <summary>
/// Appends log records to a file, because the Sailfish launcher gives a harbour app no usable stdout;
/// <c>tools/sf run</c> tails it.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
	private readonly string _path;
	private readonly LogLevel _minimum;
	private readonly ConcurrentDictionary<string, FileLogger> _loggers = new(StringComparer.Ordinal);
	private readonly object _gate = new();

	public FileLoggerProvider(string path, LogLevel minimumLevel = LogLevel.Information)
	{
		_path = path;
		_minimum = minimumLevel;

		try
		{
			var directory = Path.GetDirectoryName(path);
			if (!string.IsNullOrEmpty(directory))
				Directory.CreateDirectory(directory);
		}
		catch (Exception)
		{
			// If the file is not writable the app still runs; logging just goes nowhere.
		}
	}

	public ILogger CreateLogger(string categoryName) =>
		_loggers.GetOrAdd(categoryName, name => new FileLogger(name, this));

	public void Dispose() => _loggers.Clear();

	internal void Write(string categoryName, LogLevel level, string message, Exception? exception)
	{
		if (level < _minimum)
			return;

		var builder = new StringBuilder(128)
			.Append(DateTime.Now.ToString("HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture))
			.Append(' ')
			.Append(ShortLevel(level))
			.Append(' ')
			.Append(ShortCategory(categoryName))
			.Append(": ")
			.Append(message);

		if (exception is not null)
			builder.Append(Environment.NewLine).Append(exception);

		builder.Append(Environment.NewLine);

		lock (_gate)
		{
			try { File.AppendAllText(_path, builder.ToString()); }
			catch (Exception) { /* never let logging take the app down */ }
		}
	}

	private static string ShortLevel(LogLevel level) => level switch
	{
		LogLevel.Trace => "trce",
		LogLevel.Debug => "dbug",
		LogLevel.Information => "info",
		LogLevel.Warning => "warn",
		LogLevel.Error => "fail",
		LogLevel.Critical => "crit",
		_ => "????",
	};

	private static string ShortCategory(string categoryName)
	{
		var index = categoryName.LastIndexOf('.');
		return index >= 0 && index < categoryName.Length - 1 ? categoryName[(index + 1)..] : categoryName;
	}

	private sealed class FileLogger(string categoryName, FileLoggerProvider provider) : ILogger
	{
		public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

		public bool IsEnabled(LogLevel logLevel) => logLevel >= provider._minimum;

		public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
		{
			if (!IsEnabled(logLevel))
				return;

			provider.Write(categoryName, logLevel, formatter(state, exception), exception);
		}
	}
}
