Title: Multi-factor Authentication (M1032)
Source URL: https://attack.mitre.org/mitigations/M1032/
Attribution/licence: MITRE ATT&CK(R) (c) The MITRE Corporation; paraphrased here under the ATT&CK Terms of Use (https://attack.mitre.org/resources/terms-of-use/)

# Multi-factor Authentication (M1032)

Multi-factor Authentication (M1032) is a Control. It requires two or more independent proofs of identity, such as a password plus a one-time code, so a stolen or guessed password alone does not grant access.

- Multi-factor Authentication (M1032) mitigates Brute Force (T1110).
- Multi-factor Authentication (M1032) mitigates Valid Accounts (T1078).
- Multi-factor Authentication (M1032) compensates for the vulnerability Weak Passwords (VULN-WEAK-PASSWORDS).
- The session revocation procedure (DOC-SESSION-REVOCATION) implements Multi-factor Authentication (M1032) by forcing re-authentication with a second factor after revoking sessions.
