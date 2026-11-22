// Browser-only microphone capture. Transcription is handled by the configured host service.
window.studioDictation = (() => {
    const sessions = new Map();
    function release(session) {
        clearTimeout(session.timer);
        session.stream?.getTracks().forEach(track => track.stop());
    }
    return {
        async start(id) {
            if (!navigator.mediaDevices?.getUserMedia || !window.MediaRecorder)
                throw new Error('Microphone recording requires a supported browser and HTTPS or localhost.');
            const session = { cancelled: false, chunks: [], bytes: 0 };
            sessions.set(id, session);
            try {
                session.stream = await navigator.mediaDevices.getUserMedia({ audio: true });
                if (session.cancelled) { release(session); return; }
                const mimeType = ['audio/webm;codecs=opus', 'audio/ogg;codecs=opus', 'audio/mp4'].find(type => MediaRecorder.isTypeSupported(type));
                if (!mimeType) throw new Error('No supported audio recording format is available.');
                const recorder = session.recorder = new MediaRecorder(session.stream, { mimeType });
                session.finished = new Promise(resolve => { session.resolve = resolve; });
                recorder.ondataavailable = event => {
                    session.bytes += event.data.size;
                    if (session.bytes > 4 * 1024 * 1024) {
                        session.error = 'Recording exceeded 4 MB. Try a shorter recording.';
                        if (recorder.state !== 'inactive') recorder.stop();
                        release(session);
                    } else session.chunks.push(event.data);
                };
                recorder.onerror = () => { session.error = 'Microphone recording failed.'; release(session); session.resolve(); };
                recorder.onstop = () => { release(session); session.resolve(); };
                recorder.start(250);
                session.timer = setTimeout(() => { if (recorder.state !== 'inactive') recorder.stop(); release(session); }, 60000);
            } catch (error) { release(session); sessions.delete(id); throw error; }
        },
        async stop(id) {
            const session = sessions.get(id);
            if (!session?.recorder || session.cancelled) throw new Error('Recording was cancelled.');
            if (session.recorder.state !== 'inactive') session.recorder.stop();
            await session.finished;
            if (session.cancelled) throw new Error('Recording was cancelled.');
            if (session.error) throw new Error(session.error);
            const blob = new Blob(session.chunks, { type: session.recorder.mimeType });
            if (!blob.size) throw new Error('No audio was recorded.');
            // A standard mono WAV keeps provider support independent of browser recording codecs.
            const decoded = await new OfflineAudioContext(1, 1, 16000).decodeAudioData(await blob.arrayBuffer());
            if (session.cancelled) throw new Error('Recording was cancelled.');
            const length = Math.min(decoded.length, 60 * 16000);
            const wav = new ArrayBuffer(44 + length * 2);
            const view = new DataView(wav);
            const write = (offset, text) => [...text].forEach((character, i) => view.setUint8(offset + i, character.charCodeAt(0)));
            write(0, 'RIFF'); view.setUint32(4, wav.byteLength - 8, true); write(8, 'WAVE'); write(12, 'fmt ');
            view.setUint32(16, 16, true); view.setUint16(20, 1, true); view.setUint16(22, 1, true);
            view.setUint32(24, 16000, true); view.setUint32(28, 32000, true); view.setUint16(32, 2, true); view.setUint16(34, 16, true);
            write(36, 'data'); view.setUint32(40, length * 2, true);
            const channels = Array.from({ length: decoded.numberOfChannels }, (_, i) => decoded.getChannelData(i));
            for (let i = 0; i < length; i++) {
                const sample = Math.max(-1, Math.min(1, channels.reduce((sum, channel) => sum + channel[i], 0) / channels.length));
                view.setInt16(44 + i * 2, sample < 0 ? sample * 32768 : sample * 32767, true);
            }
            const data = await new Promise((resolve, reject) => {
                const reader = new FileReader();
                reader.onerror = reject;
                reader.onload = () => resolve(reader.result.split(',')[1]);
                reader.readAsDataURL(new Blob([wav], { type: 'audio/wav' }));
            });
            if (session.cancelled) throw new Error('Recording was cancelled.');
            return JSON.stringify({ Data: data, MediaType: 'audio/wav' });
        },
        cancel(id) {
            const session = sessions.get(id);
            if (!session) return;
            session.cancelled = true;
            sessions.delete(id);
            if (session.recorder?.state === 'recording') session.recorder.stop();
            release(session);
            session.resolve?.();
        }
    };
})();
