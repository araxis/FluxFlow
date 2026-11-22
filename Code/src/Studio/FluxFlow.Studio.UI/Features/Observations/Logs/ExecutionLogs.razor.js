export function create(viewport, receiver, following) {
    let disposed = false;
    let version;
    let layout = viewport.dataset.logLayout;
    let top = viewport.scrollTop;
    let height = viewport.scrollHeight;
    let clientHeight = viewport.clientHeight;

    function rememberPosition() {
        top = viewport.scrollTop;
        height = viewport.scrollHeight;
        clientHeight = viewport.clientHeight;
    }
    function pause() {
        if (!following || disposed) return;
        following = false;
        receiver.invokeMethodAsync("PauseFollowing").catch(() => {});
    }
    function scroll() {
        // Sorting and filtering can reposition the grid before the render callback reaches us.
        if (viewport.dataset.logLayout !== layout) { rememberPosition(); return; }
        // Filtering or enlarging the panel can clamp the position without user input.
        if (Math.abs(viewport.scrollTop - top) > 1 && viewport.scrollHeight >= height && viewport.clientHeight <= clientHeight) pause();
        rememberPosition();
    }
    function wheel(event) { if (event.deltaY !== 0) pause(); }
    function keydown(event) {
        if (event.target === viewport && ["ArrowUp", "ArrowDown", "PageUp", "PageDown", "Home", "End"].includes(event.key)) pause();
    }
    function jump() {
        const latest = viewport.querySelector('[data-log-latest="true"]');
        if (!latest) return;
        const row = latest.closest('tr').getBoundingClientRect();
        const bounds = viewport.getBoundingClientRect();
        const headerHeight = viewport.querySelector('thead')?.getBoundingClientRect().height ?? 0;
        if (row.top < bounds.top + headerHeight) viewport.scrollTop += row.top - bounds.top - headerHeight;
        else if (row.bottom > bounds.bottom) viewport.scrollTop += row.bottom - bounds.bottom;
        rememberPosition();
    }
    viewport.addEventListener("scroll", scroll, { passive: true });
    viewport.addEventListener("wheel", wheel, { passive: true });
    viewport.addEventListener("keydown", keydown);

    return {
        update(live, visibleVersion) {
            if (disposed) return;
            // Observe manual movement even when its scroll event is still queued.
            scroll();
            if (following && live && version !== visibleVersion) jump();
            version = visibleVersion;
            layout = viewport.dataset.logLayout;
            rememberPosition();
        },
        setFollowing(value) {
            if (disposed) return;
            following = value;
            if (following) jump();
        },
        jumpToLatest() {
            if (!disposed) jump();
        },
        dispose() {
            disposed = true;
            viewport.removeEventListener("scroll", scroll);
            viewport.removeEventListener("wheel", wheel);
            viewport.removeEventListener("keydown", keydown);
        }
    };
}
