#!/usr/bin/env python3
"""Curate the Neo4j LLM Graph Builder export into the PotchMatch knowledge seed.

Reads  security-knowledge.raw.json  (one row {nodes, relationships}, see export.cypher)
Writes security-knowledge.json      (seed format, see ../README.md)
       CURATION.md                  (every merge, drop and curated addition, generated)

Deterministic and re-runnable: `python3 curate.py` from any directory. Python 3 stdlib only.
All curation decisions are explicit data in the tables below; the code only applies them.
"""
import collections
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
RAW_PATH = os.path.join(HERE, "security-knowledge.raw.json")
OUT_PATH = os.path.join(HERE, "security-knowledge.json")
CURATION_PATH = os.path.join(HERE, "CURATION.md")

MITRE = "MITRE ATT&CK"
OWASP = "OWASP Top 10:2025"
PROCEDURE = "PotchMatch procedure"

# ---------------------------------------------------------------------------
# Official MITRE ATT&CK mitigation tables (the "Mitigations" section of each
# technique page in sources/web-sources.txt plus the sub-technique pages),
# checked on attack.mitre.org on 2026-10-09. A MITIGATES edge from an Mxxxx
# control to a technique is kept only if this table lists it.
# ---------------------------------------------------------------------------
MITRE_MITIGATIONS = {
    "T1110": ["M1018", "M1027", "M1032", "M1036"],
    "T1110.001": ["M1027", "M1032", "M1036", "M1051"],
    "T1110.002": ["M1027", "M1032"],
    "T1110.003": ["M1027", "M1032", "M1036"],
    "T1110.004": ["M1018", "M1027", "M1032", "M1036"],
    "T1078": ["M1013", "M1015", "M1017", "M1018", "M1026", "M1027", "M1032", "M1036"],
    "T1078.001": ["M1027", "M1032"],
    "T1078.002": ["M1017", "M1018", "M1026", "M1027", "M1032"],
    "T1078.003": ["M1018", "M1026", "M1027", "M1032"],
    "T1078.004": ["M1015", "M1017", "M1018", "M1026", "M1027", "M1032", "M1036"],
    "T1499": ["M1037"],
    "T1499.001": ["M1037"],
    "T1499.002": ["M1037"],
    "T1499.003": ["M1037"],
    "T1499.004": ["M1037"],
}

# ---------------------------------------------------------------------------
# Kept nodes. Each entry: label, key, display name/title, description, extra
# properties, and `raw`: the raw export names (label, name) merged into it.
# `desc` is (raw name whose extracted description it is based on, text);
# text None = copy that raw description verbatim; raw name None = written by
# the curator from the official page (node or description is curated).
# ---------------------------------------------------------------------------
def T(name):
    return ("Threat", name)


def C(name):
    return ("Control", name)


def V(name):
    return ("Vulnerability", name)


def D(name):
    return ("Document", name)


