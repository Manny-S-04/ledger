# Ledger-only rules (the general, honesty, purity and C# rules live in ~/.pair/rules.md).

## Ledger types
- Expected failures use the existing result types: DbResult / DbResult<T> in repositories, CreateUserResult-style
  records in handlers. Validated values follow UserId.TryCreate: return T? when invalid.

## Postgres via Npgsql [cs sql]
- Always parameterised queries ($1 / @name parameters), never string-concatenated values.
- timestamptz not timestamp; text not varchar(n); identity columns not serial.
- Prefer one statement (INSERT ... ON CONFLICT, CREATE ... IF NOT EXISTS) over check-then-act.
