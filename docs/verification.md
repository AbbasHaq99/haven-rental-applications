# Verification

This document distinguishes checks executed locally from checks provided for a SQL Server environment. It is not a claim that unexecuted checks passed.

## Executed locally

- .NET 10 SDK installed into temporary storage; no machine-wide SDK change.
- Release build and publish succeeded with zero warnings/errors; Razor views compiled.
- Final test result: **47 passed, 2 skipped, 0 failed**. The two skipped tests require SQL Server.
- EF tooling reported no pending model changes and generated an idempotent SQL migration script.
- NuGet audit reported no known vulnerable direct or transitive packages.
- JavaScript syntax check passed.
- Business rule tests for editable/terminal statuses, required review comments, inactive type retention, lease date boundaries, and field validation.
- Relational SQLite workflow tests for ownership, unsaved-section submission, read-only states, returned correction/resubmission, stale versions and concurrent-context optimistic writes, residence completion invalidation, twelve-month leases, active/future lease rejection, and competing application preservation.
- MVC HTTP tests with actual Identity authentication and antiforgery: anonymous redirects, CSRF rejection, role denial, ownership isolation, invalid/valid modal responses, Back without saving, Summary-only submit dispatch, and idempotent seed.

SQLite is confined to the test project. A test-only context converts UTC timestamps to sortable ticks because SQLite does not natively support sorting DateTimeOffset. These checks do not exercise SQL Server's row locks or migration application.

## Browser verification

Executed against a temporary host using the test-only relational database and generated demo accounts:

- Inspected the manager catalog at desktop size.
- Opened the property modal, submitted an empty form, verified field errors stayed inside the dialog, then saved valid data and confirmed the dialog closed and catalog refreshed.
- Filtered applications by Submitted and opened the remaining result.
- Submitted Return without a comment and verified an in-place error; completed Return with feedback and verified refreshed status and manager history.
- Signed in as the applicant, confirmed the Returned application was editable and reviewer feedback was visible without manager history.
- Corrected applicant information, continued to residence history, submitted invalid residence dates, corrected them, and confirmed successful in-place refresh.
- Saved history, inspected Summary, resubmitted, and verified no editable inputs or manager history were rendered.
- Checked a 390-pixel mobile viewport; the document width matched the viewport and showed no horizontal overflow. Restored the viewport afterward.

The preview host and its database are temporary test artifacts; production startup still requires SQL Server.

## SQL Server verification

The checked-in initial migration was generated using the SQL Server provider. `SqlServerTests` applies the actual migration twice, checks the seed and model snapshot, exercises the filtered/paged query, and races two approvals of the same unit using separate DbContext connections. These tests require `HAVEN_TEST_SQL`; without it they are explicitly skipped. GitHub Actions supplies a SQL Server service and runs them.

A SQL Server instance and Docker Engine are not present in the local development environment. SQL Server tests and the Docker image/Compose startup therefore have not been executed locally.
