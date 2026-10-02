using System.Text.Json;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.Communication;
using Microsoft.Maui.Authentication;
using Microsoft.Maui.SailfishOS.Platform.QtHost;

namespace Microsoft.Maui.SailfishOS.Platform;

/// <summary>
/// App actions (Android shortcuts, iOS quick actions) as the actions of the app's home-screen cover, the Sailfish place
/// for them: the first two actions get a cover button each (Silica covers have two), tapping one raises
/// <see cref="AppActionActivated"/>. An icon is a theme cover icon name (<c>icon-cover-search</c>), an
/// <c>image://</c> or file URL; without one the button shows <c>icon-cover-next</c>. It replaces actions set through
/// <see cref="SailfishCover.SetActions"/>.
/// </summary>
public sealed class SailfishAppActions : IAppActions
{
	internal const string DefaultIcon = "image://theme/icon-cover-next";
	private IReadOnlyList<AppAction> _actions = Array.Empty<AppAction>();

	public bool IsSupported => true;

	public event EventHandler<AppActionEventArgs>? AppActionActivated;

	public Task<IEnumerable<AppAction>> GetAsync() => Task.FromResult<IEnumerable<AppAction>>(_actions);

	public Task SetAsync(IEnumerable<AppAction> actions)
	{
		ArgumentNullException.ThrowIfNull(actions);
		_actions = actions.ToList();
		if (_actions.Count > 2)
			QtHostDiag.Warn(QtHostDiagChannel.Lifecycle, $"AppActions: {_actions.Count} actions, the cover shows the first two");
		SailfishCover.SetActions(_actions.Take(2)
			.Select(a => new SailfishCoverAction(IconOf(GetIcon(a)), () => AppActionActivated?.Invoke(this, new AppActionEventArgs(a))))
			.ToArray());
		return Task.CompletedTask;
	}

	// The icon given to AppAction's constructor is internal in MAUI 11 (the platforms read it from inside).
	[System.Runtime.CompilerServices.UnsafeAccessor(System.Runtime.CompilerServices.UnsafeAccessorKind.Method, Name = "get_Icon")]
	internal static extern string? GetIcon(AppAction action);

	internal static string IconOf(string? icon) => icon switch
	{
		null or "" => DefaultIcon,
		_ when icon.Contains("://", StringComparison.Ordinal) => icon,
		_ when icon.StartsWith('/') => "file://" + icon,
		_ => "image://theme/" + icon,
	};
}

/// <summary>
/// WebAuthenticator: opens the sign-in URL in the system browser and completes when the browser hands the callback URL
/// back to the app. The callback scheme must be one the app declares (<c>&lt;SailfishUrlSchemes&gt;</c>), so the browser
/// can launch it; that URL goes to the waiting call instead of <c>Application.OnAppLinkRequestReceived</c>, as Android's
/// callback activity takes it. A new sign-in or the token cancels a pending one; the browser has no "closed" signal,
/// so a sign-in the user abandons stays pending until then.
/// </summary>
public sealed class SailfishWebAuthenticator : IWebAuthenticator
{
	private TaskCompletionSource<Uri>? _pending;

	/// <summary>Opens the sign-in page (tests replace it).</summary>
	internal static Func<Uri, Task> OpenBrowser = uri => Browser.Default.OpenAsync(uri, BrowserLaunchMode.External);

	public Task<WebAuthenticatorResult> AuthenticateAsync(WebAuthenticatorOptions webAuthenticatorOptions) =>
		AuthenticateAsync(webAuthenticatorOptions, CancellationToken.None);

