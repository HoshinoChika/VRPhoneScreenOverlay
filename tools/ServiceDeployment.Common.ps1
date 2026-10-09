Set-StrictMode -Version Latest

function Assert-ServiceBinary {
    param([Parameter(Mandatory = $true)][string]$Path)

    $stream = [IO.File]::OpenRead($Path)
    try {
        $header = New-Object byte[] 64
        if ($stream.Read($header, 0, $header.Length) -ne 64 -or
            $header[0] -ne 127 -or $header[1] -ne 69 -or
            $header[2] -ne 76 -or $header[3] -ne 70 -or
            $header[4] -ne 2 -or $header[5] -ne 1 -or
            $header[18] -ne 62 -or $header[19] -ne 0) {
            throw 'The service artifact must be a Linux x64 ELF executable.'
        }
    } finally {
        $stream.Dispose()
    }
}

function ConvertTo-ServiceShellLiteral {
    param([Parameter(Mandatory = $true)][string]$Value)
    if ($Value.IndexOfAny([char[]]@([char]0, [char]10, [char]13)) -ge 0) {
        throw 'Remote arguments cannot contain control line separators.'
    }
    $singleQuote = [string][char]39
    $doubleQuote = [string][char]34
    $escapedQuote = $singleQuote + $doubleQuote + $singleQuote + $doubleQuote + $singleQuote
    return $singleQuote + $Value.Replace($singleQuote, $escapedQuote) + $singleQuote
}

