# ledger

A small bank-style backend in C# and .NET 8 on Postgres. I work in C# every day but never on money, so this is where I learn what goes wrong when you move it.

WIP: users, accounts and top-ups work. Transfers are next.

## What it does

- users sign up and log in, passwords are PBKDF2 and the token is a hand-rolled HMAC JWT
- a user can hold one account per currency, accounts open at 0
- money only comes in through a top-up. The top-up is stored as pending, the payment provider is asked to charge, and the account is only credited when the provider's signed callback says the charge went through
- a background job expires top-ups that never got a callback
- an account with money in it can't be deleted

## Layout

- `Domain` - Account, User and value types (`Amount`, `Currency`, `Email`, `Password`) that can't be built invalid
- `Application` - one handler per action (`CreateAccountHandler.Handle(command) -> result`), ports for the repository and provider
- `Infrastructure` - Npgsql repositories with raw SQL, auth, the callback signature check
- `Api` - minimal API endpoints, the background job, startup creates the tables
- `FakeProvider` - a separate service that will play the payment provider, in progress (the Api uses a stub for now)
- `Ledger.Tests` - xUnit, including repository tests against a real Postgres database

Handlers return result types instead of throwing, so every outcome an endpoint can see is in the type. Warnings fail the build and nullable is on everywhere.

## Run

Needs Postgres. The dev connection string and dev-only secrets are in `Api/appsettings.Development.json`.

```sh
dotnet run --project Api
dotnet test   # repository tests use LEDGER_TEST_CONNECTION, or the default in DatabaseFixture.cs
```

## Endpoints

| | |
|---|---|
| `POST /users` | sign up |
| `POST /users/auth/login` | get a token |
| `POST /accounts` | open an account in a currency |
| `GET /accounts/{email}` | accounts for a user |
| `GET /accounts/user/{userId}` | accounts for a user id |
| `POST /accounts/topup` | start a top-up |
| `POST /accounts/topup/callback` | provider callback, checked by signature |
| `DELETE /accounts/{id}` | close an empty account |

## Todo

- [ ] store money as integer pence everywhere
- [ ] transfers between two accounts
- [ ] idempotency keys on client requests
- [ ] real fake provider: charges processed in the background, calls back
- [ ] deleting an account with top-up history
- [ ] CI with a Postgres service
