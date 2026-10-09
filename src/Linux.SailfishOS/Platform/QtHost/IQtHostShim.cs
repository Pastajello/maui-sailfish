namespace Microsoft.Maui.SailfishOS.Platform.QtHost;

/// <summary>
/// Test seam for the native calls of <see cref="QtHostRuntime"/>, installed via
/// <see cref="QtHostRuntime.TestShim"/>. Production calls the shim library directly.
/// </summary>
internal interface IQtHostShim
{
	string Eval(string expression);

	/// <summary>sailfish_host_invoke: <paramref name="method"/>(<paramref name="arg"/>) on the object behind the
	/// handle; the result, or null with <paramref name="rc"/> negative when the call failed.</summary>
	string? Invoke(long handle, string method, string? arg, out int rc);
	long FindObject(string objectName);
	int SetProperty(long handle, string name, string? valueJson);
	int ApplyProperties(long handle, string propsJson);
	string GetProperty(long handle, string name);
	bool TryItemGeometry(long handle, out NativeGeometry geometry);
	bool SetParentItem(long handle, long parent);
	int ApplyGeometry(string geoJson);
	bool TryMeasureText(string json, out double widthPx, out double heightPx);
	string ScreenInfo();

	/// <summary>sailfish_host_image_info: the decoded size of encoded image bytes; false when they do not decode.</summary>
	bool TryImageInfo(byte[] data, out int width, out int height);

	/// <summary>sailfish_host_image_transform: the bytes re-encoded after the op (JSON {w,h,mode,format,quality}); null
	/// when they do not decode or the format cannot be written.</summary>
	byte[]? ImageTransform(byte[] data, string opJson);
	void DestroyObject(long handle);
	int PushPage(string qmlPath, string? propsJson);
	int PopPage();
	void Post(Action action);

	/// <summary>A drawing-surface commit (pixels are only valid during the call).</summary>
	int SurfaceCommit(long handle, IntPtr pixels, int width, int height, int stride) => QtHostRuntime.SfhostOk;

	/// <summary>Surface touch on/off; the test delivers touches with <see cref="QtHostSurface.DeliverTouch"/>.</summary>
	int SurfaceSetTouch(long handle, bool enabled) => QtHostRuntime.SfhostOk;

	/// <summary>Asks for a frame; the test raises it with <see cref="QtHostSurface.RunFrame"/>.</summary>
	void RequestFrame()
	{
	}
}