function New-ServiceDeploymentScript {
    param(
        [Parameter(Mandatory = $true)][string]$Token,
        [Parameter(Mandatory = $true)][string]$ExpectedHash,
        [Parameter(Mandatory = $true)][string]$PublicHealthUri,
        [string]$PrivateHealthUri = 'http://127.0.0.1:8080/vrphonescreen/api/v1/health',
        [string]$ServiceName = 'vrphonescreen.service',
        [string]$InstallDirectory = '/opt/vrphonescreen-service'
    )
    if ($Token -notmatch '^[0-9a-f]{32}$' -or $ExpectedHash -notmatch '^[0-9a-f]{64}$') {
        throw 'Deployment token/hash is invalid.'
    }
    if ($ServiceName -notmatch '^[A-Za-z0-9_.@-]+\.service$' -or
        $InstallDirectory -notmatch '^/opt/[A-Za-z0-9_-]+$') {
        throw 'Deployment must target one named service directory under /opt.'
    }
    $uri = [Uri]$PublicHealthUri
    $private = [Uri]$PrivateHealthUri
    if (-not $private.IsAbsoluteUri -or $private.Host -ne '127.0.0.1' -or
        $private.Scheme -ne 'http' -or $private.UserInfo -or $private.Query -or $private.Fragment) {
        throw 'Private health URI must target the configured local HTTP listener.'
    }
    if (-not $uri.IsAbsoluteUri -or $uri.Scheme -ne 'https' -or
        -not [string]::IsNullOrEmpty($uri.UserInfo) -or
        $uri.AbsolutePath -ne '/vrphonescreen/api/v1/health' -or
        -not [string]::IsNullOrEmpty($uri.Query) -or
        -not [string]::IsNullOrEmpty($uri.Fragment)) {
        throw 'Public health URI must be the HTTPS health endpoint.'
    }

    $template = @'
#!/usr/bin/env bash
set -euo pipefail
umask 077
install_dir=__INSTALL__
service=__SERVICE__
token=__TOKEN__
expected=__HASH__
public_health=__PUBLIC__
private_health=__PRIVATE__
listen_uri=__LISTEN__
deployment="$install_dir/.service-deploy-$token"
current="$install_dir/VRPhoneScreenOverlay.Service"
candidate="$deployment/VRPhoneScreenOverlay.Service.upload"
backup="$deployment/previous"
replacement="$install_dir/.service-new-$token"
dropin_dir="/etc/systemd/system/$service.d"
dropin="$dropin_dir/20-vrphone-bundle-extraction.conf"
mode="${1:-apply}"
changed=0

test -d "$install_dir" && test ! -L "$install_dir"
test -d "$deployment" && test ! -L "$deployment"
test -f "$current" && test ! -L "$current"
exec 9>"$install_dir/.service-deploy.lock"
flock -n 9 || { echo 'Another service deployment is running.' >&2; exit 20; }

hash_of() { sha256sum -- "$1" | cut -d ' ' -f 1; }
configuration_hash() { systemctl cat "$service" | sha256sum | cut -d ' ' -f 1; }
probe() {
    response=$(curl --fail --silent --show-error --connect-timeout 2 --max-time 3 "$1") || return 1
    printf '%s' "$response" | grep -Eq '"ok"[[:space:]]*:[[:space:]]*true'
}
wait_private_health() {
    attempt=0
    while test "$attempt" -lt 10; do
        if systemctl is-active --quiet "$service" && probe "$private_health"; then
            return 0
        fi
        attempt=$((attempt + 1))
        sleep 1
    done
    return 1
}
verify_running_hash() {
    pid=$(systemctl show "$service" --property=MainPID --value)
    case "$pid" in ''|*[!0-9]*) return 1;; esac
    test "$pid" -gt 0 && test "$(hash_of "/proc/$pid/exe")" = "$1"
}
rollback() {
    test -f "$backup" && test ! -L "$backup" || return 1
    old_hash=$(cat "$deployment/previous.sha256")
    test "$(hash_of "$backup")" = "$old_hash" || return 1
    live_hash=$(hash_of "$current")
    if test "$live_hash" != "$expected" && test "$live_hash" != "$old_hash"; then
        echo 'Rollback refused: installed binary changed outside this transaction.' >&2
        return 1
    fi
    if test -e "$dropin" || test -L "$dropin"; then
        test -f "$dropin" && test ! -L "$dropin" || return 1
        dropin_hash=$(hash_of "$dropin")
        new_dropin_hash=$(hash_of "$deployment/new-dropin")
        previous_dropin_hash='absent'
        if test -f "$deployment/previous-dropin"; then
            previous_dropin_hash=$(hash_of "$deployment/previous-dropin")
        fi
        if test "$dropin_hash" != "$new_dropin_hash" && test "$dropin_hash" != "$previous_dropin_hash"; then
            echo 'Rollback refused: managed drop-in changed outside this transaction.' >&2
            return 1
        fi
    fi
    if test "$(cat "$deployment/previous-dropin-state")" = present; then
        cp -p -- "$deployment/previous-dropin" "$dropin_dir/.bundle-restore-$token" || return 1
        mv -f -- "$dropin_dir/.bundle-restore-$token" "$dropin" || return 1
    else
        rm -f -- "$dropin" || return 1
    fi
    timeout 45s systemctl daemon-reload || return 1
    cp -p -- "$backup" "$replacement.rollback" || return 1
    mv -f -- "$replacement.rollback" "$current" || return 1
    timeout 45s systemctl restart "$service" || return 1
    wait_private_health && verify_running_hash "$old_hash" || return 1
    test "$(configuration_hash)" = "$(cat "$deployment/configuration.sha256")" || return 1
    printf '%s\n' 'rolled-back' > "$deployment/state"
    echo "SERVICE_ROLLBACK_OK $old_hash"
}
on_exit() {
    result=$?
    trap - EXIT HUP INT TERM
    if test "$result" -ne 0 && test "$changed" -eq 1; then
        if ! rollback; then
            echo "SERVICE_ROLLBACK_FAILED: retained backup in $deployment" >&2
            exit 71
        fi
    fi
    exit "$result"
}
trap on_exit EXIT
trap 'exit 129' HUP
trap 'exit 130' INT
trap 'exit 143' TERM