NODES = [
    # ---- Threats: MITRE techniques and sub-techniques -----------------------
    dict(label="Threat", key="T1110", name="Brute Force", source=MITRE,
         desc=("Brute Force", "Adversaries attempt to guess passwords to gain access to accounts; "
               "repeated attempts can also lock accounts out and cause a denial of service condition."),
         raw=[T("T1110"), T("Brute Force"), T("Brute Force (T1110)"), T("T1110 Brute Force"),
              T("Brute Force Attacks"), T("Brute-Force Attacks"), T("Brute Force Attempts"),
              T("Bruteforce Attempts"), T("Multiple Failed Logins"), T("Guessed Credentials")]),
    dict(label="Threat", key="T1110.001", name="Password Guessing", source=MITRE,
         desc=("Password Guessing T1110.002", "Adversaries attempt to guess passwords to gain unauthorized access "
               "to accounts without prior knowledge of the credentials."),
         raw=[T("T1110.001"), T("T1110.001 Password Guessing"), T("Brute Force: Password Guessing (T1110.001)"),
              T("001 Sub-Technique Of: T1110"), T("Password Guessing"), T("Password Guessing Attempts"),
              T("Password Guessing T1110.002")]),
    dict(label="Threat", key="T1110.002", name="Password Cracking", source=MITRE,
         desc=("T1110.002 Password Cracking", "Adversaries attempt to crack obtained password hashes offline "
               "to recover usable plaintext credentials."),
         raw=[T("T1110.002"), T("T1110.002 Password Cracking"), T("Password Cracking"),
              T("Password Cracking T1110.003")]),
    dict(label="Threat", key="T1110.003", name="Password Spraying", source=MITRE,
         desc=("T1110.003 Password Spraying", None),
         raw=[T("T1110.003"), T("T1110.003 Password Spraying"), T("Brute Force: Password Spraying"),
              T("Password Spraying"), T("Distributed Password Spraying"), T("Throttled Password Spraying")]),
    dict(label="Threat", key="T1110.004", name="Credential Stuffing", source=MITRE,
         desc=("T1110.004 Credential Stuffing", "Adversaries use credentials obtained from breach dumps of "
               "unrelated accounts to gain access to accounts where users reused the same passwords."),
         raw=[T("T1110.004 Credential Stuffing"), T("Credential Stuffing"), T("Credential Stuffing Attacks")]),
    dict(label="Threat", key="T1078", name="Valid Accounts", source=MITRE,
         desc=("T1078 Valid Accounts", "Adversaries obtain and abuse the credentials of existing accounts to gain "
               "initial access, escalate privileges, maintain persistence or evade defenses."),
         raw=[T("T1078"), T("T1078 Valid Accounts"), T("Valid Accounts"), T("Valid Accounts (T1078)"),
              T("Abusing Valid Accounts"), T("Valid Account Abuse"), T("Valid Account Misuse"),
              T("Compromised Or Misused Valid Accounts"), T("Unauthorized Access"),
              T("Unauthorised Endpoint Access"), T("Account Or Token Theft")]),
    dict(label="Threat", key="T1078.001", name="Default Accounts", source=MITRE,
         desc=("Default Accounts (T1078.001)", "Adversaries obtain and abuse credentials of default accounts "
               "(built-in accounts or vendor default username/password pairs) to gain access."),
         raw=[T("T1078.001"), T("Default Accounts (T1078.001)"), T("Default Accounts"),
              T("Abuse Of Default Accounts"), T("Default Account Exploitation"), T("Use Of Default Credentials")]),
    dict(label="Threat", key="T1078.002", name="Domain Accounts", source=MITRE,
         desc=("Domain Accounts (T1078.002)", "Adversaries obtain and abuse credentials of domain accounts "
               "as a means of gaining initial access, persistence, privilege escalation or defense evasion."),
         raw=[T("T1078.002"), T("Domain Accounts (T1078.002)"), T("Domain Account"),
              T("Exploitation Of Domain Accounts")]),
    dict(label="Threat", key="T1078.003", name="Local Accounts", source=MITRE,
         desc=("Local Accounts (T1078.003)", "Adversaries obtain and abuse credentials of local accounts "
               "as a means of gaining initial access, persistence, privilege escalation or defense evasion."),
         raw=[T("T1078.003"), T("Local Accounts (T1078.003)")]),
    dict(label="Threat", key="T1078.004", name="Cloud Accounts", source=MITRE,
         desc=("Cloud Accounts (T1078.004)", "Adversaries obtain and abuse credentials of cloud accounts "
               "as a means of gaining initial access, persistence, privilege escalation or defense evasion."),
         raw=[T("T1078.004"), T("Cloud Accounts (T1078.004)"), T("Cloud Accounts"), T("Cloud Account")]),
    dict(label="Threat", key="T1499", name="Endpoint Denial of Service", source=MITRE,
         desc=("Endpoint Denial Of Service", None),
         raw=[T("T1499"), T("Endpoint Denial Of Service"), T("Endpoint Denial Of Service (T1499)"),
              T("Endpoint Dos"), T("Possible_Dos"), T("Traffic Flood"), T("Abusive Traffic"),
              T("Service Outage"), T("Service Outage Attack")]),
    dict(label="Threat", key="T1499.001", name="OS Exhaustion Flood", source=MITRE,
         desc=("T1499.001 Os", "Adversaries exhaust operating system resources (for example with SYN or ACK "
               "floods) so the endpoint can no longer serve requests."),
         raw=[T("T1499.001"), T("T1499.001 Os"), T("Os Exhaustion Flood")]),
    dict(label="Threat", key="T1499.002", name="Service Exhaustion Flood", source=MITRE,
         desc=("Service Exhaustion Flood", "Adversaries flood a specific network service (such as a web "
               "server) with requests to exhaust it and deny its availability."),
         raw=[T("T1499.002"), T("Service Exhaustion Flood")]),
    dict(label="Threat", key="T1499.003", name="Application Exhaustion Flood", source=MITRE,
         desc=("Application Exhaustion Flood", "Adversaries send floods of requests to resource-intensive "
               "application features to exhaust the application and deny its availability."),
         raw=[T("T1499.003"), T("Application Exhaustion Flood")]),
    dict(label="Threat", key="T1499.004", name="Application or System Exploitation", source=MITRE,
         desc=("Application Or System Exploitation", "Adversaries exploit software vulnerabilities to crash "
               "an application or system and deny its availability."),
         raw=[T("T1499.004"), T("Application Or System Exploitation")]),
    # ---- Threat: OWASP --------------------------------------------------------
    dict(label="Threat", key="OWASP-A01-2025", name="A01:2025 Broken Access Control", source=OWASP,
         desc=("A01:2025 Broken Access Control", "Access control failures let users act outside their intended "
               "permissions, leading to unauthorized information disclosure, modification or destruction of data, "
               "or performing business functions outside the user's limits."),
         raw=[T("A01:2025 Broken Access Control"), T("A01 Broken Access Control"), T("Broken Access Control")]),

    # ---- Controls: MITRE mitigations -----------------------------------------
    dict(label="Control", key="M1036", name="Account Use Policies", source=MITRE,
         desc=("M1036: Account Use Policies", "Configure features related to account use: lock accounts after "
               "a number of failed login attempts, and use conditional access policies to block logins from "
               "non-compliant devices, outside defined IP ranges or from anonymizing services."),
         raw=[C("Account Use Policies"), C("Account Use Policies (M1036)"), C("M1036 Account Use Policies"),
              C("M1036: Account Use Policies"), C("1036 Account Use Policies"), C("Account Lockout Policies"),
              C("Account Lockout Policy"), C("Login Failure Policies"), C("Limit Failed Login Attempts"),
              C("Conditional Access Policies"), C("Location-Based Conditional Access Policies"),
              C("Blocking Risky Authentication Requests"), C("Block Risky Authentication Requests"),
              C("Restricting Login Times")]),
    dict(label="Control", key="M1032", name="Multi-factor Authentication", source=MITRE,
         desc=("M1032 Multi-Factor Authentication", "Use multi-factor authentication. Where possible, also enable "
               "multi-factor authentication on externally facing services."),
         raw=[C("Multi-Factor Authentication"), C("Multi-Factor Authentication (M1032)"),
              C("M1032 Multi-Factor Authentication"), C("M1032: Multi-Factor Authentication"), C("Mfa"),
              C("Multi-Factor Authentication (Mfa)"), C("Require Mfa For All Delegated Administrator Accounts")]),
    dict(label="Control", key="M1027", name="Password Policies", source=MITRE,
         desc=("Password Policies", "Set and enforce secure password policies (length, complexity, no default "
               "or reused passwords); change default credentials immediately after installation."),
         raw=[C("Password Policies"), C("Password Policies (M1027)"), C("Password Complexity Policy"),
              C("Enforce Secure Password Policies"), C("Secure Password Policies"),
              C("Strong Password Policies For Domain Accounts"), C("Change Default Credentials"),
              C("Changing Default Credentials"), C("M1026")]),
    dict(label="Control", key="M1037", name="Filter Network Traffic", source=MITRE,
         desc=("M1037 Filter Network Traffic", "Filter traffic upstream of services using CDNs or DoS mitigation "
               "providers, and filter boundary traffic by blocking attacking source addresses, targeted ports "
               "or abused protocols."),
         raw=[C("Filter Network Traffic"), C("Filter Network Traffic (M1037)"), C("M1037 Filter Network Traffic"),
              C("Filter Boundary Traffic"), C("Boundary Traffic Filtering"), C("Upstream Traffic Filtering"),
              C("Cdn And Dos Mitigation Services"), C("Cdn Or Dos Mitigation Services"),
              C("Content Delivery Network (Cdn)"), C("Content Delivery Networks (Cdn)"),
              C("Dos Mitigation Providers")]),
    dict(label="Control", key="M1018", name="User Account Management", source=MITRE,
         desc=("User Account Management", None),
         raw=[C("M1018 User Account Management"), C("User Account Management")]),
    dict(label="Control", key="M1017", name="User Training", source=MITRE,
         desc=("User Training", None),
         raw=[C("User Training"), C("M1017 User Training")]),
    dict(label="Control", key="M1051", name="Update Software", source=MITRE,
         desc=("M1051", "Upgrade management services to the latest supported version, specifically any version "
               "that adds password complexity or policy enforcement preventing default or weak passwords."),
         raw=[C("M1051")]),
    # ---- Controls: PotchMatch procedure steps --------------------------------
    dict(label="Control", key="CTRL-IP-BLOCKING", name="Gateway IP blocking", source=PROCEDURE,
         desc=("Gateway Blocking", "Confirm the alert, identify the source IP, check it is not a legitimate "
               "shared address, add it to the gateway block list and monitor that the flood stops."),
         raw=[C("Gateway Blocking"), C("Ip Blocking")]),
    dict(label="Control", key="CTRL-SESSION-REVOCATION", name="Session and account revocation", source=PROCEDURE,
         desc=("Session And Account Revocation Control", "Revoke all active tokens and sessions for the user and "
               "disable the account until ownership is verified."),
         raw=[C("Session And Account Revocation Control")]),
    dict(label="Control", key="CTRL-PASSWORD-RESET", name="Password reset", source=PROCEDURE,
         desc=("Password Reset", None),
         raw=[C("Password Reset")]),
    dict(label="Control", key="CTRL-SERVICE-RESTART", name="Service restart and redeployment", source=PROCEDURE,
         desc=("Service Restart And Redeployment", None),
         raw=[C("Service Restart And Redeployment")]),

    # ---- Vulnerabilities -----------------------------------------------------
    dict(label="Vulnerability", key="VULN-WEAK-PASSWORDS", name="Weak passwords",
         desc=("Weak Passwords", "Weak, common or guessable passwords (including patterns such as Welcome2023) "
               "that make guessing, spraying and stuffing attacks succeed."),
         raw=[T("Weak Passwords"), V("Default Or Weak Passwords"), V("Weak Credentials"),
              V("Weak Or Static Credentials"), V("Commonly Used Passwords"), V("Common Password Patterns"),
              V("Common Passwords On Local Accounts"), V("Lazy Passwords")]),
    dict(label="Vulnerability", key="VULN-BROKEN-ACCESS-CONTROL", name="Broken access control",
         desc=("Missing Access Controls", "Missing or bypassable server-side authorization checks (OWASP A01:2025), "
               "for example an API that does not enforce access control on POST, PUT or DELETE."),
         raw=[V("Access Control Vulnerabilities"), V("Missing Access Control"), V("Missing Access Controls")]),
    dict(label="Vulnerability", key="VULN-NO-RATE-LIMIT", name="No rate limiting",
         desc=(None, "Unbounded request rates from a single source let an attacker exhaust endpoint resources "
               "or make unlimited login attempts."),
         raw=[],
         why="required ID (VULN-NO-RATE-LIMIT). The T1499 page describes exhausting resources with floods of "
             "requests and M1037 filtering/blocking of the attacking source; the IP-blocking procedure stops "
             "abusive traffic from one source. The extraction produced no matching weakness ('Resource "
             "Exhaustion' is the effect, not the missing control), so it is added by hand."),
    dict(label="Vulnerability", key="VULN-DEFAULT-CREDENTIALS", name="Default credentials",
         desc=("Default Username And Password", "Applications, appliances and devices still using vendor default "
               "usernames and passwords, or default accounts left enabled."),
         raw=[V("Default Username And Password"), V("Default Username And Password Configuration"),
              V("Default Passwords"), V("Default Manufacturer Passwords"), V("Default Manufacturers Passwords"),
              V("Default Accounts Enabled")]),
    dict(label="Vulnerability", key="VULN-EXPOSED-PASSWORD-HASHES", name="Exposed password hashes",
         desc=("Exposed Password Hashes", "Password hashes obtained by an adversary, enabling offline cracking."),
         raw=[V("Exposed Password Hashes"), V("Obtained Password Hashes")]),
    dict(label="Vulnerability", key="VULN-LEAKED-CREDENTIALS", name="Compromised or leaked credentials",
         desc=("Leaked Credentials", "Credentials of existing accounts that were stolen, leaked in breaches or "
               "reused across services, available to attackers for stuffing or direct login."),
         raw=[V("Leaked Credentials"), V("Breached Credentials"), V("Credential Overlap"),
              V("Credentials Of Existing Accounts"), T("Compromised Credentials")]),

    # ---- Documents: PotchMatch procedures ------------------------------------
    dict(label="Document", key="DOC-PASSWORD-RESET", title="Password Reset Procedure", kind="Procedure",
         desc=("Reset", "Incident response procedure to return control of a possibly compromised account to its "
               "owner and make guessed credentials useless."),
         raw=[D("Doc-Password-Reset"), D("Password Reset (Doc-Password-Reset)"), D("Reset")]),
    dict(label="Document", key="DOC-SESSION-REVOCATION", title="Session Revocation Procedure", kind="Procedure",
         desc=("Doc-Session-Revocation", "Incident response procedure to cut off an attacker using a valid account "
               "or stolen token and force the legitimate user to authenticate again."),
         raw=[D("Doc-Session-Revocation"), D("Session Revocation (Doc-Session-Revocation)")]),
    dict(label="Document", key="DOC-IP-BLOCKING", title="IP Blocking Procedure", kind="Procedure",
         desc=("Blocking", "Incident response procedure to stop abusive traffic from a hostile source address so "
               "services stay available."),
         raw=[D("Doc-Ip-Blocking"), D("Ip Blocking (Doc-Ip-Blocking)"), D("Blocking")]),
    dict(label="Document", key="DOC-SERVICE-OUTAGE", title="Service Outage Runbook", kind="Procedure",
         desc=("Doc-Service-Outage", "Runbook to restore a failed PotchMatch service and determine whether the "
               "outage is an attack."),
         raw=[D("Doc-Service-Outage"), D("Service Outage Runbook (Doc-Service-Outage)")]),
    # ---- Documents: the official source pages ---------------------------------
    dict(label="Document", key="DOC-T1110", title="MITRE ATT&CK T1110 Brute Force", kind="MITRE",
         url="https://attack.mitre.org/techniques/T1110/",
         desc=("Techniques Of Brute Force (4)", "MITRE ATT&CK technique page for Brute Force and its four "
               "sub-techniques, with mitigations and detection guidance."),
         raw=[D("Techniques Of Brute Force (4)")]),
    dict(label="Document", key="DOC-T1110-001", title="MITRE ATT&CK T1110.001 Password Guessing", kind="MITRE",
         url="https://attack.mitre.org/techniques/T1110/001/",
         desc=(None, "MITRE ATT&CK sub-technique page for Password Guessing."), raw=[],
         why="one Document per official source page; the T1110.001 page is in sources/web-sources.txt "
             "but Graph Builder created no Document node for it."),
    dict(label="Document", key="DOC-T1110-003", title="MITRE ATT&CK T1110.003 Password Spraying", kind="MITRE",
         url="https://attack.mitre.org/techniques/T1110/003/",
         desc=(None, "MITRE ATT&CK sub-technique page for Password Spraying."), raw=[],
         why="one Document per official source page; the T1110.003 page is in sources/web-sources.txt "
             "but Graph Builder created no Document node for it."),
    dict(label="Document", key="DOC-T1110-004", title="MITRE ATT&CK T1110.004 Credential Stuffing", kind="MITRE",
         url="https://attack.mitre.org/techniques/T1110/004/",
         desc=(None, "MITRE ATT&CK sub-technique page for Credential Stuffing."),
         raw=[D("T1110.004")]),
    dict(label="Document", key="DOC-T1078", title="MITRE ATT&CK T1078 Valid Accounts", kind="MITRE",
         url="https://attack.mitre.org/techniques/T1078/",
         desc=(None, "MITRE ATT&CK technique page for Valid Accounts and its four sub-techniques."), raw=[],
         why="one Document per official source page; the T1078 page is in sources/web-sources.txt "
             "but Graph Builder created no Document node for it."),
    dict(label="Document", key="DOC-T1499", title="MITRE ATT&CK T1499 Endpoint Denial of Service", kind="MITRE",
         url="https://attack.mitre.org/techniques/T1499/",
         desc=("Technique T1499", "MITRE ATT&CK technique page for Endpoint Denial of Service and its four "
               "sub-techniques."),
         raw=[D("Technique T1499")]),
    dict(label="Document", key="DOC-M1036", title="MITRE ATT&CK M1036 Account Use Policies", kind="MITRE",
         url="https://attack.mitre.org/mitigations/M1036/",
         desc=(None, "MITRE ATT&CK mitigation page for Account Use Policies."), raw=[D("M1036")]),
    dict(label="Document", key="DOC-M1032", title="MITRE ATT&CK M1032 Multi-factor Authentication", kind="MITRE",
         url="https://attack.mitre.org/mitigations/M1032/",
         desc=(None, "MITRE ATT&CK mitigation page for Multi-factor Authentication."), raw=[D("M1032")]),
    dict(label="Document", key="DOC-M1027", title="MITRE ATT&CK M1027 Password Policies", kind="MITRE",
         url="https://attack.mitre.org/mitigations/M1027/",
         desc=(None, "MITRE ATT&CK mitigation page for Password Policies (refers to NIST guidelines)."),
         raw=[D("M1027"), D("M1027 Password Policies")]),
    dict(label="Document", key="DOC-M1037", title="MITRE ATT&CK M1037 Filter Network Traffic", kind="MITRE",
         url="https://attack.mitre.org/mitigations/M1037/",
         desc=(None, "MITRE ATT&CK mitigation page for Filter Network Traffic."), raw=[D("M1037")]),
    dict(label="Document", key="DOC-OWASP-A01", title="OWASP Top 10:2025 A01 Broken Access Control", kind="OWASP",
         url="https://owasp.org/Top10/2025/A01_2025-Broken_Access_Control/",
         desc=(None, "OWASP Top 10:2025 category page for A01 Broken Access Control: description, mapped CWEs, "
               "prevention and attack scenarios."),
         raw=[D("A01:2025 Broken Access Control Background"), D("Owasp Top 10:2025"), D("List Of Mapped Cwes")]),
]

