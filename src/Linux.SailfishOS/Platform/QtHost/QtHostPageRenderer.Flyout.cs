using System.Runtime.CompilerServices;
using Microsoft.Maui.Controls;

namespace Microsoft.Maui.SailfishOS.Platform.QtHost;

// Shell.FlyoutIsPresented (tracker S23, D15 a): on Sailfish the flyout is the pull-down menu, which no API opens from
// code. Presenting it from code opens the same entries as a Silica ContextMenu under the page header; closing the menu
// (a pick or a tap outside) writes FlyoutIsPresented back to false.
internal sealed partial class QtHostPageRenderer
{
	private Shell? _shellFlyoutOpen;                                             // the Shell whose entries the menu shows
	private List<(string Text, bool Enabled, Action Activate)>? _shellFlyoutRows;   // its rows, by QML index (kept for a late pick)
	private bool _shellFlyoutClosing;                                            // asked to close; "context-closed" follows
	private static readonly ConditionalWeakTable<Shell, object> FlyoutContentWarned = new();
	private static readonly ConditionalWeakTable<Shell, object> FlyoutEmptyWarned = new();

	/// <summary>Shell flyout members the pulley (text entries) does not show; set ones are reported once per Shell.</summary>
	private static readonly BindableProperty[] FlyoutUnsupported =
	{
		Shell.FlyoutHeaderProperty, Shell.FlyoutHeaderTemplateProperty, Shell.FlyoutFooterProperty,
		Shell.FlyoutFooterTemplateProperty, Shell.FlyoutContentProperty, Shell.FlyoutContentTemplateProperty,
		Shell.ItemTemplateProperty, Shell.MenuItemTemplateProperty, Shell.FlyoutBackgroundProperty,
		Shell.FlyoutBackgroundColorProperty, Shell.FlyoutBackgroundImageProperty, Shell.FlyoutIconProperty,
		Shell.FlyoutWidthProperty, Shell.FlyoutHeightProperty, Shell.FlyoutBackdropProperty,
	};

	/// <summary>A context menu is open (a ContextFlyout or the presented Shell flyout): native stack steps wait.</summary>
	private bool AnyMenuOpen => _openFlyout is not null || _shellFlyoutOpen is not null;

	/// <summary>The page needs the context-menu host for the presented Shell flyout.</summary>
	private bool ShellFlyoutWantsMenu => _shellFlyoutOpen is not null || RootPage() is Shell { FlyoutIsPresented: true };

	private static void WarnUnsupportedFlyout(Shell shell)
	{
		if (FlyoutContentWarned.TryGetValue(shell, out _))
			return;
		FlyoutContentWarned.Add(shell, FlyoutContentWarned);
		var set = FlyoutUnsupported.Where(shell.IsSet).Select(p => p.PropertyName).ToList();
		if (set.Count > 0)
			QtHostDiag.Warn(QtHostDiagChannel.Navigation,
				$"Shell: {string.Join(", ", set)} not shown on Sailfish (the flyout is the pull-down menu: one text entry per item)");
	}

	/// <summary>After a pass: FlyoutIsPresented true opens the menu (once its host exists); false closes it.</summary>
	private void SyncShellFlyoutMenu(Page page)
	{
		var shell = RootPage() as Shell;
		if (shell is not null)
			WarnUnsupportedFlyout(shell);
		if (_shellFlyoutOpen is { } open)
		{
			if (!_shellFlyoutClosing && (!ReferenceEquals(open, shell) || !open.FlyoutIsPresented))
			{
				_shellFlyoutClosing = true;
				QtHostRuntime.Eval($"{TopModelPageJs}.__closeFlyoutMenu()");
				QtHostDiag.Trace(QtHostDiagChannel.Input, "Shell flyout closed from code");
			}
			return;
		}
		if (shell is not { FlyoutIsPresented: true } || _openFlyout is not null)
			return;
		var entries = FlyoutEntries(page).ToList();
		if (entries.Count == 0)
		{
			// Disabled flyout, a modal on top: nothing to present, as FlyoutIsPresented does nothing elsewhere then.
			if (!FlyoutEmptyWarned.TryGetValue(shell, out _))
			{
				FlyoutEmptyWarned.Add(shell, FlyoutEmptyWarned);
				QtHostDiag.Warn(QtHostDiagChannel.Navigation, "Shell.FlyoutIsPresented = true with no flyout entries on this page: ignored");
			}
			shell.FlyoutIsPresented = false;
			return;
		}
		var rc = QtHostRuntime.Eval($"{TopModelPageJs}.__openFlyoutMenu({BridgeValue.Quote(MenuEntriesJson(entries))})");
		if (rc != "true")
		{
			QtHostDiag.Trace(QtHostDiagChannel.Input, $"Shell flyout menu not open yet (rc={rc}); the next pass retries");
			return;
		}
		_shellFlyoutOpen = shell;
		_shellFlyoutRows = entries;
		_openFlyoutRows = null;
		QtHostDiag.Trace(QtHostDiagChannel.Input, $"Shell.FlyoutIsPresented → Silica ContextMenu with {entries.Count} entries");
	}

	/// <summary>The presented flyout's menu closed: FlyoutIsPresented back to false. True when it was that menu.</summary>
	private bool CloseShellFlyoutMenu()
	{
		if (_shellFlyoutOpen is not { } shell)
			return false;
		_shellFlyoutOpen = null;
		_shellFlyoutClosing = false;
		if (shell.FlyoutIsPresented)
			shell.FlyoutIsPresented = false;
		QtHostDiag.Trace(QtHostDiagChannel.Input, "Shell flyout menu closed");
		RequestPoll();
		return true;
	}

	/// <summary>A pick in the presented flyout's menu → its entry (a Shell item, a MenuItem). False when that menu
	/// was not the last one opened.</summary>
	private bool ActivateShellFlyoutRow(int index)
	{
		if (_shellFlyoutRows is not { } rows)
			return false;
		if (index < 0 || index >= rows.Count)
		{
			QtHostDiag.Warn(QtHostDiagChannel.Input, $"flyout menu pick {index} out of range ({rows.Count})");
			return true;
		}
		ContextMenuActivations++;
		QtHostDiag.Trace(QtHostDiagChannel.Input, $"flyout menu item {index} ('{rows[index].Text}') → MAUI");
		rows[index].Activate();
		return true;
	}
}
