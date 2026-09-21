let context;
let master;
let sources = [];
const journalKey = "devpulse.focus-journal.v1";

function audioContext() {
    if (!context) context = new (window.AudioContext || window.webkitAudioContext)();
    if (!master) {
        master = context.createGain();
        master.gain.value = 0.35;
        master.connect(context.destination);
    }
    return context;
}

function noiseBuffer(ctx, brown = false) {
    const length = ctx.sampleRate * 5;
    const buffer = ctx.createBuffer(1, length, ctx.sampleRate);
    const data = buffer.getChannelData(0);
    let previous = 0;
    for (let i = 0; i < length; i++) {
        const white = Math.random() * 2 - 1;
        previous = brown ? (previous + 0.02 * white) / 1.02 : white;
        data[i] = brown ? previous * 3.5 : white;
    }
    return buffer;
}

function addNoise(ctx, brown, filterType, frequency, gainValue) {
    const source = ctx.createBufferSource();
    const filter = ctx.createBiquadFilter();
    const gain = ctx.createGain();
    source.buffer = noiseBuffer(ctx, brown);
    source.loop = true;
    filter.type = filterType;
    filter.frequency.value = frequency;
    gain.gain.value = gainValue;
    source.connect(filter).connect(gain).connect(master);
    source.start();
    sources.push(source, filter, gain);
    return { source, filter, gain };
}

export async function startAmbient(kind, volume) {
    stopAmbient();
    const ctx = audioContext();
    await ctx.resume();
    setAmbientVolume(volume);
    if (kind === "rain") {
        addNoise(ctx, false, "highpass", 1800, 0.28);
        addNoise(ctx, true, "lowpass", 500, 0.22);
    } else if (kind === "ocean") {
        const wave = addNoise(ctx, true, "lowpass", 650, 0.42);
        const lfo = ctx.createOscillator();
        const depth = ctx.createGain();
        lfo.frequency.value = 0.09;
        depth.gain.value = 0.28;
        lfo.connect(depth).connect(wave.gain.gain);
        lfo.start();
        sources.push(lfo, depth);
    } else {
        addNoise(ctx, true, "lowpass", 900, 0.5);
    }
}

export function setAmbientVolume(percent) {
    if (master) master.gain.setTargetAtTime(Math.max(0, Math.min(100, Number(percent) || 0)) / 100, context.currentTime, 0.03);
}

export function stopAmbient() {
    for (const node of sources) {
        try { if (typeof node.stop === "function") node.stop(); } catch { }
        try { node.disconnect(); } catch { }
    }
    sources = [];
}

export function loadJournal() {
    try { return localStorage.getItem(journalKey) || ""; } catch { return ""; }
}

export function saveJournal(value) {
    try {
        const safe = String(value || "").slice(0, 2000);
        if (safe) localStorage.setItem(journalKey, safe);
        else localStorage.removeItem(journalKey);
    } catch { throw new Error("Browser storage is unavailable."); }
}

export async function requestNotifications() {
    if ("Notification" in window && Notification.permission === "default") await Notification.requestPermission();
}

export function notifySession(message) {
    const ctx = audioContext();
    const oscillator = ctx.createOscillator();
    const gain = ctx.createGain();
    oscillator.frequency.value = 523.25;
    gain.gain.setValueAtTime(0.0001, ctx.currentTime);
    gain.gain.exponentialRampToValueAtTime(0.12, ctx.currentTime + 0.02);
    gain.gain.exponentialRampToValueAtTime(0.0001, ctx.currentTime + 0.65);
    oscillator.connect(gain).connect(ctx.destination);
    oscillator.start();
    oscillator.stop(ctx.currentTime + 0.7);
    if ("Notification" in window && Notification.permission === "granted") new Notification("DevPulse Focus Lounge", { body: String(message || "Session complete.") });
}

export async function setFullscreen(enabled) {
    document.body.classList.toggle("devpulse-calm-mode", Boolean(enabled));
    if (enabled && !document.fullscreenElement) await document.documentElement.requestFullscreen();
    if (!enabled && document.fullscreenElement) await document.exitFullscreen();
}

export function dispose() {
    document.body.classList.remove("devpulse-calm-mode");
    stopAmbient();
    if (context) void context.close();
    context = undefined;
    master = undefined;
}
