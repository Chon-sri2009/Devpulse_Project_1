let sdkPromise;
let player;
let dotNetReference;
let browserDeviceId = "";
let cachedToken;
let tokenExpiresAt = 0;
let activationButton;

function loadSdk() {
    if (window.Spotify?.Player) return Promise.resolve();
    if (sdkPromise) return sdkPromise;

    sdkPromise = new Promise((resolve, reject) => {
        const previousReady = window.onSpotifyWebPlaybackSDKReady;
        window.onSpotifyWebPlaybackSDKReady = () => {
            if (typeof previousReady === "function") previousReady();
            resolve();
        };

        const existing = document.querySelector('script[src="https://sdk.scdn.co/spotify-player.js"]');
        if (existing) {
            existing.addEventListener("error", () => reject(new Error("Spotify SDK could not be loaded.")), { once: true });
            return;
        }

        const script = document.createElement("script");
        script.src = "https://sdk.scdn.co/spotify-player.js";
        script.async = true;
        script.addEventListener("error", () => reject(new Error("Spotify SDK could not be loaded.")), { once: true });
        document.head.appendChild(script);
    });
    return sdkPromise;
}

async function fetchAccessToken() {
    if (cachedToken && tokenExpiresAt > Date.now() + 60_000) return cachedToken;

    const response = await fetch("/spotify/browser-token", {
        method: "GET",
        credentials: "same-origin",
        cache: "no-store",
        headers: { "Accept": "application/json" }
    });
    const result = await response.json().catch(() => ({}));
    if (!response.ok || !result.accessToken) {
        throw new Error(result.error || "Spotify authorization is unavailable.");
    }

    cachedToken = result.accessToken;
    tokenExpiresAt = Date.now() + Math.max(1, Number(result.expiresIn) || 1) * 1000;
    return cachedToken;
}

function notify(method, value) {
    if (dotNetReference) void dotNetReference.invokeMethodAsync(method, value).catch(() => {});
}

function safeImage(track) {
    const value = track?.album?.images?.[0]?.url;
    try {
        const url = new URL(value);
        return url.protocol === "https:" && url.hostname === "i.scdn.co" ? url.href : null;
    } catch {
        return null;
    }
}

function playbackState(state) {
    if (!state) return null;
    const track = state.track_window?.current_track;
    return {
        paused: Boolean(state.paused),
        position: Number(state.position) || 0,
        duration: Number(state.duration) || Number(track?.duration_ms) || 0,
        title: track?.name || "Nothing playing",
        creator: Array.isArray(track?.artists) ? track.artists.map(artist => artist.name).filter(Boolean).join(", ") : "",
        image: safeImage(track),
        uri: typeof track?.uri === "string" ? track.uri : "",
        disallowed: Object.keys(state.disallows || {}).filter(key => state.disallows[key] === true)
    };
}

function reportError(kind, error) {
    notify("OnBrowserPlayerError", { kind, message: error?.message || "Spotify browser playback failed." });
}

function activateFromUserGesture() {
    if (player) void player.activateElement().catch(error => reportError("autoplay", error));
}

function visibilityChanged() {
    notify("OnSpotifyVisibilityChanged", !document.hidden);
}

export async function initialize(reference, activationButtonId) {
    if (player) return true;
    dotNetReference = reference;

    try {
        await loadSdk();
        player = new window.Spotify.Player({
            name: "DevPulse Web Player",
            getOAuthToken: callback => {
                void fetchAccessToken()
                    .then(callback)
                    .catch(error => {
                        reportError("authentication", error);
                        callback("");
                    });
            },
            volume: 0.5,
            enableMediaSession: true
        });

        player.addListener("ready", ({ device_id }) => {
            browserDeviceId = device_id || "";
            notify("OnBrowserPlayerReady", browserDeviceId);
        });
        player.addListener("not_ready", ({ device_id }) => {
            if (!device_id || device_id === browserDeviceId) browserDeviceId = "";
            notify("OnBrowserPlayerNotReady", device_id || "");
        });
        player.addListener("player_state_changed", state => notify("OnBrowserPlayerStateChanged", playbackState(state)));
        player.addListener("initialization_error", error => reportError("initialization", error));
        player.addListener("authentication_error", error => reportError("authentication", error));
        player.addListener("account_error", error => reportError("account", error));
        player.addListener("playback_error", error => reportError("playback", error));
        player.addListener("autoplay_failed", () => reportError("autoplay", { message: "Your browser blocked automatic playback. Select Play in this browser again." }));

        activationButton = document.getElementById(activationButtonId);
        activationButton?.addEventListener("click", activateFromUserGesture, true);
        document.addEventListener("visibilitychange", visibilityChanged);

        const connected = await player.connect();
        if (!connected) reportError("connection", { message: "Spotify could not connect the browser player." });
        return connected;
    } catch (error) {
        reportError("initialization", error);
        return false;
    }
}

export async function setVolume(percent) {
    if (!player) return false;
    await player.setVolume(Math.max(0, Math.min(100, Number(percent) || 0)) / 100);
    return true;
}

export async function seek(positionMs) {
    if (!player) return false;
    await player.seek(Math.max(0, Number(positionMs) || 0));
    return true;
}

export async function disconnect() {
    activationButton?.removeEventListener("click", activateFromUserGesture, true);
    document.removeEventListener("visibilitychange", visibilityChanged);
    if (player) player.disconnect();
    player = undefined;
    dotNetReference = undefined;
    browserDeviceId = "";
    cachedToken = undefined;
    tokenExpiresAt = 0;
}
