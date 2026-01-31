Klar – hier ist eine **vollständige, solo-dev-taugliche To-Do-Liste** als roter Faden. Sie zielt auf „Production-grade Secure Baseline“ (ohne Enterprise-Overkill), passt zu **.NET / Identity / HotChocolate / GraphQL** und berücksichtigt **HttpOnly Refresh-Cookie + Access Token im Response** (mein empfohlenes Pattern).

---

## Phase 0: Zielbild & Entscheidungen

**Ziel:** Session-Diebstahl & Missbrauch minimieren, dabei simpel bleiben.

* [ ] **Auth-Pattern festlegen:** Refresh Token **nur** im HttpOnly Cookie, Access Token (JWT) **im Response** (Memory-only im Frontend).
* [ ] **Domain-Setup klären:** gleiche Site vs. cross-site (entscheidet über SameSite/CSRF).
* [ ] **Token-Lifetimes festlegen:**

  * Access: 10–15 Minuten
  * Refresh: 7–30 Tage
  * Absolute Session Lifetime: z. B. 30 Tage ab Session-Start

---

## Phase 1: Fundament & Datenhaltung (DB)

### 1.1 Identity & User

* [ ] `AppUser : IdentityUser` erstellt
* [ ] `AppDbContext : IdentityDbContext<AppUser>`

### 1.2 Refresh Token Entity (sicher)

* [ ] RefreshToken Tabelle angelegt mit:

  * `TokenHash` (kein Klartext)
  * `Expires`, `Created`, `Revoked` als **DateTimeOffset**
  * `CreatedByIp`, `RevokedByIp`, `ReasonRevoked` (optional)
  * `UserId` + Nav-Property
* [ ] **Session/Family hinzufügen** (empfohlen):

  * `SessionId` (GUID) pro Login
  * Optional: `AbsoluteExpires` (oder ableitbar aus SessionStart)
* [ ] **Chain-Tracking sauber machen:**

  * lieber `ReplacedByTokenId` (FK) statt Hash-String

### 1.3 EF Core Constraints/Indizes

* [ ] Index auf `UserId`
* [ ] Unique Index auf `TokenHash`
* [ ] Index auf `Expires` (Cleanup)
* [ ] Relationship Config:

  * User 1:n RefreshTokens (Cascade ok)
  * `ReplacedByTokenId` (Restrict delete)

### 1.4 Migration

* [ ] Migration & DB Update laufen lassen
* [ ] Seeder optional: Test-User / Dev-only

---

## Phase 2: Core Logic – AuthService / TokenService

### 2.1 Kryptografie & Helpers

* [ ] **Secure random token generation**

  * 32–64 Bytes random, Base64Url oder Hex
* [ ] **Hashing**:

  * SHA-256 über `(token + pepper)`
  * Pepper aus Secret/Env (nicht in Repo)
* [ ] Vergleich konstantzeitnah (nicht super kritisch bei Hash-Lookup, aber sauber)

### 2.2 JWT Access Token

* [ ] JWT Signing Key (mind. 256-bit symm) über Secrets
* [ ] Claims:

  * `sub` = UserId
  * `email`
  * `roles` (falls genutzt)
  * optional `jti`
* [ ] Expiry 10–15 Minuten
* [ ] Token Validation Parameters sauber konfigurieren (Issuer/Audience, ClockSkew klein)

### 2.3 Refresh Token Lifecycle

* [ ] Create refresh token record:

  * speichere **nur Hash**
  * setze `Expires`, `Created`, `CreatedByIp`, `SessionId`
* [ ] Rotation flow:

  1. incoming refresh cookie → hash → finde in DB
  2. prüfe `IsActive`
  3. revoke old + create new + chain link
* [ ] **Reuse Detection** (Kill Switch):

  * Wenn Token **nicht aktiv** / bereits revoked / nicht gefunden, aber Cookie vorhanden:

    * **revoke all tokens of session** (oder userweit)
    * logge Security Event

### 2.4 Absolute Session Lifetime

* [ ] Beim Login SessionStart setzen (oder `AbsoluteExpires`)
* [ ] Refresh verweigern, wenn absolute lifetime überschritten

---

## Phase 3: GraphQL Mutations (API)

> Empfehlung: Auth als separate Mutation-Group (AuthMutations)

### 3.1 Register

* [ ] Input: email, password (+ optional name)
* [ ] Identity `UserManager.CreateAsync`
* [ ] optional: Email confirm später

### 3.2 Login

