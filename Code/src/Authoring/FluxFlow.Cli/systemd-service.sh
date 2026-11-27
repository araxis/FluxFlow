set -eu
PATH=/usr/sbin:/usr/bin:/sbin:/bin
export PATH
umask 022
fail() { echo "$*" >&2; exit 1; }
action=$1
name=$2
case "$name" in ''|[!a-zA-Z0-9]*|*[!a-zA-Z0-9_-]*) fail "Invalid Linux service name.";; esac
[ "${#name}" -le 63 ] || fail "Invalid Linux service name."
unit=fluxflow-$name.service
registration=/etc/systemd/system/$unit
root=/var/lib/fluxflow/services
deployment=$root/service-$name
marker="# FluxFlow managed service: $name"

# Serialize registration and removal, including rollback, for this name.
install -d -m 0755 /run/fluxflow-services
exec 9>"/run/fluxflow-services/$name.lock"
flock -x 9

if [ "$action" = install ]; then
    startup=$3
    case "$startup" in manual|automatic) ;; *) fail "Invalid startup mode.";; esac
    [ "$(systemctl show "$unit" -p LoadState --value)" = not-found ] &&
        [ ! -e "$registration" ] && [ ! -L "$registration" ] || fail "Service '$name' is already registered."
    install -d -m 0755 "$root"
    [ ! -e "$deployment" ] && [ ! -L "$deployment" ] || fail "The installation path already exists: $deployment"
    mkdir "$deployment"
    registered=false
    staged=
    cleanup() {
        if [ "$registered" = true ]; then
            systemctl disable "$unit" >/dev/null 2>&1 || :
            rm -f -- "$registration"
            systemctl daemon-reload
        fi
        if [ -n "$staged" ]; then rm -f -- "$staged"; fi
        rm -f -- "$deployment/App"
        rmdir -- "$deployment"
    }
    trap cleanup EXIT
    trap 'exit 130' INT TERM HUP
    cat > "$deployment/App"
    printf '%s  %s\n' "$4" "$deployment/App" | sha256sum --check --status || fail "Incomplete executable transfer."
    chmod 0755 "$deployment/App"
    staged=$(mktemp /etc/systemd/system/.fluxflow-XXXXXXXX)
    cat > "$staged" <<EOF
$marker
[Unit]
Description=FluxFlow $name

[Service]
Type=exec
DynamicUser=yes
StateDirectory=fluxflow-$name
WorkingDirectory=/var/lib/fluxflow-$name
ExecStart=$deployment/App

[Install]
WantedBy=multi-user.target
EOF
    # Publish atomically on the unit filesystem without replacing another registration.
    chmod 0644 "$staged"
    ln "$staged" "$registration"
    registered=true
    rm -- "$staged"
    systemctl daemon-reload
    if [ "$startup" = automatic ]; then systemctl enable "$unit"; fi
    trap - EXIT INT TERM HUP
    echo "Installed $unit at $deployment/App"
    exit 0
fi

[ -f "$registration" ] && [ ! -L "$registration" ] &&
    [ "$(head -n 1 -- "$registration")" = "$marker" ] &&
    [ "$(systemctl show "$unit" -p FragmentPath --value)" = "$registration" ] ||
    fail "FluxFlow service '$name' is not registered."

case "$action" in
    start) systemctl start "$unit"; exit 0;;
    stop|uninstall) ;;
    *) fail "Unknown service action.";;
esac
systemctl stop "$unit"
result=$(systemctl show "$unit" -p Result --value)
if [ "$result" != success ]; then
    status=$(systemctl show "$unit" -p ExecMainStatus --value)
    if [ "$status" = 2 ]; then echo "Shutdown incomplete: $name" >&2; exit 2; fi
    fail "Shutdown of '$name' failed ($result, status $status)."
fi
[ "$(systemctl show "$unit" -p ActiveState --value)" = inactive ] || fail "Stop of '$name' is unconfirmed."
if [ "$action" = uninstall ]; then
    systemctl disable "$unit"
    rm -- "$registration"
    systemctl daemon-reload
    [ "$(systemctl show "$unit" -p LoadState --value)" = not-found ] || fail "Removal of '$name' is unconfirmed."
    echo "Uninstalled $unit. Preserved $deployment."
fi
