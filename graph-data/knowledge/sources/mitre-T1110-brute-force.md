Title: Brute Force (T1110)
Source URL: https://attack.mitre.org/techniques/T1110/
Attribution/licence: MITRE ATT&CK(R) (c) The MITRE Corporation; paraphrased here under the ATT&CK Terms of Use (https://attack.mitre.org/resources/terms-of-use/)

# Brute Force (T1110)

Brute Force (T1110) is a Threat. An adversary who does not know the valid credentials for an account repeatedly guesses them until one works. Success gives access to the account and often to the data and functions behind it.

Brute force exploits weak passwords. The vulnerability Weak Passwords (VULN-WEAK-PASSWORDS) is what makes guessing practical: short, common or predictable passwords fall quickly. Brute force also exploits the absence of rate limiting. The vulnerability No Rate Limit (VULN-NO-RATE-LIMIT) lets an attacker try unlimited logins without being slowed or locked out.

## Sub-techniques
- Password Guessing (T1110.001): guessing passwords for a known account, one or many attempts per account.
- Password Spraying (T1110.003): trying one or a few common passwords across many accounts so no single account is locked.
- Credential Stuffing (T1110.004): replaying username and password pairs leaked from other breaches against this system.

## Mitigations
- Multi-factor Authentication (M1032) mitigates Brute Force (T1110): a guessed password alone is not enough.
- Account Use Policies (M1036) mitigates Brute Force (T1110): lockout and attempt limits stop unlimited guessing.
- Password Policies (M1027) mitigates Brute Force (T1110): strong, long passwords resist guessing.

## Detection
Many failed logins for one account, or one source failing across many accounts, indicate brute force. In PotchMatch the alert "Multiple Failed Logins" indicates T1110. The password reset procedure (DOC-PASSWORD-RESET) supports response to T1110, and the IP blocking procedure (DOC-IP-BLOCKING) also supports response to T1110.
