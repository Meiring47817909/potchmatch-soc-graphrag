Title: Valid Accounts (T1078)
Source URL: https://attack.mitre.org/techniques/T1078/
Attribution/licence: MITRE ATT&CK(R) (c) The MITRE Corporation; paraphrased here under the ATT&CK Terms of Use (https://attack.mitre.org/resources/terms-of-use/)

# Valid Accounts (T1078)

Valid Accounts (T1078) is a Threat. An adversary obtains and uses credentials of an existing legitimate account, for example a stolen player or admin login or session token. Because the activity uses real accounts it blends in with normal use, and can be used for initial access, persistence, privilege escalation and defence evasion.

Valid Accounts exploits broken access control. The vulnerability Broken Access Control (VULN-BROKEN-ACCESS-CONTROL) lets a stolen or low-privilege account reach endpoints and data it should not. Valid Accounts also exploits weak passwords: VULN-WEAK-PASSWORDS makes accounts easy to take over.

## Mitigations
- Multi-factor Authentication (M1032) mitigates Valid Accounts (T1078): a stolen password or session is not sufficient on its own.
- Account Use Policies (M1036) mitigates Valid Accounts (T1078): policies limit when, where and how accounts may be used.
- Password Policies (M1027) mitigates Valid Accounts (T1078): strong passwords reduce account takeover.

## Detection
Access to endpoints outside the account's role is a sign of misuse. In PotchMatch the alert "Unauthorised Endpoint Access" indicates T1078. The session revocation procedure (DOC-SESSION-REVOCATION) supports response to T1078.
