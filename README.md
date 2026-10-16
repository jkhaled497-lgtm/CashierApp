# Cashier POS

A Windows desktop point-of-sale starter built on .NET 8, WPF, CommunityToolkit.Mvvm, WPF-UI, EF Core 8, and SQL Server. The solution is split into Domain, Application, Infrastructure, and UI projects.

## Run

```powershell
dotnet restore Cashier.sln
dotnet build Cashier.sln
dotnet run --project src\Cashier.UI\Cashier.UI.csproj
```

Create and apply EF Core migrations from the solution root:

```powershell
dotnet ef migrations add <MigrationName> --project src\Cashier.Infrastructure\Cashier.Infrastructure.csproj --startup-project src\Cashier.Infrastructure\Cashier.Infrastructure.csproj --output-dir Migrations
dotnet ef database update --project src\Cashier.Infrastructure\Cashier.Infrastructure.csproj --startup-project src\Cashier.Infrastructure\Cashier.Infrastructure.csproj
```

The application connects to the SQL Server and database configured by `src\Cashier.UI\appsettings.json` (default: server `Eng-Khaled`, database `Cashier`, Windows Authentication). On first launch it creates the schema if needed, a walk-in customer, and an initial administrator. Sign in with username `admin` and password `Admin@12345`; the application requires the administrator to change this password before continuing. This is a public bootstrap credential, so change it immediately. Passwords are stored as BCrypt hashes with BCrypt salts.

The development connection encrypts traffic but trusts the SQL Server certificate without validating its chain. This accommodates the current development server's untrusted certificate; for production, install a certificate trusted by client machines and set `TrustServerCertificate=False`. The production connection is configured separately as described below. The SQL account must be permitted to connect and create/use the target database. Existing data in the former local SQLite file is not automatically transferred.

## Production deployment, remote SQL, and updates

### MonsterASP SQL Server configuration

`deploy\appsettings.Production.example.json` is a template, not a working connection string. Replace its host, database, username, and password with the values from the MonsterASP control panel, then put the configured file at:

```text
C:\ProgramData\Cashier\appsettings.json
```

Use these commands on the client machine to install and edit the template:

```powershell
New-Item -ItemType Directory -Path "$env:ProgramData\Cashier" -Force
Copy-Item "deploy\appsettings.Production.example.json" "$env:ProgramData\Cashier\appsettings.json"
notepad "$env:ProgramData\Cashier\appsettings.json"
```

The application loads this machine-level file after the bundled development settings, and reads environment variables last. You can instead set `ConnectionStrings__Cashier` as a Windows environment variable. The connection-string options enable encryption and pooling and cap connection establishment at 30 seconds. `TrustServerCertificate=True` encrypts traffic but does not validate the server certificate; use a trusted server certificate and `False` where available. The hosting plan must allow remote SQL connections from the client's public IP. Give the application a least-privilege SQL account; credentials in a desktop application cannot be kept secret from a determined client.

The database configuration is kept under `C:\ProgramData\Cashier`, outside the installation directory. This is intentional: the updater replaces the dedicated installation directory, while leaving the client's connection settings intact. The application needs internet access both to reach MonsterASP and to check GitHub for updates; self-contained publishing only removes the .NET runtime prerequisite.

### GitHub Releases auto-update

The application checks `https://raw.githubusercontent.com/jkhaled497-lgtm/CashierApp/main/version.xml` after startup. `version.xml` in this repository is an initial manifest example; its release URL is not usable until a matching ZIP asset and GitHub Release have been published. AutoUpdater.NET is configured to hide skip/remind-later options, treat every newer version as mandatory, suppress update-check errors/no-update messages, and extract a clean ZIP into the application's dedicated installation directory. Do not run it from a USB root or a folder containing user files.

The package reference is already installed in the WPF project. To add it to a fresh checkout:

```powershell
dotnet add src\Cashier.UI\Cashier.UI.csproj package Autoupdater.NET.Official --version 1.9.3
```

Publish a self-contained single-file x64 build:

```powershell
dotnet publish src\Cashier.UI\Cashier.UI.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:IncludeAllContentForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false
```

