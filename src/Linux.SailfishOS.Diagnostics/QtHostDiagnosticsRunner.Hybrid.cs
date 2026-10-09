using System.Text.Json.Serialization;
using Microsoft.Maui.Controls;

namespace Microsoft.Maui.SailfishOS.Platform;

/// <summary>
/// F3 leg, N (tracker S48): a HybridWebView loads the sample's hybridroot/index.html from its loopback origin. The page
/// says "ready" (JS → .NET), answers .NET's "ping" with "echo:ping:42" after calling InvokeDotNet('Multiply', [6, 7])
/// (.NET → JS → .NET), and InvokeJavaScriptAsync("add", [2, 3]) returns 5.
/// </summary>
internal sealed partial class QtHostDiagnosticsRunner
{
	public sealed class F3HybridTarget
	{
		public int Multiply(int a, int b) => a * b;
	}

	private void F3HybridN(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher)
	{
		if (renderer.CurrentPage?.Navigation is not { } navigation)
		{
			_qtF3Checks.Check("N HybridWebView: a page to push from", false);
			FinishF3();
			return;
		}
		var messages = new List<string>();
		var view = new HybridWebView { HybridRoot = "hybridroot", DefaultFile = "index.html" };
		view.RawMessageReceived += (_, e) => messages.Add(e.Message ?? string.Empty);
		view.SetInvokeJavaScriptTarget(new F3HybridTarget(), F3HybridJson.Default);
		Console.Error.WriteLine("[Sailfish] Qt f3 diag: N — pushing the HybridWebView page");
		_ = navigation.PushAsync(new ContentPage { Title = "Hybrid", Content = view }, false);

		WaitFor(dispatcher, () => messages.Contains("ready"), 20000, () =>
		{
			var ready = messages.Contains("ready");
			_qtF3Checks.Check($"N HybridWebView: the HybridRoot page loads and says 'ready' (JS → .NET; messages [{string.Join(", ", messages)}]; " +
				$"origin {(view.Handler as Handlers.SailfishHybridWebViewHandler)?.Origin})", ready);
			view.SendRawMessage("ping");
			WaitFor(dispatcher, () => messages.Contains("echo:ping:42"), 10000, () =>
			{
				_qtF3Checks.Check($"N HybridWebView: SendRawMessage 'ping' reaches the page, which calls InvokeDotNet Multiply(6, 7) and answers " +
					$"(messages [{string.Join(", ", messages)}])", messages.Contains("echo:ping:42"));
				var sum = view.InvokeJavaScriptAsync<int>("add", F3HybridJson.Default.Int32, [2, 3], [F3HybridJson.Default.Int32, F3HybridJson.Default.Int32]);
				WaitFor(dispatcher, () => sum.IsCompleted, 10000, () =>
				{
					var result = sum.IsCompletedSuccessfully ? sum.Result.ToString(System.Globalization.CultureInfo.InvariantCulture) : $"<{sum.Status}: {sum.Exception?.GetBaseException().Message}>";
					_qtF3Checks.Check($"N HybridWebView: InvokeJavaScriptAsync add(2, 3) → {result}", sum.IsCompletedSuccessfully && sum.Result == 5);
					Shot(dispatcher, "f3-n-hybrid", () =>
					{
						var popped = navigation.PopAsync(false);
						WaitFor(dispatcher, () => popped.IsCompleted, 4000, FinishF3);
					});
				});
			});
		});
	}
}

[JsonSerializable(typeof(int))]
internal sealed partial class F3HybridJson : JsonSerializerContext
{
}
