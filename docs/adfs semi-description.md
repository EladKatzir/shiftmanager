
//not to be taken as a single truth. taken from multiple half screenshots.
ADFS + Griffin Authentication Service

Final Documentation (Official Reference vs. Migration Plan)

This document consolidates what’s shown in the screenshots and separates:

Official documentation: stable facts (concepts, APIs, library methods, claims behavior) that describe “how it works”.

Migration plan: environment-specific execution steps to deploy/enable it in your OpenShift + DNS/TLS + backend.

Where the screenshots don’t provide an exact value (e.g., a specific Helm command or exact env-var name), the doc uses TODO placeholders instead of guessing.

1) Scope

This guide covers:

What Griffin is and how it integrates with ADFS

Griffin service endpoints (Swagger)

Griffin JS library API

Default claims behavior

A practical migration plan for deploying Griffin on OpenShift, enabling DNS/TLS, verifying, and integrating your backend via middleware.

2) Terms

ADFS: Active Directory Federation Services (Microsoft federation identity provider).

Token: Authentication artifact issued after login, used to prove identity.

Claims: User attributes embedded in the token (e.g., name, account, UPN).

Redirect: Browser navigation to/from ADFS during login.

Client ID / Client Secret: Application registration credentials used by ADFS.

Griffin: Internal auth microservice + tooling that helps teams integrate with ADFS and expose a standard API.

Part A — Official Documentation (Reference)
A1) Authentication model (Redirect-based)

The model shown is a standard redirect flow:

User accesses your backend

Backend redirects the user to ADFS

ADFS performs domain authentication

ADFS redirects back to your backend with an authentication artifact (token / code → token)

Backend uses token to identify user and enforce access

A2) Griffin Service API (Swagger)

When deployed, the Griffin auth microservice exposes a Swagger UI titled:

“Authentication Service For The People”

Authorization endpoints

GET /authorization/getClaims
Returns claims extracted from a provided token.

GET /authorization/validate
Validates a provided token.

Authentication endpoints

GET /authentication
Starts the auth flow.

GET /authentication/login
Redirect/login step (ADFS handoff).

GET /authentication/claimToken
“Claim” token using a generated hash/token mechanism (as shown in the UI).

Health endpoints

GET /beep
Health check endpoint.

GET /version
Returns the service version.

A3) Token structure and claims
Token structure

The documentation describes a token as 3 parts:

Metadata/header

Payload (claims and useful data)

Signature (integrity / tamper protection)

Default claims (when using Griffin)

The official doc states that Griffin config includes default claims such as:

sAMAccountName

DisplayName

Surname

GivenName

UPN

Adding more claims

Official doc states: additional claims should be requested via the relevant ADFS-owning team
(mentioned in the doc as “Team Genesis (6828)”).

A4) Griffin JS Library (Official API)
Import + initialization
import GriffinAuth from '@hasamba/griffin-auth'

const griffin = new GriffinAuth(your_griffin_fqdn)

Methods

generateToken() (async)
Generates a token using the griffin-auth microservice. Returns a string token.

getClaims(token: string) (sync)
Returns an object with claims from the token.

validateToken(token: string) (async)
Returns boolean. Throws if invalid.

A5) Example (.NET / MSAL-style) reference

The screenshots include a reference example using Microsoft.Identity.Client (MSAL) and an ADFS authority, using an interactive flow to acquire an access token.

This is shown as a reference pattern for clients that authenticate directly via ADFS using MSAL-style libraries.

Part B — Migration Plan (How we deploy + integrate in our environment)
B1) Goal

Deploy Griffin in OpenShift, expose it securely (DNS + TLS), confirm the Swagger page works, and integrate it with your backend so requests are authenticated and user identity is loaded as claims.

B2) Step-by-step migration
Step 1 — Create the Griffin package (Helm ZIP) via the Griffin/DevOps flow

Outcome: a ZIP containing a Helm chart (Chart.yaml, values.yaml, templates/).

From the screenshots, the flow includes:

Create a Deployment

Choose Griffin

Provide your app details

Provide the ClientSecret

Fill “Your Griffin Service Hostname” with the ADFS hostname

Download the ZIP artifact

Important operational note (from the guide):

