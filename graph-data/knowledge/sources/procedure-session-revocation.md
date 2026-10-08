Title: PotchMatch Procedure - Session Revocation (DOC-SESSION-REVOCATION)
Source URL: n/a (internal PotchMatch document, no public URL)
Attribution/licence: PotchMatch internal SOC procedure, written for ITRI623 coursework; no external licence

# Session Revocation (DOC-SESSION-REVOCATION)

Session Revocation (DOC-SESSION-REVOCATION) is a Document, an incident response procedure.

## Purpose
Cut off an attacker who is using a valid account or stolen token, and force the legitimate user to authenticate again.

## Trigger alerts
- "Unauthorised Endpoint Access"

## Steps
1. Confirm the alert and identify the user and endpoint involved.
2. Revoke all active tokens and sessions for the user.
3. Disable the account until ownership is verified.
4. Review recent activity of the account for data accessed.
5. Require re-authentication with multi-factor authentication.
6. Document the incident and review role checks on the endpoint.

## Relationships
- Session Revocation (DOC-SESSION-REVOCATION) supports response to Valid Accounts (T1078).
- Session Revocation (DOC-SESSION-REVOCATION) implements Multi-factor Authentication (M1032).