case "$mode" in
    apply)
        test -f "$candidate" && test ! -L "$candidate"
        test "$(hash_of "$candidate")" = "$expected" || { echo 'Uploaded service hash mismatch.' >&2; exit 21; }
        test ! -e "$backup" || { echo 'This deployment token was already used.' >&2; exit 22; }
        test ! -L "$dropin_dir"
        if test -e "$dropin_dir"; then test -d "$dropin_dir"; fi
        configuration_hash > "$deployment/configuration.sha256"
        if test -e "$dropin" || test -L "$dropin"; then
            test -f "$dropin" && test ! -L "$dropin"
            cp -p -- "$dropin" "$deployment/previous-dropin"
            printf '%s\n' present > "$deployment/previous-dropin-state"
        else
            printf '%s\n' absent > "$deployment/previous-dropin-state"
        fi
        old_hash=$(hash_of "$current")
        printf '%s\n' "$old_hash" > "$deployment/previous.sha256"
        cp -p -- "$current" "$backup"
        test "$(hash_of "$backup")" = "$old_hash"
        install -m 0755 -- "$candidate" "$replacement"
        test "$(hash_of "$replacement")" = "$expected"
        printf '%s\n' '[Service]' 'Environment="DOTNET_BUNDLE_EXTRACT_BASE_DIR=/var/lib/vrphonescreen-service/.dotnet-bundle"' "Environment=\"VRPSO_SERVICE_URL=$listen_uri\"" > "$deployment/new-dropin"
        # Rollback becomes active before touching configuration, not just binary.
        # The service itself creates its extraction directory inside StateDirectory.
        # Existing unit/drop-ins and DATA_ROOT permissions are never rewritten.
        changed=1
        if test ! -d "$dropin_dir"; then mkdir -m 0755 -- "$dropin_dir"; fi
        install -m 0644 -- "$deployment/new-dropin" "$dropin_dir/.bundle-new-$token"
        mv -f -- "$dropin_dir/.bundle-new-$token" "$dropin"
        timeout 45s systemctl daemon-reload
        configuration_hash > "$deployment/applied-configuration.sha256"
        mv -f -- "$replacement" "$current"
        timeout 45s systemctl restart "$service"
        wait_private_health
        verify_running_hash "$expected"
        probe "$public_health"
        test "$(configuration_hash)" = "$(cat "$deployment/applied-configuration.sha256")"
        printf '%s\n' 'applied' > "$deployment/state"
        changed=0
        echo "SERVICE_DEPLOY_APPLIED $expected PREVIOUS=$old_hash"
        ;;
    verify)
        test "$(hash_of "$current")" = "$expected"
        wait_private_health
        verify_running_hash "$expected"
        test "$(configuration_hash)" = "$(cat "$deployment/applied-configuration.sha256")"
        echo "SERVICE_DEPLOY_VERIFIED $expected"
        ;;
    rollback)
        rollback || exit 71
        ;;
    *) echo 'Unknown deployment operation.' >&2; exit 2;;
esac
'@
    $replacements = @{
        '__INSTALL__' = (ConvertTo-ServiceShellLiteral $InstallDirectory)
        '__SERVICE__' = (ConvertTo-ServiceShellLiteral $ServiceName)
        '__TOKEN__' = (ConvertTo-ServiceShellLiteral $Token)
        '__HASH__' = (ConvertTo-ServiceShellLiteral $ExpectedHash)
        '__PUBLIC__' = (ConvertTo-ServiceShellLiteral $uri.AbsoluteUri)
        '__PRIVATE__' = (ConvertTo-ServiceShellLiteral $private.AbsoluteUri)
        '__LISTEN__' = (ConvertTo-ServiceShellLiteral $private.GetLeftPart([UriPartial]::Authority))
    }
    foreach ($entry in $replacements.GetEnumerator()) {
        $template = $template.Replace($entry.Key, $entry.Value)
    }
    return $template.Replace("`r`n", "`n") + "`n"
}
