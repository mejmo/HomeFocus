# Chrome Web Store listing – HomeFocus

Upload: `dist/HomeFocus-extension-<version>.zip` (built by `tools/pack-extension.ps1`, also attached to each GitHub release).
Visibility: **Unlisted**. Category: **Productivity**. Language: English (Slovak is included via `_locales/sk`).

## Store listing

**Summary** (max 132 characters)
> Splits your browser time per website in the HomeFocus time tracker for Windows. Domain only, stays on your PC.

**Description**
```
HomeFocus is a self-hosted time tracker for Windows that helps you stay in focus while you work from home.

This extension lets HomeFocus split the time you spend in the browser per website, so YouTube or Reddit no longer hide inside "Google Chrome".

How it works
• The extension reads the address of the active tab in the focused window and keeps only the domain (for example youtube.com).
• It sends that domain to the HomeFocus app on your own computer over 127.0.0.1. Nothing is sent to the internet, to us or to anyone else.
• Private (incognito) windows are never reported.
• The first time it connects, HomeFocus asks you to approve the browser.

Requirements
• The HomeFocus app for Windows: https://mejmo.github.io/HomeFocus/

Privacy
No account, no telemetry, no servers. Your history stays in a local file on your PC that you can export or delete at any time.
```

**Graphics**
- Icon 128×128: included in the package (`icons/128.png`)
- Screenshots 1280×800: `docs/store/screenshot-overview.png`, `docs/store/screenshot-today.png`
- Small promo tile 440×280: `docs/store/promo-small-440x280.png`

**Privacy policy URL**: `https://mejmo.github.io/HomeFocus/#extension-privacy`

## Privacy practices tab

**Single purpose**
> Report the domain of the active browser tab to the HomeFocus desktop app on the same computer, so it can show time spent per website.

**Permission justification**
- `tabs`: Reads the URL of the active tab to take its domain and send it to the HomeFocus app on 127.0.0.1. No other tab data is used.
- `alarms`: Reconnects to the HomeFocus app once a minute when it was not running, and keeps the report current.
- `storage`: Stores the pairing token issued by the HomeFocus app after the user approves the browser, and the connection status shown in the popup.

**Remote code**: No, I am not using remote code.

**Data usage** – tick only:
- ☑ **Web history** (the domain of the active tab)

Certify all three statements:
- ☑ I do not sell or transfer user data to third parties, outside of the approved use cases
- ☑ I do not use or transfer user data for purposes that are unrelated to my item's single purpose
- ☑ I do not use or transfer user data to determine creditworthiness or for lending purposes

## After publishing

Send the item ID (32 letters, from the store URL) so it can be set in
`src/HomeFocus.App/Services/ExtensionInfo.cs` (`StoreId`) and `installer/HomeFocus.iss` (`ExtensionStoreId`).
