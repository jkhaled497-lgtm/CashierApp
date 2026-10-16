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

The current connection encrypts traffic but trusts the SQL Server certificate without validating its chain. This accommodates the current server's untrusted certificate; for production, install a certificate trusted by client machines and set `TrustServerCertificate=False`. Update the connection string in `appsettings.json` for your SQL Server host/database or authentication requirements. The Windows account running the application must be permitted to connect and create/use the target database. Existing data in the former local SQLite file is not automatically transferred.

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