The publish directory is `src\Cashier.UI\bin\Release\net8.0-windows\win-x64\publish`. `appsettings.json` is deliberately not published: supply the machine-level configuration above on the client. Zip the *contents* of the publish directory (so `Cashier.UI.exe` is at the ZIP root), not its parent directory and not GitHub's automatically generated source ZIP. Publish that ZIP as the asset `Cashier.UI-win-x64.zip` on a GitHub Release whose tag matches the manifest version. For every bug-fix release, increment `<Version>` in `src\Cashier.UI\Cashier.UI.csproj`, publish and ZIP the complete output again, upload it as a release asset, and update `<version>`, `<url>`, and `<changelog>` in `version.xml`. The manifest version must be four-part numeric (for example `1.0.1.0`).

For example, after building and creating a release ZIP, create the ZIP from PowerShell with:

```powershell
$publish = "src\Cashier.UI\bin\Release\net8.0-windows\win-x64\publish"
Compress-Archive -Path "$publish\*" -DestinationPath "Cashier.UI-win-x64.zip" -Force
```

Keep the production configuration outside the ZIP and never commit credentials to GitHub. The updater's `ClearAppDirectory` behavior removes files from the installation directory before extraction, so install the application in its own folder and back up any local files before updating.

## Included

- Four-project Clean Architecture solution, constructor-injected services, and EF Core SQL Server mappings.
- Arabic right-to-left user sign-in, BCrypt verification, forced initial administrator password change, user/role management, employee records, employee-account links, and 15-minute inactivity logout.
- Customer and employee CRUD screens; customer search, quick-add, account balances, customer payment ledger with invoice-linked balance settlement, loyalty point accrual, and walk-in fallback.
- Product, category, brand, and supplier CRUD; locally selected product images; generated internal identifiers; barcode scanning and live product search/dropdown by name/barcode/brand/category. Product selling price must be at least cost; editing products also allows changing their active state. Cost prices are only shown to administrators.
- Customer and supplier screens with side-by-side records and selected-account ledgers; supplier opening balances, additional charges, cash/card repayments, and retained paginated balance histories.
- Inventory adjustments with employee attribution and history; sale-time stock validation, deduction, and stock-out audit records.
- Paginated, refreshable management grids and reload-on-navigation data loading.
- A post-login home dashboard for navigating application modules; shared Fluent colors with light/dark theme support.
- POS with a searchable product dropdown (without product tiles, category pills, or paging controls), customer selection and quick-add, quantity controls, and selected-line discounts entered as currency amounts. Cash/card tender and customer-account settlement are supported; invoices retain cashier attribution and receipt printing. New sales are tax-free and do not require a cashier shift; legacy shift and tax records remain in the database.
- WPF-UI Fluent windows with Mica backdrop, keyboard shortcuts, and 58/80-mm-style receipt page sizing.

## Reusable UI interactions

All WPF `ComboBox` controls use the shared style and `ComboBoxSearchBehavior` registered in `src/Cashier.UI/App.xaml`. Existing `ItemsSource`, `SelectedItem`, `SelectedValue`, `DisplayMemberPath`, item templates, and event handlers remain in place. Controls become editable so typing filters their visible options without modifying a shared collection view; focus/click opens the full list, Up/Down navigates matches, Enter/Tab selects, and Escape closes and resets the search. WPF uses `ItemsSource`/`DisplayMemberPath`/`SelectedValuePath` equivalents rather than WinForms `DataSource`/`DisplayMember`/`ValueMember`.

`ScrollNavigation` adds themed page-step up/down buttons to a scrollable panel. It is opt-in on purpose so internal scroll viewers in combo popups and data grids do not acquire overlapping buttons. To enable it on another panel, declare `xmlns:controls="clr-namespace:Cashier.UI.Controls"` on its XAML root and set `controls:ScrollNavigation.IsEnabled="True"` on the `ScrollViewer`. The buttons automatically disable at their respective scroll limits.

## Production hardening and integration

This is an executable foundation, not a certified fiscal/register deployment. Configure store identity, currency/rounding policy, receipt template, and retention/privacy requirements before go-live. New sales do not apply tax; existing tax values are retained for historical invoices. The receipt print service uses the Windows print dialog and ZXing-generated invoice QR; printer selection, hardware status, fiscal QR payloads, cash-drawer commands, and device-specific ESC/POS behavior require the target hardware and jurisdiction. The `AddSuppliers` migration adds the supplier table without changing invoice tax or shift history. Configure SQL Server backups, access controls, monitoring, and operational recovery procedures.

Employee CRUD covers core profile fields, while adding/editing an employee-to-user link is available through the user-account workflow. Held carts are kept in memory for the running app session, not persisted across process restarts. The current role policy is intentionally straightforward: administrator-only account administration and administrator/manager catalog and inventory access.
