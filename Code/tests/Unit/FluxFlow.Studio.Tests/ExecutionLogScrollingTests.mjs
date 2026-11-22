import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const source = await readFile(new URL('../../../src/Studio/FluxFlow.Studio.UI/Features/Observations/Logs/ExecutionLogs.razor.js', import.meta.url), 'utf8');
const { create } = await import(`data:text/javascript;base64,${Buffer.from(source).toString('base64')}`);

function setup() {
    const listeners = new Map();
    let position = 0;
    let pauses = 0;
    const viewport = {
        dataset: { logLayout: 'initial' },
        latestAtStart: false,
        hasLatest: true,
        scrollHeight: 1000,
        clientHeight: 200,
        get scrollTop() { return position; },
        set scrollTop(value) { position = Math.max(0, Math.min(value, this.scrollHeight - this.clientHeight)); },
        getBoundingClientRect() { return { top: 0, bottom: this.clientHeight }; },
        querySelector(selector) {
            if (selector === 'thead') return { getBoundingClientRect: () => ({ height: 30 }) };
            if (!this.hasLatest) return null;
            return { closest: () => ({ getBoundingClientRect: () => {
                const bottom = this.latestAtStart ? 60 : this.scrollHeight;
                return { top: bottom - 30 - position, bottom: bottom - position };
            } }) };
        },
        addEventListener(name, callback) { listeners.set(name, callback); },
        removeEventListener(name) { listeners.delete(name); },
        fire(name, values = {}) { listeners.get(name)?.({ target: viewport, ...values }); }
    };
    const controller = create(viewport, {
        invokeMethodAsync(name) {
            assert.equal(name, 'PauseFollowing');
            pauses++;
            return new Promise(() => {}); // Pause must work before the UI callback completes.
        }
    }, true);
    return { viewport, controller, pauses: () => pauses, listeners };
}

test('follows matching updates, but not repeated snapshots or stopped updates', () => {
    const { viewport, controller } = setup();
    controller.update(true, 'run:1');
    assert.equal(viewport.scrollTop, 800);
    viewport.scrollHeight = 1200;
    controller.update(true, 'run:1');
    assert.equal(viewport.scrollTop, 800);
    controller.update(true, 'run:2');
    assert.equal(viewport.scrollTop, 1000);
    viewport.scrollHeight = 1400;
    controller.update(false, 'run:3');
    assert.equal(viewport.scrollTop, 1000);
    controller.setFollowing(true);
    assert.equal(viewport.scrollTop, 1200);
});

test('upward wheel pauses immediately; jump preserves pause and Follow explicitly resumes', () => {
    const { viewport, controller, pauses } = setup();
    controller.update(true, 'run:1');
    viewport.fire('wheel', { deltaY: -50 });
    viewport.scrollTop = 400;
    viewport.fire('scroll');
    viewport.scrollHeight = 1200;
    controller.update(true, 'run:2');
    assert.equal(viewport.scrollTop, 400);
    assert.equal(pauses(), 1);
    controller.jumpToLatest();
    assert.equal(viewport.scrollTop, 1000);
    viewport.scrollHeight = 1300;
    controller.update(true, 'run:paused');
    assert.equal(viewport.scrollTop, 1000);
    controller.setFollowing(true);
    assert.equal(viewport.scrollTop, 1100);
    viewport.scrollHeight = 1400;
    controller.update(true, 'run:3');
    assert.equal(viewport.scrollTop, 1200);
});

test('scrollbar navigation pauses but filter shrink does not', () => {
    const { viewport, controller, pauses } = setup();
    controller.update(true, 'run:1');
    viewport.scrollHeight = 500;
    viewport.scrollTop = 300;
    viewport.fire('scroll');
    assert.equal(pauses(), 0);
    controller.update(true, 'filtered:1');
    viewport.scrollTop = 100;
    viewport.fire('scroll');
    assert.equal(pauses(), 1);
    viewport.scrollHeight = 1000;
    controller.update(true, 'filtered:2');
    assert.equal(viewport.scrollTop, 100);
});

test('a polling update cannot overwrite upward scrolling before the scroll event arrives', () => {
    const { viewport, controller, pauses } = setup();
    controller.update(true, 'run:1');
    viewport.scrollTop = 400;
    viewport.scrollHeight = 1200;
    controller.update(true, 'run:2');
    viewport.fire('scroll');
    assert.equal(pauses(), 1);
    assert.equal(viewport.scrollTop, 400);
});

test('keyboard navigation pauses for either direction only in the records viewport', () => {
    for (const key of ['ArrowUp', 'ArrowDown', 'PageUp', 'PageDown', 'Home', 'End']) {
        const { viewport, controller, pauses } = setup();
        controller.update(true, 'run:1');
        viewport.fire('keydown', { key, target: {} });
        viewport.fire('keydown', { key: 'ArrowRight' });
        assert.equal(pauses(), 0);
        viewport.fire('keydown', { key });
        assert.equal(pauses(), 1);
    }
});

test('sorting newest-first follows the latest row without mistaking grid movement for user scrolling', () => {
    const { viewport, controller, pauses } = setup();
    controller.update(true, 'run:1');
    viewport.dataset.logLayout = 'descending';
    viewport.latestAtStart = true;
    viewport.scrollTop = 0;
    viewport.fire('scroll');
    controller.update(true, 'sort:1');
    assert.equal(pauses(), 0);
    assert.equal(viewport.scrollTop, 0);
    viewport.fire('wheel', { deltaY: 50 });
    viewport.scrollTop = 400;
    viewport.fire('scroll');
    controller.jumpToLatest();
    assert.equal(viewport.scrollTop, 0);
    assert.equal(pauses(), 1);
    viewport.hasLatest = false;
    viewport.scrollTop = 200;
    controller.jumpToLatest();
    assert.equal(viewport.scrollTop, 200);
});

test('enlarging the panel does not mistake a clamped position for scrolling up', () => {
    const { viewport, controller, pauses } = setup();
    controller.update(true, 'run:1');
    viewport.clientHeight = 500;
    viewport.scrollTop = 500;
    viewport.fire('scroll');
    assert.equal(pauses(), 0);
    viewport.scrollHeight = 1200;
    controller.update(true, 'run:2');
    assert.equal(viewport.scrollTop, 700);
});

test('manual pause preserves position through Stop and rerun; disposal rejects late updates', () => {
    const { viewport, controller, pauses, listeners } = setup();
    controller.update(true, 'first:1');
    controller.setFollowing(false);
    viewport.scrollTop = 200;
    controller.update(false, 'first:2');
    controller.update(true, 'second:1');
    assert.equal(viewport.scrollTop, 200);
    controller.dispose();
    assert.equal(listeners.size, 0);
    controller.update(true, 'second:2');
    controller.setFollowing(true);
    viewport.fire('wheel', { deltaY: -1 });
    assert.equal(viewport.scrollTop, 200);
    assert.equal(pauses(), 0);
});
