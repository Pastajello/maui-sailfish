using System.Net.Http;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Maui.Controls;
using Microsoft.Maui.SailfishOS.Handlers;
using Microsoft.Maui.SailfishOS.Platform;
using Xunit;

namespace Linux.SailfishOS.Tests.Renderer;

/// <summary>Tracker S48 (D5 a): HybridWebView on the Gecko adapter. The page's origin is a loopback server that serves
/// HybridRoot and MAUI's hybridwebview.js and answers the script's Android endpoints; .NET → JS goes in as a message
/// event; InvokeJavaScriptAsync and InvokeDotNet follow MAUI's protocol.</summary>
[Collection("renderer")]
public sealed class HybridWebViewTests
{
	public sealed class Target
	{
		public int Add(int a, int b) => a + b;
	}

	private static (RendererHarness H, HybridWebView View, SailfishHybridWebViewHandler Handler, SailfishDispatcher Loop) Show(string? root = null)
	{
		var view = new HybridWebView { HybridRoot = root ?? "wwwroot", DefaultFile = "index.html", HeightRequest = 400 };
		var h = new RendererHarness(new ContentPage { Title = "Hybrid", Content = view });
		for (var i = 0; i < 4; i++)
			h.Poll();
		var loop = SailfishDispatcherProvider.BindLoopThread();
		return (h, view, Assert.IsType<SailfishHybridWebViewHandler>(view.Handler), loop);
	}

	/// <summary>Sends the request and pumps the loop (the server hands JS → .NET calls to the main thread).</summary>
	private static HttpResponseMessage Send(SailfishDispatcher loop, HttpRequestMessage request)
	{
		using var client = new HttpClient();
		var task = client.SendAsync(request);
		var until = DateTime.UtcNow.AddSeconds(10);
		while (!task.IsCompleted && DateTime.UtcNow < until)
		{
			loop.DrainQueue();
			Thread.Sleep(5);
		}
		return task.GetAwaiter().GetResult();
	}

	private static HttpRequestMessage Post(string origin, string path, string body, bool token = true)
	{
		var request = new HttpRequestMessage(HttpMethod.Post, origin + "/" + path) { Content = new StringContent(body, Encoding.UTF8, "text/plain") };
		if (token)
			request.Headers.Add("X-Maui-Invoke-Token", "HybridWebView");
		request.Headers.Add("Origin", origin);
		return request;
	}

	[Fact]
	public void The_page_loads_from_its_own_origin_with_mauis_script()
	{
		var (h, _, handler, loop) = Show();
		using var _h = h;

		var native = h.Shim.ByUri("web-view").Single();
		Assert.StartsWith("http://127.0.0.1:", native.Text("mauiUrl"));
		Assert.EndsWith("/index.html", native.Text("mauiUrl"));
		using var script = Send(loop, new HttpRequestMessage(HttpMethod.Get, handler.Origin + "/_framework/hybridwebview.js"));
		Assert.Equal(System.Net.HttpStatusCode.OK, script.StatusCode);
		Assert.Contains("__hwvSendMessage", script.Content.ReadAsStringAsync().Result);
		using var missing = Send(loop, new HttpRequestMessage(HttpMethod.Get, handler.Origin + "/nope.html"));
		Assert.Equal(System.Net.HttpStatusCode.NotFound, missing.StatusCode);
	}

	[Fact]
	public void HybridRoot_files_are_served_with_their_content_type()
	{
		var root = "hwv-test-" + Guid.NewGuid().ToString("N")[..8];
		var dir = Path.Combine(AppContext.BaseDirectory, root);
		Directory.CreateDirectory(dir);
		File.WriteAllText(Path.Combine(dir, "index.html"), "<html><body>hybrid page</body></html>");
		try
		{
			var (h, _, handler, loop) = Show(root);
			using var _h = h;
			using var page = Send(loop, new HttpRequestMessage(HttpMethod.Get, handler.Origin + "/index.html"));

			Assert.Equal(System.Net.HttpStatusCode.OK, page.StatusCode);
			Assert.Equal("text/html", page.Content.Headers.ContentType?.MediaType);
			Assert.Contains("hybrid page", page.Content.ReadAsStringAsync().Result);
		}
		finally
		{
			Directory.Delete(dir, recursive: true);
		}
	}