# ---------------------------------------------------------------------------
# Edges the system relies on (the system tests and queries rely on them). Added as curated if extraction
# does not produce them (after merging).
# ---------------------------------------------------------------------------
REQUIRED_EDGES = [
    ("MITIGATES", "M1036", "T1110"), ("MITIGATES", "M1032", "T1110"), ("MITIGATES", "M1027", "T1110"),
    ("MITIGATES", "M1032", "T1078"), ("MITIGATES", "M1037", "T1499"),
    ("EXPLOITS", "T1110", "VULN-WEAK-PASSWORDS"), ("EXPLOITS", "T1078", "VULN-BROKEN-ACCESS-CONTROL"),
    ("EXPLOITS", "T1499", "VULN-NO-RATE-LIMIT"),
    ("DESCRIBES", "DOC-PASSWORD-RESET", "M1027"), ("DESCRIBES", "DOC-SESSION-REVOCATION", "M1032"),
    ("DESCRIBES", "DOC-IP-BLOCKING", "M1037"),
    ("SUPPORTS_RESPONSE_TO", "DOC-PASSWORD-RESET", "T1110"),
    ("SUPPORTS_RESPONSE_TO", "DOC-SESSION-REVOCATION", "T1078"),
    ("SUPPORTS_RESPONSE_TO", "DOC-IP-BLOCKING", "T1499"),
    ("SUPPORTS_RESPONSE_TO", "DOC-SERVICE-OUTAGE", "T1499"),
]

