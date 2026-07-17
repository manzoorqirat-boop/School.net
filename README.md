# QMSoft School — .NET 8 skeleton

The spine. Entities and endpoints hang off this; nothing here is a placeholder.

Built against `API-CONTRACT.md` and `SCHEMA-MAP.md`. Where a decision looks
arbitrary, it's pinned to a contract clause — the comment says which.

> **No dotnet SDK in this sandbox** — same constraint as Parakh/Swad. C# is
> syntax-checked by eye, not compiled. Expect to fix a using or two on first
> `dotnet build`. The *logic* (query-filter truth table, crypto wire format) was
> verified by simulation, not assumption — see "Verified" below.

## Files

```
src/QMSoft.Api/
  Program.cs                                  pipeline wiring
  QMSoft.Api.csproj                           pinned deps
  Common/
    ErrorCodes.cs                             the code strings api.ts switches on
    AppException.cs                           AppError port + 402 factories
    Paged.cs                                  { items, pagination } + legacy aliases
    EmptyStringEnumConverter.cs               "" ⇄ null for Student enums
  Data/
    AppDbContext.cs                           global query filter + SaveChanges stamping
  Domain/Entities/
    Base.cs                                   IEntity / IAuditable / ISoftDeletable / ITenantScoped
  Infrastructure/
    Tenancy/TenantContext.cs                  JWT → tenant, no DB hit
    Crypto/CryptoService.cs                   AES-256-GCM, enc:v1: wire format
    Auth/TokenService.cs                      access/refresh generation
    Auth/JwtEvents.cs                         TOKEN_EXPIRED vs INVALID_TOKEN
    Auth/TokenRevocationStore.cs              Redis blacklist
  Authorization/
    PrivilegeDefaults.cs                      config/roles.js, verbatim
    PrivilegeAuthorization.cs                 [RequirePrivilege("x:y")]
  Middleware/
    ExceptionHandlingMiddleware.cs            the error envelope
    TokenRevocationMiddleware.cs              blacklist check
```

## The five things that would silently break the frontend

1. **`JwtEvents.cs`** — `TOKEN_EXPIRED` → api.ts refreshes and retries;
   `INVALID_TOKEN` → api.ts nukes the session and redirects. ASP.NET's default
   401 has an empty body and *no code*, so api.ts would never refresh and every
   user would bounce to login every 15 minutes. Every 401 is hand-written.
   `SecurityTokenExpiredException` is checked **first** — it derives from the
   generic validation exception.

2. **`ClockSkew = TimeSpan.Zero`** (Program.cs) — the .NET default is **5
   minutes**, which keeps a 15-minute token alive for 20 and desyncs the
   frontend's refresh timing from the server's idea of expiry.

3. **`_id`** — `Base.cs` puts `[JsonPropertyName("_id")]` on `IEntity.Id`. A
   naming policy cannot do this; it isn't a casing difference.

4. **Money stays a JSON number** — `decimal` serializes as `1234.50`, which is
   what `inr()` expects. Adding a string converter breaks every rupee in the app.

5. **`EmptyStringToNullEnumConverter.Write`** emits `""`, not `null` —
   `StudentForm.tsx` binds a `<select>`; a null gives an unselected dropdown
   instead of the "—" placeholder.

## Deliberate deviations from the Node app

| Node | Here | Why |
|---|---|---|
| `cluster` fork per core | dropped | Kestrel is already multi-threaded; Railway = 1 vCPU |
| Redis user cache (5 min) | dropped | solved a Mongoose problem Npgsql pooling doesn't have |
| Redis school cache (5 min) | dropped | same; `TenantContext` reads JWT claims, no DB hit |
| Redis token blacklist | **kept** | correctness, not perf — logout can't kill a stateless JWT |
| `cors({ origin: true, credentials: true })` | explicit origins | reflect-any + credentials is rejected by browsers anyway; we use Bearer, not cookies |
| `{ schoolId: req.tenantId }` per query | global query filter | forget-once-and-leak → structurally impossible |
| 20× `pre('save')` `updatedAt` | `SaveChanges` stamp | the hooks don't fire on `updateOne` — already unreliable today |
| `invalidatedTokens[]` on User | dropped | dead DB half of a blacklist that already moved to Redis |
| `ws` dependency | dropped | zero imports anywhere (API-CONTRACT §2) |

