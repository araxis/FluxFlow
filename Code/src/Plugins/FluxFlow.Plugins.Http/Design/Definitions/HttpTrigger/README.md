# HTTP Trigger

Receives HTTP requests through the runner's ASP.NET Core routing and middleware.
Configuration type: `http.trigger`. Studio Run supplies the ASP.NET Core integration.

## Setup

Set **Route**, such as `/telemetry`, and **Methods**, such as `["POST"]`.
**Max body bytes** applies in both response modes. The route template is parsed
by the ASP.NET Core host when it registers the endpoint.

In Studio, Run starts a listener on `127.0.0.1` with an available port for each
application instance. Find `Now listening on: ...` in the run logs and append
the configured Route. The listener address is temporary and is not saved in configuration.

- **Acknowledge** returns an immediate successful response after admission (default status 202).
  This does not mean downstream processing has finished.
- **Reply** waits for a correlated message on **Response**, up to **Reply timeout**.
  **Max pending** bounds the number of requests waiting for replies.

Changing response mode retains the other mode's settings. A wired **Response**
input requires **Reply** mode.

## Ports and shutdown

**Output** emits the parsed request, including supported Form and Files data.
**Response** accepts correlated replies. **Events** is the component event port;
Studio's **Error** port filters Output errors.

**Cancel** is the default shutdown mode: pending exchanges are cancelled with 503.
**Drain** lets accepted requests finish within the host shutdown deadline.
New requests receive 503 during shutdown; Studio closes the listener when Stop
finishes. Rerun starts a fresh listener and correlation state. Completed or cancelled
exchanges are removed from correlation tracking.

Authentication and authorization belong to a separately configured ASP.NET Core host.
Studio's listener is local only and does not provision an external HTTP dependency.
