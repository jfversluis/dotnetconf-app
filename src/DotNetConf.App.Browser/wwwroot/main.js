import { dotnet } from './_framework/dotnet.js'

// When the browser denies site storage, any use of localStorage throws and the app can't start.
// Keep this visit's settings and saved sessions in memory instead.
try {
    globalThis.localStorage.getItem('');
} catch {
    console.warn('Site storage is blocked, so settings and saved sessions last only for this visit.');
    const items = new Map();
    const methods = {
        getItem: key => items.has(String(key)) ? items.get(String(key)) : null,
        setItem: (key, value) => { items.set(String(key), String(value)); },
        removeItem: key => { items.delete(String(key)); },
        clear: () => items.clear(),
    };
    // Storage also exposes items as properties, which the app's own storage helper uses. Symbol keys, such as
    // the handle .NET tags the object with, stay on the object.
    const memory = new Proxy(methods, {
        get: (target, key) => typeof key !== 'string' || key in target ? Reflect.get(target, key) : items.get(key),
        set: (target, key, value) => typeof key !== 'string'
            ? Reflect.set(target, key, value)
            : (items.set(key, String(value)), true),
    });
    Object.defineProperty(globalThis, 'localStorage', { configurable: true, value: memory });
}

const dotnetRuntime = await dotnet
    .withDiagnosticTracing(false)
    .withApplicationArgumentsFromQuery()
    .create();

const config = dotnetRuntime.getConfig();

await dotnetRuntime.runMain(config.mainAssemblyName, [globalThis.location.href]);
