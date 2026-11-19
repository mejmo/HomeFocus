<p align="center"><img src="assets/icon-256.png" width="96" alt="HomeFocus"></p>

# HomeFocus

HomeFocus je desktopová aplikácia pre Windows, ktorá automaticky sleduje, koľko času trávite v jednotlivých aplikáciách. Všetky dáta ostávajú v počítači a do cloudu sa nič neposiela.

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

## CI/CD

- **CI** (`.github/workflows/ci.yml`): build a testy pri každom pushi do `main` a pri každom pull requeste.
- **Release** (`.github/workflows/release.yml`): tag `v1.2.3` spustí testy, self-contained publish, podpis `HomeFocus.exe`, build inštalátora, podpis inštalátora a nakoniec vytvorí GitHub Release.

  ```powershell
  git tag v0.1.0
  git push origin v0.1.0
  ```

  Bez nastaveného podpisu tagovaný release zlyhá. Ručné spustenie workflowu (*Run workflow*) vytvorí nepodpísaný inštalátor ako artefakt.

## Podpisovanie (Azure Artifact Signing)

Podpis zabezpečuje Microsoft **Artifact Signing** (predtým *Trusted Signing*), približne 10 USD mesačne. Workflow potrebuje tri tajné údaje (secrets) a tri bežné premenné (variables).

1. **Signing account**: v [Azure portáli](https://portal.azure.com) vytvorte zdroj *Artifact Signing Account* (napr. región West Europe, SKU Basic). Zapíšte si jeho **názov** a **Account URI**, napr. `https://weu.codesigning.azure.net/`.
2. **Overenie identity**: v účte otvorte *Identity validations → New* (*Organization* alebo *Individual*) a počkajte na schválenie. Môže to trvať aj niekoľko dní.
3. **Certificate profile**: *Certificate profiles → Create → Public Trust* a vyberte schválenú identitu. Zapíšte si **názov profilu**.
4. **App registration**: *Microsoft Entra ID → App registrations → New registration*, napr. `homefocus-signing`.
   - na stránke *Overview* skopírujte **Application (client) ID** a **Directory (tenant) ID**,
   - v *Certificates & secrets → New client secret* skopírujte **Value**. Zobrazí sa iba raz.
5. **Oprávnenie**: v Signing account otvorte *Access control (IAM) → Add role assignment*, zvoľte rolu **Artifact Signing Certificate Profile Signer** (v starších portáloch *Trusted Signing Certificate Profile Signer*) a priraďte ju aplikácii z kroku 4.
6. **GitHub**: v adresári repozitára spustite tieto príkazy:

   ```powershell
   gh secret set AZURE_TENANT_ID     # Directory (tenant) ID
   gh secret set AZURE_CLIENT_ID     # Application (client) ID
   gh secret set AZURE_CLIENT_SECRET # Value klientského tajomstva
   gh variable set SIGNING_ENDPOINT --body "https://weu.codesigning.azure.net/"
   gh variable set SIGNING_ACCOUNT  --body "<názov signing accountu>"
   gh variable set SIGNING_PROFILE  --body "<názov certificate profile>"
   ```

Platnosť client secretu je obmedzená (predvolene 6 až 24 mesiacov). Pred jej vypršaním vytvorte nový secret a aktualizujte `AZURE_CLIENT_SECRET`.
