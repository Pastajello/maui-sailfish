namespace Microsoft.Maui.SailfishOS.Platform.QtHost;

/// <summary>
/// Test seam for the native calls of <see cref="QtHostRuntime"/>, installed via
/// <see cref="QtHostRuntime.TestShim"/>. Production calls the shim library directly.
/// </summary>
internal interface IQtHostShim
{
	string Eval(string expression);
	long FindObject(string objectName);
	int SetProperty(long handle, string name, string? valueJson);
	int ApplyProperties(long handle, string propsJson);
	string GetProperty(long handle, string name);
	bool TryItemGeometry(long handle, out NativeGeometry geometry);
	bool SetParentItem(long handle, long parent);
	int ApplyGeometry(string geoJson);
	bool TryMeasureText(string json, out double widthPx, out double heightPx);
	string ScreenInfo();
	void DestroyObject(long handle);
	int PushPage(string qmlPath, string? propsJson);
	int PopPage();
	void Post(Action action);
}
