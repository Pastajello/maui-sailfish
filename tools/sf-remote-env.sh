# shellcheck shell=sh
# Sourced on the device by the *-remote.sh helpers (sf_push_helper uploads it next to
# them): the session's runtime dir, Wayland display and session bus.

export XDG_RUNTIME_DIR="/run/user/$(id -u)"
[ -n "${WAYLAND_DISPLAY:-}" ] || export WAYLAND_DISPLAY=wayland-0

# Session bus: the first socket of bus, dbus/user_bus_socket, dbus/user_bus.
if [ -z "${DBUS_SESSION_BUS_ADDRESS:-}" ]; then
	for cand in "$XDG_RUNTIME_DIR/bus" "$XDG_RUNTIME_DIR/dbus/user_bus_socket" "$XDG_RUNTIME_DIR/dbus/user_bus"; do
		if [ -S "$cand" ]; then
			export DBUS_SESSION_BUS_ADDRESS="unix:path=$cand"
			break
		fi
	done
fi
