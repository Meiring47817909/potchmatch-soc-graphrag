Title: A01:2021 Broken Access Control
Source URL: https://owasp.org/Top10/A01_2021-Broken_Access_Control/
Attribution/licence: OWASP Top 10:2021, (c) OWASP Foundation, licensed CC BY-SA 4.0 (https://creativecommons.org/licenses/by-sa/4.0/); paraphrased here

# Broken Access Control (VULN-BROKEN-ACCESS-CONTROL)

Broken Access Control (VULN-BROKEN-ACCESS-CONTROL) is a Vulnerability, ranked first in the OWASP Top 10 (2021). Access control should ensure users cannot act outside their intended permissions. When it fails, users can view, change or delete data or call functions that belong to other users or to higher roles.

Typical causes: missing role checks on endpoints, trusting client-supplied identifiers, allowing access by changing a URL or id, and exposing admin functions to ordinary accounts.

- Valid Accounts (T1078) exploits Broken Access Control (VULN-BROKEN-ACCESS-CONTROL) to reach endpoints beyond the account's role.
- Account Use Policies (M1036) and Multi-factor Authentication (M1032) reduce the impact of misused accounts, and enforcing deny-by-default role checks mitigates the vulnerability itself.
- In PotchMatch the alert "Unauthorised Endpoint Access" signals possible exploitation of this vulnerability.
