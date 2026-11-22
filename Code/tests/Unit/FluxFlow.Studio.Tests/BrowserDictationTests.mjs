import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import vm from 'node:vm';

const source = await readFile(new URL('../../../src/Studio/FluxFlow.DesignerApp/wwwroot/studio-dictation.js', import.meta.url), 'utf8');
function deferred() {
    let resolve;
    const promise = new Promise(done => resolve = done);
    return { promise, resolve };
}
function browser(permission = null, decode = null) {
    const tracks = [];
    const decoding = deferred();
    function stream() {
        const track = { stopped: false, stop() { this.stopped = true; } };
        tracks.push(track);
        return { getTracks: () => [track] };
    }
    class Recorder {
        static isTypeSupported() { return true; }
        constructor(_, options) { this.mimeType = options.mimeType; this.state = 'inactive'; }
        start() { this.state = 'recording'; }
        stop() {
            this.state = 'inactive';
            queueMicrotask(() => { this.ondataavailable({ data: new Blob(['audio']) }); this.onstop(); });
        }
    }
    class Reader {
        async readAsDataURL(blob) {
            this.result = 'data:audio/wav;base64,' + Buffer.from(await blob.arrayBuffer()).toString('base64');
            this.onload();
        }
    }
    const decoded = { length: 2, numberOfChannels: 1, getChannelData: () => new Float32Array([0, 1]) };
    const context = {
        window: {}, navigator: { mediaDevices: { getUserMedia: () => permission ? permission.promise : Promise.resolve(stream()) } },
        MediaRecorder: Recorder, Blob, FileReader: Reader, setTimeout: () => 1, clearTimeout: () => {},
        OfflineAudioContext: class { decodeAudioData() { decoding.resolve(); return decode?.promise ?? Promise.resolve(decoded); } }
    };
    context.window.MediaRecorder = Recorder;
    vm.runInNewContext(source, context);
    return { service: context.window.studioDictation, tracks, stream, decoded, decoding: decoding.promise };
}

test('cancel during microphone permission releases a late stream', async () => {
    const permission = deferred();
    const { service, tracks, stream } = browser(permission);
    const starting = service.start('old');
    service.cancel('old');
    permission.resolve(stream());
    await starting;
    assert.equal(tracks[0].stopped, true);
    await assert.rejects(service.stop('old'), /cancelled/);
});

test('stop releases capture and produces a mono 16 kHz WAV', async () => {
    const { service, tracks } = browser();
    await service.start('current');
    const result = JSON.parse(await service.stop('current'));
    assert.equal(tracks[0].stopped, true);
    assert.equal(result.MediaType, 'audio/wav');
    const wav = Buffer.from(result.Data, 'base64');
    assert.equal(wav.toString('ascii', 0, 4), 'RIFF');
    assert.equal(wav.readUInt32LE(24), 16000);
    assert.equal(wav.readUInt16LE(22), 1);
    assert.equal(wav.readInt16LE(46), 32767);
    service.cancel('current');
});

test('cancel during audio decoding rejects late audio and does not stop a newer recording', async () => {
    const decode = deferred();
    const { service, tracks, decoded, decoding } = browser(null, decode);
    await service.start('old');
    const stopping = service.stop('old');
    await decoding;
    service.cancel('old');
    await service.start('new');
    decode.resolve(decoded);
    await assert.rejects(stopping, /cancelled/);
    assert.equal(tracks[1].stopped, false);
    service.cancel('old');
    assert.equal(tracks[1].stopped, false);
    service.cancel('new');
    assert.equal(tracks[1].stopped, true);
});
