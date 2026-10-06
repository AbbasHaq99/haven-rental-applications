# Haven — rental applications

A server-rendered ASP.NET Core MVC application for a property management company. Applicants browse available apartments, complete and submit applications, respond to returned reviews, and withdraw open applications. Property managers maintain properties and units and approve, return, or deny submitted applications. Approval issues a twelve-month lease.

## Run with Docker

Requirements: Docker Engine / Docker Desktop with Compose and enough memory for SQL Server (at least 2 GB available to SQL Server; 4 GB or more for the overall Docker environment).

```sh
cp .env.example .env
# Edit .env to choose a strong SQL_PASSWORD.
docker compose up --build
```

Open **http://localhost:5080**. SQL Server must become healthy before the web container starts. Startup applies the checked-in EF Core migration, creates the Identity roles, and seeds the demo data. The SQL volume persists between restarts.

SQL Server's Linux container targets **x86-64**. On Apple Silicon, use a remote x86-64 SQL Server or SQL Server Express on Windows for dependable execution; container emulation is not a supported SQL Server deployment. The application itself runs natively on macOS, Windows, and Linux with .NET 10.

To stop: `docker compose down`. To intentionally reset all demo data: `docker compose down --volumes` (deletes the database).

## Run with .NET and an existing SQL Server

Requirements: .NET 10 SDK; SQL Server 2022 or SQL Server Express (a newer SQL Server is also suitable). The connection account needs permission to create the database and apply schema changes for the assessment's startup behavior.

macOS / Linux:

```sh
export ConnectionStrings__DefaultConnection='Server=YOUR_SERVER;Database=Haven;User Id=YOUR_USER;Password=YOUR_PASSWORD;Encrypt=True;TrustServerCertificate=True'
dotnet restore
dotnet run --project src/Haven.Web --launch-profile http
```

PowerShell with SQL Server Express LocalDB:

```powershell
$env:ConnectionStrings__DefaultConnection = 'Server=(localdb)\MSSQLLocalDB;Database=Haven;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True'
dotnet restore
dotnet run --project src/Haven.Web --launch-profile http
```

Both commands use the Development environment and serve **http://localhost:5080**. For local HTTPS, run `dotnet dev-certs https --trust` and use `--launch-profile https`, then open **https://localhost:7080**. Certificate trust is a local-development convenience; configure a valid server certificate for a deployed environment.

## Demo accounts

In Development, seeding is enabled. All accounts below use **`HavenDemo!2026`**, unless `Seed__DemoPassword` overrides it before the first seed.

| Role | Email |
| --- | --- |
| Property manager | `manager@haven.test` |
| Second property manager | `manager2@haven.test` |
| Applicant with all six application statuses | `applicant@haven.test` |
| Additional applicants | `applicant2@haven.test`, `applicant3@haven.test` |

Bogus generates deterministic names, addresses, contact details, and rents. Seed data contains three properties, twelve units, active and inactive unit types, one application in every required status, review history, and an active lease. A database completion marker makes demo seeding run once; repeated starts preserve renamed and deleted catalog records, existing account passwords, and edited applications. Previously seeded databases are recognized by their six original application seed keys and receive the marker without reseeding. Use a clean demo database to see the original mix of statuses again.

Sign-up allows either role as the assessment requires. This convenience should be replaced by invitations or administrative provisioning before using the application as a real tenant service.

## Try the workflows

1. As an applicant, browse **Find a home**. Only units without a lease covering today appear. Start an application; starting again for the same unit resumes an existing open application.
2. On **Applicant information**, enter the required details and choose **Save & continue**. Invalid fields remain on that section with messages. Back returns without saving posted fields.
3. Add, edit, or remove prior residences through modals. At least one prior residence is required. Choose **Save & continue** to confirm the history section.
4. The **Summary** displays the saved data and allows submission only after both sections are saved. Submitted applications are read-only. Draft, Returned, and Submitted applications can be withdrawn; Withdrawn is terminal.
5. As a manager, filter **Applications**, open a Submitted application, and use **Review application**. Return and Deny require a comment. Approval requires a start date and issues a twelve-month lease. Manager history includes who, when, status, outcome, and comment. Applicants receive reviewer feedback without the manager history.
6. Return an application, correct it as the applicant, and resubmit. Approve one of two Submitted applications for the same unit: the competing application remains unchanged, and its approval is rejected if the unit has an active or overlapping lease.
7. Create, edit, and remove properties and units through modals. An inactive type may be retained on an existing unit but cannot be assigned to another unit. Removal is blocked for records with application or lease history to preserve the audit trail.

## Tests

```sh
dotnet test Haven.sln
```

The default suite includes pure business-rule tests, relational workflow tests, and full MVC HTTP tests with Identity login and antiforgery tokens. SQLite is used **only in the test project**, with a test-only date conversion. The application always uses SQL Server. SQLite tests do not prove SQL Server lock behavior.

To additionally run the SQL Server migration and concurrency tests, supply a connection to a disposable SQL Server instance. The account must be able to create and drop a temporary database. Each test run creates a uniquely named database, applies migrations, and removes that database during teardown:

```sh
export HAVEN_TEST_SQL='Server=localhost,1433;User Id=sa;Password=YOUR_PASSWORD;Encrypt=True;TrustServerCertificate=True'
dotnet test Haven.sln
```

The GitHub Actions workflow provisions SQL Server and enables these tests. See [verification notes](docs/verification.md) for the checks actually executed in the development environment.

## Migrations and configuration

The initial migration and model snapshot are committed. EF tooling can generate or apply further changes:

```sh
dotnet tool restore
dotnet ef migrations list --project src/Haven.Web
dotnet ef database update --project src/Haven.Web
```

The design-time factory reads `ConnectionStrings__DefaultConnection`; set it before `database update`.

| Setting | Purpose / default |
| --- | --- |
| `ConnectionStrings__DefaultConnection` | Required SQL Server connection; intentionally absent from source control |
| `Database__ApplyMigrations` | `true`; startup creates the database and applies migrations |
| `Seed__Enabled` | `true` in Development; `false` otherwise |
| `Seed__DemoPassword` | Development demo password; required when seeding is enabled |
| `BusinessTimeZone` | `America/Chicago`; defines “today” for availability and lease checks |

Timestamps are stored in UTC. A lease covers `[StartDate, EndDate)`: the start date is included, and the twelve-month anniversary is excluded. An approval cannot start in the past, overlap any existing lease, or proceed when the unit has a lease covering today. Future leases do not hide an otherwise available unit today, but overlapping future approvals are rejected.

For deployment, disable demo seeding, configure a genuine SQL Server TLS certificate (`TrustServerCertificate=False`), use HTTPS and an appropriate host allowlist, provide a least-privilege database account, and persist ASP.NET Core Data Protection keys if using multiple instances or disposable containers. The app enables HTTPS redirection, HSTS, and generic error handling outside Development. Startup migrations are enabled to satisfy the assessment; a deployment pipeline can instead apply migrations and set `Database__ApplyMigrations=false`.

## Design and assessment coverage

See [design and requirement mapping](docs/design.md). This submission implements the required scope. Database paging and sorting are also included; the optional JSON grid / OpenAPI endpoint, claim queue, private notes, invalid section saving, and multiple-applicant editing are not included.