Don’t name it exactly as “your system name” (guide warns this causes issues).
Example used: 990portal-auth.

TODO: Capture the exact fields in the Griffin UI (if we want the doc to be fully prescriptive).

Step 2 — Log in to OpenShift and select the target namespace

From OpenShift console:

Use “Copy login command” and run oc login ...

Select the target project:

oc project <your-namespace>


Operational constraint (from the guide):

You cannot deploy multiple Griffin services into the same OpenShift project (as stated in the screenshots).
If you need a project, the guide references contacting “7190”.

Step 3 — Deploy the Helm chart into OpenShift

Use the ZIP produced in Step 1.

TODO (Required): Insert the exact helm command your org uses (the screenshots did not show it clearly).
Common patterns are helm upgrade --install ... or an internal wrapper script.

Expected outputs

Deployed pods/services

An OpenShift Route for the service

Step 4 — DNS + TLS enablement (Next + OpenShift Route)
4.1 Create DNS record (Next)

The guide shows DNS is created to point the service hostname to the OpenShift ingress:

ingress.ocp4-five.d8200.mil (example shown)

4.2 Issue certificate (Next)

Create a certificate for the same hostname.

4.3 Attach certificate to OpenShift Route

OpenShift:

Routes → your route → Actions → Edit

Populate:

Certificate

Private key

CA certificate (chain)

CA certificate note (from the guide):

Use the unit CA chain. The guide suggests extracting it from an internal HTTPS chain if needed.

Step 5 — Verification

Browse to the Griffin service URL.

✅ Success condition: Swagger loads and displays
“Authentication Service For The People”

B3) Backend integration plan (middleware)

This is the recommended backend integration approach described in the guide.

Middleware 1: ADFSAuthMiddleware

Purpose: enforce authentication.

If request has no valid auth state/token → return 302 redirect to ADFS login.

Middleware 2: LoadUserInfoMiddleware

Purpose: load user identity.

Uses token from state/cookie/session

Calls Griffin/ADFS to load user claims/user info

Attaches user identity to request context

Request lifecycle

First request hits backend → ADFSAuthMiddleware redirects to ADFS

After ADFS login → user returns to backend callback with auth artifact

Backend validates and stores token (cookie/session)

Subsequent requests:

validate token

load claims/user

proceed

Whitelisting
The guide notes: middleware runs on all requests except a whitelist (e.g., health endpoints, static assets, auth callback route).

TODO: Define the whitelist for your service explicitly (route patterns).

B4) HTTPS redirect fix (required for one-aman)

The guide explicitly calls out an integration issue:

Redirects must use HTTPS, not HTTP.

Plan

Update the Griffin/ADFS redirect environment configuration (via Helm chart values or deployment env vars) to force https.

TODO (Required): Insert the exact env var name / value key once captured from the deployment/chart, since it’s not readable in the screenshots.

B5) Rollout checklist

Infrastructure

 Griffin Helm chart ZIP generated

 Deployed to correct OpenShift namespace

 Route exists and is reachable internally

 DNS points to ingress

 TLS cert + key + CA chain added to Route

 Swagger loads successfully

Integration

 Backend redirect → ADFS works

 Callback route receives auth artifact

 Token stored securely (cookie/session)

 Token validation is enforced on protected routes

 Claims loaded and available to request handlers

 HTTPS redirect enforced for one-aman

B6) Troubleshooting quick hits

Swagger not reachable

Check Route host + TLS settings

Check service/pod health

Confirm DNS resolves to ingress

Browser redirects loop

Most common: HTTP/HTTPS mismatch or wrong redirect URL

Confirm redirect config is HTTPS

Token validates but claims missing

Confirm token actually includes needed claims

Request additional claims via the ADFS/claims owning team (Genesis mentioned)

Appendix — What belongs where?
Official documentation includes

Token structure (3 parts)

Default claims list

“How to request more claims”

Swagger endpoints and their names

Griffin JS library API signatures

Migration plan includes

Deploying Griffin with Helm in OpenShift

DNS + TLS via Next and OpenShift Route

Verification workflow (“Authentication Service For The People”)

Backend middleware design and whitelisting

HTTPS redirect enforcement for one-aman

Operational notes about namespace/project constraints