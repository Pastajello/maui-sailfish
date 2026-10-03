import QtQuick 2.6
import Sailfish.Silica 1.0
import Sailfish.WebView 1.0
import Sailfish.WebEngine 1.0

// Adapter: MAUI WebView -> Sailfish WebView (Gecko).
// Named MauiWebView.qml because a local WebView.qml would shadow the imported type.
// Contract: mauiId / mauiProbe / mauiEvent — see controls/Label.qml.
// A sandboxed (Sailjail) app needs the WebView permission. Commands (mauiCommand): nav {action}, js {req, script}.
// Events: webview-navigating, webview-navigated, webview-js.
WebView {
    id: root

    property string mauiId: ""
    property string mauiProbe: url + (loading ? " loading" : "")
    signal mauiEvent(string name, string payload)

    // Adapter events are suppressed while managed pushes are applied (no echo loop).
    property bool mauiApplying: false

    // UserAgent sets the HTTP header and navigator.userAgent; the latter is an
    // engine-wide Gecko preference, so it is re-applied before every load.
    property string mauiUserAgent: ""
    Binding { target: root; property: "httpUserAgent"; value: root.mauiUserAgent; when: root.mauiUserAgent.length > 0 }
    function __applyUserAgent() {
        if (mauiUserAgent.length > 0)
            WebEngineSettings.setPreference("general.useragent.override", mauiUserAgent);
    }
    onMauiUserAgentChanged: __applyUserAgent()
    property string mauiUrl: ""
    property string mauiHtml: ""
    property string mauiBaseUrl: ""
    property int mauiSourceId: 0   // identity of the MAUI source: a new one loads again, even with the same URL

    active: true

    function __load() {
        __applyUserAgent();
        if (mauiHtml.length > 0)
            loadHtml(mauiHtml, mauiBaseUrl);
        else if (mauiUrl.length > 0)
            url = mauiUrl;
    }
    Component.onCompleted: __load()
    onMauiSourceIdChanged: __load()

    onLoadingChanged: {
        if (loading)
            mauiEvent("webview-navigating", JSON.stringify({ id: mauiId, url: url.toString() }));
        else
            mauiEvent("webview-navigated", JSON.stringify({
                id: mauiId, url: url.toString(), back: canGoBack, fwd: canGoForward }));
    }

    // Managed commands (QtHostRuntime.Invoke "mauiCommand"): one call per request.
    function mauiCommand(json) {
        var c = JSON.parse(json);
        if (c.name === "nav") {
            if (c.action === "back") goBack();
            else if (c.action === "forward") goForward();
            else if (c.action === "reload") reload();
        } else if (c.name === "js") {
            __runJs(c.req, c.script);
        }
    }

    // Gecko runs scripts as a function body, dropping the completion value MAUI
    // expects, so it runs as `return eval(script)` with the raw script as fallback.
    function __runJs(req, script) {
        function done(result) {
            mauiEvent("webview-js", JSON.stringify({ id: mauiId, req: req, ok: true,
                result: result === undefined || result === null ? "null" : String(result) }));
        }
        function failed(error) {
            mauiEvent("webview-js", JSON.stringify({ id: mauiId, req: req, ok: false, error: String(error) }));
        }
        runJavaScript("return eval(" + JSON.stringify(script) + ");", done,
                      function(error) { runJavaScript(script, done, failed); });
    }
}
