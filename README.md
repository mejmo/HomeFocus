<p align="center"><img src="assets/icon-256.png" width="96" alt="HomeFocus"></p>

<h1 align="center">HomeFocus</h1>

<p align="center">
  <a href="https://github.com/mejmo/HomeFocus/actions/workflows/ci.yml"><img src="https://github.com/mejmo/HomeFocus/actions/workflows/ci.yml/badge.svg" alt="CI"></a>
  <a href="https://github.com/mejmo/HomeFocus/actions/workflows/release.yml"><img src="https://github.com/mejmo/HomeFocus/actions/workflows/release.yml/badge.svg" alt="Release"></a>
  <img src="https://img.shields.io/badge/Windows-10%20%7C%2011-0078D6?logo=windows&amp;logoColor=white" alt="Windows 10 | 11">
  <img src="https://img.shields.io/badge/.NET-9-512BD4?logo=dotnet&amp;logoColor=white" alt=".NET 9">
  <img src="https://img.shields.io/badge/WPF-desktop-2F5BEA" alt="WPF">
  <img src="https://img.shields.io/badge/d%C3%A1ta-100%25%20lok%C3%A1lne-16A34A" alt="dáta 100 % lokálne">
</p>

<p align="center">Desktopová aplikácia pre Windows, ktorá automaticky sleduje, koľko času trávite v jednotlivých aplikáciách. Všetky dáta ostávajú v počítači a do cloudu sa nič neposiela.</p>

![HomeFocus – týždenný prehľad podľa kategórií a rebríček aplikácií](docs/images/screenshot.png)

- **Sledovanie** aktívneho okna každú sekundu, s rozpoznaním nečinnosti (prah sa dá nastaviť), pauzy a uspania PC
- **Dashboard**: časová os dňa, top aplikácie, kategórie, týždenný a mesačný prehľad, export do CSV
- **Kategórie a pravidlá**: automatické zaradenie podľa procesu (`devenv.exe`, `steam*.exe`). Vlastné pravidlá platia aj pre už zaznamenanú históriu.
- **Systray**: rýchly prehľad dňa, pauza, autostart pri prihlásení
- Svetlý aj tmavý vzhľad podľa Windows

Dáta: `%APPDATA%\HomeFocus\data.db` (SQLite).

## Inštalácia

Stiahnite si `HomeFocus-Setup-x.y.z.exe` z [Releases](../../releases). Inštalátor nepotrebuje administrátorské práva a aplikáciu nainštaluje do `%LOCALAPPDATA%\Programs\HomeFocus`. .NET netreba inštalovať samostatne, je súčasťou aplikácie. Pri odinštalovaní dáta v `%APPDATA%\HomeFocus` ostanú.

## Vývoj

Potrebné je .NET 9 SDK a Windows 10 alebo novší.

```powershell
dotnet build
dotnet test
dotnet run --project src/HomeFocus.App
```

Premenná `HOMEFOCUS_DB` presmeruje aplikáciu na inú databázu, napríklad na skúšanie. Debug build nikdy nezapisuje autostart do registrov.

| Priečinok | Obsah |
|---|---|
| `src/HomeFocus.Core` | databáza, pravidlá, sledovanie (Win32), reporty |
| `src/HomeFocus.App` | WPF UI: `Themes/` (vlastné štýly), `Views/`, `ViewModels/`, `Controls/` (grafy) |
| `tests/HomeFocus.Tests` | xUnit testy jadra |
| `installer/HomeFocus.iss` | inštalátor (Inno Setup 6) |