	public async Task<WebAuthenticatorResult> AuthenticateAsync(WebAuthenticatorOptions webAuthenticatorOptions, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(webAuthenticatorOptions);
		var url = webAuthenticatorOptions.Url ?? throw new ArgumentException("Url is required.", nameof(webAuthenticatorOptions));
		var callback = webAuthenticatorOptions.CallbackUrl ?? throw new ArgumentException("CallbackUrl is required.", nameof(webAuthenticatorOptions));
		if (!SailfishOpenUrl.Enabled)
			throw new InvalidOperationException(
				$"The callback scheme '{callback.Scheme}' is not registered: add <SailfishUrlSchemes>{callback.Scheme}</SailfishUrlSchemes> to the app project.");

		var pending = new TaskCompletionSource<Uri>(TaskCreationOptions.RunContinuationsAsynchronously);
		Interlocked.Exchange(ref _pending, pending)?.TrySetCanceled();
		SailfishOpenUrl.Intercept = uri =>
		{
			if (!IsCallback(uri, callback))
				return false;
			SailfishOpenUrl.Intercept = null;
			pending.TrySetResult(uri);
			return true;
		};
		using var registration = cancellationToken.Register(() => pending.TrySetCanceled(cancellationToken));
		try
		{
			await OpenBrowser(url).ConfigureAwait(true);
			var result = await pending.Task.ConfigureAwait(true);
			return new WebAuthenticatorResult(result, webAuthenticatorOptions.ResponseDecoder);
		}
		finally
		{
			Interlocked.CompareExchange(ref _pending, null, pending);
			if (!pending.Task.IsCompletedSuccessfully)
				SailfishOpenUrl.Intercept = null;
		}
	}

