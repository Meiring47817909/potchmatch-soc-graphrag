Title: PotchMatch Procedure - Password Reset (DOC-PASSWORD-RESET)
Source URL: n/a (internal PotchMatch document, no public URL)
Attribution/licence: PotchMatch internal SOC procedure, written for ITRI623 coursework; no external licence

# Password Reset (DOC-PASSWORD-RESET)

Password Reset (DOC-PASSWORD-RESET) is a Document, an incident response procedure.

## Purpose
Return control of a possibly compromised account to its owner and make guessed credentials useless.

## Trigger alerts
- "Multiple Failed Logins"

## Steps
1. Confirm the alert and identify the targeted account and source IP.
2. Lock the account and revoke its active sessions.
3. Verify the owner's identity through a trusted channel.
4. Issue a password reset link with short expiry.
5. Require a new password that meets the password policy.
6. Notify the owner and record the incident.

## Relationships
- Password Reset (DOC-PASSWORD-RESET) supports response to Brute Force (T1110).
- Password Reset (DOC-PASSWORD-RESET) implements Password Policies (M1027).