## Verified (not assumed)

**Query filter truth table** — simulated all 9 cases:

| Case | Visible |
|---|---|
| school_admin A → A row | ✔ |
| school_admin A → B row | ✘ |
| school_admin A → soft-deleted A | ✘ |
| superadmin → A row | ✔ |
| superadmin → B row | ✔ |
| superadmin → soft-deleted | ✘ |
| **unauthenticated → any row** | **✘** |
| teacher A → B row | ✘ |
| parent A → A row | ✔ |

> The unauthenticated row is why `IsFilterActive` is `!IsSuperAdmin` and **not**
> `SchoolId.HasValue && !IsSuperAdmin`. The naive version deactivates the filter
> when there's no principal, exposing every school to any handler that forgot to
> authorise. Fail closed: no `SchoolId` ⇒ `e.SchoolId == null` ⇒ matches nothing.
> Do not "simplify" it back.

**Crypto wire format** — generated real ciphertext with Node's
`createCipheriv('aes-256-gcm')` and confirmed against the C# parser:
`enc:v1:<iv:12b><tag:16b><ct>`, lowercase hex, 3 colon-separated parts. ✔

## Phase 1 — the FK spine (built)

`School` → `User` → `Student`, the three tables everything else references.

| File | Notes |
|---|---|
| `Domain/Entities/Enums.cs` | 9 enums, explicit `[EnumMember]` wire values |
| `Domain/Entities/School.cs` | tenant root; `PlanAllows` / `MaxStudents` from planGuard |
| `Domain/Entities/User.cs` | nullable `SchoolId` (superadmin); `teacherId` dropped |
| `Domain/Entities/Student.cs` | Aadhaar masking setter; share tokens; siblings; passedExams |
| `Data/Configurations/*.cs` | citext, partial uniques, pg_trgm, check constraints |
| `Data/DatabaseSeeder.cs` | seed.js port + Parakh `Seed__*` / `Rescue__*` |
| `Infrastructure/EnumMemberNameTranslator.cs` | Npgsql label mapping |

### Enum wire values are NOT derivable — this bit me twice

Mechanical PascalCase→snake_case is wrong for **8 of 33** values. Verified by
simulating the rule against the Mongo models:

| C# member | snake_case gives | Mongo/frontend expects |
|---|---|---|
| `SuperAdmin` | `super_admin` | **`superadmin`** |
| `GEN` | `g_e_n` | **`GEN`** (also OBC/SC/ST/EWS) |
| `Hindu` | `hindu` | **`Hindu`** (every religion) |

A wrong `UserRole` label is the worst failure in the app: the JWT role claim
never matches `PrivilegeDefaults`, so **every privileged endpoint 403s** with no
obvious cause. So every member carries an explicit `[EnumMember]`, and three
places read from it — the JSON converter, the Npgsql translator, and the DDL
label list. A script checks all three agree; they do.

**Npgsql's `INpgsqlNameTranslator` is string→string with no `MemberInfo`
overload** (checked against the v8.0.5 source — I'd assumed one existed and was
wrong). A translator therefore can't read attributes at call time, so
`EnumMemberNameTranslator` pre-builds the map. And it must be **one instance per
enum**: `Gender.Other`/`TransportMode.Other` are `"other"` but `Religion.Other`
is `"Other"` — a shared map collides on the member name.

## Phase 1 cont. — attendance cluster (built)

`Attendance`, `TeacherAttendance`, `ClassTeacher`, `Subject` + 3 enums.

**Both NULL traps closed with `AreNullsDistinct(false)`** — verified to exist in
the Npgsql efcore.pg v8.0.10 source; emits `NULLS NOT DISTINCT` (Postgres 15+,
which Railway runs), making nulls collide exactly as Mongo's unique indexes do:

- `attendance`: daily rows get a partial unique on `(school, student, date)
  WHERE mode='daily'`; period rows get the full key with nulls-not-distinct so
  a NULL subject can't duplicate.
- `class_teachers`: **a trap the schema map missed.** `subject` is optional AND
  in the Mongo unique key — a plain Postgres unique would allow unlimited
  duplicate homeroom assignments (every one has `subject=NULL`). One
  nulls-not-distinct index restores the Mongo semantics.

