# Ledger rules for pair (read on every job, on top of ~/.pair/rules.md).
# Short on purpose: a local model follows a handful of concrete rules, not an essay.

## Honest functions: the signature tells the whole truth
- Expected failures are part of the return type, never a thrown exception: return DbResult / DbResult<T>
  or a result record (like CreateUserResult). Throw only for programmer errors (a broken invariant, a bug).
- A value that can be missing is nullable (T?) or a TryX method; never return null from a non-nullable type.
- Inputs that can be invalid are validated by a factory that can say no (UserId.TryCreate style), not by
  a constructor or Create that accepts anything.
- No hidden dependencies: never call DateTime.UtcNow, Guid.NewGuid, Random or static/global state inside
  domain logic. Take the value (or an IClock / ID generator) as a parameter or constructor dependency.
- No hidden side effects: a method named Get/Find/Calculate/Is/Has must not write anything.
- No meaningless return values (e.g. returning 1): use Task for "done", or a result type for "done or failed".

## As pure as reasonably possible
- Decide in pure code, act at the edges: domain types and calculations take inputs and return outputs;
  only repositories/handlers talk to the database, clock or network.
- Don't mutate arguments; return a new value instead.

## Postgres (raw SQL via Npgsql)
- Always parameterised queries ($1 / @name parameters), never string-concatenated values.
- timestamptz not timestamp; text not varchar(n); identity columns not serial.
- Prefer one statement (INSERT ... ON CONFLICT, CREATE ... IF NOT EXISTS) over check-then-act.
