---
description: Validation scope rules for the FashionSaaS backend — where FluentValidation ends and service-layer business rules begin
---

# Validation — two distinct scopes, don't blur them

Per `docs/CONVENTIONS.md` §8 (confirmed in real code: FluentValidation validators registered via
`AddValidatorsFromAssembly` + `AddFluentValidationAutoValidation`).

| Scope | Where | Validates | DB access |
|---|---|---|---|
| **Input shape** | FluentValidation `AbstractValidator<T>` in the Application feature folder | Required fields, string length, numeric range, enum/format, cross-field rules (`StartsAt < EndsAt`) | Never |
| **Business rules** | Service layer | Uniqueness, existence/ownership, state-transition legality, tenant scoping | Yes |

- New request/command DTOs ship a validator. Don't inline shape checks in the service that a
  validator should own, and don't put business rules (uniqueness, ownership) in a validator.
- Services return the `ResponseData<T>` envelope with the right status semantics (409 conflict,
  404 not found, 403 forbidden, 400 bad request) for business-rule failures.
- A `.Must(...)` predicate in a validator must handle the "value is missing" case explicitly
  (FluentValidation does not null-guard `Must` predicates) — add a `.NotNull()` rule ahead of it
  rather than letting a missing field NRE into a 500.
- When a value crosses a trust boundary from the client (e.g. an image URL the server will fetch
  server-side), validate more than shape — a same-origin/host allowlist check belongs in the
  validator too if the alternative is the server blindly fetching an attacker-controlled URL
  (SSRF). Shape-only validation is not enough when the value drives a server-side network call.
