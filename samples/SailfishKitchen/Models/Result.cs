namespace SailfishKitchen.Models;

/// <summary>
/// Outcome of a repository call that can fail in ways the UI must distinguish, without exceptions crossing
/// the view-model boundary.
/// </summary>
public readonly struct Result<T>
{
	private readonly T? _value;

	private Result(T? value, ResultFailure? failure)
	{
		_value = value;
		Failure = failure;
	}

	public bool IsSuccess => Failure is null;

	public ResultFailure? Failure { get; }

	public T Value => IsSuccess
		? _value!
		: throw new InvalidOperationException($"Result is a failure ({Failure!.Kind}); check IsSuccess before reading Value.");

	public static Result<T> Success(T value) => new(value, null);

	public static Result<T> Fail(ResultFailure failure) => new(default, failure);

	public TResult Match<TResult>(Func<T, TResult> onSuccess, Func<ResultFailure, TResult> onFailure) =>
		IsSuccess ? onSuccess(_value!) : onFailure(Failure!);

	public void Switch(Action<T> onSuccess, Action<ResultFailure> onFailure)
	{
		if (IsSuccess)
			onSuccess(_value!);
		else
			onFailure(Failure!);
	}
}

public enum FailureKind
{
	/// <summary>No network, DNS failure, TLS failure, or the server did not answer.</summary>
	Network,

	/// <summary>The server answered with 4xx/5xx.</summary>
	Server,

	/// <summary>The server answered 200 with a body we could not map.</summary>
	Malformed,

	/// <summary>The requested recipe/category does not exist.</summary>
	NotFound,

	/// <summary>The caller cancelled, usually by navigating away.</summary>
	Cancelled,

	/// <summary>Anything else; always carries the exception.</summary>
	Unknown,
}

public sealed record ResultFailure(FailureKind Kind, string Message, Exception? Exception = null)
{
	/// <summary>True when offering a Retry button makes sense.</summary>
	public bool IsRetryable => Kind is FailureKind.Network or FailureKind.Server or FailureKind.Unknown;

	/// <summary>True when the user just left, which no UI should report.</summary>
	public bool IsSilent => Kind is FailureKind.Cancelled;

	public static ResultFailure Network(string message, Exception? ex = null) => new(FailureKind.Network, message, ex);
	public static ResultFailure Server(string message, Exception? ex = null) => new(FailureKind.Server, message, ex);
	public static ResultFailure Malformed(string message, Exception? ex = null) => new(FailureKind.Malformed, message, ex);
	public static ResultFailure NotFound(string message) => new(FailureKind.NotFound, message);
	public static ResultFailure Cancelled() => new(FailureKind.Cancelled, "The request was cancelled.");
	public static ResultFailure Unknown(string message, Exception? ex = null) => new(FailureKind.Unknown, message, ex);
}

/// <summary>
/// One page of a larger result set. <see cref="TotalCount"/> is null until known, e.g. the letter-by-letter
/// catalog walk learns it only after the last letter.
/// </summary>
public sealed record PagedList<T>(IReadOnlyList<T> Items, int Skip, int Take, int? TotalCount)
{
	public static readonly PagedList<T> Empty = new(Array.Empty<T>(), 0, 0, 0);

	public bool IsTotalKnown => TotalCount.HasValue;

	public bool HasMore => TotalCount.HasValue ? Skip + Items.Count < TotalCount.Value : Items.Count > 0;

	public int NextSkip => Skip + Items.Count;

	/// <summary>0..1 progress through the whole set.</summary>
	public double Progress => !TotalCount.HasValue || TotalCount.Value <= 0
		? 0d
		: Math.Clamp((double)(Skip + Items.Count) / TotalCount.Value, 0d, 1d);
}
