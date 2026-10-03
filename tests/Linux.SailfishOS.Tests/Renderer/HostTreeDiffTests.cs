using Microsoft.Maui.Controls;
using Microsoft.Maui.SailfishOS.Platform.QtHost;
using Xunit;

namespace Linux.SailfishOS.Tests.Renderer;

/// <summary>
/// The host-tree diff bookkeeping both reconciles share (the page reconcile and a container's subtree reconcile):
/// QML child order before the batch, destroy / reparent / create / order ops and AppliedParentId. No renderer, no shim.
/// </summary>
public class HostTreeDiffTests
{
	private static NativeElementHost Host(string id, NativeElementHost? parent = null, string? appliedParent = null)
	{
		var host = new NativeElementHost(id, "content-view", new Label()) { Parent = parent };
		host.AppliedParentId = appliedParent;
		return host;
	}

	private static Dictionary<string, object?> Create(NativeElementHost host, Dictionary<string, object?> props, string parent) =>
		new() { ["op"] = "create", ["id"] = host.Id, ["parent"] = parent };

	private static string Describe(Dictionary<string, object?> op) => op["op"] switch
	{
		"order" => $"order {op["parent"]}:[{string.Join(",", (List<string>)op["ids"]!)}]",
		"reparent" => $"reparent {op["id"]}->{op["parent"]}",
		"create" => $"create {op["id"]}@{op["parent"]}",
		_ => $"{op["op"]} {op["id"]}",
	};

	[Fact]
	public void A_removed_host_is_destroyed_a_new_one_created_and_the_order_settled()
	{
		var a = Host("a", appliedParent: "");
		var b = Host("b", appliedParent: "");
		var c = Host("c", appliedParent: "");
		var d = Host("d");
		var diff = new HostTreeDiff([a, b, c]);

		diff.Destroy(b);
		diff.Create(d, new(), Create);
		var reordered = diff.Order([a, d, c]);

		Assert.True(reordered);
		Assert.Equal(["destroy b", "create d@", "order :[a,d,c]"], diff.Ops.Select(Describe));
		Assert.Equal([b], diff.Destroyed);
		Assert.Equal([d], diff.Created);
		Assert.Null(b.AppliedParentId);
		Assert.Equal("", d.AppliedParentId);
	}

	[Fact]
	public void A_survivor_that_changed_container_is_reparented_once()
	{
		var box = Host("box", appliedParent: "");
		var moved = Host("m", parent: box, appliedParent: "");
		var diff = new HostTreeDiff([box, moved]);

		Assert.True(diff.Reparent(moved));
		Assert.False(diff.Reparent(moved));     // already where it wants to be
		Assert.False(diff.Reparent(box));
		Assert.False(diff.Order([box, moved]));  // appending reproduced the order: no order op

		Assert.Equal(["reparent m->box"], diff.Ops.Select(Describe));
		Assert.Equal("box", moved.AppliedParentId);
	}

	[Fact]
	public void Children_appended_in_the_desired_order_need_no_order_op()
	{
		var root = Host("root", appliedParent: "");
		var x = Host("x", parent: root);
		var y = Host("y", parent: root);
		var diff = new HostTreeDiff([root]);

		diff.Create(x, new(), Create);
		diff.Create(y, new(), Create);

		Assert.False(diff.Order([root, x, y]));
		Assert.Equal(["create x@root", "create y@root"], diff.Ops.Select(Describe));
	}

	[Fact]
	public void The_child_order_basis_is_the_native_one_not_the_managed_one()
	{
		// Native order b, a under the canvas (AppliedParentId), desired a, b: one order op, nothing else.
		var b = Host("b", appliedParent: "");
		var a = Host("a", appliedParent: "");
		var diff = new HostTreeDiff([b, a]);

		Assert.True(diff.Order([a, b]));
		Assert.Equal(["order :[a,b]"], diff.Ops.Select(Describe));
	}
}