	/// <summary>Same scheme, and the same host when the callback names one (myapp://auth).</summary>
	internal static bool IsCallback(Uri uri, Uri callback) =>
		string.Equals(uri.Scheme, callback.Scheme, StringComparison.OrdinalIgnoreCase) &&
		(string.IsNullOrEmpty(callback.Host) || string.Equals(uri.Host, callback.Host, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Contacts from the device address book (qtcontacts-sqlite through <c>org.nemomobile.contacts</c>); the picker is Silica's
/// <c>ContactSelectPage</c> (back = cancel, null). A sandboxed app declares the <c>Contacts</c> Sailjail permission
/// (<c>&lt;SailfishPermissions&gt;Contacts&lt;/SailfishPermissions&gt;</c>), as Android needs READ_CONTACTS. Platform limit:
/// the user's address book is privileged data that Sailfish OS opens only to system apps, so a third-party app reads
/// the non-privileged store, normally empty (see docs/porting-existing-apps.md).
/// </summary>
public sealed class SailfishContacts : IContacts
{
	private const string Service = "contacts";
	private TaskCompletionSource<JsonElement?>? _pick;
	private TaskCompletionSource<bool>? _populated;
	private bool _subscribed;

	private const string Qml = """
		import QtQuick 2.6
		import Sailfish.Silica 1.0
		import Sailfish.Contacts 1.0
		import org.nemomobile.contacts 1.0
		QtObject {
		    property PeopleModel all: PeopleModel {
		        filterType: PeopleModel.FilterAll
		        onPopulatedChanged: if (populated) window.mauiAppNotify("svc-contacts-populated", "{}")
		    }
		    property Component select: Component { ContactSelectPage {} }
		    function strings(list, key) {
		        var out = [];
		        for (var i = 0; list && i < list.length; ++i) {
		            var v = list[i];
		            if (v && typeof v === "object") v = v[key];
		            if (v) out.push(String(v));
		        }
		        return out;
		    }
		    function json(p, phones, emails) {
		        return { id: String(p.id), prefix: p.namePrefix, first: p.firstName, middle: p.middleName,
		                 last: p.lastName, suffix: p.nameSuffix, display: p.displayLabel,
		                 phones: phones, emails: emails };
		    }
		    function dump() {
		        var out = [];
		        for (var i = 0; i < all.count; ++i)
		            out.push(json(all.personByRow(i), strings(all.get(i, PeopleModel.PhoneNumbersRole)),
		                          strings(all.get(i, PeopleModel.EmailAddressesRole))));
		        return JSON.stringify(out);
		    }
		    function report(contact) {
		        window.mauiAppNotify("svc-contacts-picked", JSON.stringify({ contact: contact }));
		    }
		    function pick() {
		        var page = pageStack.push(select);
		        var done = false;
		        var seenActive = false;
		        page.contactClicked.connect(function(contact) {
		            if (done) return;
		            done = true;
		            function send() {
		                report(json(contact, strings(contact.phoneDetails, "number"), strings(contact.emailDetails, "address")));
		            }
		            if (contact.complete) send();
		            else { contact.completeChanged.connect(send); contact.ensureComplete(); }
		            pageStack.pop();
		        });
		        page.statusChanged.connect(function() {
		            if (page.status === PageStatus.Active) seenActive = true;
		            else if (page.status === PageStatus.Inactive && seenActive && !done) { done = true; report(null); }
		        });
		        return "ok";
		    }
		}
		""";

	public Task<Contact?> PickContactAsync()
	{
		EnsureAllowed();
		EnsureService();
		Interlocked.Exchange(ref _pick, null)?.TrySetResult(null);   // a new pick supersedes an open one
		var pick = new TaskCompletionSource<JsonElement?>(TaskCreationOptions.RunContinuationsAsynchronously);
		_pick = pick;
		var result = QtHostServices.Eval(Service, "s.pick()");
		if (result != "ok")
		{
			_pick = null;
			throw new FeatureNotSupportedException($"The Sailfish contact picker could not open: {result}");
		}
		return pick.Task.ContinueWith(t => t.Result is { } c ? ToContact(c) : null, TaskScheduler.Default);
	}

	public async Task<IEnumerable<Contact>> GetAllAsync(CancellationToken cancellationToken = default)
	{
		EnsureAllowed();
		EnsureService();
		if (QtHostServices.Eval(Service, "s.all.populated") != "true")
		{
			var populated = _populated ??= new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
			await populated.Task.WaitAsync(cancellationToken).ConfigureAwait(true);
		}
		var json = QtHostServices.Eval(Service, "s.dump()");
		QtHostDiag.Trace(QtHostDiagChannel.Lifecycle, $"contacts: {json.Length} chars from the address book");
		return Parse(json);
	}

	private void EnsureService()
	{
		if (!QtHostServices.Ensure(Service, Qml))
			throw new FeatureNotSupportedException("The Sailfish contacts components (Sailfish.Contacts, org.nemomobile.contacts) are not available.");
		if (_subscribed)
			return;
		_subscribed = true;
		QtHostServices.Subscribe("svc-contacts-populated", _ => _populated?.TrySetResult(true));
		QtHostServices.Subscribe("svc-contacts-picked", e =>
		{
			var pick = Interlocked.Exchange(ref _pick, null);
			pick?.TrySetResult(e.TryGetProperty("contact", out var c) && c.ValueKind == JsonValueKind.Object ? c.Clone() : null);
		});
	}

	// A sandboxed app needs the Contacts permission for the contacts D-Bus names and directories at all. The user's
	// address book still stays closed: it lives in the privileged data directory (privileged:privileged, 0770), and
	// firejail's "privileged-data Contacts" only mounts it; reading it takes the privileged group, which system apps get
	// (the Privileged permission plus a mapplauncherd privileges.d entry, e.g. jolla-contacts). Every other app,
	// sandboxed or not, gets qtcontacts-sqlite's non-privileged store, which does not hold the user's contacts.
	private static void EnsureAllowed() =>
		SailfishPermissions.Demand(typeof(Permissions.ContactsRead), "Reading contacts");

	internal static IEnumerable<Contact> Parse(string json)
	{
		if (string.IsNullOrEmpty(json))
			return Array.Empty<Contact>();
		using var doc = JsonDocument.Parse(json);
		return doc.RootElement.EnumerateArray().Select(ToContact).ToList();
	}

	internal static Contact ToContact(JsonElement c)
	{
		string Text(string name) => c.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : string.Empty;
		IEnumerable<string> List(string name) => c.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
			? v.EnumerateArray().Select(x => x.GetString() ?? string.Empty).Where(x => x.Length > 0).ToList()
			: Array.Empty<string>();
		var display = Text("display");
		return new Contact(Text("id"), Text("prefix"), Text("first"), Text("middle"), Text("last"), Text("suffix"),
			List("phones").Select(p => new ContactPhone(p)).ToList(),
			List("emails").Select(e => new ContactEmail(e)).ToList(),
			display.Length > 0 ? display : null!);
	}
}