**Business rules extracted to one place, and verified against the Node math:**

- `AttendanceRules` — holiday excluded from total, late counts as present,
  percentage to 1 decimal. Simulated 8 cases incl. the exact-half boundaries
  where JS `Math.round` and banker's rounding diverge — `AwayFromZero` matches.
- `TeacherAttendanceRules.UnpaidWeight` — the verbatim `UNPAID_WEIGHT` table
  (absent=1, half_day=0.5, unpaid_leave=1, rest 0). **Payroll must read THIS**
  when it's ported; reimplementing the weights there recreates the drift this
  table exists to prevent.

Also: the period⇔mode linkage is now a CHECK constraint (was controller
convention in Node), and `Subject.code` uppercases via a ValueConverter (was a
Mongoose setter).

## Not yet built

- 17 remaining entities + migrations (`SCHEMA-MAP.md`)
- `IPrivilegeResolver` impl (needs `RolePrivilege` entity)
- `AuthController` — login/refresh/logout/me/change-password
- `planGuard` wiring — the entity logic exists (`School.PlanAllows`), the filter doesn't
- Rate limiting config (`loginLimiter`, `refreshLimiter` are distinct)
- Serilog wiring

## Deliberate deviations in the spine

**`School.RazorpayKeySecret` is `[JsonIgnore]`.** The Mongo model sets
`toJSON: { getters: true }`, so the secret **decrypts into every school
payload** — including the `school` object from `/api/auth/login`, which api.ts
writes to `localStorage` as `vy_school`. That puts a live payment secret in the
browser of every user of that school. Nothing in the frontend reads it.

**`Student.ShareToken` is `[JsonIgnore]`.** It's a bearer credential granting
unauthenticated read. Returning it in every student payload lets anyone who can
list students mint public links. `POST /:id/share` returns it explicitly, once.

**`User.Password` / `RefreshTokens` are `[JsonIgnore]`.** Replaces
`toSafeJSON()`'s `delete obj.password`, which is opt-**out** — miss one code
path and the hash ships. This is opt-in: unserializable from anywhere.

**Seeder refuses a default superadmin password.** `seed.js` defaults to
`Super@123`; fine for a local script, but this runs on Railway boot where that's
a publicly-known credential with cross-tenant read. Production requires
`Seed__SuperAdminPassword`.

**`uq_students_school_admission` is not partial on `is_deleted`.** Mongo's index
has no `isDeleted` in the key, so a soft-deleted student blocks reuse of their
admission number forever. Preserved — admission numbers are a permanent register
and must not be recycled.

## Two things to decide before Phase 1 proceeds

**Redis in production.** The blacklist falls back to `AddDistributedMemoryCache`
when `REDIS_URL` is unset. On single-instance Railway that's fine. On a scaled
deployment it means a logout on instance A leaves the token live on instance B.
Set `REDIS_URL`, or accept it knowingly.

**Revocation fails open.** `TokenRevocationStore.IsRevokedAsync` returns `false`
when the cache is unreachable — a Redis blip would otherwise 401 the entire
fleet. The trade: a narrow window where a logged-out token works until its
15-minute expiry. This matches the Node original's "graceful degradation", but
it *is* a security trade-off and should be a conscious one.

## Environment

```bash
DATABASE_URL=postgresql://...          # or ConnectionStrings__Postgres
JWT_SECRET=<32+ bytes>                 # fails at boot if shorter
JWT_REFRESH_SECRET=<32+ bytes>         # must differ from JWT_SECRET
JWT_EXPIRY=15m                         # default 15m
JWT_REFRESH_EXPIRY=7d                  # default 7d
ENCRYPTION_KEY=<64 hex chars>          # openssl rand -hex 32 — FATAL if missing in prod
CORS_ORIGINS=https://app.example.com   # required in production
REDIS_URL=redis://...                  # optional; see above
```

## Next

Phase 1 proper: the 24 entities. `School` → `User` → `Student` is the FK spine —
everything else references those three. Start there, and get the partial unique
indexes on `attendance` and `payments` right the first time (`SCHEMA-MAP.md`
§2.1, §3.2) — `NULL != NULL` in Postgres is the easiest correctness bug in the
whole migration to introduce and the hardest to notice.