# Required edges carry the justification for a curated addition here.
REQUIRED_JUSTIFICATION = {
    ("EXPLOITS", "T1110", "VULN-WEAK-PASSWORDS"):
        "T1110 page: brute force succeeds against weak or guessable passwords; M1027 exists to prevent them.",
    ("EXPLOITS", "T1078", "VULN-BROKEN-ACCESS-CONTROL"):
        "Valid-account abuse reaches data and functions when server-side authorization checks are missing "
        "(OWASP A01:2025); session-revocation procedure step 6 reviews role checks on the endpoint.",
    ("EXPLOITS", "T1499", "VULN-NO-RATE-LIMIT"):
        "T1499 page: endpoint DoS exhausts resources with floods of requests; IP-blocking procedure stops "
        "abusive traffic from one source.",
}

# Further curated edges, each with its justification (official page or procedure text).
EXTRA_CURATED_EDGES = [
    ("EXPLOITS", "OWASP-A01-2025", "VULN-BROKEN-ACCESS-CONTROL",
     "OWASP A01:2025 page: the category is the failure to enforce access control (missing/bypassable checks)."),
    ("EXPLOITS", "T1499.002", "VULN-NO-RATE-LIMIT",
     "T1499.002 page: service exhaustion floods send more requests than the service can handle."),
    ("EXPLOITS", "T1499.003", "VULN-NO-RATE-LIMIT",
     "T1499.003 page: application exhaustion floods repeat resource-intensive requests."),
    ("DESCRIBES", "DOC-IP-BLOCKING", "CTRL-IP-BLOCKING",
     "procedure-ip-blocking.md steps 1-5 are the gateway block-list control."),
    ("DESCRIBES", "DOC-SESSION-REVOCATION", "CTRL-SESSION-REVOCATION",
     "procedure-session-revocation.md steps 2-3 (revoke tokens/sessions, disable account)."),
    ("DESCRIBES", "DOC-PASSWORD-RESET", "CTRL-PASSWORD-RESET",
     "procedure-password-reset.md steps 4-5 (reset link, new policy-compliant password)."),
]

