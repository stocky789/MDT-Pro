# macOS port (later)

The WPF project is **Windows-only**. A future macOS client should:

- Reuse **`MDTProNative.Core`** and **`MDTProNative.Client`** if you target **.NET** with a cross-platform UI (e.g. **.NET MAUI**), or reimplement the small HTTP/WebSocket layer in **Swift** against the same URLs.
- Keep **one protocol**: `http://host:port/` and `ws://host:port/ws` with the same text commands the plugin already accepts.
- Expect **TLS and auth** once multi-user production hardening lands on the plugin side.

No changes to the browser MDT or game plugin are required solely for a Mac UI shell.