* [ ] Input: email, password
* [ ] Check via `SignInManager.CheckPasswordSignInAsync`
* [ ] **Generic error**: “Invalid credentials”
* [ ] On success:

  * create new **SessionId**
  * issue refresh token → set HttpOnly cookie
  * return access token (JWT) in response
* [ ] optional: revoke previous sessions? (nur wenn du “single session” willst)

### 3.3 Refresh

* [ ] Input: **kein** Body nötig (liest Refresh Cookie)
* [ ] Rotation ausführen
* [ ] Wenn ok: set neuen Refresh Cookie + return neuen Access JWT
* [ ] Wenn reuse detected: revoke session/user + Cookies löschen + “Unauthorized”

### 3.4 Logout

* [ ] Current refresh token (cookie) → hash → find → revoke
* [ ] Cookies löschen

### 3.5 Me / WhoAmI Query (Test)

* [ ] Query, die `UserId/Email/Roles` zurückgibt (auth-required)

---

## Phase 4: Cookie, CORS, CSRF – richtig konfigurieren

### 4.1 Cookie Settings (Refresh Cookie)

* [ ] `HttpOnly = true`
* [ ] `Secure = true` (Prod)
* [ ] `Path` auf refresh endpoint scope (z. B. `/graphql` oder spezifisch)
* [ ] `SameSite`:

  * **Lax** als Default (oft best)
  * **Strict** wenn Frontend/Backend gleiche Site und keine Probleme
  * **None + Secure** wenn cross-site nötig

### 4.2 CSRF-Entscheidung

* [ ] Wenn `SameSite=None` oder cross-site: **Anti-CSRF Token** einbauen (Double Submit)
* [ ] Wenn `Lax/Strict` und gleiche Site: meist ok ohne extra CSRF (aber dokumentieren!)

### 4.3 CORS

* [ ] Nur erlaubte Origins
* [ ] `AllowCredentials()`
* [ ] Keine Wildcards mit Credentials
* [ ] Preflight korrekt

---

## Phase 5: Hardening (Missbrauch & Betrieb)

### 5.1 Rate Limiting & Lockout

* [ ] Rate limit auf:

  * Login
  * Refresh
  * Register (optional)
* [ ] Identity Lockout:

  * z. B. 5 fails → 10 min lock
* [ ] Keine User-Enumeration (hast du)

### 5.2 Logging & Monitoring

* [ ] Strukturierte Logs:

  * AuthSuccess (ohne Secrets)
  * AuthFailed
  * TokenRefreshed
  * TokenReuseDetected
  * SessionRevoked
* [ ] CorrelationId / TraceId (nice to have)

### 5.3 GraphQL Protections

* [ ] Depth/Complexity Limit
* [ ] Timeout/Request size limits
* [ ] Introspection in Prod ggf. eingeschränkt

### 5.4 Security Headers (wenn du Web auslieferst)

* [ ] HSTS (Prod)
* [ ] CSP (wenn UI auf gleicher Origin)
* [ ] Referrer-Policy, X-Content-Type-Options

---

## Phase 6: Tests (minimal, aber sinnvoll)

* [ ] Unit: Token hashing deterministisch (mit fixed pepper)
* [ ] Integration:

  * Register/Login ok → cookie gesetzt, access returned
  * Refresh rotates → old revoked, new active
  * Refresh reuse → session killed, unauthorized
  * Rate limit greift
  * Lockout greift
* [ ] Negative tests:

  * falsches Passwort → generic error
  * expired refresh → unauthorized + cookie cleared

---

## Phase 7: Ops / Dokumentation

* [ ] Secrets per Env/Secret Manager (JWT key, pepper)
* [ ] Prod config: HTTPS enforced
* [ ] Cleanup-Job:

  * revoke/ delete expired refresh tokens regelmäßig
* [ ] Kurze Doku: Threat model + Entscheidungen (SameSite/CSRF, lifetimes)

---

# Mini-DoD „Secure Baseline“ (wenn du fertig bist)

* [ ] Refresh Token **gehasht** + unique index
* [ ] Rotation + Reuse Detection + Kill Switch
* [ ] Refresh im HttpOnly Cookie korrekt konfiguriert
* [ ] CSRF/CORS sauber für dein Setup
* [ ] Rate limit + Lockout aktiv
* [ ] Security logging ohne Secrets
* [ ] GraphQL Depth/Complexity Limit

---

Wenn du willst, mache ich dir als nächsten Schritt eine **konkrete Implementierungsreihenfolge in 12 kleinen Tasks** (je ~30–90 min) – oder direkt ein **Code-Skeleton** für `AuthService` + `Login/Refresh/Logout` Mutations (HotChocolate), passend zu deiner Ordnerstruktur.