# Source-page documents: each technique page supports response to its technique,
# each mitigation page describes its mitigation (added as curated where not extracted).
SOURCE_PAGE_EDGES = [
    ("SUPPORTS_RESPONSE_TO", "DOC-T1110", "T1110"),
    ("SUPPORTS_RESPONSE_TO", "DOC-T1110-001", "T1110.001"),
    ("SUPPORTS_RESPONSE_TO", "DOC-T1110-003", "T1110.003"),
    ("SUPPORTS_RESPONSE_TO", "DOC-T1110-004", "T1110.004"),
    ("SUPPORTS_RESPONSE_TO", "DOC-T1078", "T1078"),
    ("SUPPORTS_RESPONSE_TO", "DOC-T1499", "T1499"),
    ("SUPPORTS_RESPONSE_TO", "DOC-OWASP-A01", "OWASP-A01-2025"),
    ("DESCRIBES", "DOC-M1036", "M1036"),
    ("DESCRIBES", "DOC-M1032", "M1032"),
    ("DESCRIBES", "DOC-M1027", "M1027"),
    ("DESCRIBES", "DOC-M1037", "M1037"),
]

# Raw edges between kept nodes that are dropped on purpose (mis-extractions), with the reason.
DROP_EDGES = {
    ("SUPPORTS_RESPONSE_TO", D("T1110.004"), T("Password Spraying")):
        "the T1110.004 page is Credential Stuffing; linking it to Password Spraying is a mis-extraction",
}

# Allowed relationship shapes (curation rules).
ALLOWED = {
    "MITIGATES": ("Control", "Threat"),
    "EXPLOITS": ("Threat", "Vulnerability"),
    "DESCRIBES": ("Document", "Control"),
    "SUPPORTS_RESPONSE_TO": ("Document", "Threat"),
}

# Notes printed in CURATION.md for raw nodes whose raw label/ID was wrong.
RAW_CORRECTIONS = [
    ("Threat 'Password Guessing T1110.002'", "T1110.001",
     "name and description say Password Guessing; the ID in the name is wrong (T1110.002 is Password Cracking)"),
    ("Threat 'Password Cracking T1110.003'", "T1110.002",
     "name and description say Password Cracking; the ID in the name is wrong (T1110.003 is Password Spraying)"),
    ("Threat 'T1078.002' / 'T1078.003' / 'T1078.004'", "same ID",
     "names are bare IDs but descriptions name the wrong sub-technique (Local/Cloud/Domain); mapped by ID, "
     "descriptions replaced"),
    ("Threat 'T1110.001'", "T1110.001",
     "description is a procedure example (Wi-Fi brute force via wlanAPI); description replaced"),
    ("Threat 'T1499'", "T1499",
     "description is a procedure example (web censorship); description replaced"),
    ("Control 'M1026'", "M1027",
     "description is M1027 Password Policies text from the T1078.001 page (change default credentials); "
     "raw ID is wrong"),
    ("Threat 'Weak Passwords'", "VULN-WEAK-PASSWORDS",
     "extracted with label Threat; it is a weakness, so relabelled Vulnerability"),
    ("Threat 'Compromised Credentials'", "VULN-LEAKED-CREDENTIALS",
     "extracted with label Threat; T1078 EXPLOITS it, so relabelled Vulnerability"),
    ("Control 'Restricting Login Times'", "M1036",
     "M1036 page: account use policies configure login attempt lockouts and specific login times"),
    ("Threats 'Multiple Failed Logins', 'Unauthorised Endpoint Access', 'Possible_Dos'", "T1110 / T1078 / T1499",
     "SOC alert names extracted from the procedures; mapped with the system's rule-to-threat map"),
    ("Threats 'Service Outage', 'Service Outage Attack', 'Traffic Flood', 'Abusive Traffic'", "T1499",
     "procedure wording for the DoS threat (SERVICE_FAILURE maps to T1499 in the rule map)"),
    ("Threats 'Guessed Credentials', 'Account Or Token Theft'", "T1110 / T1078",
     "procedure wording for the threat the procedure responds to (password reset / session revocation)"),
    ("Threat 'Unauthorized Access'", "T1078",
     "description 'Unauthorized access to default, local, domain, and cloud accounts' is Valid Accounts"),
]


