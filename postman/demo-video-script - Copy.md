# PotchMatch — Deliverable 1 Demo Video Script

Target length: ~9:20 (buffer under the 8–10 minute limit). Timestamps are estimates from rehearsal pacing — run through it once with a stopwatch and adjust, don't treat the clock as gospel. The bracketed `[...]` lines are stage directions (what's on screen / what you do), not things to say out loud.

One change from your original outline: the circuit breaker / Consul outage demo has been moved to happen **before** the successful payment, not after. That way the video ends on the happy path (money moves, UI updates, everything green) instead of ending on a "system is down" screen.

---

## Before you hit record

- **Pre-warm Docker.** Run `docker compose up --build -d` once from `backend/` *before* recording, then `docker compose down`. When you record, you'll run `docker compose up -d` instead — starts in seconds instead of several minutes, since all four images are already built. If you build cold on camera, the "let me explain the project" narration below will either run out before it's ready or drag on far past it.
- **Import the Postman collection** (`postman/PotchMatch-Demo.postman_collection.json` + the environment file) and select the **"PotchMatch Demo"** environment in the top-right dropdown before you start.
- **Have these files/tabs already open** (minimized or on another virtual desktop, ready to alt-tab to — don't hunt for them live):
  - A terminal at `squash-platform\backend`
  - A second terminal, ready to run log-tailing commands
  - Postman, with the collection expanded so you can see all three folders
  - Browser tab 1: `http://localhost:4200` (the app)
  - Browser tab 2: `http://localhost:8500/ui` (Consul)
  - VS Code (or your editor) with these three files already opened in tabs, ready to click between: `BookingService/Controllers/BookingController.cs`, `BookingService/Program.cs`, `ApiGateway/ocelot.json`
  - `architecture-diagram.png` open in an image viewer
- **Rank field note:** when you fill in "Update Coach Profile" in Postman, `rank` is just echoed back as `"Unranked"` — it's not editable through this screen, so leave it as-is.

---

## Suggested window layout

Two ways to run this — pick whichever your recording software makes easier:

**Split-screen (recommended if you have one monitor):** browser (app) on the left half, terminal/Postman/editor swapped in on the right half as needed. Keeps the UI visible at all times so viewers see the visual effect of every backend action.

**Full-screen alt-tab (if you have two monitors):** app + Consul on monitor 1, everything else (terminal, Postman, editor) on monitor 2. Glance-cut between them rather than resizing windows live.

---

## The script

| Time | Windows on screen | What you do / say |
|---|---|---|
| **0:00–0:20** | Webcam, face only | "Hello, I am Meiring van Niekerk, and this is ITRI623 Project Deliverable 1. The title of my project is PotchMatch: Microservice Athlete Tournament & Coaching Hub" |
| **0:20–0:35** | Terminal at `squash-platform\backend` | "After you've extracted the zip file, open it and run `docker compose up`." "While that sets up, let me give you a quick overview of the project." |
| **0:35–1:35** | Go to Word document | "Squash is a growing competitive sport in South Africa, and it's set to become an Olympic sport by 2028. The purpose of this project is to support professional players and coaches financially, by creating a platform for fee-based challenge matchmaking, where coaches can also advertise their coaching services to earn income — with the ultimate goal of growing squash in South Africa. The platform currently supports squash clubs in the North-West province, with possible future expansion nationally. Accordingly, it's a web application built with a microservices architecture." |
| **1:35–2:00** | still Word document | "Quickly, on the stack: ASP.NET Core Web APIs for each service, an Ocelot API Gateway, Consul as the service registry, Docker and Docker Compose for containerisation, PostgreSQL — one database per service — JWT authentication, and Serilog for structured logging." |
| **2:00–2:45** | Still word document | "Here's the architecture. So I am using an Angular web application frontend also containerised — talks to the API Gateway over HTTPS with a JWT bearer token. The Gateway routes requests to three domain services: IdentityService (for registering and login in users), BookingService (for hosting and accepting challenges and coaching session), and PaymentService (when accepting challenges and coaching sessions and adding funds to player accounts), each with its own PostgreSQL database. Consul sits in the middle as the service registry, and every service writes structured logs with a shared correlation ID." |
| **2:45–3:20** | Switch to VS Code, `ApiGateway/ocelot.json` | "This is the Gateway's Ocelot configuration. Notice there's no hardcoded host or port anywhere in this file — each route only names a `ServiceName`, and the in the `GlobalConfiguration` section at `ServiceDiscoveryProvider` it points at Consul. Ocelot resolves the real address by asking Consul at request time, not from anything written here." |
| **3:20–4:00** | Browser tab 2: `localhost:8500/ui` | [Open the Consul UI for `identity-service`] "So here is Consul running on port 8500." [Point at the address] "And for example the  This is the address it's actually resolving to live for the identity-service — the Gateway doesn't know this in advance, it asks Consul for it at request time." |
| **4:00–4:10** | Terminal and Docker | [Confirm `docker compose ps` shows everything healthy/running] "Ok so Everything's up and running |
|| UI and Postman |"for the demo I am going to: Register a coach then log in as that coach and host a coaching session then log out and register a player and accept that coaching session. I am going to be using postman and the UI to display how the application works" |
| **4:00–4:10** | Postmnan | [Show BaseUrl in environments http://localhost:5132 and demo a register request ] "Ok so the API gateway is running on port 5132 so I have set it up as my BaseUrl for my requests" |
| **4:10–4:35** | Postman (folder "1. Coach — Register & Setup") + browser tab 1 side by side | [Click **Register Coach (Wickus)**, hit Send, show 200 + green test] "I'll register a coach — Wickus van Niekerk, at Potchefstroom Country Club, marked as a coach." |
| **4:35–4:50** | Postman | [Click **Login Coach**, Send] "Logging in returns a JWT — you can see it's been captured automatically into a variable, so I'm not copy-pasting it around." |
| **4:50–5:20** | Postman + browser tab 1 (Dashboard) | [Click **Update Coach Profile**, Send] "Now I'll fill in his coach profile — bio, coaching level, and an hourly rate of R100." [Switch to browser, refresh/open Dashboard] "And you can see that reflected immediately on the Dashboard's My Information panel." |
| **5:20–5:50** | Postman + browser tab 1 | [Click **Create Coaching Session (7 Sept, R100)**, Send] "Now he posts a coaching session for the 7th of September, R100." [Switch to browser, show it appear as an open coaching session] "That's now visible on the club page for any player to book. Worth noting — this single action just wrote to `booking_db`, completely separate from IdentityService's `identity_db` we touched a moment ago. That's the Database-per-Service pattern: no service reaches into another's database." |
| **5:50–6:00** | Browser tab 1 | [Click Log out] "I'll log the coach out." |
| **6:00–6:30** | Postman (folder "2. Player — Register & Pay") | [Click **Register Player (Meiring)**, then **Login Player**, Send both] "Now I'll register and log in as a player — myself, Meiring van Niekerk, at the Potchefstroom Defence Squash Club." |
| **6:30–6:45** | Postman | [Click **Check Wallet Balance**, Send] "A brand-new wallet always starts at R0 — nothing staged here." |
| **6:45–7:05** | Postman + browser tab 1 | [Click **Accept Coaching Session — expect 402**, Send, show the 402 + passing test] "If I try to accept the coaching session now, it correctly fails — 402, Insufficient Funds. That's a deliberate business-rule rejection coded into PaymentService, not a bug." [Show the same error surface in the UI] |
| **7:05–7:15** | Terminal 2 | [Run `docker compose stop payment-service`] "Now let's see what happens when PaymentService itself goes down." |
| **7:15–7:45** | Postman (re-send the same Accept request) + Terminal 2 (`docker compose logs -f booking-service`) | [Click Send on the Accept request again] "Watch — this first attempt is quietly retrying three times with backoff before it gives up." [Point at the logs showing `OnRetry` events] "And now it's opened the circuit breaker." [Send the same request a second time, show it fails instantly] "This second attempt fails immediately — the breaker's open, so BookingService isn't even trying to call PaymentService, it's failing fast." |
| **7:45–8:00** | Browser tab 2: Consul UI | [Refresh] "And here in Consul, PaymentService is now showing critical — its health check is failing because the container's down." |
| **8:00–8:10** | Terminal 2 | [Run `docker compose start payment-service`] "I'll bring it back up — the breaker will close again automatically once it sees healthy calls." |
| **8:10–8:35** | Postman + browser tab 1 | [Click **Add Funds (Deposit R200)**, Send] "Now let's actually fund the wallet — R200." [Switch to browser Dashboard, show updated balance] |
| **8:35–8:55** | Postman + browser tab 1 | [Click **Accept Coaching Session — expect 200 OK**, Send, show 200 + passing test] "And now the accept succeeds." [Switch to browser, show the booking now marked Accepted, with the host's contact email shown] |
| **8:55–9:10** | VS Code, `BookingController.cs`, scrolled to the `Accept()` method | [Point at the hold/release/refund calls] "Under the hood, this one action is an orchestrated Saga — BookingService holds funds in escrow, commits its own local update, and either releases the funds on success or automatically refunds them if it loses a race to accept the same booking. That compensating step, along with the Circuit Breaker and Retry pattern you just saw, and the Database-per-Service pattern from earlier, are the three microservices patterns implemented in this project."  |
| **9:10–9:20** | Face | "That's PotchMatch — thank you for watching." |

---

## Quick reference: exact data used in this demo

| Field | Coach (Wickus) | Player (Meiring) |
|---|---|---|
| Name / Surname | Wickus van Niekerk | Meiring van Niekerk |
| Club | Potchefstroom Country Club (PCC) | Potchefstroom Defence Squash Club |
| Email | wickusvn@gmail.com | meiring@gmail.com |
| Password | ne4124ne | ne4124ne |
| Coach? | Yes | No |

Coaching session: R100 fee, scheduled 7 September 2026 — **bump this date in the Postman request if you record after that date, or the backend will reject it as being in the past.**

Deposit amount: R200 (covers the R100 fee with room to spare).

Docker commands used mid-demo: `docker compose stop payment-service` and `docker compose start payment-service` (run from `backend/`).
