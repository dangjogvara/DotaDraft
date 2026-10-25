# 0004. Protect the admin endpoints with one shared API key

- **Status:** Accepted
- **Date:** 2026-10-18, implementation revised 2026-10-24
- **Issues:** #15 (PR #31), #62 (PR #69)

## Context

`/api/admin/*` started with no protection, so anyone who could reach the API could start minutes of OpenDota requests and replace the matchup table. The admin endpoints only trigger and monitor seeding, and the app has one operator and no user accounts.

## Decision

Require one shared secret, `Admin:ApiKey`, sent in the `X-Admin-Key` header:
- Compare it in fixed time (`CryptographicOperations.FixedTimeEquals`), so response times don't reveal how much of a guess matched.
- Fail closed: if the key isn't configured, the admin endpoints aren't mapped at all (404).
- Since #62 it's an ASP.NET Core authentication handler (the `AdminKey` scheme) plus an `Admin` authorization policy, replacing the original endpoint filter. That way the framework handles the 401, logs failures and describes the key in the OpenAPI document.

## Alternatives considered

- **Map the admin endpoints only in Development.** Rejected: a deployed database still needs seeding.
- **User login (ASP.NET Core Identity, JWT or an external OAuth provider).** Rejected: a lot of machinery (accounts, password storage, tokens) for one operator and three endpoints.
- **Keep the custom endpoint filter.** Replaced in #62: it worked, but the built-in authentication and authorization pipeline does the same job with standard behaviour.

## Consequences

- The key identifies no one: anyone holding it is "admin", and rotating it means changing configuration and restarting.
- It travels in a header, so production must use HTTPS.
- Adding user accounts later means adding a second authentication scheme. Endpoints that require the `Admin` policy don't need to change.