# ---------------------------------------------------------------------------
def load_raw():
    with open(RAW_PATH, encoding="utf-8") as f:
        data = json.load(f)
    row = data[0] if isinstance(data, list) else data
    return row["nodes"], row["relationships"]


def main():
    raw_nodes, raw_rels = load_raw()
    raw_by_ref = {(n["label"], n["name"]): n for n in raw_nodes}
    if len(raw_by_ref) != len(raw_nodes):
        sys.exit("raw export has duplicate (label, name) pairs; alias table would be ambiguous")

    alias = {}  # raw (label, name) -> curated key
    node_by_key = {}
    for spec in NODES:
        if spec["key"] in node_by_key:
            sys.exit(f"duplicate curated key {spec['key']}")
        node_by_key[spec["key"]] = spec
        for ref in spec["raw"]:
            if ref not in raw_by_ref:
                sys.exit(f"alias {ref} for {spec['key']} not found in raw export")
            if ref in alias:
                sys.exit(f"raw node {ref} mapped twice ({alias[ref]}, {spec['key']})")
            alias[ref] = spec["key"]
        src = spec["desc"][0]
        if src is not None and not any(r[1] == src for r in spec["raw"]):
            sys.exit(f"description source {src!r} of {spec['key']} is not one of its raw names")

    # ---- map raw relationships -------------------------------------------------
    extracted = {}  # (type, from, to) -> list of raw relationship strings
    drops = collections.Counter()
    dropped_between_kept = []
    for rel in raw_rels:
        f_ref = (rel["fromLabel"], rel["fromName"])
        t_ref = (rel["toLabel"], rel["toName"])
        f, t = alias.get(f_ref), alias.get(t_ref)
        raw_str = f"{rel['fromLabel']} '{rel['fromName']}' -{rel['type']}-> {rel['toLabel']} '{rel['toName']}'"
        if f is None or t is None:
            drops["an endpoint is not kept (dropped node)"] += 1
            continue
        key = (rel["type"], f, t)
        reason = cat = None
        shape = ALLOWED.get(rel["type"])
        if (rel["type"], f_ref, t_ref) in DROP_EDGES:
            reason, cat = DROP_EDGES[(rel["type"], f_ref, t_ref)], "mis-extracted link"
        elif shape is None or (node_by_key[f]["label"], node_by_key[t]["label"]) != shape:
            reason = (f"{node_by_key[f]['label']}-{rel['type']}->{node_by_key[t]['label']} "
                      "is not an allowed relationship shape")
            cat = "relationship shape not allowed"
        elif f == t:
            reason, cat = "self-loop after merging both ends into one node", "self-loop after merge"
        elif rel["type"] == "MITIGATES" and re.fullmatch(r"M1\d{3}", f) and t in MITRE_MITIGATIONS \
                and f not in MITRE_MITIGATIONS[t]:
            reason, cat = f"MITRE does not list {f} as a mitigation of {t}", "mitigation not listed by MITRE"
        if reason:
            drops["both ends kept: " + cat] += 1
            dropped_between_kept.append((raw_str, f"{rel['type']} {f} -> {t}", reason))
            continue
        extracted.setdefault(key, []).append(raw_str)

    # ---- curated edges ---------------------------------------------------------
    curated = {}  # (type, from, to) -> justification
    for e in REQUIRED_EDGES:
        if e not in extracted:
            just = REQUIRED_JUSTIFICATION.get(e)
            if just is None and e[0] == "MITIGATES":
                just = f"required edge; MITRE {e[2]} page lists {e[1]}"
            if just is None and e[0] in ("DESCRIBES", "SUPPORTS_RESPONSE_TO"):
                just = "required edge; stated in the procedure's Relationships section"
            curated[e] = just
    for typ, f, t, just in EXTRA_CURATED_EDGES:
        if (typ, f, t) not in extracted:
            curated[(typ, f, t)] = just
    for e in SOURCE_PAGE_EDGES:
        if e not in extracted:
            curated[e] = ("source page document: the official page "
                          + ("describes this mitigation" if e[0] == "DESCRIBES" else "documents this threat"))
    kept_m = [k for k in node_by_key if re.fullmatch(r"M1\d{3}", k)]
    for tech, ms in MITRE_MITIGATIONS.items():
        if tech not in node_by_key:
            continue
        for m in ms:
            e = ("MITIGATES", m, tech)
            if m in kept_m and e not in extracted and e not in curated:
                curated[e] = f"MITRE {tech} page lists {m} in its Mitigations table (not extracted)"

    # ---- sanity: every kept node is connected, every required edge present -----
    all_edges = set(extracted) | set(curated)
    for e in REQUIRED_EDGES:
        assert e in all_edges, e
    for (typ, f, t) in all_edges:
        assert (node_by_key[f]["label"], node_by_key[t]["label"]) == ALLOWED[typ], (typ, f, t)
    touched = {k for e in all_edges for k in e[1:]}
    orphans = sorted(set(node_by_key) - touched)
    if orphans:
        sys.exit(f"orphan nodes: {orphans}")

    # ---- build seed ------------------------------------------------------------
    label_order = ["Threat", "Control", "Vulnerability", "Document"]
    seed_nodes = []
    for spec in sorted(NODES, key=lambda s: label_order.index(s["label"])):
        src, text = spec["desc"]
        if text is None:
            text = raw_by_ref[next(r for r in spec["raw"] if r[1] == src)]["description"]
        props = {"origin": "extracted" if spec["raw"] else "curated"}
        if spec["label"] == "Document":
            props["title"] = spec["title"]
            props["kind"] = spec["kind"]
            if spec.get("url"):
                props["url"] = spec["url"]
        else:
            props["name"] = spec["name"]
        props["description"] = text
        if spec.get("source"):
            props["source"] = spec["source"]
        if spec["raw"]:
            props["rawNames"] = [name for _, name in spec["raw"]]
        seed_nodes.append({"label": spec["label"], "key": spec["key"], "properties": props})

    type_order = ["MITIGATES", "EXPLOITS", "DESCRIBES", "SUPPORTS_RESPONSE_TO"]
    key_order = {spec["key"]: i for i, spec in enumerate(NODES)}

    def edge_sort(e):
        return (type_order.index(e[0]), key_order[e[1]], key_order[e[2]])

    seed_rels = [{"type": typ,
                  "from": {"label": node_by_key[f]["label"], "key": f},
                  "to": {"label": node_by_key[t]["label"], "key": t}}
                 for typ, f, t in sorted(all_edges, key=edge_sort)]

    with open(OUT_PATH, "w", encoding="utf-8", newline="\n") as fh:
        json.dump({"nodes": seed_nodes, "relationships": seed_rels}, fh, indent=2, ensure_ascii=False)
        fh.write("\n")

    # ---- dropped raw nodes by category ----------------------------------------
    def category(n):
        name, label = n["name"], n["label"]
        if re.fullmatch(r"(An|Det)\d+", name) or re.search(r"\bDetect", name):
            return "ATT&CK detection analytics / strategies (ANxxxx, DETxxxx, 'Detection ...')"
        if label == "Document":
            if re.match(r"(Reference|Document)\s*\[?\d", name) or re.match(r"\[\d", name):
                return "Document: numbered reference / citation placeholders"
            return "Document: cited reports, articles, vendor docs and page chrome (not a source page or procedure)"
        if label == "Threat":
            if re.search(r"\bT1\d{3}", name):
                return "Threat: other ATT&CK techniques outside T1110/T1078/T1499"
            return ("Threat: threat groups, campaigns, software/tools, procedure examples and "
                    "out-of-scope attack scenarios")
        if label == "Control":
            if re.search(r"\bM1\d{3}", name):
                return "Control: MITRE mitigations not linked to a kept threat"
            return "Control: implementation detail, tools and controls not linked to a kept threat"
        return "Vulnerability: not exploited by a kept threat (ports, platforms, CWEs, group-specific)"

    dropped_nodes = collections.Counter()
    for n in raw_nodes:
        if (n["label"], n["name"]) not in alias:
            dropped_nodes[category(n)] += 1

    write_curation(raw_nodes, raw_rels, seed_nodes, seed_rels, extracted, curated, drops,
                   dropped_between_kept, dropped_nodes)

    counts = collections.Counter(n["label"] for n in seed_nodes)
    rcounts = collections.Counter(r["type"] for r in seed_rels)
    origins = collections.Counter(n["properties"]["origin"] for n in seed_nodes)
    print(f"nodes {len(seed_nodes)} {dict(counts)} origins {dict(origins)}")
    print(f"relationships {len(seed_rels)} {dict(rcounts)} extracted {len(extracted)} curated {len(curated)}")


