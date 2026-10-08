Title: Endpoint Denial of Service (T1499)
Source URL: https://attack.mitre.org/techniques/T1499/
Attribution/licence: MITRE ATT&CK(R) (c) The MITRE Corporation; paraphrased here under the ATT&CK Terms of Use (https://attack.mitre.org/resources/terms-of-use/)

# Endpoint Denial of Service (T1499)

Endpoint Denial of Service (T1499) is a Threat. An adversary floods or exhausts a service, application or host so legitimate users cannot use it. For PotchMatch this means booking, gateway or identity services becoming slow or unavailable. Variants include network flood, service exhaustion flood, application exhaustion flood and application or system exploitation.

Endpoint Denial of Service exploits the absence of rate limiting. The vulnerability No Rate Limit (VULN-NO-RATE-LIMIT) lets an attacker send unbounded request volume to an endpoint without being throttled.

## Mitigations
- Filter Network Traffic (M1037) mitigates Endpoint Denial of Service (T1499): blocking or limiting traffic from abusive sources keeps services available.

## Detection
A burst of requests from one source, or a service that stops responding, indicates T1499. In PotchMatch the alerts "POSSIBLE_DOS" and "SERVICE_FAILURE" both indicate T1499. The IP blocking procedure (DOC-IP-BLOCKING) and the service outage runbook (DOC-SERVICE-OUTAGE) support response to T1499.