	[Fact]
	public void A_raw_message_from_the_page_reaches_RawMessageReceived_and_a_foreign_post_does_not()
	{
		var (h, view, handler, loop) = Show();
		using var _h = h;
		var received = new List<string>();
		view.RawMessageReceived += (_, e) => received.Add(e.Message!);

		using var ok = Send(loop, Post(handler.Origin!, "__hwvSendMessage", "__RawMessage|" + Uri.EscapeDataString("hello | world")));
		using var foreign = Send(loop, Post(handler.Origin!, "__hwvSendMessage", "__RawMessage|evil", token: false));
		loop.DrainQueue();

		Assert.Equal(System.Net.HttpStatusCode.OK, ok.StatusCode);
		Assert.Equal(System.Net.HttpStatusCode.Forbidden, foreign.StatusCode);
		Assert.Equal(new[] { "hello | world" }, received);
	}

	[Fact]
	public void InvokeDotNet_calls_the_target_and_answers_in_mauis_format()
	{
		var (h, view, handler, loop) = Show();
		using var _h = h;
		view.SetInvokeJavaScriptTarget(new Target(), HybridTestJsonContext.Default);

		using var response = Send(loop, Post(handler.Origin!, "__hwvInvokeDotNet", "{\"MethodName\":\"Add\",\"ParamValues\":[\"2\",\"3\"]}"));
		var json = response.Content.ReadAsStringAsync().Result;

		Assert.Contains("\"Result\":\"5\"", json);
		Assert.Contains("\"IsJson\":true", json);
		Assert.Contains("\"IsError\":false", json);
	}

	[Fact]
	public void SendRawMessage_dispatches_a_message_event_into_the_page()
	{
		var (h, view, _, _) = Show();
		using var _h = h;

		view.SendRawMessage("ping \"1\"");

		var command = h.Shim.Commands.Last(c => c.Json.Contains("dispatchEvent", StringComparison.Ordinal)).Json;
		Assert.Contains("new MessageEvent", command);
		Assert.Contains("ping", command);
	}

	[Fact]
	public async Task InvokeJavaScriptAsync_completes_with_the_pages_answer_and_throws_its_error()
	{
		var (h, view, handler, loop) = Show();
		using var _h = h;

		var sum = view.InvokeJavaScriptAsync<int>("add", HybridTestJsonContext.Default.Int32, [1, 2], [HybridTestJsonContext.Default.Int32, HybridTestJsonContext.Default.Int32]);
		loop.DrainQueue();
		var call = h.Shim.Commands.Last(c => c.Json.Contains("__InvokeJavaScript(", StringComparison.Ordinal)).Json;
		Assert.Contains("__InvokeJavaScript(1, add, [1, 2])", call);
		using (Send(loop, Post(handler.Origin!, "__hwvSendMessage", "__InvokeJavaScriptCompleted|1|3")))
		{
		}
		for (var i = 0; i < 20 && !sum.IsCompleted; i++)
			loop.DrainQueue();
		Assert.Equal(3, await sum);

		var failing = view.InvokeJavaScriptAsync<int>("boom", HybridTestJsonContext.Default.Int32);
		loop.DrainQueue();
		using (Send(loop, Post(handler.Origin!, "__hwvSendMessage", "__InvokeJavaScriptFailed|2|{\"Name\":\"TypeError\",\"Message\":\"no boom\"}")))
		{
		}
		for (var i = 0; i < 20 && !failing.IsCompleted; i++)
			loop.DrainQueue();
		var error = await Assert.ThrowsAsync<SailfishHybridWebViewJavaScriptException>(() => failing);
		Assert.Contains("no boom", error.Message);
		Assert.Equal("TypeError", error.Name);
	}
}

[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(string))]
internal sealed partial class HybridTestJsonContext : JsonSerializerContext
{
}