def write_curation(raw_nodes, raw_rels, seed_nodes, seed_rels, extracted, curated, drops,
                   dropped_between_kept, dropped_nodes):
    L = []
    w = L.append
    raw_counts = collections.Counter(n["label"] for n in raw_nodes)
    counts = collections.Counter(n["label"] for n in seed_nodes)
    origins = collections.Counter((n["label"], n["properties"]["origin"]) for n in seed_nodes)
    rcounts = collections.Counter(r["type"] for r in seed_rels)
    ecounts = collections.Counter(e[0] for e in extracted)
    ccounts = collections.Counter(e[0] for e in curated)

    w("# Curation of the Graph Builder export")
    w("")
    w("<!-- Generated by curate.py; do not edit by hand. Re-run `python3 graph-data/knowledge/curate.py`. -->")
    w("")
    w("`security-knowledge.raw.json` is the Neo4j LLM Graph Builder export of the 11 official pages in "
      "`sources/web-sources.txt` (MITRE ATT&CK T1110, T1110.001/.003/.004, T1078, T1499, M1036, M1032, M1027, "
      "M1037; OWASP Top 10:2025 A01) and the four `sources/procedure-*.md` documents. "
      "`curate.py` turns it into the seed `security-knowledge.json`. This file is the record of every change.")
    w("")
    w("## Method")
    w("")
    w("1. **Scope.** Keep only the three threats PotchMatch detects (T1110, T1078, T1499), their sub-techniques "
      "found in the export, and OWASP A01:2025; the MITRE mitigations the export links to them; the "
      "procedure-level controls linked to a kept node; a few vulnerabilities they exploit; the four procedures "
      "and one document per official source page. Everything else is dropped (see *Dropped*).")
    w("2. **Merge.** Every raw variant of an entity (e.g. `Brute Force`, `Brute Force (T1110)`, `T1110`, "
      "`T1110 Brute Force`) becomes one node keyed by its ATT&CK ID or a `VULN-*` / `DOC-*` / `CTRL-*` ID. "
      "The merged raw names are stored on the node as `rawNames`. The alias table is explicit data in "
      "`curate.py` (`NODES`).")
    w("3. **Properties.** `name` (Threat/Control/Vulnerability) or `title` + `kind` (+ `url` for web pages) "
      "(Document) use the official name. `description` is based on the extracted description, tidied where "
      "the extraction was a fragment or a procedure example. `origin` is `extracted` if at least one raw node "
      "merged into it, `curated` if it was added by hand.")
    w("4. **Relationships.** Raw relationships are re-pointed through the alias table and kept only if both ends "
      "are kept, the shape is one of Control-MITIGATES->Threat, Threat-EXPLOITS->Vulnerability, "
      "Document-DESCRIBES->Control, Document-SUPPORTS_RESPONSE_TO->Threat, and (for MITRE mitigations) the "
      "official MITRE page lists the mitigation for that technique (`MITRE_MITIGATIONS`, checked on "
      "attack.mitre.org). Duplicates collapse to one edge. Missing required edges, MITRE-listed mitigation "
      "links the extraction missed, and source-page links are added as curated with a justification.")
    w("5. **Edge origin.** The seed format (`SeedRelationship`: type, from, to) has no relationship properties, "
      "so each relationship's origin is recorded in the table below instead of on the edge.")
    w("")
    w("## Final counts")
    w("")
    w(f"Raw export: {len(raw_nodes)} nodes ({', '.join(f'{v} {k}' for k, v in sorted(raw_counts.items()))}), "
      f"{len(raw_rels)} relationships.")
    w("")
    w("| Label | Nodes | extracted | curated |")
    w("|---|---|---|---|")
    for lab in ["Threat", "Control", "Vulnerability", "Document"]:
        w(f"| {lab} | {counts[lab]} | {origins[(lab, 'extracted')]} | {origins[(lab, 'curated')]} |")
    w(f"| **Total** | **{len(seed_nodes)}** | **{sum(v for (l, o), v in origins.items() if o == 'extracted')}** "
      f"| **{sum(v for (l, o), v in origins.items() if o == 'curated')}** |")
    w("")
    w("| Relationship | Total | extracted | curated |")
    w("|---|---|---|---|")
    for typ in ["MITIGATES", "EXPLOITS", "DESCRIBES", "SUPPORTS_RESPONSE_TO"]:
        w(f"| {typ} | {rcounts[typ]} | {ecounts[typ]} | {ccounts[typ]} |")
    w(f"| **Total** | **{len(seed_rels)}** | **{len(extracted)}** | **{len(curated)}** |")
    w("")
    w("## Merges (curated id <- raw names)")
    w("")
    w("Description column: the raw node whose extracted description the curated description is based on "
      "(*verbatim* if copied unchanged, *rewritten* if tidied or completed from the official page), or *curator* if written from the official page.")
    w("")
    w("| Curated id | Label | Name / title | origin | Raw names merged | Description |")
    w("|---|---|---|---|---|---|")
    specs = {s["key"]: s for s in NODES}
    for n in seed_nodes:
        p = n["properties"]
        raws = ", ".join(f"`{r}`" for r in p.get("rawNames", [])) or "- (added by curator)"
        src, text = specs[n["key"]]["desc"]
        desc = "curator" if src is None else f"`{src}`" + (" (verbatim)" if text is None else " (rewritten)")
        w(f"| `{n['key']}` | {n['label']} | {p.get('name') or p.get('title')} | {p['origin']} | {raws} | {desc} |")
    w("")
    w("### Corrected raw IDs and labels")
    w("")
    for what, to, why in RAW_CORRECTIONS:
        w(f"- {what} -> `{to}`: {why}.")
    w("")
    w("## Curated additions")
    w("")
    w("### Nodes")
    w("")
    specs = {s["key"]: s for s in NODES}
    for n in seed_nodes:
        if n["properties"]["origin"] == "curated":
            p = n["properties"]
            w(f"- `{n['key']}` ({n['label']}, {p.get('name') or p.get('title')}): not extracted as an entity; "
              f"{specs[n['key']]['why']}")
    w("")
    w("### Relationships")
    w("")
    w("| Relationship | Justification |")
    w("|---|---|")
    for (typ, f, t), just in sorted(curated.items()):
        w(f"| `{f}` -{typ}-> `{t}` | {just} |")
    w("")
    w("## All relationships and their origin")
    w("")
    w("| Relationship | origin | Raw relationships it came from |")
    w("|---|---|---|")
    for r in seed_rels:
        e = (r["type"], r["from"]["key"], r["to"]["key"])
        if e in extracted:
            src = "<br>".join(sorted(set(extracted[e])))
            w(f"| `{e[1]}` -{e[0]}-> `{e[2]}` | extracted | {src} |")
        else:
            w(f"| `{e[1]}` -{e[0]}-> `{e[2]}` | curated | - |")
    w("")
    w("## Dropped")
    w("")
    w("### Raw nodes not kept, by category")
    w("")
    w("| Category | Count |")
    w("|---|---|")
    for cat, c in sorted(dropped_nodes.items()):
        w(f"| {cat} | {c} |")
    w(f"| **Total dropped** | **{sum(dropped_nodes.values())}** |")
    w("")
    w(f"Kept from the raw export: {len(raw_nodes) - sum(dropped_nodes.values())} raw nodes merged into "
      f"{sum(1 for n in seed_nodes if n['properties']['origin'] == 'extracted')} curated nodes.")
    w("")
    w("### Raw relationships not kept")
    w("")
    w("| Reason | Count |")
    w("|---|---|")
    for reason, c in sorted(drops.items()):
        w(f"| {reason} | {c} |")
    kept_raw = sum(len(v) for v in extracted.values())
    w(f"| Kept (collapsed into {len(extracted)} extracted edges) | {kept_raw} |")
    w("")
    w("Raw relationships between two kept nodes that were dropped:")
    w("")
    for raw_str, mapped, reason in sorted(dropped_between_kept):
        w(f"- {raw_str} (= {mapped}): {reason}.")
    w("")
    with open(CURATION_PATH, "w", encoding="utf-8", newline="\n") as fh:
        fh.write("\n".join(L))


if __name__ == "__main__":
    main()
