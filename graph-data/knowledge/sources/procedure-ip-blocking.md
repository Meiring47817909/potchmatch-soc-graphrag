Title: PotchMatch Procedure - IP Blocking (DOC-IP-BLOCKING)
Source URL: n/a (internal PotchMatch document, no public URL)
Attribution/licence: PotchMatch internal SOC procedure, written for ITRI623 coursework; no external licence

# IP Blocking (DOC-IP-BLOCKING)

IP Blocking (DOC-IP-BLOCKING) is a Document, an incident response procedure.

## Purpose
Stop abusive traffic from a hostile source address so services stay available.

## Trigger alerts
- "POSSIBLE_DOS"
- "Multiple Failed Logins" (when attempts come from a single source)

## Steps
1. Confirm the alert and identify the source IP and the endpoint targeted.
2. Check that the address is not a legitimate shared or internal address.
3. Add the address to the gateway block list.
4. Monitor request volume to confirm the flood stops.
5. Set a review date to remove the block.
6. Record the incident.

## Relationships
- IP Blocking (DOC-IP-BLOCKING) supports response to Endpoint Denial of Service (T1499).
- IP Blocking (DOC-IP-BLOCKING) also supports response to Brute Force (T1110).
- IP Blocking (DOC-IP-BLOCKING) implements Filter Network Traffic (M1037).
