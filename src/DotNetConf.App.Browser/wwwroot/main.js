import { dotnet } from './_framework/dotnet.js'

// When the browser denies site storage, any use of localStorage throws and the app can't start.
// Keep this visit's settings and saved sessions in memory instead.
try {
    globalThis.localStorage.getItem('');
} catch {
    console.warn('Site storage is blocked, so settings and saved sessions last only for this visit.');
    const items = new Map();
    const memory = {
        get length() { return items.size; },
        key: index => [...items.keys()][index] ?? null,
        getItem: key => items.has(String(key)) ? items.get(String(key)) : null,
        setItem: (key, value) => { items.set(String(key), String(value)); },
        removeItem: key => { items.delete(String(key)); },
        clear: () => items.clear(),
    };
    Object.defineProperty(globalThis, 'localStorage', { configurable: true, value: memory });
}

const dotnetRuntime = await dotnet
    .withDiagnosticTracing(false)
    .withApplicationArgumentsFromQuery()
    .create();

const config = dotnetRuntime.getConfig();

await dotnetRuntime.runMain(config.mainAssemblyName, [globalThis.location.href]);
